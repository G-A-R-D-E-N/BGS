# Hard-block dialog for non-hkx loads — design

**Status:** approved 2026-08-10 (user chose option C for scope, option A for blocking, option B for implementation approach).

## Goal

When the user tries to load a file that is not a Fallout 4 behaviour file, the application must
block the load with a modal popup that explains why, instead of only writing a line in the summary
bar. The dialog invites the user to report the case if they believe it is a mistake.

## Trigger scope

Either condition blocks the load:

1. **Extension.** The file name does not end in `.hkx` or `.hkt` (case-insensitive, matching the
   existing file-picker filter). Message: *"<name> does not look like a Havok behaviour file.
   Behaviour Graph Studio opens Fallout 4 .hkx behaviour files."*
2. **Content.** The name ends in `.hkx`/`.hkt` but `HkxBinaryReader.IsFo4Hkx` fails (another
   game's or engine's packfile, or a damaged file). Message: *"<name> is not a Fallout 4
   hk_2014.1.0-r1 packfile. It may be from another game or engine, or it may be damaged."*

Both checks live in one helper, `MainWindow.RefuseReason(path)`, which returns `null` for an
acceptable file and the reason string otherwise. The dialog text, the summary bar, and the tests all
read from it.

## Dialog

New `app/NotBehaviourDialog.cs`, a `Window` modeled on `LegendWindow`:

- Title: "Can't open this file".
- Body: the reason text, wrapped.
- Footer: "If you believe this is a mistake, please report it at
  github.com/NomadsReach/BehaviorGraphStudio/issues".
- One **OK** button; closing via the window X acts the same.
- Centered on the owner, fixed size, not in the taskbar.

## Blocking behaviour

`Load()` checks `RefuseReason` right after the `File.Exists` check and before any parsing. On a
refusal it records the reason in the summary bar and shows the dialog with the main window disabled
(`IsEnabled = false`); dismissing the dialog re-enables the main window. The disabled main window
makes the block hard and prevents a second open from racing in. Nothing is parsed, and the
`--headless` CLI path keeps its existing console refusal (no dialog in a console).

## Testing

- The uismoke `gate` check is updated to expect the dialog: open a non-FO4 file, find the dialog in
  the main window's owned windows, assert it carries the reason and the report line, press OK, and
  assert the main window is re-enabled and nothing was parsed.
- A second `gate-ext` case covers an extension-refused file (e.g. a `.txt`).
- `RefuseReason` is exercised through the harness assertions on the actual files.

## Out of scope

- No "open anyway" escape hatch (hard block only).
- No change to the `--headless` console refusal text.
- No change to `.nif` handling (mesh files use the separate mesh path).
