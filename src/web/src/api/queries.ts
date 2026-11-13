import { QueryClient, useQuery } from "@tanstack/react-query";
import { useSession } from "../auth/session";
import { ApiError } from "./client";

/** GET /me. `tenantName` arrives with #52; until then it is absent. */
export interface Me {
  userId: string;
  tenantId: string;
  tenantName?: string;
  roles: string[];
}

/** GET /establishments: an unpaginated list, already sorted by name. Enums travel by name. */
export interface Establishment {
  id: string;
  name: string;
  type: string;
  jurisdiction: string;
}

export function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        // 4xx answers do not change on retry (401 already went through the session flow).
        retry: (failures, error) =>
          !(error instanceof ApiError && error.status < 500) && failures < 1,
        refetchOnWindowFocus: false,
      },
    },
  });
}

export function useMe() {
  const { api } = useSession();
  return useQuery({ queryKey: ["me"], queryFn: () => api.get<Me>("/me") });
}

export function useEstablishments() {
  const { api } = useSession();
  return useQuery({
    queryKey: ["establishments"],
    queryFn: () => api.get<Establishment[]>("/establishments"),
  });
}
