"""Load the regulation corpus into ai.sources and ai.chunks, as corpus_ingestor.

    uv run python -m normetry_ai.ingestion.ingest    # needs NORMETRY_CORPUS_DATABASE_URL

For each source in corpus/manifest.json: parse its sections, chunk them, embed the chunks the
database does not hold yet, and write. A source whose registry row, chunk hashes and chunk
positions already match is left untouched, so a second run changes nothing and loads no model.
Otherwise one transaction updates the source and replaces all its chunks, reusing the stored
embedding of every unchanged chunk; a new manifest version also bumps the source's revision (the
chunks' source_version). A parser fix at the same version changes the hashes, and chunks stored
before positions were (all at 0) differ in position, so both are rewritten.
"""

import argparse
import json
import time
from collections.abc import Sequence
from dataclasses import dataclass
from enum import StrEnum
from pathlib import Path
from typing import Any

from sqlalchemy import Engine, create_engine, text
from sqlalchemy.pool import NullPool

from normetry_ai.ingestion.chunking import Chunk, chunk_sections
from normetry_ai.ingestion.corpus import CORPUS_DIR, Section, load_manifest, parse_source
from normetry_ai.llm.embeddings import Embedder, SentenceTransformerEmbedder
from normetry_ai.retrieval.models import vector_literal
from normetry_ai.settings import CorpusSettings, EmbeddingSettings

# The ai.sources columns copied from the manifest, and the manifest field of each.
_REGISTRY = {
    "manifest_id": "id",
    "jurisdiction": "jurisdiction",
    "code": "code",
    "title": "title",
    "version": "version",
    "language": "language",
    "official_url": "official_url",
}

_SELECT_SOURCE = text(
    """
    SELECT id, revision, manifest_id, jurisdiction, code, title, version, language, official_url
    FROM ai.sources WHERE manifest_id = :manifest_id
    """
)
# Embeddings stay in pgvector's text form: read back and written again without parsing.
_SELECT_STORED_CHUNKS = text(
    """
    SELECT content_hash, embedding::text AS embedding, position_in_section FROM ai.chunks
    WHERE tenant_id IS NULL AND source_id = :source_id
      AND metadata ->> 'embedding_model' = :embedding_model
    """
)
_INSERT_SOURCE = text(
    """
    INSERT INTO ai.sources (manifest_id, jurisdiction, code, title, version, language,
                            official_url)
    VALUES (:manifest_id, :jurisdiction, :code, :title, :version, :language, :official_url)
    RETURNING id
    """
)
_UPDATE_SOURCE = text(
    """
    UPDATE ai.sources
    SET jurisdiction = :jurisdiction, code = :code, title = :title, version = :version,
        language = :language, official_url = :official_url, revision = :revision
    WHERE id = :id
    """
)
_DELETE_CHUNKS = text("DELETE FROM ai.chunks WHERE tenant_id IS NULL AND source_id = :source_id")
_INSERT_CHUNK = text(
    """
    INSERT INTO ai.chunks (tenant_id, source_type, source_id, source_version, jurisdiction,
                           language, section_path, position_in_section, content, content_hash,
                           embedding, metadata)
    VALUES (NULL, 'regulation', :source_id, :source_version, :jurisdiction, :language,
            :section_path, :position_in_section, :content, :content_hash,
            CAST(:embedding AS public.vector), CAST(:metadata AS jsonb))
    ON CONFLICT ON CONSTRAINT chunks_source_content_key DO NOTHING
    """
)


class SourceStatus(StrEnum):
    """What an ingestion did to one source, as the command prints it."""

    NEW = "new"  # registered, with its chunks
    UPDATED = "updated"  # its registry row or chunks rewritten
    UNCHANGED = "unchanged"  # left untouched


@dataclass(frozen=True)
class SourceResult:
    manifest_id: str
    status: SourceStatus
    chunks: int
    embedded: int
    seconds: float


def ingest_source(
    engine: Engine, source: dict, sections: Sequence[Section], embedder: Embedder
) -> SourceResult:
    """Bring one manifest source and its chunks in line with its parsed sections."""
    started = time.perf_counter()
    chunks = chunk_sections(sections)
    if not chunks:
        # Replacing the source's chunks with nothing would empty it: a parser fault, not a version.
        raise ValueError(f"{source['id']}: no sections parsed")
    registry = {column: source[field] for column, field in _REGISTRY.items()}

    row, stored_chunks = _stored_source(engine, source["id"], embedder.name)
    if _is_unchanged(row, stored_chunks, registry, chunks):
        status, embedded = SourceStatus.UNCHANGED, 0
    else:
        embeddings, embedded = _embeddings_for(chunks, stored_chunks, embedder)
        status = _write_source(engine, registry, row, chunks, embeddings, embedder.name)
    return SourceResult(source["id"], status, len(chunks), embedded, time.perf_counter() - started)


def _stored_source(
    engine: Engine, manifest_id: str, embedding_model: str
) -> tuple[Any, Sequence[Any]]:
    """The source's ai.sources row (None before its first ingestion) and its chunks stored with
    `embedding_model`."""
    with engine.begin() as connection:
        row = connection.execute(_SELECT_SOURCE, {"manifest_id": manifest_id}).one_or_none()
        if row is None:
            return None, []
        params = {"source_id": row.id, "embedding_model": embedding_model}
        return row, connection.execute(_SELECT_STORED_CHUNKS, params).all()


def _is_unchanged(
    row: Any, stored_chunks: Sequence[Any], registry: dict[str, Any], chunks: Sequence[Chunk]
) -> bool:
    """Whether the stored registry row, chunk hashes and positions match the parsed source."""
    return (
        row is not None
        and {column: getattr(row, column) for column in _REGISTRY} == registry
        and {(chunk.content_hash, chunk.position_in_section) for chunk in stored_chunks}
        == {(chunk.content_hash, chunk.position_in_section) for chunk in chunks}
    )


def _embeddings_for(
    chunks: Sequence[Chunk], stored_chunks: Sequence[Any], embedder: Embedder
) -> tuple[dict[str, str], int]:
    """Every chunk's embedding by content hash, in pgvector's text form, reusing the stored ones,
    and how many chunks had to be embedded. Embedding takes minutes on CPU, so it runs outside any
    transaction."""
    stored = {chunk.content_hash: chunk.embedding for chunk in stored_chunks}
    missing = [chunk for chunk in chunks if chunk.content_hash not in stored]
    vectors = embedder.embed_documents([chunk.content for chunk in missing]) if missing else []
    embeddings = stored | {
        chunk.content_hash: vector_literal(vector)
        for chunk, vector in zip(missing, vectors, strict=True)
    }
    return embeddings, len(missing)


def _write_source(
    engine: Engine,
    registry: dict[str, Any],
    row: Any,
    chunks: Sequence[Chunk],
    embeddings: dict[str, str],
    embedding_model: str,
) -> SourceStatus:
    """Register the source, or update its row and delete its chunks, then insert `chunks`, in one
    transaction. A new manifest version bumps the revision (the chunks' source_version)."""
    # ponytail: two runs at once can interleave between the read and this write; both write the
    # same rows, or the second fails on sources_manifest_id_key. Run one ingestion at a time.
    with engine.begin() as connection:
        if row is None:
            source_id, revision = connection.execute(_INSERT_SOURCE, registry).scalar_one(), 1
        else:
            source_id = row.id
            revision = row.revision + 1 if row.version != registry["version"] else row.revision
            connection.execute(_DELETE_CHUNKS, {"source_id": source_id})
            connection.execute(_UPDATE_SOURCE, {**registry, "id": source_id, "revision": revision})
        metadata = json.dumps({"embedding_model": embedding_model})
        connection.execute(
            _INSERT_CHUNK,
            [
                {
                    "source_id": source_id,
                    "source_version": revision,
                    "jurisdiction": registry["jurisdiction"],
                    "language": registry["language"],
                    "section_path": chunk.section_path,
                    "position_in_section": chunk.position_in_section,
                    "content": chunk.content,
                    "content_hash": chunk.content_hash,
                    "embedding": embeddings[chunk.content_hash],
                    "metadata": metadata,
                }
                for chunk in chunks
            ],
        )
    return SourceStatus.NEW if row is None else SourceStatus.UPDATED


def main(argv: list[str] | None = None) -> None:
    """Ingest every source of corpus/manifest.json and print what changed."""
    parser = argparse.ArgumentParser(description=main.__doc__)
    parser.add_argument("--corpus", type=Path, default=CORPUS_DIR, help="the corpus directory")
    args = parser.parse_args(argv)

    url = CorpusSettings().corpus_database_url
    if url is None:
        raise SystemExit("Set NORMETRY_CORPUS_DATABASE_URL (corpus_ingestor role) to ingest.")
    settings = EmbeddingSettings()
    embedder = SentenceTransformerEmbedder(
        settings.embedding_model, settings.embedding_model_revision
    )
    engine = create_engine(url.get_secret_value(), poolclass=NullPool)
    try:
        results = []
        print(f"{'status':<10}{'chunks':>7}{'embedded':>10}{'seconds':>9}  source")
        for source in load_manifest(args.corpus):
            try:
                sections = parse_source(source, args.corpus)
            except FileNotFoundError as error:
                hint = "fetch the corpus with `uv run corpus/scripts/fetch_corpus.py` from the root"
                raise SystemExit(f"{error}: {hint}.") from error
            result = ingest_source(engine, source, sections, embedder)
            results.append(result)
            print(
                f"{result.status:<10}{result.chunks:>7}{result.embedded:>10}"
                f"{result.seconds:>9.1f}  {result.manifest_id}",
                flush=True,
            )
    finally:
        engine.dispose()
    print(
        f"{'total':<10}{sum(r.chunks for r in results):>7}{sum(r.embedded for r in results):>10}"
        f"{sum(r.seconds for r in results):>9.1f}  embedding model {embedder.name}"
    )


if __name__ == "__main__":
    main()
