"""Load the regulation corpus into ai.sources and ai.chunks, as corpus_ingestor.

    uv run python -m normetry_ai.ingestion.ingest    # needs NORMETRY_CORPUS_DATABASE_URL

For each source in corpus/manifest.json: parse its sections, chunk them, embed the chunks the
database does not hold yet, and write. A source whose registry row and chunk hashes already match
is left untouched, so a second run changes nothing and loads no model. Otherwise one transaction
updates the source and replaces all its chunks, reusing the stored embedding of every unchanged
chunk; a new manifest version also bumps the source's revision (the chunks' source_version).
"""

import argparse
import json
import time
from collections.abc import Sequence
from dataclasses import dataclass
from pathlib import Path

from sqlalchemy import Engine, create_engine, text
from sqlalchemy.pool import NullPool

from normetry_ai.ingestion.chunking import chunk_sections
from normetry_ai.ingestion.corpus import CORPUS_DIR, Section, load_manifest, parse_source
from normetry_ai.llm.embeddings import Embedder, SentenceTransformerEmbedder
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
_SELECT_EMBEDDINGS = text(
    """
    SELECT content_hash, embedding::text FROM ai.chunks
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
                           language, section_path, content, content_hash, embedding, metadata)
    VALUES (NULL, 'regulation', :source_id, :source_version, :jurisdiction, :language,
            :section_path, :content, :content_hash, CAST(:embedding AS public.vector),
            CAST(:metadata AS jsonb))
    ON CONFLICT ON CONSTRAINT chunks_source_content_key DO NOTHING
    """
)


@dataclass(frozen=True)
class SourceResult:
    manifest_id: str
    status: str  # "new", "updated" or "unchanged"
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

    with engine.begin() as connection:
        row = connection.execute(_SELECT_SOURCE, {"manifest_id": source["id"]}).one_or_none()
        stored: dict[str, str] = {}
        if row is not None:
            params = {"source_id": row.id, "embedding_model": embedder.name}
            stored = dict(connection.execute(_SELECT_EMBEDDINGS, params).all())

    def result(status: str, embedded: int = 0) -> SourceResult:
        elapsed = time.perf_counter() - started
        return SourceResult(source["id"], status, len(chunks), embedded, elapsed)

    if (
        row is not None
        and {column: getattr(row, column) for column in _REGISTRY} == registry
        and stored.keys() == {chunk.content_hash for chunk in chunks}
    ):
        return result("unchanged")

    # Embedding takes minutes on CPU, so it runs outside any transaction.
    missing = [chunk for chunk in chunks if chunk.content_hash not in stored]
    vectors = embedder.embed_documents([chunk.content for chunk in missing]) if missing else []
    embeddings = stored | {
        chunk.content_hash: "[" + ",".join(map(str, vector)) + "]"
        for chunk, vector in zip(missing, vectors, strict=True)
    }

    # ponytail: two runs at once can interleave between the read above and this write; both write
    # the same rows, or the second fails on sources_manifest_id_key. Run one ingestion at a time.
    with engine.begin() as connection:
        if row is None:
            source_id, revision = connection.execute(_INSERT_SOURCE, registry).scalar_one(), 1
        else:
            source_id = row.id
            revision = row.revision + 1 if row.version != source["version"] else row.revision
            connection.execute(_DELETE_CHUNKS, {"source_id": source_id})
            connection.execute(_UPDATE_SOURCE, {**registry, "id": source_id, "revision": revision})
        metadata = json.dumps({"embedding_model": embedder.name})
        connection.execute(
            _INSERT_CHUNK,
            [
                {
                    "source_id": source_id,
                    "source_version": revision,
                    "jurisdiction": source["jurisdiction"],
                    "language": source["language"],
                    "section_path": chunk.section_path,
                    "content": chunk.content,
                    "content_hash": chunk.content_hash,
                    "embedding": embeddings[chunk.content_hash],
                    "metadata": metadata,
                }
                for chunk in chunks
            ],
        )
    return result("new" if row is None else "updated", len(missing))


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
