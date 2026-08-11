# Predefined Templates Design

## Goal

Complete the deferred part of work item 28: application-defined, parameterized templates for a new clip generator, a blend generator with a requested number of children, and a state with an attached generator.

## Scope and boundary

Saved templates remain unchanged. `TemplateStore` continues to capture a subtree from a real file and apply it through `NativePaste`.

Predefined templates are not saved templates and are not a second serializer. They are catalog entries that resolve values, validate them, and invoke the native mutation layer against an in-memory packfile image. No source file is written until complete replacement bytes have been built and accepted.

This work does not include a user-authored template format, a visual template designer, or arbitrary HKX construction.

## Domain model

Create a `PredefinedTemplates` domain module in `src/Hkx` with these public concepts:

- `Definition`: stable ID, display name, description, root class, slot definitions, and a materializer identifier.
- `Slot`: key, display name, description, kind, required flag, explicit default string value, and constraints.
- `SlotKind`: `Text`, `Count`, `Choice`, and `ObjectReference`. These cover the agreed templates without creating a generic schema language.
- `Values`: a key/value input map supplied by callers.
- `Resolution`: resolved slot values or a structured list of validation errors.
- `Result`: replacement bytes, created root ID, created object IDs, and a human-readable summary.

The catalog is an immutable list exposed by `All()` and looked up by stable ID. UI code submits a template ID plus raw values; it does not construct graph objects or infer defaults.

## Initial catalog

### New Clip Generator

ID: `clip-generator`.

Slots: optional name, required animation name, and playback mode choice. The name has a deterministic default. Playback mode defaults to the existing clip-generator default used by the editor. The materializer appends a real `hkbClipGenerator`, initializes its required fields through the native write layer, and attaches it to a selected compatible destination only when one was supplied.

### Blend Generator

ID: `blend-generator`.

Slots: optional name and required child count. Child count is constrained to a small explicit safe range. The materializer appends one real `hkbBlenderGenerator` plus exactly the requested `hkbBlenderGeneratorChild` objects, initializes their weights and required defaults, and writes the blender's real `children` reference array.

Generated children initially contain no generator reference. They are valid native child structures but remain visibly unconfigured until the user links generators. No artificial generator is invented.

### New State with Generator Attached

ID: `state-with-generator`.

Slots: required target state-machine reference, optional state name, and an optional generator-reference choice. If no generator is selected, the materializer first creates the catalog's default clip generator and attaches it to the state. If an existing generator is selected, it validates that the object exists and is a generator before attaching it. The state receives the next unused state ID in the selected machine. The machine's `startStateId` is never changed.

## Materialization and safety

`PredefinedTemplates.Instantiate(path, templateId, values, insertion)` performs the following sequence:

1. Resolve defaults and validate every slot before mutation.
2. Read the source packfile and keep its original bytes as the commit boundary.
3. Create an isolated in-memory `PackfileImage` from those bytes.
4. Use native object append, field writes, pointer writes, and reference-array writes to materialize the complete requested shape in that image.
5. Rebuild bytes and reopen them through `PackfileImage` and `PackfileObjects`.
6. Run the existing native graph validator against the reopened result.
7. Return replacement bytes only when every previous step succeeds.

Any error returns a structured failure. The caller receives no partial bytes and the original on-disk file is not modified. UI code preserves the current backup-and-replace behavior only after a successful result.

The native mutation additions must be general lower-level operations that are independently testable: append an object, set primitive/string fields, set one reference, and set a reference array. They do not encode template-specific defaults or UI state.

## Symbols and references

Initial predefined templates do not create event or variable symbols. They validate explicit object references before mutation. Future symbol slots must use existing symbol declaration and index-fixup behavior, but symbol creation is not needed to make the three agreed templates correct.

## UI integration

The existing saved-template controls remain available and continue to call `TemplateStore`.

Add an adjacent predefined-template control that lists the catalog. Selecting an entry shows its description and generated editors for its slots. The Create action sends only the selected catalog ID, resolved raw values, and selected insertion destination to `PredefinedTemplates.Instantiate`. It displays validation failures before writing and uses the existing replacement/load/select flow on success.

## Tests

Write tests before each production change. The domain suite must cover catalog identity, default resolution, missing required values, invalid choices/counts/references, each materialized shape, state attachment, exact blend child count, byte-for-byte unchanged originals on failure, reopen/validation of successful bytes, and saved-template regression coverage.

Run focused regression tests during implementation, then `symrm test`, `dotnet test tools/tests/BehaviourGraph.Tests.csproj`, the headless UI smoke, and an application build.

## Explicit decisions

- Predefined templates are application-shipped catalog definitions, not persisted user schema.
- The UI is a consumer of slot metadata and contains no construction rules.
- State templates leave `startStateId` unchanged.
- Existing-generator selection is supported by a validated object-reference slot.
- An omitted generator uses the predefined clip generator so the created state is never empty.
- No source file is touched before the complete native result is rebuilt, reopened, and validated.
