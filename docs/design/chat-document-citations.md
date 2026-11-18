# Spec: Document citations in the chat (#108)

Ask the Regulation also cites the establishment's own documents (#107, `src/shared/contracts/chat-api.md`). A document citation is evidence about the establishment and never a rule, so users must be able to tell it from a regulation citation by sight and with a screen reader. Everything below adds to DESIGN.md §5 (Citation chip, Source panel, Quote highlight); what is not mentioned stays as it is there.

## 1. Citation chip

The regulation chip does not change: "View source · {section}" and a 14px arrow.

A document chip uses the same button: 28px tall, `--radius-sm`, 1px `--teal-border`, mint 12px/500 text, hover `--bg-inset`, and the same open state (`--teal-deep` fill, mint border). Two things set it apart:

- A leading 14px Lucide `FileText` in mint, before the label.
- The label names the document instead of "View source": "{title} · {section}" ("Cold holding SOP · Page 1"). A document with no title (uploaded before titles reached the AI service) reads "Document · {section}". Without a section (a TXT file, or DOCX text before its first heading), the label is the title alone.

```
 [ View source · Annex II, Chapter IX → ]  [ ▤ Cold holding SOP · Page 1 → ]
```

- Accessible name: "{label} (this establishment's document), view source", for example "Cold holding SOP · Page 1 (this establishment's document), view source". It starts with the visible label (WCAG 2.5.3), so speech input that says the visible text matches.
- The title and the section are two spans: the title truncates first, and the section keeps up to half the chip before it truncates too. The icon and the arrow do not shrink.
- Chips keep the order of the claim's citations. Regulation and document chips can sit side by side under the same claim.

## 2. Source panel

The frame does not change: the inline card from 1280px, the sheet below that, the "Source" heading and Close, focus on the heading, and the quote scrolled to the center. Only the body and the footer change for a document.

Body (top to bottom):

1. 12px `--text-secondary`: "Establishment document · {type}" ("Establishment document · SOP"), with the type labels the Documents table uses. Without a type: "Establishment document".
2. 15px/600 white: the document title, wrapping. Without a title: "Untitled document".
3. 11px `--text-secondary`, tabular: "{section} · Version {n} · {language}" ("Page 1 · Version 3 · English"). Without a section: "Version {n} · {language}". The version is the one that was cited, which may not be the current one.
4. The stored passage in its original language on `--bg-inset`, with the quote highlighted (DESIGN.md Quote highlight, `QuotedPassage`). When the quote cannot be located in the passage, the quote shows apart, as it does for the regulation.

Footer:

- "Open document": a mint 13px/500 in-app link with a leading 16px `FileText`, to `/documents/{documentId}` (the document page, its heading focused on arrival). It opens in the same tab, so it has no external-link icon and no "(opens in a new tab)". It shows only when the user has `documents.read`; without it the footer has only the caption.
- 11px `--text-secondary` caption: "This is the copy stored with the answer. The document may have changed since."

```
 ┌ Source                                      ✕ ┐
 │ Establishment document · SOP                   │
 │ Cold holding SOP                               │
 │ Page 1 · Version 3 · English                   │
 │ ┌────────────────────────────────────────────┐ │
 │ │ Walk-in cooler: the display ▓must read 4°C │ │
 │ │ (39°F) or less▓ at every check.            │ │
 │ └────────────────────────────────────────────┘ │
 ├────────────────────────────────────────────────┤
 │ ▤ Open document                                │
 │ This is the copy stored with the answer. The   │
 │ document may have changed since.               │
 └────────────────────────────────────────────────┘
```

A deleted document, or one replaced by a new version, still opens its stored passage. The link then lands on the document page's own "not available" state or on its newer version.

## 3. Rate limit in the composer

A `429` with the problem code `rate_limited` (the core's per-user rate limit, a wait of seconds) is not the daily quota (`chat.quota_exceeded`). It uses the Inline notice above the field, in the same place as the quota notice: a 16px `Clock`, the title "Too many requests right now." and the line "Try again {when}." The time comes from `Retry-After` and reads like the quota's (with seconds under a minute, "in 3 seconds", rounded up; it does not count down). The question goes back into the field, Send is disabled and described by the notice, the quota counter does not change, and the notice clears when the wait ends. Both lines are also announced in the polite region.

## 4. Copy (EN; PT with the language switch)

| Key | Text |
|---|---|
| `chat.documentChip` | {title} · {section} (the accessible name's label; the title alone without a section) |
| `chat.documentChipSection` | · {section} (shown beside the title) |
| `chat.untitledDocumentChip` | Document |
| `chat.documentChipName` | {label} (this establishment's document), view source |
| `source.documentKind` / `documentKindType` | Establishment document / Establishment document · {type} |
| `source.untitledDocument` | Untitled document |
| `source.documentMeta` | {section} · Version {version} · {language} (`source.meta` without a section) |
| `source.openDocument` | Open document |
| `source.documentStoredCopy` | This is the copy stored with the answer. The document may have changed since. |
| `chat.rateLimitedTitle` / `rateLimitedBody` | Too many requests right now. / Try again {when}. |
