# Regulation corpus

The regulation texts that Normetry ingests in phase 1, pinned to an exact version. [`manifest.json`](manifest.json) records each source's version, official URL, reuse terms and SHA-256. [`scripts/fetch_corpus.py`](scripts/fetch_corpus.py) downloads the files and checks them against those hashes, so anyone can rebuild the same corpus byte for byte.

## Running it

From the repository root (needs [uv](https://docs.astral.sh/uv/); the script declares its own dependency, httpx):

```bash
uv run corpus/scripts/fetch_corpus.py                    # download missing files, check every hash
uv run corpus/scripts/fetch_corpus.py --verify           # check local files only, no network
uv run corpus/scripts/fetch_corpus.py --update-manifest  # record sha256, size_bytes and retrieved_at
```

Files go to `corpus/<jurisdiction>/<code>/<version>/<file>`. A file whose hash already matches is skipped, so a second run downloads nothing. If a download does not match the manifest, the script keeps the old file, prints both hashes and exits with code 1. That usually means the publisher changed the file. Check what changed, then accept the new version with `--update-manifest` (or add a new version directory for a new edition).

CI runs the same script before the AI service's tests (`.github/workflows/ci.yml`, job `ai`), so those tests parse the real files. The fetched files are cached under a key derived from `manifest.json`, so most runs download nothing.

## What is parsed

The AI service cuts each file into citable sections (`src/ai/normetry_ai/ingestion/corpus.py`; `uv run python -m normetry_ai.ingestion.corpus` from `src/ai` writes them to `src/ai/.data/corpus-sections.jsonl`). Only these parts are kept:

- FDA Food Code: chapters 1 to 8, one section per code section (`3-501.16`). The preface and the annexes are left out.
- Regulation (EC) No 852/2004, English and Portuguese: the articles (`Article 4`) and the annexes by part or chapter (`Annex I, Part A`, `Annex II, Chapter I`). The consolidated text has no recitals. Paths are the same in both languages.
- RDC 216/2004: the Technical Regulation in the annex, one section per item (`4.8.15`). Its items run from 1 to 4.12; the resolution's own articles (the preamble) are left out.

## Sources

Retrieved on 2026-11-12.

| Regulation | Version | Effective date | Language | Official page | Reuse terms | In git |
|---|---|---|---|---|---|---|
| FDA Food Code | 2026 edition | Released 2026-09-17. Model code: it binds only where a state or locality adopts it | en | [fda.gov](https://www.fda.gov/food/fda-food-code/food-code-2026) | US Government work, public domain ([17 U.S.C. §105](https://www.copyright.gov/title17/92chap1.html), [FDA website policies](https://www.fda.gov/about-fda/about-website/website-policies)). One table in Annex 4 is third-party material used with permission | No, fetched (5,215,431 bytes) |
| Regulation (EC) No 852/2004, hygiene of foodstuffs | Consolidated text of 2021-03-24 (CELEX 02004R0852-20210324) | 2021-03-24 for this consolidation. The regulation applies from 1 January 2006 at the earliest (Art. 18) | en | [EUR-Lex](https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:02004R0852-20210324) | Reuse allowed under [Decision 2011/833/EU](https://eur-lex.europa.eu/eli/dec/2011/833/oj). Consolidated texts are CC BY 4.0 ([EUR-Lex legal notice](https://eur-lex.europa.eu/content/legal-notice/legal-notice.html)) | Yes (372,824 bytes) |
| Same, Portuguese version | Same consolidation | Same | pt | [EUR-Lex](https://eur-lex.europa.eu/legal-content/PT/TXT/?uri=CELEX:02004R0852-20210324) | Same | Yes (385,970 bytes) |
| Anvisa RDC 216/2004, good practices for food services | Compiled text as amended by RDC 52/2014 | Original in force from 2004-09-16 (DOU publication). RDC 52/2014 in force from 2014-10-01 | pt | [AnvisaLegis](https://anvisalegis.datalegis.net/action/UrlPublicasAction.php?acao=abrirAtoPublico&num_ato=00000216&sgl_tipo=RDC&sgl_orgao=RDC/DC/ANVISA/MS&vlr_ano=2004&seq_ato=000&cod_modulo=310&cod_menu=9434) | Official act, not protected by copyright ([Lei 9.610/1998, art. 8, IV](https://www.planalto.gov.br/ccivil_03/leis/l9610.htm)) | No, fetched (221,331 bytes) |

The EU and Brazilian files are not the legally authentic texts. The first page of the EUR-Lex consolidated PDF says it is "meant purely as a documentation tool and has no legal effect", and only the Official Journal of the European Union is authentic. AnvisaLegis ends the act with "Este texto não substitui a Publicação Oficial", the official publication being the Diário Oficial da União (DOU).

### FDA Food Code

The [FDA Food Code page](https://www.fda.gov/food/retail-food-protection/fda-food-code) calls the 2026 edition "the most recent full edition". FDA announced it on 2026-09-17 ([constituent update](https://www.fda.gov/food/hfp-constituent-updates/fda-releases-2026-food-code)). It replaces the 2022 edition and the [Supplement to the 2022 Food Code](https://www.fda.gov/food/hfp-constituent-updates/fda-releases-supplement-2022-food-code) of 2024-11-04. The FDA page lists no supplement to the 2026 edition. The annexes are part of the single PDF, and FDA lists the changes on a [separate web page](https://www.fda.gov/food/fda-food-code/summary-changes-2026-fda-food-code). The FDA website policy says its content is "not copyrighted" and "may be republished, reprinted and otherwise used freely" without permission, "unless otherwise noted". The PDF contains one such note: Annex 4 says its physical hazards table (Table 3) is "Used with permission" from *HACCP Principles and Applications* (Pierson and Corlett, Chapman & Hall, 1992).

### Regulation (EC) No 852/2004

The [EUR-Lex document page](https://eur-lex.europa.eu/legal-content/EN/ALL/?uri=CELEX:32004R0852) lists four consolidated versions. The latest, 2021-03-24, includes every amending act on record: Regulations (EC) No 1019/2008, (EC) No 219/2009 and (EU) 2021/382. The Commission proposal [COM(2025) 1030](https://eur-lex.europa.eu/legal-content/EN/ALL/?uri=CELEX:52025PC1030) of 2025-12-16, procedure 2025/0410(COD), would amend the regulation, but EUR-Lex shows it only as a proposal. The PDF is used because EUR-Lex returns the same bytes on every download. The HTML version changed between two consecutive downloads, so it cannot be pinned by hash.

### Anvisa RDC 216/2004

In [AnvisaLegis](https://anvisalegis.datalegis.net/), Anvisa's legislation database, RDC 216/2004 appears as a compiled text with no revocation note. The text includes the two changes made by [RDC 52/2014](https://bvsms.saude.gov.br/bvs/saudelegis/anvisa/2014/rdc0052_29_09_2014.html): a new art. 7 and a new item 1.2 (scope) of the Annex, which adds food services in health facilities. The 2004 text without the amendment is also on [BVS](https://bvsms.saude.gov.br/bvs/saudelegis/anvisa/2004/res0216_15_09_2004.html). AnvisaLegis shows no consolidation into a newer RDC either. Anvisa has been preparing a revision since at least April 2025 ([workshop on regulatory alternatives](https://www.gov.br/anvisa/pt-br/assuntos/noticias-anvisa/2025/anvisa-realiza-oficina-presencial-de-validacao-das-alternativas-regulatorias-para-a-revisao-da-rdc-no-216-2004)). As of 2026-11-12, neither AnvisaLegis nor Anvisa's news shows a replacement. Art. 8, IV of Lei 9.610/1998 excludes "leis, decretos, regulamentos, decisões judiciais e demais atos oficiais" from copyright.

## What is committed and why

- The EU PDFs are committed. They are small (under 400 KB each), and CC BY 4.0 covers the whole file. This README and the manifest provide the attribution, and the files are unchanged.
- The FDA Food Code is fetched, not committed. At 5.2 MB it is above the 5 MB ceiling this corpus sets for committed files, every biennial edition would add another copy to the git history, and the third-party table in Annex 4 is not public domain.
- The Anvisa file is fetched, not committed. The act text is free to reuse, but the file is the whole AnvisaLegis page, with the site's navigation and scripts. That page is served from `datalegis.net`, outside `gov.br`, and states no reuse terms.

`.gitignore` excludes `corpus/us/food-code/` and `corpus/br/rdc-216-2004/`. Anything else under `corpus/` is committed by default, so check a new source's size and reuse terms before adding it.

## Open points

- Anvisa's thematic library of food norms (Biblioteca de Alimentos), which lists the acts in force, returns 404 on gov.br. The status above therefore rests on AnvisaLegis and on Anvisa's 2025 news item about the revision, not on that list. Public consultations CP 1.362 to 1.368 of 2025 would revise good practice requirements for the food production chain ([Anvisa news, 2026-01-16](https://www.gov.br/anvisa/pt-br/assuntos/noticias-anvisa/2026/anvisa-promove-dialogos-setoriais-para-discutir-revisao-das-boas-praticas-na-producao-de-alimentos)). Whether the final text also affects RDC 216/2004 is unconfirmed.
- The authentic Brazilian texts are the DOU editions of 2004-09-16 and 2014-10-01 on the Imprensa Nacional site (`pesquisa.in.gov.br`). Its PDF links carry a captcha parameter, and the site closed the connection when tested, so the script does not download them and the corpus uses the AnvisaLegis compiled text.
- The AnvisaLegis file is a web page, so a change to the site's layout changes its hash even when the act stays the same. In that case the script fails on purpose. Compare the act text, then run `--update-manifest`.
- The third-party table in the FDA Food Code (Annex 4, Table 3) stays out of phase 1: no annex is parsed (see "What is parsed"). It needs a decision again if the annexes are ever ingested.
- FDA gives no single effective date for the model code, because each jurisdiction adopts it on its own schedule. The manifest records the release date instead.
