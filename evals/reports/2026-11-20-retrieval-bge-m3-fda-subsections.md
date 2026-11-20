# Retrieval eval

- Run: 2026-11-20 04:57 UTC, CPU, 2 threads
- Embedding model: `BAAI/bge-m3@5617a9f61b028005a4858fdac845db406aefb181`
- Re-ranker: `BAAI/bge-reranker-v2-m3@953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e`
- Settings: vector_k=50, text_k=50, rrf_k=60, rerank_candidates=30, top_k=10, hnsw_ef_search=100, establishment_rerank_candidates=10, establishment_top_k=5, document_k=5, document_top_k=3, reranker_model=BAAI/bge-reranker-v2-m3, reranker_model_revision=953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e, reranker_max_length=1024
- Corpus: 750 chunks; ingestion 271 s (cached document vectors are not embedded again), eval 854 s
- Documents: 28 in 2 tenants, indexed in 121 s; 32 document questions in 587 s
- Query: the item's turns joined into one (follow-up baseline), in the item's scope
- Quotes no single chunk holds: none

59 items: 44 answerable (83 quotes), scored below; 15 refusals, checked for scope only. Metrics are means over answerable items: recall@k is the share of an item's quotes found in its top k chunks, MRR@10 uses the first top-10 chunk that holds any of its quotes.

## Each step, all regulations

| | recall@1 | recall@3 | recall@5 | recall@10 | MRR@10 |
|---|---|---|---|---|---|
| vector | 0.642 | 0.850 | 0.892 | 0.943 | 0.842 |
| text | 0.358 | 0.502 | 0.619 | 0.722 | 0.526 |
| fused | 0.498 | 0.657 | 0.748 | 0.847 | 0.699 |
| reranked | 0.710 | 0.900 | 0.919 | 0.934 | 0.883 |

The re-ranker scores the fused top 30, which hold 0.970 of the quotes (the most its top 10 can reach).

## Each step, per regulation (recall@10 / MRR@10)

| | vector | text | fused | reranked |
|---|---|---|---|---|
| FDA Food Code (US, en), 14 items | 0.893 / 0.833 | 0.827 / 0.520 | 0.875 / 0.800 | 0.863 / 0.798 |
| Reg. 852/2004 (EU, en), 8 items | 1.000 / 0.795 | 0.833 / 0.698 | 0.875 / 0.812 | 1.000 / 1.000 |
| Reg. 852/2004 (EU, pt), 7 items | 1.000 / 0.929 | 0.714 / 0.423 | 0.714 / 0.607 | 1.000 / 1.000 |
| RDC 216/2004 (BR, pt), 15 items | 0.933 / 0.833 | 0.567 / 0.487 | 0.867 / 0.587 | 0.933 / 0.844 |

## Quotes missing from the re-ranked top 10

| item | section | rank after fusion | quote |
|---|---|---|---|
| us-002 | 3-501.17 | 25 | refrigerated, *READY-TO-EAT *TIME/TEMPERATURE CONTROL FOR SAFETY FOOD ingredient or a portion of ... |
| us-008 | 3-203.12 | 11 | *MOLLUSCAN SHELLFISH tags or labels shall remain attached to the container in ... |
| us-008 | 3-203.12 | 11 | The date when the last *MOLLUSCAN SHELLFISH from the container is sold ... |
| us-008 | 3-203.12 | 11 | retaining product tags, labels, or invoices that contain identification in ¶¶ 3-202.18 ... |
| us-009 | 2-401.11 | 5 | an *EMPLOYEE shall eat, drink, or use any form of *TOBACCO PRODUCTS ... |
| us-009 | 2-401.11 | 5 | A *FOOD EMPLOYEE may drink from a closed *BEVERAGE container if the ... |
| us-015 | 2-301.16 | 52 | (3) Be applied only to hands that are cleaned as specified under ... |
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
| embed | 0.131 | 0.157 |
| vector | 0.003 | 0.006 |
| text | 0.001 | 0.003 |
| documents | 0.002 | 0.003 |
| rerank | 14.329 | 25.330 |

## Document questions (document-qa)

32 items, each asked from its establishment with the three synthetic establishments indexed in the demo's two tenants: 24 answerable (72 document quotes), scored below; 8 refusals (cross-tenant and cross-establishment), checked for isolation only. A document quote is a hit when a document chunk of the re-ranked result holds it; the result keeps at most 3 document chunks. Candidates are the establishment's document chunks before the re-ranker, the most the result can hold.

| | recall@1 | recall@3 | recall@5 | recall@10 | MRR@10 |
|---|---|---|---|---|---|
| documents, re-ranked result | 0.604 | 0.773 | 0.828 | 0.828 | 0.842 |
| documents, candidates | 0.512 | 0.763 | 0.885 | 0.885 | 0.774 |
| regulation quotes, 15 items | 0.067 | 0.533 | 0.700 | 0.933 | 0.330 |

| establishment | items | document recall@10 / MRR@10 | document chunks in the result |
|---|---|---|---|
| Bakery Austin | 8 | 0.979 / 1.000 | 2.62 |
| Bistro Lisboa | 8 | 0.694 / 0.713 | 2.88 |
| Bistro São Paulo | 8 | 0.812 / 0.812 | 3.00 |

### Document quotes missing from the re-ranked result

| item | file | rank among candidates | quote |
|---|---|---|---|
| br-001 | procedimentos-operacionais-padronizados.docx | not a candidate | A higienização é feita a cada seis meses, no máximo, e também ... |
| br-006 | manual-de-boas-praticas.pdf | not a candidate | Revisão 4, de 5 de outubro de 2026. ... |
| eu-001 | plano-de-higienizacao.txt | 2 | APÓS CADA UTILIZAÇÃO - Bancadas de preparação, tábuas de corte, facas e ... |
| eu-003 | auditoria-interna-outubro-2026.pdf | not a candidate | Rever o plano HACCP e incluir o forno combinado e o novo ... |
| eu-004 | relatorio-controlo-de-pragas.pdf | 4 | Até à data deste relatório, a atividade de roedores detetada em 2026-09-22 ... |
| eu-006 | registo-de-formacao.docx | 4 | Nuno Carvalho | Ajudante de cozinha | 2026-06-02 | Sem registo | ... |
| us-003 | sanitizer-and-warewashing-log.csv | 4 | 2026-10-23 | 07:00 | 114 | 100 | 157 | 182 | ... |
| sec-003 | registo-de-temperaturas.csv | not a candidate | 2026-11-10 | 09:00 | PCC 1 | Câmara frigorífica | 6,8 | ... |
| sec-003 | registo-de-temperaturas.csv | not a candidate | 2026-11-10 | 18:00 | PCC 1 | Câmara frigorífica | 7,5 | ... |
| sec-003 | registo-de-temperaturas.csv | not a candidate | 2026-11-11 | 09:00 | PCC 1 | Câmara frigorífica | 7,9 | ... |
| sec-003 | registo-de-temperaturas.csv | not a candidate | 2026-11-11 | 18:00 | PCC 1 | Câmara frigorífica | 8,3 | ... |
| sec-003 | registo-de-temperaturas.csv | not a candidate | 2026-11-12 | 09:00 | PCC 1 | Câmara frigorífica | 6,2 | ... |
| sec-003 | plano-haccp.docx | not a candidate | se continuar acima do limite, transferir os produtos para outro equipamento, avaliar ... |

## Corpus questions with the establishment's documents

The regulation-qa items again, each asked from the synthetic establishment of its jurisdiction (US Bakery Austin, EU Bistro Lisboa, BR Bistro São Paulo), so its documents compete for the top 10. Only regulation chunks can hold a quote; a document chunk takes its rank. The floors apply to the run without documents.

| | recall@1 | recall@3 | recall@5 | recall@10 | MRR@10 |
|---|---|---|---|---|---|
| without documents | 0.710 | 0.900 | 0.919 | 0.934 | 0.883 |
| with the establishment's documents | 0.335 | 0.706 | 0.892 | 0.934 | 0.613 |

Document chunks in the top 10, mean per item: 2.54.
Items whose recall@10 dropped: none.

## Isolation

Document chunks of another establishment or tenant in any result (document questions and corpus questions with documents): 0. Must be 0.
