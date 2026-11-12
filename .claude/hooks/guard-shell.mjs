#!/usr/bin/env node
// PreToolUse guard for Bash|PowerShell: blocks commands that leak secrets or cause hard-to-undo damage.
// Claude should ask the user to run these manually when they are really needed.
// ponytail: regex/token heuristics, not a shell parser - obfuscated commands can slip through; the permission
// deny list and GitHub push protection are the other layers.
import { block, parseHookEvent, readStdinRaw } from "./lib/hook-io.mjs";

const RULES = [
  // `.env`, `.env.local`, `src/ai/.env.production`... but not `.env.example` or `process.env.X`.
  ["reads or touches a secret .env file", /(?:^|[\s"'`=/\\])\.env(?!\.example\b)(?:\.[\w.-]+)?(?=$|[\s"'`;|&)])/],
  ["prints dotnet user-secrets", /\buser-secrets\s+list\b/i],
  ["force-pushes", /\bgit\s+push\b[^;&|\n]*\s(?:--force(?:-with-lease)?|-f)(?=\s|$)/],
  ["applies or destroys cloud infrastructure", /\bterraform\b[^;&|\n]*\s(?:apply|destroy)\b/],
];

const DELETE_COMMANDS = new Set(["rm", "remove-item", "ri", "del", "rd", "rmdir"]);
const BROAD_TARGET = /^(?:\/\*?|~[\\/]?|\$home[\\/]?|\$env:userprofile[\\/]?|\.{1,2}[\\/]?|\*|[a-z]:[\\/]?)$/i;

// True when a recursive delete targets root, home, the drive, the current/parent dir or a bare wildcard.
function isBroadRecursiveDelete(command) {
  return command.split(/&&|\|\||[;|\n]/).some((segment) => {
    const tokens = segment.trim().split(/\s+/).map((t) => t.replace(/^["']|["']$/g, ""));
    if (!DELETE_COMMANDS.has(tokens[0]?.toLowerCase())) return false;
    const recursive = tokens.some((t) => /^-[a-z]*r/i.test(t) || /^\/s$/i.test(t));
    return recursive && tokens.slice(1).some((t) => !t.startsWith("-") && BROAD_TARGET.test(t));
  });
}

const event = parseHookEvent(readStdinRaw());
if (event === null) process.exit(0);

const command = event.tool_input?.command;
if (typeof command !== "string") process.exit(0);

const hit = RULES.find(([, re]) => re.test(command));
const reason = hit?.[0] ?? (isBroadRecursiveDelete(command) ? "recursively deletes a broad target" : null);
if (reason) block(`Blocked by guard-shell: this command ${reason}. Ask the user to run it themselves if it is really needed.`);
process.exit(0);
