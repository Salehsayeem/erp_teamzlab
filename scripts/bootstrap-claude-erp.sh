#!/usr/bin/env bash
# Claude Code workspace bootstrap for this ERP repo — developer onboarding + CI runners.
# NOTE: the extraKnownMarketplaces/enabledPlugins in .claude/settings.json auto-install
# ONLY on the interactive workspace-trust dialog. Headless (claude -p) and CI must run
# this script instead.
set -euo pipefail

echo "=== 1. LSP & browser prerequisites ==="
if command -v dotnet >/dev/null 2>&1; then
  dotnet tool update --global csharp-ls
else
  echo "skip csharp-ls (dotnet not on PATH)"
fi
npm install -g typescript typescript-language-server
npx playwright install chromium

echo "=== 2. Register the official marketplace (idempotent) ==="
claude plugin marketplace add anthropics/claude-plugins-official \
  || echo "marketplace already registered"

echo "=== 3. Project-scoped plugin installs ==="
PLUGINS=(
  csharp-lsp
  typescript-lsp
  frontend-design
  playwright
  context7
  security-guidance
  code-review
  claude-security
  chrome-devtools-mcp
)
for name in "${PLUGINS[@]}"; do
  claude plugin install "${name}@claude-plugins-official" --scope project --yes
done

echo "=== 4. Verify plugin state ==="
claude plugin list
echo "Done. In an already-open interactive session run /reload-plugins to activate."
