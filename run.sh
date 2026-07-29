#!/usr/bin/env bash
# Behaviour Graph Studio.
#   ./run.sh                          open empty
#   ./run.sh path/to/Behavior00.hkx   open that file on start
#   ./run.sh --headless --quit-after 90 f.hkx   anything that is not a .hkx goes to the engine
set -e
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENGINE="${OCW_GODOT:-$HERE/../OpenCommonwealth/engine/godot.linuxbsd.editor.double.x86_64.mono}"
if [ ! -x "$ENGINE" ]; then
  echo "Godot binary not found at $ENGINE. Set OCW_GODOT to a double-precision mono build." >&2
  exit 1
fi
engine_args=()
user_args=()
for a in "$@"; do
  case "${a,,}" in
    *.hkx) user_args+=("$a") ;;
    *)     engine_args+=("$a") ;;
  esac
done
exec "$ENGINE" --path "$HERE" "${engine_args[@]}" -- "${user_args[@]}"
