import { copy } from "../copy";
import { Logo } from "../ui/Logo";
import { Button, Spinner } from "../ui/primitives";
import type { AuthStatus } from "./session";

type ScreenStatus = Exclude<AuthStatus, "authenticated">;

const statusRows: Partial<Record<ScreenStatus, string>> = {
  checking: copy.auth.checking,
  redirecting: copy.auth.redirecting,
  expired: copy.auth.expired,
  "signing-out": copy.auth.signingOut,
};

const actionCards = {
  failed: { title: copy.auth.failedTitle, body: copy.auth.failedBody, action: copy.auth.tryAgain },
  unreachable: {
    title: copy.auth.unreachableTitle,
    body: copy.auth.unreachableBody,
    action: copy.auth.tryAgain,
  },
  "signed-out": {
    title: copy.auth.signedOutTitle,
    body: copy.auth.signedOutBody,
    action: copy.auth.signIn,
  },
};

/** Every screen shown outside the app shell: progress while signing in or out, and failures. */
export function AuthScreen({ status, onAction }: { status: ScreenStatus; onAction: () => void }) {
  const row = statusRows[status];
  const card = row === undefined ? actionCards[status as keyof typeof actionCards] : undefined;
  const signedOut = status === "signed-out" || status === "signing-out";

  return (
    <main className="flex min-h-dvh flex-col items-center justify-center gap-5 bg-canvas-glow px-4 py-10">
      <title>{signedOut ? copy.titles.signedOut : copy.titles.signingIn}</title>
      <div className="flex flex-col items-center gap-3">
        <Logo className="h-10" />
        <p className="text-body text-text-secondary">{copy.tagline}</p>
      </div>
      {card ? (
        <section
          aria-labelledby="auth-title"
          className="w-full max-w-90 rounded-card border border-border bg-card p-5 motion-safe:animate-fade-in"
        >
          <h1 id="auth-title" className="text-title font-semibold">
            {card.title}
          </h1>
          <p className="mt-2 text-body text-text-secondary">{card.body}</p>
          <Button variant="primary" className="mt-5 w-full" onClick={onAction}>
            {card.action}
          </Button>
        </section>
      ) : (
        <div role="status" className="flex items-center gap-2">
          <Spinner />
          <h1 className="text-body font-normal text-text-secondary">{row}</h1>
        </div>
      )}
    </main>
  );
}
