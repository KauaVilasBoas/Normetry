import { useId } from "react";

// DESIGN.md Donut and Area chart, as inline SVG: no chart library until one needs interaction.

const DONUT_RADIUS = 63;
const DONUT_LENGTH = 2 * Math.PI * DONUT_RADIUS;
const DONUT_GAP = 2;

/**
 * A 140px ring from 12 o'clock, one arc per non-zero segment (`className` gives its stroke), 2
 * units apart when there are several; `value` and `caption` in the middle. Decorative: the caller
 * gives a legend list as its text equivalent.
 */
export function DonutChart({
  segments,
  value,
  caption,
}: {
  segments: { value: number; className: string }[];
  value: string;
  caption: string;
}) {
  const shown = segments.filter((segment) => segment.value > 0);
  const total = shown.reduce((sum, segment) => sum + segment.value, 0);
  const gap = shown.length > 1 ? DONUT_GAP : 0;
  const arcs = [];
  let start = 0;
  for (const segment of shown) {
    const length = (segment.value / total) * DONUT_LENGTH;
    // An arc shorter than the gap still shows, at 1 unit.
    arcs.push({ ...segment, start, drawn: gap > 0 && length <= gap ? 1 : length - gap });
    start += length;
  }
  return (
    <div aria-hidden="true" className="relative size-35 shrink-0">
      <svg viewBox="0 0 140 140" className="size-full -rotate-90">
        {arcs.map((arc) => (
          <circle
            key={arc.className}
            cx="70"
            cy="70"
            r={DONUT_RADIUS}
            strokeWidth="14"
            strokeDasharray={`${arc.drawn} ${DONUT_LENGTH}`}
            strokeDashoffset={-arc.start}
            className={`fill-none ${arc.className}`}
          />
        ))}
      </svg>
      <span className="absolute inset-0 flex flex-col items-center justify-center">
        <span className="text-value font-bold tabular-nums">{value}</span>
        <span className="text-small text-text-secondary">{caption}</span>
      </span>
    </div>
  );
}

// The 0–100% axis, top to bottom; fixed positions, so classes.
const yTicks = [
  { label: "100%", className: "top-0" },
  { label: "75%", className: "top-1/4" },
  { label: "50%", className: "top-1/2" },
  { label: "25%", className: "top-3/4" },
  { label: "0%", className: "top-full" },
];

/**
 * A single series on a fixed 0–100% axis (`ratio` 0 to 1), its points evenly spaced in order and
 * labelled with `label` under the plot: the first and the last always, the others from 768px when
 * there are six or fewer. The plot is a `role="img"` named by `summary`; each dot's `title` is a
 * mouse convenience, so the caller gives the values as a table too.
 */
export function AreaChart({
  points,
  summary,
}: {
  points: { id: string; label: string; ratio: number; title: string }[];
  summary: string;
}) {
  const gradient = useId();
  const last = points.length - 1;
  const placed = points.map((point, index) => ({
    ...point,
    x: last > 0 ? (index / last) * 100 : 50,
    y: (1 - Math.min(Math.max(point.ratio, 0), 1)) * 100,
  }));
  const line = placed.map(({ x, y }) => `${x},${y}`).join(" ");
  return (
    <div role="img" aria-label={summary}>
      <div className="flex h-40">
        <div className="relative w-9 shrink-0">
          {yTicks.map((tick) => (
            <span
              key={tick.label}
              className={`absolute right-2 -translate-y-1/2 text-caption text-text-secondary tabular-nums ${tick.className}`}
            >
              {tick.label}
            </span>
          ))}
        </div>
        <div className="relative flex-1">
          <svg
            viewBox="0 0 100 100"
            preserveAspectRatio="none"
            className="absolute inset-0 size-full overflow-visible"
          >
            <defs>
              <linearGradient id={gradient} x1="0" y1="0" x2="0" y2="1">
                <stop offset="0" stopColor="var(--chart-1)" stopOpacity={0.4} />
                <stop offset="1" stopColor="var(--chart-1)" stopOpacity={0} />
              </linearGradient>
            </defs>
            <polygon
              points={`${placed[0]?.x ?? 0},100 ${line} ${placed[last]?.x ?? 100},100`}
              fill={`url(#${gradient})`}
            />
            <polyline
              points={line}
              strokeWidth={2}
              vectorEffect="non-scaling-stroke"
              className="fill-none stroke-chart-1"
            />
          </svg>
          {/* HTML, so the stretched SVG can't turn the dots into ovals. */}
          {placed.map((point) => (
            <span
              key={point.id}
              title={point.title}
              className="absolute size-2 -translate-1/2 rounded-pill bg-chart-1"
              style={{ left: `${point.x}%`, top: `${point.y}%` }}
            />
          ))}
        </div>
      </div>
      <div className="relative mt-2 ml-9 h-4 text-caption whitespace-nowrap text-text-secondary tabular-nums">
        {placed.map((point, index) =>
          index === 0 ? (
            <span key={point.id} className="absolute left-0">
              {point.label}
            </span>
          ) : index === last ? (
            <span key={point.id} className="absolute right-0">
              {point.label}
            </span>
          ) : (
            points.length <= 6 && (
              <span
                key={point.id}
                className="absolute hidden -translate-x-1/2 md:block"
                style={{ left: `${point.x}%` }}
              >
                {point.label}
              </span>
            )
          ),
        )}
      </div>
    </div>
  );
}
