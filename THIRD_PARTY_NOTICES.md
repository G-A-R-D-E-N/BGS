# Third party notices

`LICENSE` covers the tool's own source and nothing else. Software written by others and shipped
alongside it is listed below with its own licence. Fallout 4 and its file formats are Bethesda's;
nothing in this repository is derived from their code or ships their assets.

Software currently distributed with this tool that was written by someone else is listed below.

## hkxpack

- Upstream: https://github.com/Dexesttp/hkxpack
- Licence: MIT, Copyright (c) 2016 DexesTTP.

The hkxpack executable and Java integration are retired and are not included in this repository,
application builds, or releases. This notice remains because generated class metadata is derived
from its published class-description database.

**`src/Hkx/HavokClassTypes.json` is derived from hkxpack's published class-description metadata and
carries the same licence.** The native table combines those member descriptions with instance sizes
read from Fallout 4. It is now a checked-in input to the native reader and writer, not a runtime or
build dependency on hkxpack.

`src/Hkx/BehaviourClasses.json` records the same historical measurement provenance. It is checked-in
native metadata and is not a runtime, build, or packaging dependency on hkxpack.

## Avalonia

- Upstream: https://github.com/AvaloniaUI/Avalonia
- Licence: MIT, Copyright (c) .NET Foundation and Contributors.

The window is built with Avalonia and a release embeds it along with the .NET runtime, which is
MIT, Copyright (c) .NET Foundation and Contributors.
