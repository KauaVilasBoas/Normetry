import { QueryCache, QueryClient, useQuery, type UseQueryResult } from "@tanstack/react-query";
import { useSession } from "../auth/session";
import { useFocusAfterCommit, useRefocusWhenGone } from "../ui/focus";
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

const establishmentsPath = "/establishments";

/** An establishment's path: its documents, inspections and overview sit below it. */
export const establishmentPath = (id: string) => `${establishmentsPath}/${encodeURIComponent(id)}`;

/** `enabled`: the user holds `establishments.read` (without it the list is never requested). */
export function useEstablishments(enabled = true) {
  const { api } = useSession();
  return useQuery({
    queryKey: ["establishments"],
    queryFn: () => api.get<Establishment[]>(establishmentsPath),
    enabled,
  });
}

/** A failed query, kept failed while Try again refetches it (it goes back to pending then). */
export const queryFailed = (query: UseQueryResult) =>
  query.isError || (query.isPending && query.errorUpdateCount > 0);

/** 404, or 400 for an id that is not one: what the query asked for is not there. */
const notFoundStatuses = [404, 400];

/** A failed query whose answer says what it asked for is not there. */
export const queryNotFound = (query: UseQueryResult) =>
  queryFailed(query) &&
  query.error instanceof ApiError &&
  notFoundStatuses.includes(query.error.status);

/**
 * A query's load failure and its Try again: `failed` as `queryFailed`, `retrying` while Try again
 * refetches it. Once it loads the button goes away, so focus moves to the heading, after the
 * commit that shows what loaded (a detail view's heading is a new element then). When the error
 * (`errorShown`, the failure by default) goes away some other way, a refetch elsewhere, and focus
 * fell to the page with it, the heading takes it too.
 */
export function useQueryRetry(
  query: UseQueryResult,
  headingId: string,
  errorShown = queryFailed(query),
) {
  const focusAfterCommit = useFocusAfterCommit();
  const failed = queryFailed(query);
  useRefocusWhenGone(errorShown, headingId);

  const refetchThenFocusHeading = async () => {
    if ((await query.refetch()).isSuccess) focusAfterCommit(headingId);
  };

  return {
    failed,
    retrying: failed && query.isFetching,
    retry: () => void refetchThenFocusHeading(),
  };
}
