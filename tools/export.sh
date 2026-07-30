#!/usr/bin/env bash
# Builds the standalone Windows release: one exe with the engine and the project inside it, plus a
# data folder carrying the .NET runtime. Someone who downloads that needs neither Godot nor .NET.
#
#   tools/export.sh [outDir]
#
# The export template is a template_release build of the same engine this project runs on. A stock
# Godot template will not do, because the assembly is compiled against the double precision
# packages in nuget/. Point BGS_TEMPLATE at one, or leave it and the path in export_presets.cfg is
# used.
set -e
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${1:-$HERE/../../dist/BehaviourGraphStudio}"
ENGINE="${BGS_GODOT:-$HERE/engine/godot.linuxbsd.editor.double.x86_64.mono}"

if [ ! -x "$ENGINE" ]; then
  echo "Godot editor binary not found at $ENGINE. Set BGS_GODOT." >&2
  exit 1
fi

# Godot's C# exporter refuses to pack a script unless a solution exists next to the project.
if [ ! -f "$HERE/BehaviourGraphStudio.sln" ]; then
  echo "BehaviourGraphStudio.sln is missing; the exporter needs it." >&2
  exit 1
fi

mkdir -p "$OUT"
cd "$HERE"

if [ -n "$BGS_TEMPLATE" ]; then
  sed -i "s|^custom_template/release=.*|custom_template/release=\"$BGS_TEMPLATE\"|" export_presets.cfg
fi

"$ENGINE" --headless --path . --export-release "Windows Desktop"

echo
echo "wrote $(du -sh "$OUT" | cut -f1) to $OUT"
ls -la "$OUT"
echo
echo "Ship the whole folder. The exe alone will not start: the .NET runtime lives beside it."
echo "Reading a file needs nothing else. Editing and saving still need Java and hkxpack-cli.jar."
