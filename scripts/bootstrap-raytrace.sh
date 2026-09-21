#!/usr/bin/env bash
# Bootstrap RayTraceApi.dll into plugin libs/ folders for local/CI builds.
# Runtime still requires shared/RayTraceApi/RayTraceApi.dll on the game server.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WORK="${RAYTRACE_BUILD_DIR:-/tmp/ray-trace-build}"
REPO_URL="${RAYTRACE_REPO:-https://github.com/FUNPLAY-pro-CS2/Ray-Trace.git}"

mkdir -p "$WORK"
if [[ ! -d "$WORK/Ray-Trace/.git" ]]; then
  git clone --depth 1 "$REPO_URL" "$WORK/Ray-Trace"
fi

API_PROJ="$WORK/Ray-Trace/managed/RayTrace/RayTraceApi/RayTraceApi.csproj"
OUT="$WORK/out"
dotnet build "$API_PROJ" -c Release -o "$OUT"

TARGETS=(
  "$ROOT/addons/counterstrikesharp/plugins/NadeSystem/libs"
  "$ROOT/addons/counterstrikesharp/plugins/BotAimImprover/libs"
)

for dest in "${TARGETS[@]}"; do
  mkdir -p "$dest"
  cp -f "$OUT/RayTraceApi.dll" "$dest/RayTraceApi.dll"
  echo "Installed RayTraceApi.dll -> $dest"
done
