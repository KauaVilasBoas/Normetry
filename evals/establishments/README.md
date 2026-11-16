# Demo establishment documents

Placeholder documents for the three demo establishments of `deploy/keycloak/README.md`. In
Development, the API uploads them once it has started, so a fresh run of the AppHost shows
documents that go through the whole pipeline: upload, blob, outbox, AI ingestion, then `Indexed`.

| Folder | Establishment | Tenant | Language | Files |
|---|---|---|---|---|
| `bistro-sao-paulo/` | Bistro São Paulo (BR) | Demo Bistro | Portuguese (Brazil) | PDF, TXT, CSV |
| `bistro-lisboa/` | Bistro Lisboa (EU) | Demo Bistro | Portuguese (Portugal) | DOCX, TXT, CSV (`;` delimiter, decimal commas) |
| `bakery-austin/` | Bakery Austin (US) | Demo Bakery | English | PDF, DOCX, TXT, CSV |

They are short and plainly fake: every document says it describes no real business, and the
only names in them are the demo tenants and demo users. Design 2 replaces them with synthetic
documents written for the evals.

## Layout

Each `<establishment>/documents/` folder holds the files and a `manifest.json`:

```json
{
  "establishment": "Bistro São Paulo",
  "documents": [
    { "title": "Manual de Boas Práticas", "type": "Sop", "file": "manual-de-boas-praticas.pdf" }
  ]
}
```

- `establishment` is the demo establishment's exact name (`DemoTenantSeed` in `src/core`).
- `type` is a document type by name: `Sop`, `License`, `Log`, `Report` or `Other`.
- `file` is relative to the manifest's folder.

`.gitattributes` keeps the files byte for byte (`-text`), so their SHA-256 is the same on every
checkout.

## The seed

`DemoDocumentSeed` (`src/core/Normetry.Infrastructure/Persistence`) runs only in Development. For
each manifest it uploads every listed file through the upload commands the API endpoints use, as
the establishment's tenant with the tenant's demo Owner as uploader. It matches a document by
title and skips it when its current or newest version already has the file's SHA-256; a changed
file becomes a new version. A demo document deleted in the app comes back at the next start.

The PDFs are one page each with a real text layer, and the DOCX files use Word heading styles,
which the AI service turns into section paths.
