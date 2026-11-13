# Retrieval eval

> The re-ranker was not measured in this run. The Hugging Face file CDN was unreachable from the machine that ran it (the Hub API answered, every download host timed out), so bge-reranker-v2-m3 could not be downloaded. The run kept the fused order where the re-ranker would reorder, so its `reranked` rows below are only fused order cut to 10 and are marked as not measured. Vector, full-text and fusion are real measurements. The first run of the CI job `ai (retrieval eval)` measures the re-ranked step against the floors (recall@10 0.9, MRR@10 0.75).

- Run: 2026-11-13 15:45 UTC, CPU, 14 threads
- Embedding model: `BAAI/bge-m3@5617a9f61b028005a4858fdac845db406aefb181`
- Re-ranker: none (weights not downloadable, see the note); configured: `BAAI/bge-reranker-v2-m3@953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e`
- Settings: vector_k=50, text_k=50, rrf_k=60, rerank_candidates=30, top_k=10, hnsw_ef_search=100, reranker_model=BAAI/bge-reranker-v2-m3, reranker_model_revision=953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e, reranker_max_length=1024
- Corpus: 724 chunks; ingestion 2 s (cached document vectors are not embedded again), eval 10 s
- Query: the item's turns joined into one (follow-up baseline), in the item's scope
- Quotes no single chunk holds: none

59 items: 44 answerable (83 quotes), scored below; 15 refusals, checked for scope only. Metrics are means over answerable items: recall@k is the share of an item's quotes found in its top k chunks, MRR@10 uses the first top-10 chunk that holds any of its quotes.

## Each step, all regulations

| | recall@1 | recall@3 | recall@5 | recall@10 | MRR@10 |
|---|---|---|---|---|---|
| vector | 0.642 | 0.850 | 0.892 | 0.943 | 0.842 |
| text | 0.358 | 0.502 | 0.631 | 0.714 | 0.524 |
| fused | 0.506 | 0.669 | 0.748 | 0.864 | 0.711 |
| reranked | not measured | | | | |

The re-ranker scores the fused top 30, which hold 0.977 of the quotes (the most its top 10 can reach).

## Each step, per regulation (recall@10 / MRR@10)

| | vector | text | fused | reranked |
|---|---|---|---|---|
| FDA Food Code (US, en), 14 items | 0.893 / 0.833 | 0.804 / 0.513 | 0.929 / 0.836 | not measured |
| Reg. 852/2004 (EU, en), 8 items | 1.000 / 0.795 | 0.833 / 0.698 | 0.875 / 0.812 | not measured |
| Reg. 852/2004 (EU, pt), 7 items | 1.000 / 0.929 | 0.714 / 0.423 | 0.714 / 0.607 | not measured |
| RDC 216/2004 (BR, pt), 15 items | 0.933 / 0.833 | 0.567 / 0.490 | 0.867 / 0.588 | not measured |

## Quotes missing from the fused top 10 (the re-ranked list was not measured)

| item | section | rank after fusion | quote |
|---|---|---|---|
| us-002 | 3-501.17 | 26 | refrigerated, *READY-TO-EAT *TIME/TEMPERATURE CONTROL FOR SAFETY FOOD ingredient or a portion of ... |
| us-015 | 2-301.16 | 15 | A hand antiseptic used as a topical application, a hand antiseptic solution ... |
| us-015 | 2-301.16 | 23 | (3) Be applied only to hands that are cleaned as specified under ... |
| eu-001 | Annex II, Chapter I | 13 | Cleaning agents and disinfectants are not to be stored in areas where ... |
| eu-011 | Annex II, Chapter IX | 16 | Devem ser igualmente instituídos procedimentos adequados para prevenir que animais domésticos tenham ... |
| eu-018 | Annex II, Chapter VI | 22 | Os resíduos alimentares, os subprodutos não comestíveis e os outros resíduos deverão ... |
| eu-018 | Annex II, Chapter VI | 22 | Os resíduos alimentares, os subprodutos não comestíveis e os demais resíduos devem ... |
| br-009 | 4.8.18 | 15 | Caso o alimento preparado seja armazenado sob refrigeração ou congelamento deve-se apor ... |
| br-009 | 4.8.6 | 12 | Quando as matérias-primas e os ingredientes não forem utilizados em sua totalidade, ... |
| br-016 | 4.3.1 | not retrieved | Deve existir um conjunto de ações eficazes e contínuas de controle de ... |
| br-016 | 4.3.2 | 31 | Quando as medidas de prevenção adotadas não forem eficazes, o controle químico ... |

## Scope

Chunks returned outside the item's jurisdiction (or, for EU, outside the question's language): 0.

## Best re-ranker score (input for the refusal threshold)

Not measured in this run.

## Time per question (seconds)

| step | mean | max |
|---|---|---|
| embed | 0.097 | 0.172 |
| vector | 0.046 | 0.050 |
| text | 0.003 | 0.014 |

## Notes

- Against the target (recall@10 of at least 0.9 on answerable items, phase 1 design): the vector search alone reaches 0.943. Fusion drops to 0.864 because the full-text list is weak in Portuguese (recall@10 0.567 on RDC 216/2004, 0.714 on 852/2004 pt) and RRF gives it as much weight as the vector list. The re-ranker reorders the fused top 30, which hold 0.977 of the quotes, so its input can reach the target; the first CI run measures whether its top 10 does. If it does not, the levers are, in order: more re-ranked candidates, a larger RRF constant or a weighted fusion, then the full-text query.
- br-016 (pest control, RDC 4.3.1) is the one quote neither search returns in its top 50.
- Full-text ranking, measured with the same setup (recall of the full-text list at 10 and 30; fused list at 10 and 30): `ts_rank` 0.714 / 0.841, fused 0.864 / 0.977; `ts_rank` with length normalization 1: 0.714 / 0.822, fused 0.835 / 0.970; `ts_rank_cd`: 0.629 / 0.795, fused 0.864 / 0.977; `ts_rank_cd` normalized: 0.640 / 0.818, fused 0.864 / 0.977. `ts_rank` stays. An RRF constant of 10 instead of 60 lifts fused recall@10 to 0.932 but lowers fused recall@30 to 0.970, the re-ranker's ceiling, so 60 stays.
- Query plans (EXPLAIN ANALYZE as app_ai on the 724 chunks): both searches read `chunks_corpus_jurisdiction_idx` for the corpus, jurisdiction and language filter and sort the filtered rows (exact KNN for vectors, `ts_rank` for text); the join reads `sources_chunk_key`. Under 1 ms each.
