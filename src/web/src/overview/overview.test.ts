import { describe, expect, it } from "vitest";
import { percent } from "../copy";
import { delta, plotted, rowThreeSpans, share } from "./overview";

const point = (compliance: number | null, id = "i") => ({
  inspectionId: id,
  completedAt: "2026-11-16T16:05:00Z",
  compliance,
});

describe("overview numbers", () => {
  it("rounds down, from integers or from a ratio", () => {
    expect(share(29, 100)).toBe(29);
    expect(share(33, 43)).toBe(76);
    expect(share(0, 0)).toBe(0);
    expect(percent(0.29)).toBe("29%");
    expect(percent(0.999)).toBe("99%");
    expect(percent(1)).toBe("100%");
  });

  it("compares the latest result with the nearest earlier one", () => {
    expect(delta([point(0.5), point(null), point(0.58)])).toBe(8);
    expect(delta([point(0.68), point(0.6)])).toBe(-8);
    expect(delta([point(0.29), point(0.29)])).toBe(0);
    expect(delta([point(null), point(0.6)])).toBeNull();
    expect(delta([point(0.6), point(null)])).toBeUndefined();
    expect(delta([])).toBeUndefined();
  });

  it("plots only the points with a result", () => {
    expect(plotted([point(0.5, "a"), point(null, "b"), point(0.7, "c")]).map((p) => p.id)).toEqual([
      "a",
      "c",
    ]);
  });

  it("spans row 3 by the cards present", () => {
    expect(rowThreeSpans(["trend", "documents", "risk"])).toMatchObject({
      trend: "md:col-span-2 xl:col-span-5",
      documents: "xl:col-span-3",
      risk: "xl:col-span-4",
    });
    expect(rowThreeSpans(["trend", "risk"])).toMatchObject({
      trend: "md:col-span-2 xl:col-span-7",
      risk: "md:col-span-2 xl:col-span-5",
    });
    expect(rowThreeSpans(["documents"]).documents).toBe("md:col-span-2 xl:col-span-12");
  });
});
