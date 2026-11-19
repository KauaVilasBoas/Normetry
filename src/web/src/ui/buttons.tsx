import type { ComponentProps } from "react";

const buttonVariants = {
  primary: "bg-mint text-on-mint font-semibold hover:bg-mint-hover",
  secondary: "border border-teal-border text-text-primary font-medium",
};

const buttonBase =
  "inline-flex h-9 items-center justify-center gap-2 rounded-control px-4 text-body";

/** A secondary button's classes, for a link that looks like one ("Back to profiles"). */
export const secondaryButton = `${buttonBase} ${buttonVariants.secondary} hover:bg-elevated`;

/**
 * DESIGN.md Text action: a secondary inline action shown as a link ("Edit expiry date", "Open
 * document"), never the view's primary. 24px tall, a reachable target beside text (WCAG 2.5.8).
 */
export const textAction =
  "inline-flex h-6 items-center rounded-sm text-small font-medium text-mint hover:underline";

/**
 * DESIGN.md buttons: one primary per view; text on mint is always --on-mint. A secondary button
 * hovers to --bg-elevated, or to --bg-inset when it sits on --bg-elevated (`onElevated`).
 */
export function Button({
  variant,
  onElevated = false,
  className = "",
  ...props
}: ComponentProps<"button"> & {
  variant: keyof typeof buttonVariants;
  onElevated?: boolean;
}) {
  const hover = variant === "primary" ? "" : onElevated ? "hover:bg-inset" : "hover:bg-elevated";
  return (
    <button
      type="button"
      className={`${buttonBase} ${buttonVariants[variant]} ${hover} ${className}`}
      {...props}
    />
  );
}

/** 36px icon-only button (give it an aria-label); hovers to --bg-elevated. */
export const iconButton =
  "inline-flex size-9 shrink-0 items-center justify-center rounded-control text-text-secondary hover:bg-elevated";
