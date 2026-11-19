import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderHook } from "@testing-library/react";
import { createElement, type ReactNode } from "react";
import { describe, expect, it } from "vitest";
import { SessionContext, type Session } from "../auth/session";
import { useSend } from "./send";

describe("useSend", () => {
  const sendWith = (response: Response) => {
    const session = { api: { request: () => Promise.resolve(response) } } as unknown as Session;
    const wrapper = ({ children }: { children: ReactNode }) =>
      createElement(
        QueryClientProvider,
        { client: new QueryClient() },
        createElement(SessionContext, { value: session }, children),
      );
    return renderHook(() => useSend(), { wrapper }).result.current.send("/x", { method: "POST" });
  };

  it("reads a refusal's code and Retry-After seconds", async () => {
    const response = new Response(JSON.stringify({ code: "inspection_quota" }), {
      status: 429,
      headers: { "Retry-After": "120" },
    });

    await expect(sendWith(response)).resolves.toStrictEqual({
      ok: false,
      status: 429,
      code: "inspection_quota",
      retryAfter: 120,
    });
  });

  it("leaves Retry-After out when it is absent or malformed", async () => {
    const response = new Response(null, { status: 429, headers: { "Retry-After": "soon" } });

    await expect(sendWith(response)).resolves.toMatchObject({ ok: false, retryAfter: undefined });
  });
});
