# Structured Flow design

## Purpose

Behaviour Graph Studio has two distinct graph-reading jobs. Freeform explains the raw object and
reference dependency graph used for reverse engineering. Structured Flow explains the state-machine
hierarchy used to understand behaviour. Structured Flow is an additional visualization mode. It does
not replace Freeform, alter the parsed model, alter graph ownership, alter `StateRoutes`, alter
simulation, or write to XML.

## Evidence and constraints

`symrm nesting dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx` reports 33 state machines, 141
states, and 167 resolved transition routes. Eight state machines are reached from a state belonging
to another machine. The file has 95 wildcard transitions, so rendering a state-level edge for every
possible wildcard firing would make 1,029 lines. The existing resolved-route representation instead
keeps wildcard routes at their declaring machine until a selected state gives them a concrete source.

`GraphAuthor.Layout` already produces one owner for each placed object. `GraphOwnership` exposes the
owner tree and `StateRoutes` separately exposes state transition relationships. Structured Flow uses
those existing answers unchanged. It must not change `GraphAuthor.PointsAt` to create additional
reachability or grouping links.

The files currently prove serialized numeric object identifiers. Every Structured Flow label writes
the stored identifier as `#` plus the internal numeric string. It must not claim literal identifier
support such as `State1` until a real file proves both syntax and reference behavior.

## View model

The Graph tab receives a layout selector with exactly two modes:

- Freeform: the existing object dependency canvas and its current placement behavior.
- Structured Flow: a behavior-first hierarchical overview.

The selected mode changes only coordinates, containers, and emphasis. Selection, inspector data,
editing, collapsing, static trace, focus tree, diagnostics, Workspace, simulation, and saved graph
data keep their current meanings.

## Structured Flow hierarchy

Structured Flow derives its major hierarchy strictly from `GraphOwnership`:

1. Place each ownership root at the top of its own vertical hierarchy.
2. Give every visible `hkbStateMachine` a visual container labelled with its real display name and
   serialized `#ID`.
3. Place an owned nested state machine as a container inside the nearest ancestor machine container.
4. Place the selected machine's direct behavior states before exposing lower-level helper objects.
5. Group a direct state's owned descendants beneath that state. A collapsed group summarizes its
   hidden descendants and does not remove them from the data model.
6. Retain a deterministic order based on the existing ownership walk, with object ID as the stable
   tie breaker where required.

This is a structural grouping only. The renderer may not invent labels such as `Movement`, `Combat`,
or `Idle` from a name substring. A container title is either a real serialized name or the class
fallback plus `#ID`. Any future author-defined semantic groups are a separate feature and require
explicit persisted metadata.

## Behavior-first visibility and level of detail

Structured Flow uses progressive disclosure. At overview scale it primarily renders state-machine
containers, direct states, and nested state-machine containers. Helper generators, blenders,
modifiers, events, and other lower-level Havok plumbing are not competing peers in the default
overview. They are revealed from the selected or expanded ownership branch when the user asks for
detail.

The same layout has three zoom-dependent detail bands:

- Far zoom: show the root and state-machine container hierarchy. Containers summarize their state
  counts and hide direct state tiles when the tiles would no longer be readable.
- Medium zoom: show readable direct state tiles inside their state-machine containers and nested
  machine containers beneath their ownership parent.
- Close zoom: show selected or expanded state descendants, including real helper generators,
  blenders, modifiers, and event-related nodes, under their owning state.

The thresholds are based on readable node dimensions, not semantic guesses. Zoom only changes
presentation and never changes ownership, routes, selection, trace traversal, parsed data, XML, or
simulation state. A node suppressed at one zoom remains addressable through selection, inspector,
search, editing, focus, and trace.

The existing Focus tree action can restrict either layout mode to the selected ownership subtree.
Show full graph explicitly restores all branches.

## Edges and visual emphasis

Ownership hierarchy is primary in Structured Flow. State-machine containers are the strongest visual
element in the view: their boundary, title, nesting, and owner-to-child links must read before any
route does. Owner-to-child links are straight or orthogonal, readable at overview zoom, and visually
connect the container hierarchy. Whenever a rendering choice conflicts, the hierarchy wins over a
transition route.

State transition routes are secondary:

- Routes are thin and dim in the normal overview.
- Selection lights routes relevant to the selected state or machine using existing `StateRoutes`.
- Static trace lights only the existing upstream, downstream, or both-direction trace result and
  dims unrelated visible content.
- A later runtime overlay may highlight active execution separately, but runtime execution tracing is
  outside this work.

The default mode does not duplicate wildcard transitions as a line from every possible source state.
It uses the existing route and wildcard policy.

## Interaction compatibility

Static trace remains an overlay, not a layout mode. Its traversal remains limited to visible nodes
when Focus tree is active. Clear trace returns normal emphasis without clearing the selected object.

Selection, framing, dragging, and Properties retain their current behavior in both modes. A machine
selected through Workspace selects and frames it in the active mode, but does not automatically enter
Focus tree or start a trace. The Tree tab remains unchanged.

## Future Execution view

Execution is a future third display mode built on Structured Flow coordinates and containers, not a
third graph model. It will add runtime execution overlays such as active states, recent transitions,
and timeline history to the existing behavior hierarchy. Structured Flow therefore keeps static
layout computation, visual ownership containers, and route emphasis independent from `GraphRun` and
any future execution-history store. Runtime must illuminate the chosen layout without deciding its
coordinates or visibility rules.

## Scope

Included in the prototype:

- Freeform and Structured Flow mode selection.
- Behavior-first, top-to-bottom placement from existing ownership data.
- One state-machine container per visible machine, including nested containers.
- Real name and `#ID` headers.
- Hierarchy-primary and transition-secondary rendering.
- Existing focus tree and static trace behavior in both modes.
- Progressive disclosure at far, medium, and close zoom without semantic inference.
- Headless 1600x1000 render comparison of Dogmeat in both modes.
- Test-first smoke and regression coverage.

Excluded:

- ELK, Graphviz, or another layout engine dependency.
- Parser, serializer, `GraphAuthor.PointsAt`, state-route, or simulation-semantic changes.
- User-defined metadata or inferred semantic categories.
- Runtime execution tracing.
- Changes to Workspace, Legend, diagnostics, or Properties architecture.
- Replacement of Freeform or changes to the Tree tab.

## Verification

Smoke coverage must prove:

- The layout selector presents Freeform and Structured Flow and preserves the selected mode through
  a graph rebuild.
- Structured Flow has a state-machine root above its owned descendants.
- Each visible state machine has one bounded container with a real `Name #ID` header.
- Nested state machines remain nested under their ownership parent rather than becoming a sibling
  group.
- Normal Structured Flow gives ownership links higher visual emphasis than transition routes.
- Focus tree limits either layout to visible ownership descendants without altering the model.
- Static trace works in Structured Flow, contains only visible trace IDs under focus, and Clear
  trace retains selection.
- Far, medium, and close zoom show progressively more ownership detail without changing graph data
  or route traversal.
- Containers and ownership links have stronger normal visual emphasis than transition routes.
- The existing Freeform layout remains available and unchanged by the new mode.
- Dogmeat renders at 1600x1000 in both modes without a node or route painting outside the canvas or
  Properties pane.

Before the implementation commit, run the repository's four required gates, render both Dogmeat
layouts at 1600x1000, and inspect the rendered images. Automated rendering is evidence of layout
output, not a claim of manual GUI verification.
