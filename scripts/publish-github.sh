#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
command -v gh >/dev/null || { echo 'Установите GitHub CLI: https://cli.github.com'; exit 1; }
gh auth status
if git remote get-url origin >/dev/null 2>&1; then
  echo 'origin уже существует; сначала проверьте его вручную.'
  exit 1
fi
gh repo create broadcast-studio --public --source=. --remote=origin --push --description 'LUMIÈRE — система автоматизации телевизионного вещания на ASP.NET Core, Next.js, CasparCG и MediaMTX'
