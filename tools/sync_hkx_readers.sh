#!/usr/bin/env bash
set -e
[ -n "$1" ] || { echo "usage: $0 <path to OpenCommonwealth checkout>" >&2; exit 2; }
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="$1/Services/Hkx"
[ -d "$SRC" ] || { echo "not found: $SRC" >&2; exit 1; }
for f in HkxBinaryReader HkxBehaviorParser HkxTextEdit HkxDataStructures BehaviourGraphModel; do
  cp -v "$SRC/$f.cs" "$HERE/src/Hkx/$f.cs"
done
cp -v "$SRC/BehaviourClasses.json" "$HERE/src/Hkx/"
