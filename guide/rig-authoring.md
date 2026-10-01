# Skeleton, skin and physics tools

On **Home**, the **Authoring tools** section has **Open skeleton editor**, **Open skin editor** and **Open physics simulation** buttons. Each opens its respective tab. You can also select **Skeleton / skin authoring** from the **Animation** toolbar.

The preview fills the space below the settings and resizes with the window. Scroll settings separately; use **Fit preview** to reset zoom and pan and centre the loaded asset. Zoomed geometry stays clipped inside the viewport.

## Skeleton

Open a skeleton HKX and choose the skeleton and bone. Edit its name, reference translation, quaternion, scale or translation lock, then select **Apply bone**. Parent changes use **Reparent bone**. Validation and mirror-pair inspection use the existing skeleton tools.

Undo and redo change the in-memory document. **Save skeleton** verifies the rebuilt file and keeps the previous file as `.bak`. Opening another asset or closing with unsaved edits offers Save, Discard or Cancel. A file changed externally is refused instead of overwritten.

Standalone skeleton files support bone addition, removal and reordering. Files containing animation, physics, mapper or other dependent data refuse index-changing edits; remapping those assets is not implemented. Existing transform padding and unrelated skeleton fields are preserved.

## Skin weights

Open a Fallout 4 NIF, select a supported shape and vertex, and edit its four bone/weight slots. **Apply influences** changes that vertex. Normalize, prune, copy and mirror operate on the selected shape. Mirroring reports unmatched or ambiguous vertices/bones and changes only confirmed matches.

The mesh preview shows the shape's geometry. Save writes only the existing vertex weight/index slots; geometry and skin bindings are unchanged. Weights are quantized to the file's half precision and checked after reopening. Unsupported versions/layouts are refused. Undo, redo, backup and external-change protection apply to this document independently of the main behavior graph.

## Physics simulation

Open a ragdoll HKX on **Physics simulation**. Enter a positive simulation mass for each body, or use **Set all masses**. Choose a joint, axes, angular range and cone, then select **Set preview joint**; **Set all preview joints** explicitly applies those settings to all joints.

Start/resume runs gravity, joint solving and collisions. Pause freezes the state; Step advances it; Push applies an impulse to the selected body. Reset disposes the simulation and restores the source pose. Closing or replacing the rig also disposes the simulation.

The preview uses measured convex hulls and source joint frames. Inertia is calculated assuming uniform hull density. Connected bodies do not collide with one another; unconnected bodies and the ground do. Missing/degenerate hulls, incomplete frames and unsupported geometry are refused.

These are editor preview settings. They stay in the window and do not write HKX physics data. Native angular atoms, motors, collision materials, source inertia and game solver behavior are not imported. The existing Drop/Recover control remains a constrained engineering preview; this tab provides the separate collision-based simulation.
