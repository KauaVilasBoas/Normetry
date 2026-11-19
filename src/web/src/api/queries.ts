import { QueryCache, QueryClient, useQuery, type UseQueryResult } from "@tanstack/react-query";
import { useSession } from "../auth/session";
import { ApiError } from "./client";
import { permissions } from "./contract";

type Catalog = typeof permissions;

/** The permission catalog the UI knows (the server may add more; unknown ones show as sent). */
export type Permission = {
  [Area in keyof Catalog]: Catalog[Area][keyof Catalog[Area]];
}[keyof Catalog];

/** A profile as other resources show it. `systemKey` names a system profile; null for custom. */
export interface ProfileSummary {
  id: string;
  name: string;
  systemKey: "owner" | "manager" | "auditor" | null;
}

/** GET /me. `tenantName` is null for a tenant the core has no row for. */
export interface Me {
  userId: string;
  tenantId: string;
  tenantName?: string | null;
  /** What the user's profiles grant. Hides what they cannot do; the server checks every call. */
  permissions: string[];
  profiles: ProfileSummary[];
}

/** Whether the user holds a permission; false while /me is unknown, so nothing gated flashes. */
export const can = (me: Me | undefined, permission: Permission) =>
  me?.permissions.includes(permission) ?? false;

/** GET /establishments: an unpaginated list, already sorted by name. Enums travel by name. */
export interface Establishment {
  id: string;
  name: string;
  type: string;
  jurisdiction: string;
}

export const meKey = ["me"];

export function createQueryClient() {
  const client: QueryClient = new QueryClient({
    // A 403 elsewhere means a permission was taken away mid-session: /me again, which hides what
    // the user can no longer do. Only a 403 from /me itself means the token has no tenant.
    queryCache: new QueryCache({
      onError: (error, query) => {
        if (error instanceof ApiError && error.status === 403 && query.queryKey[0] !== meKey[0])
          void client.invalidateQueries({ queryKey: meKey });
      },
    }),
    defaultOptions: {
      queries: {
        // 4xx answers do not change on retry (401 already went through the session flow).
        retry: (failures, error) =>
          !(error instanceof ApiError && error.status < 500) && failures < 1,
        refetchOnWindowFocus: false,
      },
    },
  });
  return client;
}

export function useMe() {
  const { api } = useSession();
  return useQuery({ queryKey: meKey, queryFn: () => api.get<Me>("/me") });
}

/** `enabled`: the user holds `establishments.read` (without it the list is never requested). */
export function useEstablishments(enabled = true) {
  const { api } = useSession();
  return useQuery({
    queryKey: ["establishments"],
    queryFn: () => api.get<Establishment[]>("/establishments"),
    enabled,
  });
}

/** A failed query, kept failed while Try again refetches it (it goes back to pending then). */
export const queryFailed = (query: UseQueryResult) =>
  query.isError || (query.isPending && query.errorUpdateCount > 0);
