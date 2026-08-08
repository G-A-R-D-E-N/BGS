# Third party notices

`LICENSE` covers the tool's own source and nothing else. Software written by others and shipped
alongside it is listed below with its own licence. Fallout 4 and its file formats are Bethesda's;
nothing in this repository is derived from their code or ships their assets.

Software distributed with this tool that was written by someone else.

## hkxpack

- Upstream: https://github.com/Dexesttp/hkxpack
- Version: 0.1.5-beta (`tools/hkxpack-cli.jar`)
- Licence: MIT, Copyright (c) 2016 DexesTTP. Full text in `tools/hkxpack-LICENSE.txt`, and shipped
  beside the jar in every release because the licence requires it to travel with the software.

Reading a behaviour file needs none of this; the reader is C# and native to the tool. hkxpack is
what turns edited XML back into a binary `.hkx`, so it is only reached when saving. Without a Java
runtime present the tool stays read only and says so, rather than failing at the point of save.

**`src/Hkx/HavokClassTypes.json` is derived from this jar and carries the same licence.** The jar
holds a database of Havok class descriptions under `classxml/`, and `symrm classes` reads it out —
as a zip, not by running Java — and writes it into one file alongside the instance sizes read out of
Fallout 4 itself. What is taken from hkxpack is the shape of each class: its members and their
types, which of them are ever written to a file, what class an inline struct is an instance of, and
every enum's values. Rebuild it with `symrm classes <jar> src/Hkx/HavokClassTypes.json`.

## Google Guava

- Upstream: https://github.com/google/guava, tag `v19.0`. The `code.google.com/p/guava-libraries`
  address the 19.0 release was originally published under now redirects there.
- Version: 19.0, shaded into `tools/hkxpack-cli.jar` rather than shipped as a jar of its own.
- Licence: Apache-2.0. Full text in `tools/apache-2.0.txt`, fetched from
  https://www.apache.org/licenses/LICENSE-2.0.txt and byte identical to the `COPYING` file at Guava's
  own `v19.0` tag.

Not a dependency of this tool, and not chosen by it. hkxpack is a fat jar and carries Guava inside
itself: 1732 classes under `com/google/common/`, alongside its own `com/dexesttp/`. The jar ships no
licence of its own for it, and Apache-2.0 requires the licence to travel with any redistribution, so
the text is bundled here.

## Avalonia

- Upstream: https://github.com/AvaloniaUI/Avalonia
- Licence: MIT, Copyright (c) .NET Foundation and Contributors.

The window is built with Avalonia and a release embeds it along with the .NET runtime, which is
MIT, Copyright (c) .NET Foundation and Contributors.
