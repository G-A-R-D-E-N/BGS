#!/usr/bin/env bash
# The Hkx readers under src/Hkx are verbatim copies of OpenCommonwealth's, namespace included, so a
# fix in either place is a clean diff away from the other. This pulls the OpenCommonwealth side over.
set -e
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="${1:-$HERE/../OpenCommonwealth}/Services/Hkx"
[ -d "$SRC" ] || { echo "not found: $SRC" >&2; exit 1; }
for f in HkxBinaryReader HkxBehaviorParser HkxTextEdit HkxDataStructures BehaviourGraphModel BehaviourGraphPlan; do
  cp -v "$SRC/$f.cs" "$HERE/src/Hkx/$f.cs"
done
cp -v "$SRC/BehaviourClasses.json" "$HERE/src/Hkx/"
