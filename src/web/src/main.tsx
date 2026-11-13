import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { createUserManager } from "./auth/oidc";
import { createAuth } from "./auth/session";
import "./index.css";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App auth={createAuth(createUserManager())} />
  </StrictMode>,
);
