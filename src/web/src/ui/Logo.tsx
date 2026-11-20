import { useId } from "react";

// The Normetry logo (docs/design/brand/README.md), drawn on a 100-unit grid. The app shows the logo
// at 28-40px, so it uses the compact mark: three text lines instead of five, and a wider fold gap.
const stems = "M12 10H34V90H12ZM66 10H88V90H66Z";
const diagonal = "M12 10H37L88 90H63Z";
const textLines = [50, 66, 82];
const foldCorner = "M77 9H89V21Z";
const foldGap = "M77 9V21H89";
const foldFlap = "M79.2 15.31V18.8H82.69Z";

function MarkPath() {
  const id = useId();
  const gradient = `${id}-gradient`;
  const mask = `${id}-mask`;
  return (
    <>
      <defs>
        <linearGradient
          id={gradient}
          x1="12"
          y1="10"
          x2="88"
          y2="90"
          gradientUnits="userSpaceOnUse"
        >
          <stop offset="0" style={{ stopColor: "var(--logo-mint-light)" }} />
          <stop offset="0.5" style={{ stopColor: "var(--mint)" }} />
          <stop offset="1" style={{ stopColor: "var(--teal)" }} />
        </linearGradient>
        <mask id={mask} maskUnits="userSpaceOnUse" x="-2" y="-2" width="104" height="104">
          <rect x="-2" y="-2" width="104" height="104" fill="white" />
          {textLines.map((y) => (
            <rect key={y} x="11" y={y - 2.4} width="15" height="4.8" fill="black" />
          ))}
          <path d={foldCorner} fill="black" />
          <path d={foldGap} fill="none" stroke="black" strokeWidth="4.4" />
        </mask>
      </defs>
      <g mask={`url(#${mask})`} fill={`url(#${gradient})`}>
        <path d={stems} />
        <path d={diagonal} />
      </g>
      <path d={foldFlap} style={{ fill: "var(--logo-fold)" }} />
    </>
  );
}

export function Logo({ className }: { className: string }) {
  return (
    <svg viewBox="0 0 382 100" aria-hidden="true" className={`w-auto ${className}`}>
      <MarkPath />
      <text
        x="102"
        y="72.4"
        fontSize="61.6"
        fontWeight="600"
        letterSpacing="-0.74"
        className="fill-text-primary"
      >
        Normetry
      </text>
    </svg>
  );
}

/** The N mark alone, for compact places such as the assistant's chat avatar. */
export function LogoMark({ className }: { className: string }) {
  return (
    <svg viewBox="0 0 100 100" aria-hidden="true" className={className}>
      <MarkPath />
    </svg>
  );
}
