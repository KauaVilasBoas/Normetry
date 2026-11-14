# Retrieval eval

- Run: 2026-11-14 03:41 UTC, CPU, 14 threads
- Embedding model: `BAAI/bge-m3@5617a9f61b028005a4858fdac845db406aefb181`
- Re-ranker: `BAAI/bge-reranker-v2-m3@953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e`
- Settings: vector_k=50, text_k=50, rrf_k=60, rerank_candidates=30, top_k=10, hnsw_ef_search=100, reranker_model=BAAI/bge-reranker-v2-m3, reranker_model_revision=953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e, reranker_max_length=1024
- Corpus: 724 chunks; ingestion 2 s (cached document vectors are not embedded again), eval 921 s
- Query: the item's turns joined into one (follow-up baseline), in the item's scope
- Quotes no single chunk holds: none

59 items: 44 answerable (83 quotes), scored below; 15 refusals, checked for scope only. Metrics are means over answerable items: recall@k is the share of an item's quotes found in its top k chunks, MRR@10 uses the first top-10 chunk that holds any of its quotes.

## Each step, all regulations

| | recall@1 | recall@3 | recall@5 | recall@10 | MRR@10 |
|---|---|---|---|---|---|
| vector | 0.642 | 0.850 | 0.892 | 0.943 | 0.842 |
| text | 0.358 | 0.502 | 0.631 | 0.714 | 0.524 |
| fused | 0.506 | 0.669 | 0.748 | 0.864 | 0.711 |
| reranked | 0.710 | 0.900 | 0.919 | 0.934 | 0.883 |

The re-ranker scores the fused top 30, which hold 0.977 of the quotes (the most its top 10 can reach).

## Each step, per regulation (recall@10 / MRR@10)

| | vector | text | fused | reranked |
|---|---|---|---|---|
| FDA Food Code (US, en), 14 items | 0.893 / 0.833 | 0.804 / 0.513 | 0.929 / 0.836 | 0.863 / 0.798 |
| Reg. 852/2004 (EU, en), 8 items | 1.000 / 0.795 | 0.833 / 0.698 | 0.875 / 0.812 | 1.000 / 1.000 |
| Reg. 852/2004 (EU, pt), 7 items | 1.000 / 0.929 | 0.714 / 0.423 | 0.714 / 0.607 | 1.000 / 1.000 |
| RDC 216/2004 (BR, pt), 15 items | 0.933 / 0.833 | 0.567 / 0.490 | 0.867 / 0.588 | 0.933 / 0.844 |

## Quotes missing from the re-ranked top 10

| item | section | rank after fusion | quote |
|---|---|---|---|
| us-002 | 3-501.17 | 26 | refrigerated, *READY-TO-EAT *TIME/TEMPERATURE CONTROL FOR SAFETY FOOD ingredient or a portion of ... |
| us-008 | 3-203.12 | 9 | *MOLLUSCAN SHELLFISH tags or labels shall remain attached to the container in ... |
| us-008 | 3-203.12 | 9 | The date when the last *MOLLUSCAN SHELLFISH from the container is sold ... |
| us-008 | 3-203.12 | 9 | retaining product tags, labels, or invoices that contain identification in ¶¶ 3-202.18 ... |
| us-009 | 2-401.11 | 5 | an *EMPLOYEE shall eat, drink, or use any form of *TOBACCO PRODUCTS ... |
| us-009 | 2-401.11 | 5 | A *FOOD EMPLOYEE may drink from a closed *BEVERAGE container if the ... |
| us-015 | 2-301.16 | 23 | (3) Be applied only to hands that are cleaned as specified under ... |
| br-016 | 4.3.1 | not retrieved | Deve existir um conjunto de ações eficazes e contínuas de controle de ... |
| br-016 | 4.3.2 | 31 | Quando as medidas de prevenção adotadas não forem eficazes, o controle químico ... |

## Scope

Chunks returned outside the item's jurisdiction (or, for EU, outside the question's language): 0.

## Best re-ranker score (input for the refusal threshold)

- answerable items: min 0.000, median 0.120, max 0.993
- refusals: min 0.000, median 0.005, max 0.720

## Time per question (seconds)

| step | mean | max |
|---|---|---|
| embed | 0.111 | 0.428 |
| vector | 0.047 | 0.054 |
| text | 0.005 | 0.012 |
| rerank | 15.425 | 32.679 |
