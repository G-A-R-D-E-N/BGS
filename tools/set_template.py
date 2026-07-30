#!/usr/bin/env python3
"""Point one export preset at a template binary.

    tools/set_template.py "<preset name>" /path/to/godot.*.template_release.*

export_presets.cfg records an absolute path to whoever exported last, which means nothing on a
build runner or on anyone else's machine. This rewrites it in place for a single preset, leaving
every other preset alone.
"""
import re
import sys


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__, file=sys.stderr)
        return 2

    preset, template = sys.argv[1], sys.argv[2]
    with open("export_presets.cfg", encoding="utf-8") as handle:
        text = handle.read()

    # Split on preset headers so the rewrite cannot leak into the next preset's options block.
    blocks = re.split(r"(?m)^(?=\[preset\.\d+\]$)", text)
    hit = False

    for index, block in enumerate(blocks):
        if f'name="{preset}"' not in block:
            continue
        blocks[index] = re.sub(
            r'(?m)^custom_template/release=.*$',
            f'custom_template/release="{template}"',
            block,
        )
        hit = blocks[index] != block or 'custom_template/release=' in block

    if not hit:
        print(f'no preset called "{preset}" in export_presets.cfg', file=sys.stderr)
        return 1

    with open("export_presets.cfg", "w", encoding="utf-8") as handle:
        handle.write("".join(blocks))

    print(f'{preset}: custom_template/release = {template}')
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
