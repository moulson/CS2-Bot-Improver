#!/usr/bin/env bash
# Build active CounterStrikeSharp plugins (skips plugins/disabled).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PLUGINS_DIR="$ROOT/addons/counterstrikesharp/plugins"
CONFIG="${1:-Release}"

export PATH="${HOME}/.dotnet:${PATH}"

"$ROOT/scripts/bootstrap-raytrace.sh"

shopt -s nullglob
failed=0
built=()
for csproj in "$PLUGINS_DIR"/*/*.csproj; do
  dir="$(dirname "$csproj")"
  base="$(basename "$dir")"
  case "$base" in
    disabled) continue ;;
  esac
  # Skip nested projects under disabled/
  if [[ "$dir" == *"/disabled/"* ]]; then
    continue
  fi
  echo "==> Building $base"
  if dotnet build "$csproj" -c "$CONFIG" --nologo; then
    built+=("$base")
  else
    echo "FAILED: $base" >&2
    failed=1
  fi
done

echo "Built: ${built[*]:-none}"
exit "$failed"
