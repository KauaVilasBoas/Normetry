# Keycloak sign-in

The sign-in page uses the auth canvas and the card, input, primary-action and logo
patterns from `reference.png` and `DESIGN.md`. It is served by Keycloak, before the
Vite app receives an authorization code.

## Layout

At 1024px and above, a centered frame has a brand introduction on the left and one
form card on the right. The introduction uses the existing N mark, the tagline,
two short descriptions of cited answers and establishment context, and the three
supported regulations. It contains no invented compliance scores or customer data.

Below 1024px, the introduction is reduced to the wordmark and tagline above the
form. The form fits a 320px viewport and remains scrollable with an onscreen
keyboard or at 200% zoom. The footer explains how to request access from an
organization administrator. There are no links to unimplemented product pages.

## Components and tokens

- Canvas: `--bg-canvas`, with the existing `--canvas-glow` radial treatment.
- Form card: `--bg-card`, `--border`, 12px radius, 20px padding.
- Intro heading: `--text-auth-hero`; form heading: 22px/600.
- Body: 13px, `--text-secondary`; labels: 12px, `--text-primary`.
- Inputs: `--bg-inset`, `--teal-border`, 10px radius, at least 44px high.
  Field text is 16px below 640px and 13px above it.
- One mint primary button, with dark `--on-mint` text, at least 44px high.
- Neutral inline authentication messages use `--bg-inset` and readable text.
  Compliance status colors are not used for authentication errors.
- Lucide outline icons use 1.5px strokes. The logo matches the SPA's current mark.
- English is the default, matching the SPA. Custom messages have a Portuguese
  translation ready for the realm's future language switch.

## Behavior and accessibility

The theme inherits Keycloak's form templates, action URLs, field names, credential
selection, password visibility, passkey support and session-checking scripts.
Recovery, remember-me, registration and identity-provider controls appear only
when enabled by the realm; this change does not enable them.

Native labels, autocomplete and keyboard submission remain available. Inputs have
visible mint focus rings. Credential errors are announced and associated with the
invalid fields. Submission marks the form busy and updates the button label.
Decorative icons are hidden from assistive technology. Motion is optional and
disabled under `prefers-reduced-motion`.

Review the rendered login, invalid credentials, a successful authorization-code
redirect, generic error and expired-session pages on desktop and a narrow screen.
