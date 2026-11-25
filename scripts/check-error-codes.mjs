// Guard: every top-level API error code emitted by apps/api must have an
// errors.<CODE> key in BOTH web dictionaries (en + bn). Fix-round guard for
// feat/error-code-i18n — the catalog sweep under-extracted tuple-return codes.
// Node stdlib only. Exit 0 clean, exit 1 listing missing codes.
import { readdirSync, readFileSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const apiDir = join(root, "apps", "api");
const dicts = ["en", "bn"].map((l) => join(root, "apps", "web", "lib", "i18n", `${l}.ts`));

// Nested-only carriers (never the top-level error.code): production close/release
// gate item entries and payroll close-run blocking/warnings array entries.
const NESTED_ONLY = new Set(["OUTPUT_SHORTFALL", "WIP_REMAINDER", "EMPTY_POPULATION", "UNAPPROVED_TIME", "ZERO_PAY_WORKERS", "EXCLUDED_WITH_TIME"]);

// Emission shapes: Envelope.Fail/FailBody*("CODE", …), service tuple
// (null, "CODE", "msg"), and parenthesized ("CODE", $"msg") tuples.
const PATTERNS = [
  /(?:Envelope\.)?Fail(?:Body|BodyPrerequisites|BodyItems)?\(\s*"([A-Z][A-Z0-9_]{2,})"/g,
  /\(\s*null\s*,\s*"([A-Z][A-Z0-9_]{2,})"\s*,/g,
  /\(\s*"([A-Z][A-Z0-9_]{2,})"\s*,\s*\$?"/g,
];

function walk(dir) {
  return readdirSync(dir, { withFileTypes: true }).flatMap((e) =>
    e.isDirectory() && e.name !== "bin" && e.name !== "obj" && e.name !== "Migrations"
      ? walk(join(dir, e.name))
      : e.name.endsWith(".cs") ? [join(dir, e.name)] : [],
  );
}

const emitted = new Set();
for (const file of walk(apiDir)) {
  const text = readFileSync(file, "utf8");
  for (const p of PATTERNS) for (const m of text.matchAll(p)) emitted.add(m[1]);
  for (const n of NESTED_ONLY) if (text.includes(`"${n}"`)) emitted.delete(n);
}

const dictKeys = (file) => {
  const block = readFileSync(file, "utf8").match(/^  errors: \{([\s\S]*?)^  \},/m)?.[1] ?? "";
  return new Set([...block.matchAll(/^    ([A-Z][A-Z0-9_]+):/gm)].map((m) => m[1]));
};

const missing = [];
for (const locale of dicts) {
  const keys = dictKeys(locale);
  for (const code of [...emitted].sort()) if (!keys.has(code)) missing.push(`${code} -> missing from ${locale}`);
}
if (missing.length) {
  console.error(`FAIL: ${missing.length} emitted API error code(s) with no dict key:\n${missing.map((m) => `  - ${m}`).join("\n")}`);
  process.exit(1);
}
console.log(`OK: ${emitted.size} emitted API error codes all have errors.* keys in en + bn.`);
