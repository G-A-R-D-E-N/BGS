# Behaviour Graph Studio

A standalone viewer and editor for Fallout 4 Havok behaviour graphs (`hk_2014.1.0-r1` packfiles).
Split out of OpenCommonwealth's in-editor tool so it can be used without the game-conversion project.

Fallout 4 keeps its animation logic in behaviour files: which clip plays, when, how it blends, what
events fire. Havok never released the authoring tool for this format, and the editors that do exist
(Skyrim Behavior Editor, Haviour) target `hk_2010.2.0-r1`, which is Skyrim, and will not open a
Fallout 4 file. This reads the Fallout 4 format directly.

## What it does

- Opens any FO4 behaviour, character or project `.hkx` and shows the object graph.
- **Tree view**: nesting, Havok class per row, the animation each clip points at, file offset.
- **Graph view**: a node canvas laid out in columns by depth from the root, edges drawn from the real
  reference fields and labelled with the field that owns each link, so an edge says why it exists.
  Nodes are coloured by class family. Clip nodes show their animation path and any non-default
  playback speed inline.
- **Editing**: select a node, edit its fields, save back to `.hkx`. The original is kept as `.bak`.
- Filter by name, class or animation.

## Running

```
./run.sh                                  open empty
./run.sh /path/to/Behavior00.hkx          open a file on start
./run.sh --headless file.hkx --quit-after 90    parse and print the summary, no window
```

`run.sh` uses the Godot binary from the sibling OpenCommonwealth checkout. Point `OCW_GODOT` at a
different one if yours lives elsewhere. It must be a **double-precision mono** build, because the
assembly is compiled against the `4.7.1-double` packages vendored in `nuget/`; a stock single
precision editor will refuse to load it.

## Requirements

- A Godot 4.7.1 double-precision mono build.
- .NET 8 SDK to build (`dotnet build BehaviourGraphStudio.csproj`).
- **A Java runtime and `hkxpack-cli.jar`** for anything beyond structure. The tree and the graph come
  from the native C# reader and work without Java, but field-level editing and saving go through
  hkxpack. Without it the tool stays read-only and says so in the status line rather than pretending.

## Layout

```
src/Hkx/     packfile readers, verbatim copies of OpenCommonwealth's Services/Hkx
src/Ui/      Ux.cs design system, GraphCanvas.cs node canvas, StudioRoot.cs the app itself
tools/       sync_from_opencommonwealth.sh re-pulls the readers after an upstream fix
nuget/       vendored double-precision Godot packages
```

`src/Hkx` keeps the original `OpenCommonwealth.Services.Hkx` namespace on purpose. The files are byte
identical to upstream, so a fix on either side is a clean diff away from the other, and
`tools/sync_from_opencommonwealth.sh` pulls the upstream side over.

## Known limits

- Structural editing (adding or removing states and transitions) is not implemented. Field edits only.
- Reading is proven against all 531 vanilla behaviour files; 5292 of 5323 states resolve to a
  generator we understand and every transition resolves its event name. Numbers and method are in
  OpenCommonwealth's `docs/BEHAVIOR_GRAPH_RESEARCH.md`.
- hkxpack will happily repack a graph the game then rejects. Nothing here validates that a saved
  file still loads. Keep the `.bak`.
