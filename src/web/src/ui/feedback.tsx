import { CircleAlert, WifiOff } from "lucide-react";
import type { ComponentProps, ReactNode } from "react";
import { offersReconnect, type Live } from "../api/live";
import type { Announcement } from "./announcer";
import { Button } from "./buttons";

// Track and arc per surface: an --bg-elevated track vanishes inside an elevated surface, and a
// mint arc on a mint button.
const spinnerSurfaces = {
  card: "border-elevated border-t-mint",
  elevated: "border-inset border-t-mint",
  mint: "border-mint-hover border-t-on-mint",
};

/**
 * Indeterminate ring, 16px (12px when `small`, inside a pill); static under reduced motion.
 * `surface`: what it sits on.
 */
export function Spinner({
  surface = "card",
  small = false,
}: {
  surface?: keyof typeof spinnerSurfaces;
  small?: boolean;
}) {
  return (
    <span
      aria-hidden="true"
      className={`inline-block ${small ? "size-3" : "size-4"} shrink-0 rounded-pill border-2 ${spinnerSurfaces[surface]} motion-safe:animate-spin`}
    />
  );
}

/** A note inside a card (DESIGN.md Inline notice): an icon, a title, its lines, an optional action. */
export function Notice({
  icon,
  title,
  action,
  children,
  className = "",
  ...props
}: Omit<ComponentProps<"div">, "title"> & { icon: ReactNode; title: string; action?: ReactNode }) {
  return (
    <div
      className={`flex flex-wrap items-center gap-x-3 gap-y-2 rounded-sm bg-inset p-3 ${className}`}
      {...props}
    >
      <div className="flex min-w-0 flex-1 gap-2">
        <span className="mt-0.5 flex shrink-0 text-text-secondary">{icon}</span>
        <div className="min-w-0">
          <p className="text-body font-semibold">{title}</p>
          {children && <div className="text-small text-text-secondary">{children}</div>}
        </div>
      </div>
      {action}
    </div>
  );
}

/**
 * Under a card's header: a status region, always there so screen readers hear what arrives in it,
 * that holds the notice while live updates are paused or stopped. `labels`: the screen's copy.
 * `className` places the notice (inside a card by default).
 */
export function ConnectionNotice({
  live,
  labels,
  className = "mx-5 mb-4",
}: {
  live: Live;
  labels: { reconnecting: string; stopped: string; reconnect: string };
  className?: string;
}) {
  const reconnecting = live.state === "reconnecting";
  const retrying = live.state === "retrying";
  return (
    <div role="status">
      {live.state !== "live" && (
        <Notice
          className={className}
          icon={reconnecting ? <Spinner /> : <WifiOff aria-hidden="true" className="size-4" />}
          title={reconnecting ? labels.reconnecting : labels.stopped}
          action={
            offersReconnect(live.state) && (
              // aria-disabled, not disabled, while it reconnects: focus stays on the button.
              <Button
                variant="secondary"
                aria-disabled={retrying}
                onClick={retrying ? undefined : live.reconnect}
              >
                {retrying && <Spinner />}
                {labels.reconnect}
              </Button>
            )
          }
        />
      )}
    </div>
  );
}

/** A screen's polite live region (`useAnnouncer`): screen readers hear each line as it arrives. */
export function PoliteRegion({ announcements }: { announcements: Announcement[] }) {
  return (
    <div aria-live="polite" className="sr-only">
      {announcements.map((item) => (
        <p key={item.id} lang={item.lang}>
          {item.text}
        </p>
      ))}
    </div>
  );
}

/** Loading placeholder bar; pulses gently unless reduced motion is on. */
export function Skeleton({ className }: { className: string }) {
  return (
    <span
      aria-hidden="true"
      className={`block rounded-sm bg-elevated motion-safe:animate-pulse ${className}`}
    />
  );
}

/** Empty and error block: optional icon, title and body. `heading`: h2 when no card title is above. */
export function Message({
  icon,
  title,
  body,
  heading: Heading = "h3",
  className = "mt-4 items-start",
}: {
  icon?: ReactNode;
  title: string;
  body: string;
  heading?: "h2" | "h3";
  className?: string;
}) {
  return (
    <div className={`flex flex-col gap-2 ${className}`}>
      {icon}
      <Heading className="text-title font-semibold">{title}</Heading>
      <p className="text-body text-text-secondary">{body}</p>
    </div>
  );
}

/**
 * A load failure: the message, then Try again (`aria-disabled` with a spinner while it retries, so
 * focus stays on it). `className` places it (16px below what is above, by default).
 */
export function ErrorMessage({
  title,
  body,
  retryLabel,
  retrying,
  onRetry,
  className = "mt-4",
}: {
  title: string;
  body: string;
  retryLabel: string;
  retrying: boolean;
  onRetry: () => void;
  className?: string;
}) {
  return (
    <div role="alert" className={className}>
      <Message
        className="items-start"
        icon={<CircleAlert aria-hidden="true" className="size-5 text-text-secondary" />}
        title={title}
        body={body}
      />
      <Button
        variant="secondary"
        className="mt-4"
        aria-disabled={retrying}
        onClick={retrying ? undefined : onRetry}
      >
        {retrying && <Spinner />}
        {retryLabel}
      </Button>
    </div>
  );
}
