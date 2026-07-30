#!/usr/bin/env bash
# Behaviour Graph Studio.
#   ./run.sh                          open empty
#   ./run.sh path/to/Behavior00.hkx   open that file on start
#   ./run.sh --headless --quit-after 90 f.hkx   engine flags and their values pass through
#   ./run.sh f.hkx -- chain bind=98,x,y            anything after a literal -- goes to the app
set -e
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENGINE="${BGS_GODOT:-$HERE/engine/godot.linuxbsd.editor.double.x86_64.mono}"
if [ ! -x "$ENGINE" ]; then
  echo "Godot binary not found at $ENGINE." >&2
  echo "Put a double-precision mono build in engine/, or set BGS_GODOT to one." >&2
  exit 1
fi
engine_args=()
user_args=()
passthrough=0
for a in "$@"; do
  if [ "$passthrough" = 1 ]; then user_args+=("$a"); continue; fi
  case "${a,,}" in
    --)                              passthrough=1 ;;
    *.hkx)                           user_args+=("$a") ;;
    chain|bind=*|unbind=*|save=*)    user_args+=("$a") ;;
    *)                               engine_args+=("$a") ;;
  esac
done
exec "$ENGINE" --path "$HERE" "${engine_args[@]}" -- "${user_args[@]}"
