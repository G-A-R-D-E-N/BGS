#!/usr/bin/env bash
#
# Run something with Java hidden every way the tool looks for it.
#
# The window has two behaviours, one with a Java runtime and one without, and the second is the one
# nobody exercises by accident. Testing it means genuinely hiding Java, and Java is not only on PATH.
# HkxTextEdit.FindJava looks in four places, in this order:
#
#   1. the path saved in the tool's own settings, which live under the user's home directory
#   2. $JAVA_HOME/bin/java
#   3. ~/.local/jdk/bin/java
#   4. every directory on PATH
#
# Clearing PATH alone leaves three of those four intact. On this machine ~/.local/jdk exists, so a
# run that only emptied PATH would have found Java anyway and quietly exercised the with-Java path
# while reporting itself as the without-Java one. That is the failure this script exists to prevent,
# and it is silent: the checks all pass, they just pass on the wrong build.
#
# Home is pointed at an empty directory rather than picked apart, which takes out the saved setting
# and ~/.local/jdk together, since both are resolved from it.
#
# Usage: tools/no-java.sh <command> [args...]

set -euo pipefail

home=$(mktemp -d)
trap 'rm -rf "$home"' EXIT

# PATH with java taken out of it and nothing else.
#
# Dropping a directory because it holds java takes everything else in it as well, and on this machine
# that directory is /usr/bin, so the first attempt hid the compiler and the shell along with the
# runtime. Each such directory is mirrored into a shim of symlinks instead, everything except java,
# and the shim stands in for it.
shim="$home/bin"
mkdir -p "$shim"

clean=""
IFS=':' read -ra parts <<< "${PATH:-}"
for dir in "${parts[@]}"; do
    [ -n "$dir" ] || continue

    if [ ! -e "$dir/java" ] && [ ! -e "$dir/java.exe" ]; then
        clean="${clean:+$clean:}$dir"
        continue
    fi

    for entry in "$dir"/*; do
        name=${entry##*/}
        case "$name" in java | java.exe) continue ;; esac
        [ -e "$shim/$name" ] || ln -s "$entry" "$shim/$name" 2>/dev/null || true
    done
done

clean="${clean:+$clean:}$shim"

hidden=(env -u JAVA_HOME HOME="$home" PATH="$clean")

# Proved, not assumed. A harness that fails to hide Java reports success either way, so it checks its
# own work before handing over and refuses rather than running the wrong thing.
if "${hidden[@]}" sh -c 'command -v java' >/dev/null 2>&1; then
    echo "no-java.sh: java is still on PATH after filtering, refusing to run" >&2
    exit 1
fi

leftovers=("$home/.local/jdk/bin/java")

# Only when JAVA_HOME is actually set. Unset it expands to nothing, which makes the path /bin/java,
# and that exists on this machine, so the guard refused every run while nothing was wrong.
[ -n "${JAVA_HOME:-}" ] && leftovers+=("$JAVA_HOME/bin/java")

for leftover in "${leftovers[@]}"; do
    if [ -e "$leftover" ]; then
        echo "no-java.sh: java is still reachable at $leftover, refusing to run" >&2
        exit 1
    fi
done

exec "${hidden[@]}" "$@"
