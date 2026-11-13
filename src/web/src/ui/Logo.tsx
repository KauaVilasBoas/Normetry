import { useId } from "react";

// Placeholder until a traced SVG of the reference logo exists (spec #53, D3): replace this file.
const mark = "M4 28V4h6l12 15.5V4h6v24h-6L10 12.5V28z";

function MarkPath() {
  const gradient = useId();
  return (
    <>
      <defs>
        <linearGradient id={gradient} x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" style={{ stopColor: "var(--mint)" }} />
          <stop offset="1" style={{ stopColor: "var(--teal)" }} />
        </linearGradient>
      </defs>
      <path fill={`url(#${gradient})`} d={mark} />
    </>
  );
}

export function Logo({ className }: { className: string }) {
  return (
    <svg viewBox="0 0 132 32" aria-hidden="true" className={`w-auto ${className}`}>
      <MarkPath />
      <text x="36" y="23" fontSize="20" fontWeight="600" className="fill-text-primary">
        Normetry
      </text>
    </svg>
  );
}

/** The N mark alone, for compact places such as the assistant's chat avatar. */
export function LogoMark({ className }: { className: string }) {
  return (
    <svg viewBox="0 0 32 32" aria-hidden="true" className={className}>
      <MarkPath />
    </svg>
  );
}
