import { describe, expect, it } from "vitest";
import { allPermissions } from "../test/fakes";
import { countLabel, groupPermissions, sortProfiles, togglePermission } from "./profiles";

const profile = (name: string, systemKey: "owner" | "manager" | "auditor" | null = null) => ({
  id: name,
  name,
  systemKey,
});

describe("sortProfiles", () => {
  it("puts Owner, Manager and Auditor first by system key, then custom profiles A–Z", () => {
    const sorted = sortProfiles([
      profile("Zeladoria"),
      profile("Auditoria", "auditor"),
      profile("Kitchen lead"),
      profile("Dono", "owner"),
      profile("Gerente", "manager"),
    ]);

    expect(sorted.map((item) => item.name)).toEqual([
      "Dono",
      "Gerente",
      "Auditoria",
      "Kitchen lead",
      "Zeladoria",
    ]);
  });
});

describe("countLabel", () => {
  it.each([
    [true, 8, "All permissions"],
    [false, 0, "No permissions"],
    [false, 1, "1 permission"],
    [false, 7, "7 permissions"],
  ])("grantsAll %s, %i permissions: %s", (grantsAll, permissionCount, label) => {
    expect(countLabel({ ...profile("x"), grantsAll, permissionCount })).toBe(label);
  });
});

describe("groupPermissions", () => {
  it("groups by area in the editor's order, with unknown names as sent at the end", () => {
    const groups = groupPermissions([
      "reports.read",
      ...[...allPermissions].reverse(),
      "documents.share",
    ]);

    expect(groups.map((group) => [group.legend, group.names])).toEqual([
      ["Establishments", ["establishments.read", "establishments.manage"]],
      ["Documents", ["documents.read", "documents.upload", "documents.delete", "documents.share"]],
      ["Simulated Inspection", ["inspections.read", "inspections.run", "inspections.review"]],
      ["Ask the Regulation", ["chat.ask"]],
      ["Profiles", ["profiles.read", "profiles.manage"]],
      ["reports", ["reports.read"]],
    ]);
  });
});

describe("togglePermission", () => {
  it("grants the area's read with any other permission of it", () => {
    expect(togglePermission([], "documents.upload", true, allPermissions)).toEqual([
      "documents.upload",
      "documents.read",
    ]);
  });

  it("adds no read the catalog does not have", () => {
    expect(togglePermission([], "chat.ask", true, allPermissions)).toEqual(["chat.ask"]);
  });

  it("takes the rest of the area away with its read", () => {
    const granted = ["documents.read", "documents.upload", "documents.delete", "chat.ask"];

    expect(togglePermission(granted, "documents.read", false, allPermissions)).toEqual([
      "chat.ask",
    ]);
    expect(togglePermission(granted, "documents.upload", false, allPermissions)).toEqual([
      "documents.read",
      "documents.delete",
      "chat.ask",
    ]);
  });
});
