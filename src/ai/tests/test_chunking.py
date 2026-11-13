"""Chunking of parsed sections (no database, no model)."""

from normetry_ai.ingestion.chunking import chunk_sections, content_hash, split_text
from normetry_ai.ingestion.corpus import Section


def section(section_path: str, text: str) -> Section:
    return Section("src", "US", "en", "2026", section_path, text)


def test_a_short_section_is_one_chunk() -> None:
    assert split_text("Keep cold food at 5 C or below.", limit=100) == [
        "Keep cold food at 5 C or below."
    ]


def test_a_long_section_is_cut_at_sentence_ends() -> None:
    text = " ".join(f"Sentence {n} says something about cold holding." for n in range(30))
    chunks = split_text(text, limit=200)

    assert " ".join(chunks) == text
    assert all(len(chunk) <= 200 and chunk.endswith("holding.") for chunk in chunks)


def test_an_fda_priority_tag_stays_with_its_provision() -> None:
    text = (
        "(A) Food shall be marked with the date. The day of preparation is Day 1. Pf "
        "(B) Opened containers shall be marked too. Pf (C) A hand dip shall be kept clean. Pf"
    )
    chunks = split_text(text, limit=80)

    assert " ".join(chunks) == text
    assert chunks[0].endswith("Day 1. Pf")
    assert all(chunk.startswith("(") for chunk in chunks)


def test_an_overlong_sentence_falls_back_to_clauses_then_spaces() -> None:
    clauses = "; ".join(f"clause {n} of a list" for n in range(20)) + "."
    word_run = " ".join(["word"] * 60)

    for text in (clauses, word_run):
        chunks = split_text(text, limit=60)
        assert " ".join(chunks) == text
        assert all(len(chunk) <= 60 for chunk in chunks)
    assert split_text(clauses, limit=60)[0].endswith(";")


def test_chunks_of_a_split_section_have_similar_sizes() -> None:
    # 110 characters over a 100 limit: two halves, not 100 and a 9-character scrap.
    text = " ".join(f"Rule {n:02}." for n in range(12))[:110].rstrip()
    chunks = split_text(text, limit=100)

    assert len(chunks) == 2
    assert min(len(chunk) for chunk in chunks) > 40


def test_chunk_sections_keeps_paths_hashes_location_and_text_and_drops_duplicates() -> None:
    chunks = chunk_sections(
        [
            section("3-501.16", "Hot and cold holding."),
            section("3-501.17", "Hot and cold holding."),  # same text, another section
            section("3-501.16", "Hot and cold holding."),  # an exact duplicate
        ]
    )

    assert [chunk.section_path for chunk in chunks] == ["3-501.16", "3-501.17"]
    assert chunks[0].content_hash == content_hash("3-501.16", "Hot and cold holding.")
    assert chunks[0].content_hash != chunks[1].content_hash
