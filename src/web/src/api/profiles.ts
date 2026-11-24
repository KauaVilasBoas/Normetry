import { useQuery } from "@tanstack/react-query";
import { useSession } from "../auth/session";
import type { ProfileSummary } from "./queries";

/** GET /profiles. `permissionCount` is the whole catalog for Owner (`grantsAll`). */
export interface ProfileListItem extends ProfileSummary {
  grantsAll: boolean;
  permissionCount: number;
}

/** GET /profiles/{id}: what it grants, the whole catalog for Owner. */
export interface Profile extends ProfileSummary {
  grantsAll: boolean;
  permissions: string[];
}

/** GET /users: the tenant's users, each after their first sign-in. */
export interface TenantUser {
  id: string;
  displayName: string | null;
  email: string | null;
  isCurrentUser: boolean;
  profiles: ProfileSummary[];
}

/** GET the tenant's profiles; POST creates one. */
export const profilesPath = "/profiles";
/** GET one profile; PATCH renames it; DELETE removes it. */
export const profilePath = (id: string) => `${profilesPath}/${encodeURIComponent(id)}`;
/** PUT replaces what the profile grants. */
export const profilePermissionsPath = (id: string) => `${profilePath(id)}/permissions`;
const usersPath = "/users";
/** PUT gives the user the profile; DELETE takes it away. */
export const userProfilePath = (userId: string, profileId: string) =>
  `${usersPath}/${encodeURIComponent(userId)}/profiles/${encodeURIComponent(profileId)}`;

export const profilesKey = ["profiles"];
export const profileKey = (id: string) => ["profile", id];
export const usersKey = ["users"];

/** `enabled` (here and below): the user holds `profiles.read`. */
export function useProfiles(enabled: boolean) {
  const { api } = useSession();
  return useQuery({
    queryKey: profilesKey,
    queryFn: () => api.get<ProfileListItem[]>(profilesPath),
    enabled,
  });
}

export function useProfile(id: string | undefined, enabled: boolean) {
  const { api } = useSession();
  return useQuery({
    queryKey: profileKey(id ?? ""),
    queryFn: () => api.get<Profile>(profilePath(id ?? "")),
    enabled: enabled && id !== undefined,
  });
}

/** The permission names the server knows, for the profile editor. */
export function usePermissionCatalog(enabled: boolean) {
  const { api } = useSession();
  return useQuery({
    queryKey: ["permissions"],
    queryFn: async () =>
      (await api.get<{ name: string }[]>("/permissions")).map((permission) => permission.name),
    enabled,
    staleTime: Infinity,
  });
}

export function useUsers(enabled: boolean) {
  const { api } = useSession();
  return useQuery({
    queryKey: usersKey,
    queryFn: () => api.get<TenantUser[]>(usersPath),
    enabled,
  });
}
