import { useQueryClient } from "@tanstack/react-query";
import { useRef } from "react";
import { problemCode, type RequestOptions } from "../api/client";
import { meKey, type ProfileListItem, type ProfileSummary } from "../api/queries";
import { useSession } from "../auth/session";
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

export type Sent =
  { ok: true; response: Response } | { ok: false; status: number; code: string | undefined };

/**
 * Sends a change and reads its refusal. A 403 means a permission was taken away mid-session:
 * /me is fetched again, which hides the action. A network failure is status 0.
 *
 * For a change made `inside` a dialog or menu, that refetch waits for `closed()`: it would remove
 * the dialog or menu, and the refusal shown in it, before anyone could read it.
 */
export function useSend({ inside = false } = {}) {
  const { api } = useSession();
  const queryClient = useQueryClient();
  const denied = useRef(false);
  const refetchMe = () => void queryClient.invalidateQueries({ queryKey: meKey });

  const send = async (path: string, options: RequestOptions): Promise<Sent> => {
    try {
      const response = await api.request(path, options);
      if (response.ok) return { ok: true, response };
      if (response.status === 403) {
        if (inside) denied.current = true;
        else refetchMe();
      }
      return { ok: false, status: response.status, code: await problemCode(response) };
    } catch {
      return { ok: false, status: 0, code: undefined };
    }
  };

  /** The dialog or menu closed: a refusal it showed now refetches /me. */
  const closed = () => {
    if (!denied.current) return;
    denied.current = false;
    refetchMe();
  };

  return { send, closed };
}
