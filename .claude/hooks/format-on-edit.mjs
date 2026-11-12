#!/usr/bin/env node
// PostToolUse for Edit|Write|MultiEdit: formats the edited file with the project's formatter.
// Convenience only - always exits 0. Each formatter is skipped until it is installed in the repo.
import { spawnSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { parseHookEvent, readStdinRaw } from "./lib/hook-io.mjs";

try {
  const file = parseHookEvent(readStdinRaw())?.tool_input?.file_path;
  if (typeof file === "string") formatFile(file);
} catch {
  // Formatting must never break the session.
}
process.exit(0);

function formatFile(file) {
  const root = process.env.CLAUDE_PROJECT_DIR || process.cwd();
  const run = (cmd, args) => spawnSync(cmd, args, { cwd: root, stdio: "ignore", timeout: 30_000 });
  const ext = path.extname(file).toLowerCase();

  if (ext === ".cs") {
    const manifest = path.join(root, ".config", "dotnet-tools.json");
    if (existsSync(manifest) && readFileSync(manifest, "utf8").includes("csharpier")) {
      run("dotnet", ["csharpier", "format", file]);
    }
  } else if (ext === ".py") {
    const venv = path.join(root, "src", "ai", ".venv");
    const ruff = [path.join(venv, "Scripts", "ruff.exe"), path.join(venv, "bin", "ruff")].find(existsSync);
    if (ruff) run(ruff, ["format", file]);
  } else if ([".ts", ".tsx", ".css"].includes(ext)) {
    const prettier = path.join(root, "src", "web", "node_modules", "prettier", "bin", "prettier.cjs");
    if (existsSync(prettier)) run(process.execPath, [prettier, "--write", file]);
  }
}
