import { useQueryClient } from "@tanstack/react-query";
import { useRef } from "react";
import { useSession } from "../auth/session";
import { copy } from "../copy";
import { problemCode, seconds, type RequestOptions } from "./client";
import { meKey } from "./queries";

export type Sent =
  | { ok: true; response: Response }
  | { ok: false; status: number; code: string | undefined; retryAfter?: number };

/** A refusal as the screen says it: a 403 is a permission taken away, anything else `fallback`. */
export const refusalText = ({ status }: { status: number }, fallback: string) =>
  status === 403 ? copy.common.denied : fallback;

/**
 * Sends a change and reads its refusal, with its `Retry-After` seconds when it has them (a 429).
 * A 403 means a permission was taken away mid-session: /me is fetched again, which hides the
 * action. A network failure is status 0.
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
      return {
        ok: false,
        status: response.status,
        code: await problemCode(response),
        retryAfter: seconds(response.headers, "Retry-After"),
      };
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
