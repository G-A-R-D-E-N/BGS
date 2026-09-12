# Skeleton mapping authoring

`SkeletonMapperAuthor` builds `hkaSkeletonMapperData::SimpleMapping` rows for a
pair of skeleton rigs. Everything it does comes from Fallout 4 itself, not from
the field names. The reverse-engineering record lives in `commonlib_NonVR`,
in the RE-Work docs numbered 63 (runtime semantics), 64 (the Creation Kit
authoring path) and 65 (validation matrix and unresolved questions).

## What a row is

A row's stored transform is

```
inverse(modelSpace_A[boneA]) * modelSpace_B[boneB]
```

in **model space**, where `modelSpace_x[i]` composes bone `i`'s reference
transform down its own hierarchy with the rule the runtime itself uses:

```
compose(a, b):  t = a.t + rotate(a.q, b.t)
                q = a.q * b.q
```

That is the whole computation. It was confirmed by measuring all 18 rows of both
mappers shipped in `Meshes/Actors/Character/CharacterAssets/skeleton.hkx`
against the same expression built from the reference poses: worst translation
error `4.1e-5` in the animation-to-ragdoll direction and `4.7e-5` in the reverse
direction, with rotations exact. Reading the same expression in local space is
wrong by up to 13.3 units and 2 radians, which is why model space is the one
implemented.

The row scale lane is not used and not interpreted. The shipped reference poses
are unit-scaled only to within quantisation, and the two rigs differ: the
ragdoll skeleton is unit to `5.96e-8`, the animation skeleton only to `1.99e-4`
(worst per-component deviation from one; doc 63 §3). That is why the refusal
threshold for reference scale is `ScaleTolerance = 1e-3` - looser than the
quantisation noise in real shipped data, and still far below any deliberate
scale. A rig scaled by one percent is refused rather than answered with a row
whose scale lane is a guess. The `1e-4` tolerance the physics model uses is a
*warning* threshold and is deliberately not used here, because it would refuse
the shipped animation skeleton.

## Direction

The engine applies a row from `skeletonA` to `skeletonB` and writes `skeletonB`,
so a mapper's direction is a statement about which rig is the output. A rig that
needs both directions needs both mapper objects; one cannot serve the other.
`SkeletonMapperAuthor.Reciprocal` returns both, and the reverse rows are the
forward rows with the two bone indices swapped and each transform inverted.
On the human fixture, that reproduces the shipped reciprocal mapper to `2.3e-5`
with exact rotations, which is tighter than recomputing it from the reference
poses the way the identified mapper-generation algorithm does when it
recomputes the second direction (`4.7e-5`). Both are within float noise of the
shipped bytes. The wider corpus shows that this mirrored construction is not a
description of every shipped reverse mapper: some rigs have different row
counts or pairing decisions in the two directions.

## Rotation normalisation

The module never substitutes a plausible value for an unusable one: a degenerate
or non-finite rotation is refused, not replaced with the identity. It does
normalise a rotation whose length is already within `UnitTolerance` of one, and
that is a deliberate, measured contract rather than incidental cleanup.

The measurement: composing the reference frames from the **raw** stored
rotations reproduces the shipped rows to `6.9e-5` / `8.8e-5`, while normalising
each rotation that is already within `UnitTolerance` reproduces them to
`4.1e-5` / `4.7e-5`. The shipped rows are therefore consistent with arithmetic
over unit rotations, which the stored poses are only to within quantisation. So
`UnitTolerance` is the fail-closed boundary - outside it, refuse - and inside it,
normalise.

This is why the author has its own `CheckedWorldFrames` rather than calling
`HavokRagdollSkeleton.WorldReferenceFrames()`. That method is the runtime
preview's composition and it silently treats an unreadable parent as a root and
normalises unconditionally. The author's copy composes the same proven product
but refuses on drift, so no input can reach it that the validation did not
already accept.

## Refusals

The engine's generator refuses a candidate pair when either bone is already
mapped. Within candidate pairing that duplicate rule is the only one observed -
partition agreement is a separate, earlier precondition (doc 63 §6). Shipped
assets nevertheless contain exact duplicate rows, so this is a conservative
authoring policy rather than a format or loader limitation. Fallout 4
performs no validation at load time on the traced paths - the only mapping
checks observed in the game are debug-build warnings that the release runtime
never reaches. So an authoring surface is the last place a malformed mapper can
be stopped, and `Refusals` covers the conditions a mapper can fail on before it
is written:

* a bone index outside either skeleton;
* a bone of either side mapped more than once;
* an empty pair list.

`FromReferencePose` additionally refuses the inputs its own arithmetic cannot
read safely, rather than repairing them:

* a skeleton whose bone, parent and reference-pose counts disagree;
* a parent index that is not `-1` or a bone of that skeleton;
* a parent stored at or after its child. Model-space composition is done in
  array order, so this primitive needs parents before children; that is an
  implementation constraint of this module, not a claim about Havok itself;
* a non-finite translation or rotation component;
* a rotation whose length is not one within `UnitTolerance`, including a zero
  quaternion, which is refused rather than replaced with the identity;
* a reference scale that is not identity within `ScaleTolerance` (`1e-3`),
  because the row scale lane is not yet understood. The shipped vanilla rig
  passes this at `1.99e-4`, so the threshold is not merely theoretical;
* a composed transform that fails any of the above, and any row handed to
  `Reverse` whose values cannot be inverted.

`FromReferencePose` throws on the first refusal set instead of writing a
plausible-looking wrong mapper.

## What is not here

Deriving the bone pairing from names is not implemented. Vanilla names do not
match between the animation rig and the ragdoll rig (`LArm_UpperArm` against
`Ragdoll_NPC L UpperArm`), so name matching demonstrably cannot reproduce the
shipped rows and it is not known which of the generator's matching modes
produced them. Callers supply the pairing.

Shipped Fallout 4 mappers do contain chain mappings. This authoring slice
deliberately does not create them because the consumer's interior weighting and
complete chain semantics have not yet been reduced.

Writing the rows back into a packfile is not implemented. This module computes
and validates mapping data; it does not yet serialise it.
