#!/usr/bin/env bash
#
# Lighthouse against the public templates (task P9-15, NFR-3 and NFR-4).
#
#   ./run.sh                                   # the seeded dataset's three shapes
#   BASE_URL=https://staging.example.com ./run.sh /about /news/2026/launch
#
# Three runs per URL, median reported, mobile emulation with simulated throttling — which is what
# NFR-3's "≥ 90 mobile" is stated against. The assertions are in lighthouserc.json and a breach
# exits non-zero.

set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
base="${BASE_URL:-http://localhost:5080}"

# Lighthouse needs a Chrome. A developer machine usually has one; a build agent usually has the
# browser Playwright installed for the E2E suite, and reusing it is better than a second download.
if [[ -z "${CHROME_PATH:-}" ]]; then
  for candidate in \
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" \
    "$(ls -d "${HOME}"/Library/Caches/ms-playwright/chromium-*/chrome-mac/Chromium.app/Contents/MacOS/Chromium 2>/dev/null | head -1)" \
    "$(command -v google-chrome || true)" \
    "$(command -v chromium || true)"; do
    if [[ -n "${candidate}" && -x "${candidate}" ]]; then
      export CHROME_PATH="${candidate}"
      break
    fi
  done
fi

if [[ -z "${CHROME_PATH:-}" ]]; then
  echo "No Chrome found. Set CHROME_PATH, or install the Playwright browsers the E2E suite uses." >&2
  exit 1
fi

paths=("$@")

if [[ ${#paths[@]} -eq 0 ]]; then
  # One of each template, which is what "representative" means here: a landing page, a landing page
  # deeper in the tree, and an article. Seed the dataset first — see docs/load-testing.md.
  paths=("/load-test" "/load-test/section-01" "/load-test/section-01/topic-0001")
fi

urls=()

for path in "${paths[@]}"; do
  urls+=("--collect.url=${base}${path}")
done

echo "Chrome:   ${CHROME_PATH}"
echo "Target:   ${base}"
echo "URLs:     ${paths[*]}"

cd "${here}"

npx --yes @lhci/cli@0.15.1 autorun \
  --config="${here}/lighthouserc.json" \
  "${urls[@]}" \
  "${@:$#+1}"
