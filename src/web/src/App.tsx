import { QueryClientProvider } from "@tanstack/react-query";
import { useState } from "react";
import { BrowserRouter, Navigate, Route, Routes } from "react-router";
import { createQueryClient } from "./api/queries";
import { AuthScreen } from "./auth/AuthScreen";
import { SessionContext, useAuth, type Auth } from "./auth/session";
import { AppShell } from "./shell/AppShell";

export function App({ auth }: { auth: Auth }) {
  const state = useAuth(auth);
  const [queryClient] = useState(createQueryClient);

  if (state.status !== "authenticated") {
    return (
      <AuthScreen
        status={state.status}
        onAction={state.status === "signed-out" ? auth.signIn : auth.retry}
      />
    );
  }

  return (
    <QueryClientProvider client={queryClient}>
      <SessionContext value={state.session}>
        {/* Navigation commits with the click, not as a transition, so focus moved after it lands
            on the new view (the chat's conversation heading or composer). */}
        <BrowserRouter useTransitions={false}>
          <Routes>
            <Route path="/" element={<AppShell screen="chat" />} />
            <Route path="/conversations/:conversationId" element={<AppShell screen="chat" />} />
            <Route path="/documents/:documentId?" element={<AppShell screen="documents" />} />
            <Route path="/inspections/:inspectionId?" element={<AppShell screen="inspections" />} />
            <Route path="/settings" element={<Navigate to="/settings/profiles" replace />} />
            <Route path="/settings/profiles/:profileId?" element={<AppShell screen="settings" />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </BrowserRouter>
      </SessionContext>
    </QueryClientProvider>
  );
}
