# Third party notices

Software distributed with this tool that was written by someone else.

## hkxpack

- Upstream: https://github.com/Dexesttp/hkxpack
- Version: 0.1.5-beta (`tools/hkxpack-cli.jar`)
- Licence: MIT, Copyright (c) 2016 DexesTTP. Full text in `tools/hkxpack-LICENSE.txt`, and shipped
  beside the jar in every release because the licence requires it to travel with the software.

Reading a behaviour file needs none of this; the reader is C# and native to the tool. hkxpack is
what turns edited XML back into a binary `.hkx`, so it is only reached when saving. Without a Java
runtime present the tool stays read only and says so, rather than failing at the point of save.

## Avalonia

- Upstream: https://github.com/AvaloniaUI/Avalonia
- Licence: MIT, Copyright (c) .NET Foundation and Contributors.

The window is built with Avalonia and a release embeds it along with the .NET runtime, which is
MIT, Copyright (c) .NET Foundation and Contributors.
