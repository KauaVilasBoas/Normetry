// Self-test for the Claude Code hooks: `node --test ".claude/hooks/*.test.mjs"`
// Black-box: spawns each hook with a stdin payload and checks the exit code (0 = allow, 2 = block).
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

const dir = path.dirname(fileURLToPath(import.meta.url));
const run = (hook, stdin) =>
  spawnSync(process.execPath, [path.join(dir, hook)], { input: stdin, encoding: "utf8" }).status;
const toolInput = (tool_input) => JSON.stringify({ tool_input });

// Fake credentials are assembled at runtime so this file never contains a literal secret
// (guard-secrets would refuse to write it).
const fake = {
  aws: "AKIA" + "Z".repeat(16),
  github: "ghp" + "_" + "a".repeat(36),
  openrouter: "sk-or-v1-" + "ab".repeat(32),
  anthropic: "sk-ant-" + "api03-" + "x".repeat(40),
  pem: "-----BEGIN " + "PRIVATE KEY-----",
};

test("guard-secrets blocks credentials in Write, Edit and MultiEdit payloads", () => {
  assert.equal(run("guard-secrets.mjs", toolInput({ content: `AWS_KEY=${fake.aws}` })), 2);
  assert.equal(run("guard-secrets.mjs", toolInput({ new_string: fake.github })), 2);
  assert.equal(run("guard-secrets.mjs", toolInput({ edits: [{ new_string: fake.openrouter }] })), 2);
  assert.equal(run("guard-secrets.mjs", toolInput({ content: fake.anthropic })), 2);
  assert.equal(run("guard-secrets.mjs", toolInput({ content: fake.pem })), 2);
});

test("guard-secrets allows placeholders and ordinary code", () => {
  for (const content of [
    "OPENROUTER_API_KEY=sk-or-v1-your-key-here",
    "ANTHROPIC_API_KEY=",
    'builder.Configuration["Llm:ApiKey"]',
    "const skLearnVersion = '1.5';",
  ]) {
    assert.equal(run("guard-secrets.mjs", toolInput({ content })), 0, content);
  }
});

test("guard-shell blocks secret reads, force pushes, broad deletes and terraform apply/destroy", () => {
  for (const command of [
    "cat .env",
    "Get-Content src/ai/.env.local",
    "type .env.production",
    "dotnet user-secrets list --project src/core/Normetry.Api",
    "git push --force origin main",
    "git push -f",
    "git push --force-with-lease origin feat/1-tenants",
    "cd src && rm -rf /",
    "rm -rf ~",
    "rm -fr ..",
    'Remove-Item -Recurse -Force "C:\\"',
    "terraform -chdir=deploy/terraform destroy",
    "terraform apply -auto-approve",
  ]) {
    assert.equal(run("guard-shell.mjs", toolInput({ command })), 2, command);
  }
});

test("guard-shell allows routine commands", () => {
  for (const command of [
    "cat .env.example",
    "dotnet test",
    "git push origin feat/1-tenants",
    "git push -u origin feat/1-tenants",
    "rm -rf src/core/Normetry.Api/bin",
    "Remove-Item -Recurse -Force src/web/node_modules",
    'node -e "console.log(process.env.CLAUDE_PROJECT_DIR)"',
    "terraform plan",
    "uv run pytest",
  ]) {
    assert.equal(run("guard-shell.mjs", toolInput({ command })), 0, command);
  }
});

test("every hook fails open on an unusable event", () => {
  for (const hook of ["guard-secrets.mjs", "guard-shell.mjs", "format-on-edit.mjs"]) {
    for (const stdin of ["", "null", "not json", "42", "{}", '{"tool_input":null}']) {
      assert.equal(run(hook, stdin), 0, `${hook} <- ${stdin}`);
    }
  }
});

test("format-on-edit is a silent no-op when no formatter is installed", () => {
  assert.equal(run("format-on-edit.mjs", toolInput({ file_path: "src/core/Normetry.Domain/Tenant.cs" })), 0);
  assert.equal(run("format-on-edit.mjs", toolInput({ file_path: "README.md" })), 0);
});
