#!/usr/bin/env node
// PreToolUse guard for Edit|Write|MultiEdit: refuses to write content that looks like a real credential.
// Normetry is a public repository. Policy: block on a match (security), allow when the event is unusable
// (nothing to inspect). GitHub push protection is the second line of defense.
import { block, parseHookEvent, readStdinRaw } from "./lib/hook-io.mjs";

const SECRET_PATTERNS = [
  ["AWS access key", /\bAKIA[0-9A-Z]{16}\b/],
  ["private key", /-----BEGIN (?:RSA |EC |OPENSSH |DSA |PGP |ENCRYPTED )?PRIVATE KEY-----/],
  ["Anthropic API key", /\bsk-ant-[A-Za-z0-9_-]{20,}/],
  ["OpenRouter API key", /\bsk-or-v1-[A-Fa-f0-9]{32,}/],
  ["Langfuse secret key", /\bsk-lf-[A-Za-z0-9-]{20,}/],
  ["OpenAI-style API key", /\bsk-(?:proj-)?[A-Za-z0-9_-]{32,}/],
  ["GitHub token", /\b(?:gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{40,})/],
  ["Google API key", /\bAIza[0-9A-Za-z_-]{35}\b/],
  ["Slack token", /\bxox[baprs]-[A-Za-z0-9-]{10,}/],
  ["Azure storage account key", /AccountKey=[A-Za-z0-9+/]{40,}={0,2}/],
];

const event = parseHookEvent(readStdinRaw());
if (event === null) process.exit(0);

const input = event.tool_input ?? {};
const edits = Array.isArray(input.edits) ? input.edits.map((e) => e?.new_string) : [];
const text = [input.content, input.new_string, ...edits].filter((v) => typeof v === "string").join("\n");

const hit = SECRET_PATTERNS.find(([, re]) => re.test(text));
if (hit) {
  block(
    `Blocked by guard-secrets: the content looks like a ${hit[0]}. Normetry is public - never write secret values ` +
      "to files. Read them from configuration (.env, dotnet user-secrets, GitHub Actions secrets) and use a placeholder in examples.",
  );
}
process.exit(0);
