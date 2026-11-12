// Shared stdin/exit helpers for Normetry's Claude Code hooks.
// Contract: exit 0 = allow, exit 2 = block (stderr goes back to Claude). Never any other code.
import { readFileSync } from "node:fs";

// Never throws: any read failure becomes an empty string.
export function readStdinRaw() {
  try {
    return readFileSync(0, "utf8");
  } catch {
    return "";
  }
}

// Returns the event object, or null for invalid JSON, literal `null` or a non-object payload.
// JSON.parse("null") does not throw, so the object check is what keeps callers safe.
export function parseHookEvent(raw) {
  try {
    const parsed = JSON.parse(raw);
    return parsed !== null && typeof parsed === "object" ? parsed : null;
  } catch {
    return null;
  }
}

export function block(reason) {
  process.stderr.write(`${reason}\n`);
  process.exit(2);
}
