"""The real re-ranker: bge-reranker-v2-m3 on CPU, through the re-ranker interface.

Marked `model` like tests/test_embeddings.py: `uv run pytest -m model` (about 2.3 GB on first use).
"""

import pytest

from normetry_ai.llm.reranker import CrossEncoderReranker
from normetry_ai.settings import RetrievalSettings

pytestmark = pytest.mark.model


def test_the_reranker_scores_the_relevant_provision_highest_in_both_languages() -> None:
    settings = RetrievalSettings(_env_file=None)
    reranker = CrossEncoderReranker(
        settings.reranker_model, settings.reranker_model_revision, settings.reranker_max_length
    )
    provision = (
        "Cleaning agents and disinfectants are not to be stored in areas where food is handled."
    )
    unrelated = "The board approved the annual financial statements of the company."

    for question in (
        "Can we keep the bleach in the kitchen?",
        "Podemos guardar a lixívia na cozinha?",
    ):
        relevant, other = reranker.score(question, [provision, unrelated])

        assert 0 <= other < relevant <= 1
        assert relevant - other > 0.3
    assert reranker.score("question", []) == []
