# Keycloak sign-in

The sign-in page uses the auth canvas and the card, input, primary-action and logo
patterns from `reference.png` and `DESIGN.md`. It is served by Keycloak, before the
Vite app receives an authorization code.

## Layout

At 1024px and above, a centered frame has a brand introduction on the left and one
form card on the right. The introduction uses the tagline as its heading, two
short descriptions of cited answers and establishment context, and the three
supported regulations under a sentence-case label. It contains no invented
compliance scores or customer data. The form column comes first in the DOM, so
the page heading is read before the introduction; the grid still places the form
on the right.

Below 1024px the introduction is hidden, and the wordmark and tagline sit above
the form. The form fits a 320px viewport and remains scrollable with an onscreen
keyboard or at 200% zoom.

On the login page, when the realm does not allow registration, a note under the
card explains how to request access from an organization administrator. The
footer shows "Normetry" (from 1024px) and a one-line description. There are no
links to unimplemented product pages.

## Components and tokens

- Canvas: `--bg-canvas`, with the existing `--canvas-glow` radial treatment.
- Wordmark: the SPA's `Logo` (`src/web/src/ui/Logo.tsx`) at its 40px auth-screen
  height, drawn with the same SVG geometry, with -1.2% letter-spacing (-0.74 at
  font-size 61.6) as in `brand/README.md`.
- Tagline: "Understand Regulations. Stay Ahead." (DESIGN.md §1 casing) in both
  layouts. Intro heading: `--text-auth-hero` (40/48, 600, no letter-spacing);
  compact tagline: 13px, `--text-secondary`.
- Form card: `--bg-card`, `--border`, 12px radius, 20px padding. The log-in icon
  tile (40px, `--bg-inset`, `--radius-sm`) appears on the login page only. Form
  heading: 22/28, 600.
- Line heights follow the SPA's tokens: body 13/20, labels and small text 12/16,
  captions 11/16, fields below 640px 16/24.
- Body: 13px, `--text-secondary`; labels: 12px, `--text-primary`.
- Inputs: `--bg-inset`, 1px `--teal-border`, 10px radius, at least 44px high.
  Field text is 16px below 640px and 13px above it. The password toggle sits
  inside the field border.
- One mint primary button (`.pf-m-primary`), with dark `--on-mint` text, at least
  44px high. Any other submit on an inherited page is a secondary button. Hover
  does not change disabled buttons.
- Authentication messages use `--bg-inset` and 13px/500 `--text-primary` text.
  Errors add a 16px Lucide `circle-alert` icon in `--text-secondary`. Compliance
  status colors are not used for authentication errors.
- Links inside instruction text are underlined. Paragraphs on info pages are
  12px apart.
- Lucide outline icons use 1.5px strokes and are bundled locally.
- English is the default, matching the SPA. Custom messages have a Portuguese
  translation ready for the realm's future language switch.

## Behavior and accessibility

The theme inherits Keycloak's form templates, action URLs, field names, credential
selection, password visibility, passkey support and session-checking scripts.
Recovery, remember-me, registration and identity-provider controls appear only
when enabled by the realm; this change does not enable them.

Native labels, autocomplete and keyboard submission remain available. Every
interactive element has a 2px mint focus ring with a 2px offset (drawn inside the button on the password toggle). Controls,
locale menu items and checkbox labels are at least 44px tall. There is no skip
link: the username field takes autofocus.

Credential errors are associated with the invalid fields, and the first invalid
field is focused again once linked so the error is announced. Submission marks
the form busy, updates the button label and announces "Signing in…" in a visually
hidden status region. The locale button keeps its visible language name in its
accessible name, after a visually hidden "Language:". Decorative icons are hidden
from assistive technology. Motion is optional and disabled under
`prefers-reduced-motion`.

Review the rendered login, invalid credentials, a successful authorization-code
redirect, generic error and expired-session pages on desktop and a narrow screen.
