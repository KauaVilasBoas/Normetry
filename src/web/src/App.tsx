import { QueryClientProvider } from "@tanstack/react-query";
import { useState } from "react";
import { BrowserRouter, Navigate, Route, Routes } from "react-router";
import { createQueryClient } from "./api/queries";
import { AuthScreen } from "./auth/AuthScreen";
import { SessionContext, useAuth, type Auth } from "./auth/session";
import { paths, routePatterns } from "./routes";
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
            <Route path={paths.home} element={<Navigate to={paths.overview} replace />} />
            <Route path={paths.overview} element={<AppShell screen="overview" />} />
            <Route path={paths.ask} element={<AppShell screen="chat" />} />
            <Route path={routePatterns.conversation} element={<AppShell screen="chat" />} />
            <Route path={routePatterns.documents} element={<AppShell screen="documents" />} />
            <Route path={routePatterns.inspections} element={<AppShell screen="inspections" />} />
            <Route path={paths.settings} element={<Navigate to={paths.profiles} replace />} />
            <Route path={routePatterns.profiles} element={<AppShell screen="settings" />} />
            <Route path={routePatterns.fallback} element={<Navigate to={paths.home} replace />} />
          </Routes>
        </BrowserRouter>
      </SessionContext>
    </QueryClientProvider>
  );
}
