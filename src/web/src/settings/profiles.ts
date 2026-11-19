import type { ProfileListItem, ProfileSummary } from "../api/queries";
import { copy, format, locale } from "../copy";

// Matched by system key, never by name, so the names can be translated.
const systemOrder = ["owner", "manager", "auditor"];
const systemRank = ({ systemKey }: ProfileSummary) => {
  const rank = systemOrder.indexOf(systemKey ?? "");
  return rank < 0 ? systemOrder.length : rank;
};

/** Owner, Manager, Auditor, then custom profiles A–Z. */
export function sortProfiles<T extends ProfileSummary>(profiles: T[]): T[] {
  return [...profiles].sort(
    (a, b) => systemRank(a) - systemRank(b) || a.name.localeCompare(b.name, locale),
  );
}

/** "All permissions", "No permissions", "1 permission", "{count} permissions". */
export function countLabel({ grantsAll, permissionCount }: ProfileListItem): string {
  if (grantsAll) return copy.profiles.allPermissions;
  if (permissionCount === 0) return copy.profiles.noPermissions;
  if (permissionCount === 1) return copy.profiles.onePermission;
  return format(copy.profiles.permissions, { count: String(permissionCount) });
}

const areaOf = (name: string) => name.split(".")[0];
const known = Object.keys(copy.permissions);
const rank = (name: string) => {
  const index = known.indexOf(name);
  return index < 0 ? known.length : index;
};

/**
 * The catalog by area, for the editor: known areas and permissions in the copy's order, then
 * unknown ones as sent (an unknown permission joins its area's group, an unknown area comes last).
 */
export function groupPermissions(catalog: string[]) {
  const areas = new Map<string, string[]>();
  for (const name of [...catalog].sort((a, b) => rank(a) - rank(b))) {
    const area = areaOf(name);
    areas.set(area, [...(areas.get(area) ?? []), name]);
  }
  return [...areas].map(([area, names]) => ({ area, legend: copy.areas[area] ?? area, names }));
}

/**
 * Grants or takes one permission (spec decision 3, which the server applies too): granting any
 * permission of an area grants its `.read`, if the catalog has one; taking `.read` away takes the
 * rest of the area.
 */
export function togglePermission(
  granted: string[],
  name: string,
  on: boolean,
  catalog: string[],
): string[] {
  const area = areaOf(name);
  const read = `${area}.read`;
  if (on) {
    const added = name !== read && catalog.includes(read) ? [name, read] : [name];
    return [...new Set([...granted, ...added])];
  }
  return granted.filter((item) => item !== name && !(name === read && areaOf(item) === area));
}

export const sameSet = (a: string[], b: string[]) =>
  a.length === b.length && a.every((item) => b.includes(item));
