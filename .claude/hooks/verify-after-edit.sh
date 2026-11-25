#!/usr/bin/env bash
# PostToolUse hook: fast compile feedback after Claude edits C# or TypeScript files.
# Drains the hook JSON on stdin, then gates on which file kinds actually changed.
cat >/dev/null 2>&1 || true

fail=0

if git status --porcelain | grep -qE '\.(cs|csproj)$'; then
  echo "[verify-after-edit] C# changed — building apps/api"
  dotnet build apps/api --no-restore -v q --nologo || fail=1
fi

if git status --porcelain | grep -qE '\.(ts|tsx)$'; then
  echo "[verify-after-edit] TS changed — typechecking apps/web"
  (cd apps/web && npx tsc --noEmit) || fail=1
fi

exit $fail
