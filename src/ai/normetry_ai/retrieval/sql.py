"""The SQL pieces the searches share: the corpus search (`hybrid.py`), the document search
(`establishment.py`) and the judge's regulation text (`inspection/judge.py`), with the parameters
they bind and the rows they read.

They hold the rules of "Retrieval under RLS" in src/ai/CLAUDE.md: a corpus query filters on the
(jurisdiction, language) pairs `policy.corpus_versions` gives, and joins ai.sources on all three
foreign key columns. The queries put them in with f-strings, so a fragment that spans lines is
laid out for a query indented by four spaces.
"""

import re
from collections.abc import Sequence
from typing import Any

from normetry_ai.retrieval.models import RetrievedChunk

# The (jurisdiction, language) pairs to search, for `(jurisdiction, language) IN (...)`, which
# chunks_corpus_jurisdiction_idx serves. Bound by corpus_version_params.
CORPUS_VERSIONS = "SELECT * FROM unnest(CAST(:jurisdictions AS text[]), CAST(:languages AS text[]))"
# The CTE `query`: :terms (any_word_terms) parsed as a Portuguese and as an English tsquery.
TSQUERIES = """query AS (
        SELECT websearch_to_tsquery('portuguese', :terms) AS pt,
               websearch_to_tsquery('english', :terms) AS en
    )"""


def tsquery_for(language: str) -> str:
    """The TSQUERIES tsquery for a chunk whose language is the column `language`: the question is
    parsed with each chunk's own configuration, as its generated tsv column is."""
    return f"CASE {language} WHEN 'pt' THEN query.pt ELSE query.en END"


def sources_join(chunks: str) -> str:
    """The join from the corpus chunks `chunks` (an alias) to their source `s`, on all three
    foreign key columns: a tenant row's source_id can equal a corpus source id (the foreign key
    skips tenant rows), so source_id alone would label a tenant's own chunk as regulation text."""
    return (
        "JOIN ai.sources s\n"
        f"      ON s.id = {chunks}.source_id AND s.jurisdiction = {chunks}.jurisdiction"
        f" AND s.language = {chunks}.language"
    )


def regulation_columns(chunks: str) -> str:
    """What regulation_chunk reads: the chunk's columns, from `chunks` (an alias), and its
    source's, from sources_join."""
    return (
        f"{chunks}.id, {chunks}.content, {chunks}.section_path, {chunks}.language, "
        f"{chunks}.jurisdiction, {chunks}.source_id,\n"
        "           s.title AS source_title, s.version AS source_version, s.official_url"
    )


def corpus_version_params(versions: Sequence[tuple[str, str]]) -> dict[str, list[str]]:
    """The parameters CORPUS_VERSIONS binds, for the pairs of `policy.corpus_versions`."""
    return {
        "jurisdictions": [jurisdiction for jurisdiction, _ in versions],
        "languages": [language for _, language in versions],
    }


def any_word_terms(text: str) -> str:
    """The :terms of TSQUERIES for `text`: its words joined with "or", so any word may match.
    websearch_to_tsquery never fails on user input."""
    return " or ".join(re.findall(r"\w+", text))


def regulation_chunk(row: Any, score: float) -> RetrievedChunk:
    """A row with the regulation_columns as a chunk."""
    return RetrievedChunk(
        chunk_id=row.id,
        content=row.content,
        section_path=row.section_path,
        language=row.language,
        jurisdiction=row.jurisdiction,
        source_id=row.source_id,
        source_title=row.source_title,
        source_version=row.source_version,
        official_url=row.official_url,
        score=score,
    )
