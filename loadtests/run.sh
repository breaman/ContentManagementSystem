#!/usr/bin/env bash
#
# Runs one k6 script in Docker against a running site.
#
#   ./run.sh cached-delivery.js
#   BASE_URL=https://staging.example.com ./run.sh scale.js
#   RATE=1000 DURATION=30s ./run.sh cached-delivery.js
#
# k6 is not a dependency of this repository — it runs from the official image, so there is nothing
# to install and every run uses the same version.

set -euo pipefail

script="${1:-cached-delivery.js}"
shift || true

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
manifest_dir="${MANIFEST_DIR:-${here}/../src/ContentManagementSystem.Server/App_Data/load-test}"

if [[ ! -f "${manifest_dir}/manifest.json" ]]; then
  echo "No manifest at ${manifest_dir}/manifest.json." >&2
  echo "Seed the dataset first: cd src/ContentManagementSystem.Server && dotnet run -- cms seed load" >&2
  exit 1
fi

# host.docker.internal resolves on Docker Desktop already; the --add-host is what makes the same
# default work on Linux, where it does not.
docker run --rm -i \
  --add-host=host.docker.internal:host-gateway \
  -v "${here}:/scripts:ro" \
  -v "$(cd "${manifest_dir}" && pwd):/manifest:ro" \
  -e BASE_URL="${BASE_URL:-http://host.docker.internal:5000}" \
  -e MANIFEST=/manifest/manifest.json \
  -e RATE="${RATE:-}" \
  -e VUS="${VUS:-}" \
  -e MAX_VUS="${MAX_VUS:-}" \
  -e DURATION="${DURATION:-}" \
  -e HOLD="${HOLD:-}" \
  -e RAMP="${RAMP:-}" \
  -e HOT_URLS="${HOT_URLS:-}" \
  -e ITERATIONS="${ITERATIONS:-}" \
  grafana/k6:latest run "/scripts/${script}" "$@"
