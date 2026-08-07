# Changelog

Notable changes, newest first. Read the commit messages for the detail; this is the shape of the
work rather than a list of every edit.

## 2026-08-07, an animation's frames can be written back

The first half of #35, and the half that needed no encoder. A clip can be decoded, changed and
written back into a file.

Nothing here re-encodes a compressed animation, and both formats the game ships are compressors, so
an editor that can read a clip and not write one is an editor that cannot change a clip. Havok's own
answer to that is a format with no compression in it, `hkaInterleavedUncompressedAnimation`, which is
every frame of every track written out as it is. **Fallout 4 registers that class at startup**, read
out of the class initializers the game itself runs, so the engine has the code to read one; it simply
never ships a file that is one. The file gets much larger, which is the honest cost of not having an
encoder and is what a re-encode would later have to beat rather than match.

The old clip is not touched. Its bytes stay where they are and every pointer that named it is aimed
at the new one, so it is left in the file unreferenced, which is the same shape as every other write
here.

**Three facts were measured rather than reasoned about**, and each would have been silently wrong.

The frames are frame major, `transforms[frame * tracks + track]`, which is Havok's own indexing in
its constructor and its sampler rather than the ordering that looks natural.

A transform is four floats per channel and only three are the value. The fourth is the one nobody can
look up: no decoder produces it and the class table only says the field is a transform. Counted with
a new `symrm qstransform` over every reference pose in all 119 vanilla skeletons, 3,769 transforms:
the translation's fourth lane takes **2,838 different values**, which is leftover memory rather than
a number anybody wrote. So writing zero is as valid as anything the game ships.

The annotations were shared with the clip they came from at first, which the format allows, since
every array carries the flag saying the memory is not Havok's to free. It fails anyway, and not at
runtime: the pointer tables are in the order the writer walked the objects, and a shared run's inner
pointers can only sit at one place in that order. hkxpack read the second object's track names as
empty and then lost its place, dropping the transform array entirely. They are copied now, with
everything hanging off them.

**A defect came out from under it**, the way one did last time. The lossless decoder never read
annotation tracks at all, so a lossless clip came back with no bone names and no annotations while a
spline clip beside it came back with both. It was invisible until the same clip was read two ways and
the two readings disagreed. All 857 vanilla lossless animations were affected.

Proved by carrying it out on real animations, not by planning it. `symrm interleave` decodes a clip,
writes it out, decodes the file it produced, and compares every frame of every track, the bone names,
the annotations and the duration. Then it moves one frame of one track by a known amount and requires
that exactly that moved and nothing else did.

| set | files | written | wrong |
|---|---|---|---|
| Dogmeat, checked with hkxpack as well | 443 | **443** | 0 |
| Power Armor, our own reader only | 1,877 | **1,877** | 0 |

Translation and scale come back bit for bit. Rotation comes back within a hundred thousandth of a
degree, which is the normalising Havok itself applies to a stored rotation.

One more thing that was the instrument rather than the data, worth recording because it read exactly
like a real fault. The angle between two rotations was measured as `acos` of the dot product, which is
the formula everyone writes and is useless near zero: the slope is infinite there, so a rounding error
of one part in ten million came out as four hundredths of a degree and failed the threshold. Measured
from how far apart the two lie instead, the same rotations agree to a hundred thousandth.

## 2026-08-06, an array of structs can be given a new length

The second half of #44, and the half that closes #40. Bounding a variable the bounds array does not
reach is written into the file's own bytes now, which is the common case rather than the rare one:
`variableBounds` is empty in 224 of the 531 vanilla behaviours and short in 87 more, so before this
two thirds of the corpus needed a Java runtime to bound a variable at all.

Three moves, the same ones `Resize` already made for an array of pointers. A run of the new length
goes on the end of the section, the array's own pointer is aimed at it and the count beside it is
rewritten. Nothing already in the file moves, because every offset is derived from the sizes of what
precedes it and appending has nothing after it.

**What an array of pointers never had to do is carry anything over.** An element here is a struct
with fields of its own and some of them are things this cannot spell, a string, a pointer, an array.
So the elements the file already had are copied across as bytes rather than rebuilt from the
document, and every fixup naming a byte inside them is moved with them. The fixups belonging to
elements a shorter array drops are dropped with them, since a pointer left aiming at an abandoned run
is one the game would still follow.

**The capacity flag was measured rather than reasoned about.** Growing an array means writing a
capacity for it, and the flag in its top bits is what tells the game whether it owns the memory.
Keeping what was there is obviously right for an array that already holds something and says nothing
about one that starts empty. `symrm capacity` counts them: across all 533 vanilla files, every one of
the 74,567 arrays carries `0x80000000`, empty and full alike, and not one has a capacity that
disagrees with the count beside it.

Proved by carrying it out on every vanilla behaviour it applies to. `symrm grow` bounds the last
variable in each file, writes it, reads it back through hkxpack and sets every value against what the
edit asked for: **180 files, 180 written, none refused, none wrong.** In each, the bound reads back
as the number asked for, no other value in the file differs, and the file grows only by the new run.
The same 180 pass with Java hidden every way the tool looks for it, reading and checking through our
own reader instead, which is the point of the exercise.

## 2026-08-06, a number inside an array of structs is written where it sits

The first half of #44. Changing a bound the array already holds no longer goes back through hkxpack:
it is the same fixed width write as any other value, aimed somewhere the object's own class does not
describe.

What was in the way was the comparison, not the writing. An array of structs was kept as one blob of
every element's text joined together, so a single number moving inside it read as the whole field
changing and there was nothing left to say which element or which member. It is split into a key per
member now, `variableBounds[2].min.value`, and the comparison finds the one number that moved without
knowing anything about arrays.

**A counting bug came out from under it.** hkxpack writes a struct held inside another object as an
`hkobject` too, and every one of those was being counted as one of the file's own objects. A
behaviour with no `hkbVariableValue` object in it appeared to hold hundreds, two for every bound. It
never showed because a change inside an inline struct was refused before anything tried to write it;
the moment those changes were written, the first one was aimed at an object that does not exist. The
file's objects are the ones with an id now.

Proved by carrying it out rather than by planning it. On three vanilla behaviours the bound reads
back through hkxpack as the number asked for, every other value in the file is unchanged, and the
file does not grow by a single byte:

| file | objects compared | differing | growth |
|---|---|---|---|
| `WeaponBehavior.hkx` | 4,645 | 0 | 0 bytes |
| `SuperMutantBehavior.hkx` | 209 | 0 | 0 bytes |
| `DogmeatRoot.hkx` | 161 | 0 | 0 bytes |

Lengthening the array is the other half of #44 and is still refused, by name rather than by silence.

## 2026-08-06, the mesh can be looked at without opening the window

"Does this look right" kept ending up as a question only a person with the program open could answer,
and that is a poor place for it, because the answer was in the data the whole time.

`symrm meshpng` draws the posed mesh to a PNG, front and side, with any bones named on the command
line drawn in their own colour. No display, no window, no GPU: the wireframe goes into a byte buffer
and out through zlib as four PNG chunks.

It earned itself immediately. The human body drew as an unrecognisable vertical column, which is what
the placement fix on `fix/one-answer-for-class-names` had been sitting unmerged to fix, and no number
had made that as obvious as the picture did. With the fix applied the same mesh draws as a standing
human with both feet on the ground, and the left toe that had been reported as 5.140 units out sits
on its own foot, mirrored against the right, indistinguishable by eye.

## 2026-08-06, a variable can be given a bound

`variableBounds` could be inherited from vanilla or lost, never authored, so a variable added by this
tool never got a bound at all. The Symbols tab has a min and a max now, and a Set bounds button.

The array is positional and allowed to stop short, and usually does: of the 531 vanilla files it is
empty in 224 and shorter than the variable list in 87. So bounding variable 82 in a file with no
bounds means writing 82 entries before it. Those are written `0` to `0`, which is what the file
already means by an unbounded variable inside the array, rather than copying a neighbour's bound onto
a variable nobody asked to bound. The entries already there are left exactly as they were, which is
the whole risk of extending a positional array: a bound that slides lands on the wrong variable and
the file stays valid while the wrong thing gets clamped.

**Saving a bound still needs Java.** `variableBounds` is an array of structs and nothing can write
one into the file's bytes yet, so the save falls back to hkxpack. That is true whether the array has
to grow or the bound is already there, and it is reported rather than discovered: `symrm remove` now
says which way a bound would be written for the file it is given.

## 2026-08-06, where a clip takes you

Root motion is read now, off `hkaAnimation.extractedMotion`. Every offset comes out of the class
table rather than being written down, so a field that moves in some other build moves here too
instead of quietly reading its neighbour.

**The limit this was filed against was not real.** The README said a travelling clip walks off the
middle of the view because no root motion is applied to the camera. It does not. Motion is extracted
in this format: a walk plays on the spot and its displacement lives in its own object, never reaching
a bone. Measured on a Dogmeat walk that travels 1,060 units, the root bone moves 0.000 and the centre
of mass 0.312. The camera never needed holding, because the character never leaves.

The real gap was the opposite one. Travel was invisible, and a clip that takes a character across a
room looked exactly like one that goes nowhere. So Playback says it in words on every clip, and
**Follow travel** puts the pose and the path back together and walks the character along it.

The reading checks itself against the game's own file names, which is as close to an oracle as this
gets: `TurnLeft90` comes back as 90 degrees and no displacement, `turnLeft180` as 178, and a straight
walk as pure forward travel with no turn at all. Across 619 vanilla walk animations, 608 carry motion
and 11 stay on the spot, none failed to read.

## 2026-08-06, editing stops needing Java

The text form a file is edited through is written from the file's own bytes now, instead of being
unpacked by hkxpack. Reading had been native for a while; editing had not, and for one reason: an
edit is made by rewriting that text and then working out what changed by comparing two texts. With no
text there was nothing to rewrite, so every edit was refused.

The measure that matters is whether our text is hkxpack's text. Over all 498 vanilla behaviours:

| | |
|---|---|
| files hkxpack reads correctly | 370 |
| of those, identical to ours | **370** |
| lines compared | 385,773 |
| files holding a class hkxpack strides wrongly | 128 |

Those 128 are not a failure to match. hkxpack derives 520 bytes for `BSLookAtModifierBoneData` where
the game uses 528, so its own reading of every element after the first is misaligned and its text is
not a thing to match. The comparison says `COMPARABLE=no` on those rather than folding them into an
average that would mean nothing.

Seven rules were needed to reproduce that text, every one measured rather than reasoned about, and
two of them only surfaced because the sweep ran over the whole corpus rather than a handful of files.
The first reading of the array layout was three vectors a line, fitted to a single array in a single
file; it is a sixty four character width, and 12,833 arrays say so.

**The window's checks now pass identically with Java present and with Java hidden**, 77 either way,
on a real 906 object behaviour. Comparing two files went native with the same change. What still
needs Java is packing a file back after objects have been added or removed.

## 2026-08-06, behaviours open straight out of an archive

Every behaviour in the game is inside `Fallout4 - Animations.ba2`. Opening one meant extracting the
archive first, which is 29,716 files written to disk to reach one of them.

"From archive..." lists the archive itself. Reading the index takes about a second and touches no
file data at all, so the list is the archive rather than a folder somebody prepared earlier. The
filter takes words in any order, because the query a person actually types is "dogmeat behavior" and
the archive stores that as `meshes/actors/dogmeat/behaviors/dogmeatroot.hkx`, where no single
substring matches both.

**Read only, and it says so.** The chosen file is copied into a temporary folder and opened from
there, since everything downstream of opening works on a path. That is not somewhere to save, so Save
is greyed out with the reason on hover, Save refuses as well rather than trusting the button, and the
status line says where the copy went so it can be moved somewhere of your own to edit.

The archive reader moved out of the checking tool into `src/Archive`, where the window can reach it,
and grew an index-and-read API instead of only being able to extract in bulk. `symrm ba2` browses an
archive from the command line and reads one file back out, which is what proves the reader against
real game archives; the suite covers the same ground on an archive written in the test, so it runs on
a machine with no Fallout 4 on it.

## 2026-08-06, the human mesh was never drifting

The male body mesh read as 120.864 units of drift on the vertices whose bones all matched the
skeleton, which had been left as an open question about the bind pose. It is not a bind fault. Every
one of its matched bones composes to the same transform on the reference pose, and that transform is
a lift of 120.84 units, because the mesh is authored with its origin at the neck: its vertices run
from -120.2 to -5.7 and the bind puts them on the ground. Posed, the body now measures from 0.6 at
the feet to 115.2 at the neck, which is where a body of that height belongs.

The measure was wrong, not the transforms. It compared the posed mesh against the vertices as written
in the file, which assumes the mesh was authored in the skeleton's own space. Dogmeat is, so it read
0.245 there and nobody noticed the assumption.

What it measures now is whether the bones agree with each other, which is the thing that cannot be
innocent: a mesh is rigid in whatever space it was authored in, so on the reference pose every bone
has to compose to one and the same transform, whatever that transform is. Dogmeat's 21 bones agree to
within a thousandth of a unit. The male body's 13 agree to within a fifth, except `LLeg_Toe1`, which
is 5.140 out while its right hand twin is 0.172, so the mesh and the skeleton disagree about that one
toe. Reading the stored rotation the wrong way round still fails, and fails harder than before: 97
percent of bones disagreeing by up to 166 units.

**A drawing bug fell out of it.** Vertices whose bones the skeleton does not have were being left at
their authored position while the rest of the mesh was lifted, so the male body drew a second copy of
itself 120 units underground. They are placed with the mesh now.

`symrm mesh` prints the per bone breakdown whenever anything disagrees, which is what turned this
from a hunt into a measurement.

## 2026-08-06, one answer to where a class name lives

Two pieces of code put an object into a file, and they disagreed about a file that has never named
that object's class. The append path wrote the name into the table. The editor's save path refused,
on the grounds that growing that section was unsolved. It was not unsolved; the append path had
already been proved against hkxpack.

Saving now names the class, through the same function the append path uses, and the lookup that only
knew how to refuse is gone. So a new state with a clip generator can go into a file that has no clip
generator in it, which is the case that was worth having.

The trap underneath this is why there is one implementation rather than two agreeing ones. The name
table is padded out to sixteen bytes with `0xFF`, and a name written after that padding is one our
own reader finds, because it looks a name up at the offset the fixup names, and hkxpack never does,
because it walks the section from the front and stops at the filler. The object then exists for us
and not for the game, with every check on our side passing. The one function strips the padding
first. A second implementation would have had to know that, and the refusing path's comment did not
mention it.

`symrm append` now makes the same addition both ways and compares the name tables, so the two
staying in step is measured rather than assumed.

## 2026-08-06, the pointer tables' order is worked out and reproduced

The array work found that where an entry sits in a pointer table is not free, and left the rule as
something inferred from one failure. It is measured now.

The order is the order the writer walked the objects: objects as they sit in the file, and inside an
object its members in offset order, stepping into an array or an inline struct at the point the
member holding it is reached rather than after the object is finished. That is why the table runs
backwards in places. An array's elements live elsewhere in the section, so reaching the array field
emits entries with much larger offsets and the walk then carries on with the fields after it.

| | |
|---|---|
| files whose tables are in that exact order | 533 of 533 |
| entries accounted for | 151,853 |
| out of order | 0 |

Both tables, not just the pointer one. That second half matters more than it looks: it means the
string appends have been getting away with it. Setting an entry that already exists leaves it where
it is, and a renamed string always had one, so appending never came up. An array going from empty to
holding something adds one, and that would have gone on the end and been wrong.

So writing no longer places entries by hand. Every save puts both tables back into walk order at the
end, which is why a save that changes nothing still has to come back byte for byte: that check is
what proves the reorder reproduces the file's own order rather than imposing ours.

One fault found doing it. The reorder was first given the object view the caller already had, which
had resolved its pointers when it was built, so after an array was repointed it still answered with
the run the array used to hold and the walk predicted sources the file no longer had. It reads the
edited image again now.


## 2026-08-06, an array of children saves without Java, and the pointer table turns out to be ordered

Adding or removing a child node writes into the file's own bytes now. The new run of pointers goes
on the end of the section and the array's own pointer is aimed at it, so nothing already in the file
moves and no offset anybody holds goes stale. The capacity word beside the count carries flags in
its top bits, and both zero and the high bit occur across the corpus, so what was there is kept and
only the length part is rewritten.

On the same 40 vanilla behaviours as before, each now given a longer array on top of the rewire, the
cleared pointer, the longer animation name and three value changes: 40 saved, none refused, none
failing a check, and every saved file read back by hkxpack agreeing with our own reading field for
field.

### The pointer table is in traversal order, and something downstream depends on it

The first attempt dropped the array's element entries and appended the new ones. Our own reader,
which looks entries up by source, read the result perfectly. hkxpack read every element of that
array as null.

The second attempt sorted the table by source, on the theory that something was binary searching it.
That made hkxpack misread more than a hundred fields rather than one array.

So the order is not incidental. The table is written in the order the writer walked the objects,
which is not offset order: an array's element pointers are written while the array is being walked,
before the fields that follow it in the owning object. On Dogmeat 22 of the 1,151 steps go
backwards and every one of them is an array. The fix is to put the new entries back at the position
the old ones held. The run of bytes still goes on the end; only the table entries stay put.

This is worth stating plainly because it is the kind of thing that would have passed every check we
own. Our reader is indifferent to the order. Only setting the file in front of a second
implementation showed it.

### Checks tightened rather than loosened, again

The pointer table check now counts how many entries each planned change is allowed to move, worked
out from the original file: one for a repointed field, and for a resized array every element it had
plus every element it now has. An array that was longer than the plan expects therefore cannot hide
extra movement inside the allowance. The local table check was changed the same way, comparing by
source rather than by position, since an entry appearing or going shifts the rest without changing
any pointer.


## 2026-08-06, rewiring a node saves without Java

The first structural edit to write into the file's own bytes.

Rewiring reads as a structural change because the graph's shape changes, and in the file it is not
one. No object moves, nothing is appended, the file does not change length: a pointer from one
object to another is an entry in the global fixup table naming a source and a destination, and
aiming it somewhere else rewrites that one entry. Adding and deleting nodes still go out through
hkxpack.

Clearing a pointer is the other half, and it is not the same operation. A null pointer is the
absence of a fixup, not a fixup to nowhere, so the entry is dropped rather than aimed at offset
zero, which would quietly point the field at whichever object happens to sit first.

On a sample of 40 vanilla behaviours, each given a rewire, a cleared pointer, a longer animation
name and three value changes: 40 files saved, none refused, none failing any check. Each saved file
was read back by hkxpack and agreed with our own reading field for field, and every edit was
confirmed present rather than assumed.

### What the sample caught

Four files came back reporting the file had changed size without appending anything. Dropping a
fixup makes the table twelve bytes shorter, sixteen once it is padded, so the file legitimately
shrinks. The guard had been written when growing was the only way a save could change the length.
It now expects a shrink of at most sixteen bytes per cleared pointer and still fails anything else.

The pointer table check was tightened at the same time rather than loosened. It used to require the
table to be identical, which a rewire cannot satisfy. It now compares entries by source rather than
by position, since dropping one shifts every entry after it without changing any pointer, and it
requires that no more pointers move than the plan repoints. A pointer change also no longer buys any
allowance in the data check, because it writes nothing into the data at all.


## 2026-08-06, an event says what it is for without Java, and so does the checker

The last two things the reading still needed hkxpack for. The symbols tab could list the events but
not say which one is raised where, and the checker could not run its symbol index pass at all.

Both are the same walk: every place in the file where an event or variable index is written. The
graph model genuinely cannot answer it, because those indices sit deeper than the one level of
nesting the model records, an event property inside a transition inside a transition array. So this
walks the class table over the bytes rather than the model, into every inline struct and every
element of every struct array, as far down as the classes go.

Two things the text says out loud have to be worked out instead. hkxpack writes a class attribute on
a struct written under a name and none on an array element, so inside an element the nearest named
class is still the one the array belongs to. Reporting the element's own class was the only thing the
two walks disagreed about, ten times on Dogmeat. And the value goes through the same renderer as the
rest of the reading, so a number spelled a particular way in the text is spelled that way here.

| | |
|---|---|
| files agreeing | 533 of 533 |
| index usages compared | 28,701 |
| differing | 0 |

The same file opened with a Java runtime present and with Java hidden every way the tool looks for
it:

| | with Java | without |
|---|---|---|
| nodes drawn | 799 | 799 |
| symbol rows | 883 | 883 |
| events naming a role | 143 | 143 |
| editing and saving | yes | no |

Editing and saving still rewrite the text and hand it back to be repacked, which is the one thing
left that needs Java, and it is gated on loading something this tool wrote in Fallout 4 first.

### Hiding Java is harder than emptying PATH

`tools/no-java.sh` exists because the first two attempts at testing this proved nothing. The lookup
checks the saved setting, `JAVA_HOME`, `~/.local/jdk` and then PATH, and this machine has the third,
so a run that only cleared PATH found Java anyway and exercised the with-Java path while reporting
itself as the other one. Every check passed. They passed on the wrong build.

## 2026-08-06, the window reads the graph from the file, and no longer needs Java to show it

Opening a behaviour used to mean handing it to Java. Without a Java runtime and the jar, the window
showed a tree and four empty tabs and said so. That was the honest message at the time, because
everything except the tree came out of the text hkxpack produces.

It comes out of the file's own bytes now. On Dogmeat, with Java on the machine and with no Java on
it at all:

| | with Java | without |
|---|---|---|
| nodes drawn on the canvas | 799 | **799** |
| symbol rows | 883 | 380 |
| properties, tree, filtering | yes | **yes** |
| editing and saving | yes | no |

The canvas is identical. The symbol rows are not, and the difference is one thing: what each event
is used for. That is a scan of the text form for every place an index appears, including nesting the
model does not carry, so the rows are built either way and the roles are only filled when there is
text to scan. The checker's symbol index pass is the same case. Both are named rather than hidden.

Editing still rewrites the text and hands it back to Java to repack, so saving still needs both. The
window now says which of the two things it can do rather than calling itself read only.

### The smoke test was passing on a build nobody ships

Found while proving the above. The headless window test compiles the application's own source but
had never embedded the class table data file the application embeds, so in that build the class
table was empty, the window could not read a file from its bytes, and every check passed while
testing the old path. It passed through the whole of this work for that reason.

The data is embedded there now, and the test tells apart the two states it used to treat as one: no
symbol rows at all is a fault, rows without roles is the no Java case. The canvas check was moved
outside the guard that skipped it, because inside it a window that drew nothing would have skipped
the check and passed.

## 2026-08-06, the behaviour graph is read from the file, and the tool behaves the same on it

The reading built from the bytes agrees with hkxpack field by field across the whole corpus. This is
the other half of that: not whether the two readings hold the same values, but whether the tool does
the same thing with them.

Every reader the window draws a tab from is run against both readings and the output compared. The
canvas wiring, the variables and their values and types, the events, the bindings, the checker's
findings, the empty states, every state machine's states and transitions, and what points at what.

| | |
|---|---|
| files behaving the same | 533 of 533 |
| files without a reading | 0 |
| outputs compared | 6,929 |
| differing | 0 |

The field comparison should make this redundant, which is exactly why it was worth running. It
answers a question the field walk cannot: a field comparison that came back clean for the wrong
reason, an excuse too wide or a bucket the walk quietly skipped, would still show up here as a
canvas with different wires on it.

The checker was split so its model-only checks can run without the file's text. One check cannot:
the symbol index pass reads the text as well as the model, because the indices it looks for sit in
places the model does not carry. That one still needs hkxpack, and is left out of the comparison
rather than papered over.

A single wrong value reaching two consumers is what this is for. Pointing a wire at an object that
is not there changes the canvas and gives the checker a dangling reference, so the suite asserts both
rather than one.

## 2026-08-06, the measured enum names are gone

`HavokEnumNames.json` was the enum value names read off vanilla files by setting our reading of the
bytes beside hkxpack's reading of the same field. It stopped being consulted when the class table
arrived, and the previous entry left it in place as an independent check on the table. It was never
an automated one, so it was checked once and then removed rather than left sitting there looking
load bearing.

The check: 22 fields, 47 values, all 47 named the same thing in both. Nothing the measurement found
is missing from the table and nothing disagrees. The table declares 1,007 values across 195 enums,
so the measurement was a strict subset of it.

Removed with it: `HavokEnums.cs`, the `symrm names` command that rebuilt the file, and the embedded
resource entries in both project files. The suite still stands at 389 checks, because the seven that
covered the measurement now cover `HavokClassTypes.NameOf` instead, including the two that matter
most: a value the table does not declare comes back unnamed, and a combination of flags holding one
bit with no name is refused whole rather than answered in part.

## 2026-08-05, the panel's field list comes from the file now, not from hkxpack

The values in the properties panel have come from the file's own bytes for a while. The list of
*names* did not: it was hkxpack's list, read back out of the XML, and it was the reason nearly half
the panel still fell back. A name on its own cannot be read — a struct written inside an object sits
at no offset that object's class describes — so the fallback was not stubbornness, it was the only
honest thing to do with a name and no address.

The class table fixed that. The list is built by walking the class: into every struct written inline,
through every array of them at the count the file itself states, expanding a fixed length `hkReal[8]`
into the eight fields hkxpack writes. Every field comes back **with the offset it sits at**, which is
what makes it readable rather than merely nameable.

On Dogmeat's behaviour:

| | before | after |
|---|---|---|
| values on the panel | 11,882 | 11,882 |
| read from the file | 7,062 | **11,882** |
| fallen back to hkxpack | 4,820 | **0** |
| agreeing with hkxpack | 11,882 | 11,882 |

The fallback did not get better at guessing. It stopped being needed.

Underneath, the renderer now reads at an offset rather than by field name, which is the same change
in a smaller place: asking for a field by name finds a different field that happens to share it, and
`hkbStateMachine` and the `hkbEvent` written inside it both have an `id`. Two pieces of scaffolding
came out with it. The XML nesting depth that told the panel which fields were the object's own is
gone, because the walk knows structurally. And `HavokEnumNames.json`, the enum names measured off
vanilla, is no longer consulted when reading: the table declares 1,007 values where the measurement
found 47, and the two agree on all 47. The measurement stays as what it is, an independent check on
the table rather than a source.

**hkxpack has not left the read path, and it is worth being exact about what it still does.** It is
still what produces the XML the window parses for the graph, the symbols tab, the validator, event
usage and the compare tab, and it is still where a field falls back to when the bytes cannot answer.
What it no longer does is decide what a field list is.

### Two things reading the inner fields turned up

Neither could have been seen before, because nothing had ever read them.

**A byte of `0xFF` in an enum is `-1` to whoever declared the names and `255` to whoever prints the
bytes.** `hkbVariableInfo` declares `VARIABLE_TYPE_INVALID = -1`, so the name is only found signed;
hkxpack writes `255`. The name is looked up with one and printed with the other, because picking
either alone loses the name or loses the comparison.

**And in one place hkxpack is wrong rather than us.** A struct holding a vector or a transform is
sixteen aligned, so the compiler pads it out, and the game's own class registration records the
padded size — `BSLookAtModifierBoneData` is 528 bytes where its last member ends at 520. hkxpack has
no size in its data and works one out by rounding up to eight, which is right until a class is
sixteen aligned. From the second element of an array of one of those onwards, it reads from eight
bytes short of where the element is.

Which of us is right is not a matter of opinion here. At our stride the second bone reads
`index 13`, `fwdAxisLS (0 1 0 0)`, `upAxisLS (1 0 0 0)` — a bone index and two unit axes. At
hkxpack's it reads `index 0` and `(0 0 0 1)`, which is that entry seen eight bytes early.

Telling those apart takes both halves of a test and not one: rounding to eight has to be wrong *and*
rounding to sixteen has to be right. The first half alone catches `hkbVariableInfo`, which is six
bytes and is neither, and hkxpack strides it perfectly well across 309 arrays of them. A check that
excused a disagreement in one of those would be worse than no check at all. 14 of the 165 classes
used as array elements answer to both halves.

`symrm panel` counts them apart and names the class rather than calling them our disagreements or
quietly dropping them.

**Over all 531 vanilla behaviours**, with that test in place:

| | panel | crosscheck |
|---|---|---|
| files with anything wrong | **0 of 531** | **0 of 531** |
| values | 485,793 | 258,933 |
| read from the file | **485,793** | |
| fallen back to hkxpack | **0** | |
| agreeing | 484,736 | **258,933** |
| hkxpack striding a padded struct wrongly | 1,057 | |

Every value is accounted for as one or the other, and nothing is left over. The 1,057 are three
classes in 22 files: `BSLookAtModifierBoneData` (1,049 values across 20 files),
`hkbHandIkControlData` (6) and `hkbHandIkControlsModifierHand` (2). The middle one is reached
through the last: it is an inline struct inside a hand, and the hand is the array element hkxpack
misplaces.

## 2026-08-05, the signature check reaches the save path, and says why on every load

The check landed with the class table and was wired into loading a file: a packfile whose classes are
signed differently from the ones this build describes puts the byte reader aside, and the panel goes
back to reading through hkxpack, which uses the file's own definitions rather than ours. Going back
over it turned up two things that wiring did not cover.

**Saving never asked.** Refusing to read a file whose classes we do not describe is the smaller half.
`NativeSave` writes values straight into a file's bytes at offsets taken from this build's idea of
the class — so on a file written against a different definition, a value lands in somebody else's
field and the file still looks perfectly valid afterwards. It builds its own reader and never
consulted the check. It does now, and refuses rather than attempting, naming the class; the caller
falls back to the rebuild through hkxpack, which goes through the file's own class definitions.

**The reason reached the status line on one path out of four.** It was set where the load reports
"Editable", and `PrepareEditing` has four ways out: no Java, object counts disagreeing, an exception,
or success. A file with classes we do not describe *and* no Java present reported the Java and
swallowed the rest. It is said once now, on the summary line, which is set on every load and
overwritten by nothing.

One deliberate hole, written down rather than left to be found: with no class table present at all —
a build where the data file did not make it in — the check reports nothing rather than reporting
every class in every file as unknown. A missing data file should not turn the tool into one that
refuses to open anything.

## 2026-08-05, a class table, so a field list can come from the file

The properties panel gets its list of field *names* from hkxpack's XML, and that is the last thing
holding the Java requirement in place for reading. The values already come from the bytes. The names
cannot, because the class dump read out of Fallout 4 keeps two things back: it does not say which of
a class's members the engine ever writes to a file, and where a struct is written inline it records
the word `struct` and not which class that struct is.

Both are in the class database hkxpack carries inside its own jar, under `classxml/`. `symrm classes`
reads it out — **as a zip, so nothing here runs Java** — and merges it with the instance sizes from
the dump, which hkxpack's data does not carry and which is what an array of structs needs to step
through its elements. The result is `src/Hkx/HavokClassTypes.json`: **908 classes, 3,915 members, of
which 482 are never written out and 722 name the class of a struct, plus 1,007 enum values across 195
enums.** One class per line, because a generated file that cannot be read in a diff hides its own
mistakes.

**Gated on the only question it exists to answer.** `symrm fields` builds each object's whole field
list from the table and the file's own bytes — walking into every inline struct, stepping through
every array of them at the count the file states, and expanding a fixed length `hkReal[8]` into the
eight fields hkxpack writes — and compares it to what hkxpack writes for the same object. Across all
**531** vanilla behaviours: **36,340 objects, every list exactly right, none wrong, none it could not
work out.**

**And a check the tool has never had.** A packfile stores four bytes in front of every class name,
and those four bytes are what a class definition *is*: change a member and the signature changes. So
a file can now be asked whether it was written against the same classes we read it with, rather than
merely whether it parsed. **20,833 class names across the 531 vanilla behaviours and 1,331 files in a
mod folder: every signature matches, none unknown.** On load, a file whose classes disagree puts the
byte reader aside and goes back to reading through hkxpack, which reads the file's own definitions
rather than ours, and the status line says why.

Nothing about the panel changed yet. That is the next piece.

## 2026-08-05, the check stops tidying up what it is meant to be checking

Every "all agreeing" number here so far came from a 76 file sample. Run over all **531**, the check
reports **7 files disagreeing on 14 values** — and every one of the 14 was the check's fault rather
than the reader's.

**It was trimming whitespace off hkxpack's values.** Four state machines and a layer generator in
vanilla are named with a leading space, `" StateMachine00"`, and one event payload ends in one. The
bytes have the space, hkxpack's XML has the space, and the comparison quietly removed it from one
side before looking. An earlier pass had trimmed only the trailing end, which is why this surfaced as
four new disagreements rather than none. Measured before changing it: across **374,120** single
valued fields in the unpacked corpus, **six** carry a space that means something and **not one** runs
over more than a line, so there was nothing here that trimming was normalising.

**It was reading a transform array as three times as many elements.** hkxpack writes one bracket per
vector, so a transform arrives as three of them, and a skeleton's 9 element reference pose read as 27
elements against our 9.

**And an array of `int16` was read signed where the same type on its own is read unsigned.** A
skeleton's root has parent index `0xFFFF`; hkxpack prints `65535` in both places, and we printed
`65535` on its own and `-1` inside an array.

**One renderer, finally.** The check had kept its own copy of the field renderer when the window
moved to `FieldRender`, so it had been checking code the window does not run. It calls the same one
now, which is how the `int16` fix reached it at all.

Before and after, over all 531 vanilla behaviours:

| | files clean | values compared | agreeing |
|---|---|---|---|
| before | 524 of 531 | 258,933 | 258,919 |
| after | **531 of 531** | 258,933 | **258,933** |

The compared count is identical on both sides, which is the number that matters: nothing was dropped
from the comparison to make it pass. `symrm panel`, over the same 531, shows **485,793 values,
231,693 of them read from the bytes and 254,100 fallen back, all agreeing**.

## 2026-08-05, the properties panel reads the file rather than the text form

The panel used to take every value from hkxpack's XML. It reads them out of the file's own bytes
now, and where it cannot, it falls back to the XML **for that one field** rather than for the whole
panel. Across the sample below that is 48,655 values read from the file against 45,961 still coming
through hkxpack, in the same panels, side by side.

The fallback is not a small corner. Nearly half the rows in a properties panel belong to objects
written *inside* the object rather than to the object itself: a state machine's block carries its
transitions, and each transition's `eventId` and `enterTime` look exactly like fields of the machine.
They are shown and edited the same way, and they cannot be read from the machine's bytes, because
they are not at any offset the machine's class describes.

**Which is how this nearly went wrong.** `hkbStateMachine` has an `hkbEvent` written inside it, and
both of them have an `id`. Reading `id` off the state machine found a real field at a real offset
and returned a real number — the wrong one. It agreed with nothing and would have been shown as
fact. Fields now carry whether they belong to the object itself, and only those are read from the
bytes.

**Checked against what the panel actually displays, not against the reader.** `symrm panel` runs the
same `PanelFields.For` the window runs and compares every value it produces to hkxpack's text for
the same field. Across the same 76 vanilla behaviours: **94,616 values shown, 48,655 of them from
the bytes, 45,961 fallen back, and all 94,616 agreeing.** That is a different question from
`crosscheck`, which asks whether the reader agrees with hkxpack: a fallback that quietly returned a
wrong value instead of falling back would pass that check and fail this one.

`crosscheck` still reports 53,956 values agreeing across the same files, so nothing regressed on the
way.

One more thing it turned up, which predates all of this: a value is XML, so an expression like
`cond(fAccelOrDecel &gt; 0.0, ...)` was being shown with the escape still in it, and anything typed
with an `&` in it wrote a file no XML reader would take back. Values are unescaped on the way in and
escaped on the way out now.

Java is still needed to open a file for editing, because the panel's field list, and every fallback
in it, still comes from the XML.

## 2026-08-05, reading a whole file out of its own bytes

The rest of the reading side, towards taking hkxpack off it. Values still reach the properties panel
through hkxpack's XML; what this settles is that the byte reader can account for nearly all of a
file on its own, which has to be true before anything is switched over.

Four kinds of field it could not read:

**References between objects.** The one that mattered, and the one that was being read wrongly rather
than not at all. A pointer from one object to another is a **global** fixup, not a local one, even
when both objects sit in the same section, because the format allows it to cross into another section
even though nothing in these files does. Reading only the local table finds every string and every
array and no reference at all, which reads as a file where nothing points at anything.

**Arrays.** An `hkArray` is a pointer, a count, and a capacity with flags in its top bits. Arrays of
references, of strings, of numbers, and of vectors and transforms all read now. An array of inline
structs reads only as its count, because the class dump does not name the struct's own class, so
there is nothing to read the elements with. That is counted separately rather than presented as a
field we can read.

**Enums and flags.** Not a byte problem: the bytes hold `0` where hkxpack writes `MODE_SINGLE_PLAY`,
so reading one means having the value names, and the class dump kept the fields and their types but
not the names. So they were **measured rather than looked up**: every enum field of every object in a
set of vanilla files was read out of the bytes and set beside what hkxpack calls the same field, and
the pairs that come out are the table. `symrm names` rebuilds it. A value no vanilla file uses has no
name and is reported as unnamed rather than guessed at.

Flags combine, and a combination is only as good as its parts: a value with a bit nothing has named
is refused whole rather than half translated. Which turned up something worth knowing — **hkxpack
gives up on those.** Where a flags field holds two flags at once it prints the bare number, `6`,
rather than the two names. We print both, and the comparison meets it either way.

Where neither side has a name, the number is still the whole value, and it is compared as one. A
field is only unreadable when hkxpack has a name for it and we do not.

**Proved against a second opinion, and on a set the table had never seen.** Every field the reader
can render was compared to hkxpack's reading of the same field, across the same 76 vanilla
behaviours the rename work was proved on: **53,956 values compared, all 53,956 agreeing, no file
disagreeing about anything.** Up from 29,689 at the start of the day. 2,759 of those are arrays of
inline structs, where only the count is checked.

The names in the shipped table come from all 531 behaviours, which includes those 76, so that run
alone would not show whether the method generalises. It was therefore run again with a table built
from a **disjoint 228** and nothing else: **76 files, 53,955 values, all agreeing**, with a single
flags value left unnamed. Reading a file the table was not built from works.

What still needs hkxpack, counted rather than passed over in silence: **519 inline structs**, and
nothing else. The class dump does not name the class of a struct written inline, so there is nothing
to read its fields with. Every other field type in these files now reads from the bytes.

## 2026-08-05, reading the wider fields out of the bytes

Groundwork for taking hkxpack off the reading side as well as the writing side. Values still reach
the properties panel through hkxpack's XML; what changed is how much of a file the byte reader can
account for on its own, which is the thing that has to be true first.

Three kinds of field it could not read before:

- **Eight byte values.** `hkbNode.userData` is one, and 430 of Dogmeat's 906 objects carry it.
  Reading it as an int is right only while the top half happens to be zero.
- **Vectors and quaternions**, four floats in a row.
- **Transforms**, twelve.

Measured the only way that means anything: `symrm crosscheck` reads every field it can out of the
bytes and compares it to what hkxpack says the same field holds. Across the same 76 vanilla
behaviours the rename work was proved on, it now compares **32,736 field values, up from 29,689, and
all 32,736 agree**. Dogmeat's own file goes from 4,678 to 5,109, none disagreeing.

The 3,047 new values are almost entirely `userData`. The vectors and transforms read correctly and
are barely exercised, because hkxpack does not write out the runtime fields they mostly sit on —
`hkbClipGenerator.extractedMotion` and `hkbBlendingTransitionEffect`'s six pose fields never appear
in its XML, so there is nothing to compare them against. They are checked by hand instead, in
`symrm test`.

One fix came out of it. A vector was being compared as text, so `(0 1 0 0)` and `(0.0 1.0 0.0 0.0)`
read as a disagreement. Numbers are compared as numbers now.

What still needs hkxpack to read: pointers between objects, arrays, and enums. Enums are the awkward
one and are not a byte problem: hkxpack writes `MODE_SINGLE_PLAY` and the bytes hold `0`, so reading
them means having the value names, which the class dump did not keep. That is the same table #36
needs.

## 2026-08-05, a name can be any length it likes

Renaming an animation is the commonest edit anyone makes here, and it was the one thing the new save
path could not do. It wrote a value over a value of the same width, and a new name is almost never
the length of the old one, so every rename fell back to a full rebuild through hkxpack.

It does not have to. Nothing in the format says a string has to sit anywhere in particular: what
makes a run of bytes mean something is a fixup pointing at it. So the new text goes **on the end of
the section**, where no offset anybody already holds can reach, and the one fixup that names it is
aimed at the new place. Every other byte, and every other pointer, is left exactly as it was. The old
text stays where it is and stops being referenced, which is what an unreferenced run of bytes in this
format already looks like. A field the file left empty gains a pointer the same way, so a name that
was never set can be given one.

Two things came out of measuring rather than reasoning, both the same shape as last time:

**The fixup table is not in order and must not be put in order.** Sorting it by source offset looked
like tidying up. Fallout 4's own tables are not sorted: 383 of Dogmeat's 1,587 entries move if you
sort them, so a sort rewrites a quarter of the table to no purpose and buries the one entry that
really did change. Entries are left where they were found and a new one goes on the end.

**A growing file cannot be checked by counting changed bytes.** Appending 39 bytes to Dogmeat's
behaviour makes 13,561 of the original bytes differ, because the fixup tables sit after the data
inside the section and all of them slide along. None of that is a change to anything the file says.
So `symrm savecheck` now compares the pieces instead of the bytes: every section's data must be
unchanged except for the few bytes of the values written over in place, the cross-section pointers
must be identical, and exactly one local pointer may move per name changed.

**Proved on vanilla.** `symrm savecheck` renames an animation to something longer, saves, and then
asks three things of the file that came out: hkxpack can still read it, every value in it still
agrees with our reading, and nothing moved that was not meant to. Across a sample of **76 vanilla
behaviours, all 76 pass**, and 28 of them exercised the rename. Dogmeat's own is 238,096 bytes in and
238,144 out, one pointer repointed, 4,678 field values still agreeing with hkxpack.

One thing fixed on the way past. The single disagreement with hkxpack anywhere in the corpus,
`OBJSwitchToggleLightOff ` in `GenericButton01`'s `Behavior00`, was neither implementation being
wrong: the name ends in a space, the value sits on its own indented line in the XML, and every reader
trims it. Compared without the trailing space, the corpus now agrees everywhere.

Java is still needed to edit at all, because the field values still come out of hkxpack's XML on the
way in. Reading them from the bytes instead is the next piece, and it is what actually removes the
requirement. See #34.

## 2026-08-05, writing packfile bytes without hkxpack in the way

Every save currently goes out through hkxpack: the file becomes XML, is edited, and becomes a file
again. That one dependency is behind three separate limitations, so removing it is one piece of work
rather than three. Saving an animation is refused outright because the XML cannot carry
`hkaLosslessCompressedAnimation` without losing data, animations cannot be written at all, and every
save is only as faithful as the round trip.

First half of that is in: a packfile reader and writer of our own, `src/Hkx/PackfileImage.cs`. It
takes a `.hkx` apart into its header, sections, object bytes and three pointer tables, and puts it
back together with every offset recomputed from the sizes of what precedes it.

**Proved by rebuilding, not by inspection.** `symrm packfile` reads a file, writes it back, and
compares the bytes. Since nothing about a packfile's structure is stored twice, a file that comes
back identical is a file whose every offset was derived correctly. Run against all 15,320 `.hkx` in
Fallout 4's animation archive and the 453 shipped as examples: **every one identical, none refused.**

The format was read out of Fallout 4's own writer, which the game still carries, rather than guessed
or taken from a Havok SDK. Notes and decompiles are in the F4SE workspace under
`ReverseEngineering/03-FINDINGS.md` and `Findings/Havok/`.

Two things worth knowing, both found by measuring rather than reasoning:

**There are two header shapes and the difference is invisible unless you look for it.** The section
headers do not begin at a fixed place. A field near the end of the file header gives the size of an
area that sits between the two, and Fallout 4 uses both settings: its animation and skeleton files
put 16 bytes there, its behaviour files put none. Reading the section headers at a fixed offset works
on one kind and produces silent nonsense on the other.

**Padding inside a table cannot be told from content.** Each table is padded up to a 16 byte boundary
with `0xFF` and the next offset is recorded after the padding, so nothing anywhere records where the
real entries stop. Read naively, up to one invented fixup appears per table. Entries that are
nothing but `0xFF` are therefore skipped, which also covers a pointer the writer could not resolve.

Second half is in too: the objects inside the file, and their fields.

A packfile does not list its objects anywhere. What it has is one entry per object saying "an
instance of this class sits at this offset", so the object list is that table read in order.
`src/Hkx/HavokClasses.cs` supplies where each field sits inside an instance, from
`HavokClassLayouts.json`, which is 935 classes read out of Fallout 4's own startup code rather than
guessed or taken from an SDK. `src/Hkx/PackfileObjects.cs` puts the two together and reads or
overwrites a field.

Overwrite, not resize. Every offset in a packfile is derived from the sizes of what precedes it, so
changing an object's size means rebuilding every pointer past it, while writing a value over another
value of the same width leaves the whole file valid. Only the second is offered.

**Proved against a second opinion.** `symrm crosscheck` reads every field it can out of the bytes and
compares it to what hkxpack says the same field holds: two independent readings of one file, ours by
byte offset and hkxpack's by its own schema. Dogmeat's behaviour alone is **4,678 field values, all
agreeing**; across 22 behaviour files it is **5,604 values, no disagreements**. Every object in that
file is of a class we have the layout for, Bethesda's own additions included, because those are
registered in the game the same way Havok's are.

And saving now uses it. A change to a value is written straight into the file's own bytes, leaving
every other byte exactly as Bethesda shipped it. Anything that resizes something, adds or removes an
object, or changes the length of a string still goes the old way through hkxpack, because those move
what follows them and every offset in the file is derived from what precedes it. Which path a save
takes is decided by comparing the file as loaded against the file as edited, so an edit that cannot
be written in place is detected rather than attempted.

**The blanket refusal on animation files is gone.** It was there because rebuilding through XML
cannot carry `hkaLosslessCompressedAnimation` intact. Writing values in place never rebuilds
anything, so there is nothing to lose, and all **856** of those files in the vanilla animation
archive now rebuild byte for byte identically. The warning still stands, but only in front of the
rebuild path where it is actually true.

**Verified as a full save, not just as a read.** `symrm savecheck` changes a float, a whole word and
a single byte flag, saves through the new path, and then asks three things of the file that came
out: hkxpack can still read it, every value in it still agrees with our reading, and it differs from
the original only where it was meant to. On Dogmeat's behaviour that is **3 values changed, exactly
3 bytes different in a 238KB file, and 4,678 field values still agreeing with hkxpack**. Across the
behaviour files it is the same story with no disagreements anywhere.

Still to come: confirming in game, which is the gate #19 has been waiting on.

## 2026-08-05, what an undriven channel means

Spline compression defines an undriven channel as no translation, no rotation and unit scale. The
viewer had been substituting the skeleton's reference pose instead, which is right for a bone with no
track at all and wrong for a track that names a channel and drives none of it.

On whole body clips the two readings are the same answer. The bones such a clip leaves undriven are
the ones the rig already places at no offset and no rotation, so nothing moves either way, which is
why this survived being looked at. On additive clips they are not the same answer at all: across
Dogmeat's 237 of them, up to 17 bones sit as much as 17.2 units from where the rig puts them and up
to 20 sit as much as 91.7 degrees away. An additive clip is a delta, so the identity reading is the
one that makes it one.

The change is scoped to spline compression, which is the format that says what it means. Lossless
compression keeps the reference pose, because nothing has shown it means the same thing and guessing
moves bones.

New command, `symrm channels`, which is the measurement above and will run on any rig: how many bone
tracks leave each channel undriven, and how far the reference pose puts those bones from the identity
the format would use.

## 2026-08-05, the mesh, not just the bones

The Playback viewport draws the actual character. Point the tool at a `.nif`, with the Mesh button or
by naming it on the command line beside the `.hkx`, and it is skinned to the Havok skeleton and posed
with the clip. Wireframe rather than shaded, so the same 2D surface the rest of the window uses draws
it and the tool takes on no new dependency.

New readers under `src/Nif`: the Gamebryo packfile header, BSTriShape and BSSubIndexTriShape
geometry, and BSSkin::Instance and BSSkin::BoneData for the weights. Every offset was read off the
game's own files and the arithmetic closes on itself, which is what makes it checkable: the block
table's sizes sum to eight bytes short of the file and those eight are the footer, each shape's
declared dataSize equals its vertex count times its stride plus six per triangle, and BoneData's
block size is four bytes of count plus exactly 68 per bone.

**The fault that would have shipped.** BSSkin::BoneData writes a rotation row by row for column
vectors and System.Numerics multiplies row vectors, so read straight across the two disagree by a
transpose. The mesh came out plausibly shaped and wrongly placed, which reads as a camera problem
rather than a maths one. Picked by measuring rather than reasoning: posing a mesh back onto the
skeleton's own reference pose, which is the pose it is authored on and so must not move it, drifts
0.245 units per vertex transposed against 50 to 107 read straight across, inverted, or both.

That measurement is now part of `symrm mesh` and fails the command rather than printing a warning.
It counts only the vertices every one of whose bones matched the skeleton, which matters: a human
body mesh weights 45 of its 58 bones to skin helper bones no Havok skeleton carries, and counting
those reported the binding gap as though it were a transform fault.

Bone matching is by name and never by position, and a mesh bone with no skeleton bone of that name is
named in the panel and on the console rather than dropped. Vertices weighted only to bones that did
not match stay at their rest position instead of collapsing to the origin.

Also: picking a clip in the Tree tab now loads its pose the same as picking one on the canvas does.
The tree filled only the properties panel, so a clip chosen from that side left the viewport empty
and the tab looked broken.

## 2026-08-05, the animation a clip points at, drawn

Select a clip generator and see what it plays, on its own skeleton, with play, pause, step and a
scrub bar. The rig comes off the project chain rather than the open file, because a behaviour names
no skeleton and the character does.

The split is the same one the node canvas has with `GraphAuthor`: `AnimationPose` composes every bone
position and `SkeletonView` only projects them, so a pose that is wrong is wrong somewhere a headless
check can reach. `symrm pose <skeleton.hkx> <animation.hkx> [frame]` prints exactly what the viewport
draws, and `symrm skeleton` now composes through the same code instead of its own copy of the maths.

**The bug this would have shipped with.** A Havok track drives a channel or leaves it clear, and clear
means the bone keeps its reference pose value there. Both decoders prefill a cleared channel with
zero, identity or one, which is what the engine does to the transform before it fills anything in and
is indistinguishable from a bone genuinely at the origin once it has. Posing on the raw value
collapses every rotation-only bone onto its parent, which is most of a character. `HkxTrackData` now
carries the per-axis mask flags from both decoders and `AnimationPose` falls back per channel.
Pinned by `AClearChannelKeepsTheReferencePose`.

Tracks are not bones either: `transformTrackToBoneIndices` says which bone each track drives, a bone
with no track keeps its reference pose, and an animation authored against a different rig is refused
by name rather than drawn wrong. Vanilla ships plenty of the last case, since a shared behaviour
references per creature animations not every creature has.

Nothing here writes to the document. Scrubbing is a view of a file on disk, so it takes no undo step
and cannot arm Save, which the window checks assert rather than assume.

Also: `symrm extract` pulls anything out of a BA2 by path substring, `--tree` keeping the archive's
own folders, which is what resolving a project chain afterwards needs.

## 2026-08-05, undo, a way out of read only, and reading two files against each other

**Undo and redo.** Every edit used to rewrite the loaded document in place with no way back except
reloading and losing the session. The document is one string, so a step back is a copy of it: every
mutating path now goes through `MainWindow.Commit`, which is the only place the document changes
outside a load, so nothing can edit around the stack. Ctrl+Z and Ctrl+Y, plus buttons, capped at 100
steps. Creating a node and wiring it up is one step, not two, and so is declaring a variable and
binding it. The unsaved marker is measured against what was last written rather than latched on, so
undoing back past a save says the file matches disk instead of claiming there is still work to save.

**Find Java.** When autodetection missed a Java install the window went read only with no way to fix
it from inside. There is now a Find Java button, shown only when Java is what is missing. The pick is
validated by running `-version` rather than accepted on its name, and read only is lifted by redoing
the unpack, not by flipping a flag.

**Compare tab.** Point the object walk the repack guard already uses at somebody else's file instead
of at this file's own repack, and it reads mod conflicts: what each side added, removed, and which
field values differ, with both sides shown. Ids are meaningless across files, so matching is on class
and normalised contents, and renumbering alone reads as no difference. The two sequences resynchronise
with a lookahead, so a file with an object added or removed still lines up after it.

**Where a symbol is used, both directions.** `SymbolIndexFixup` already walked every site; it now
keeps the object id it found each one in, so the Symbols tab lists usages as rows that can be clicked
through to the node instead of as a summary string. The other direction is on the node itself: a
selected node lists the symbols it touches, resolved to their declared names, with an index past the
end of the declared list called out in red.

**Which scripts send an event.** Option three from the closed ticket about events with no sender in
their own file: scan a folder of Papyrus `.psc` sources for `PlayAnimation` and its siblings and show
which scripts name each event. Information, never a verdict, and silent when no folder is set.
Compiled `.pex` is deliberately not read: its string table holds every string the script uses, so
matching against it would claim a sender for names the script only prints.

**Check project.** The same checks, run over every behaviour in the project and reported grouped by
file. A clip playing an animation no file in the chain provides reads as fine one file at a time.

Also: the hkxpack fallback no longer names another project's folder layout, and `LICENSE` is the
canonical MIT text with nothing appended, so licence detectors read it as MIT. The scope note that
used to sit at the bottom of it now heads `THIRD_PARTY_NOTICES.md`.

## 2026-08-05, editing did not work on Windows at all

**Reported from the first beta: every node shows zero editable fields, nodes cannot be connected or
disconnected, and adding one after a delete fails.** All three are the same bug, and it is not in any
of that code.

hkxpack writes the platform's line ending, so an unpacked file on Windows is CRLF throughout. The
regex that finds a parameter is anchored to end of line, and .NET's multiline `$` matches *between*
the `\r` and the `\n`, so `[ \t]*$` could never match a line ending `</hkparam>\r\n`. It matched
nothing. `ReadParams` returned an empty list, which is the zero fields, and `SetParam` threw "no
simple parameter named x", which is every connect, disconnect and attach, since all of them write a
reference through it.

Reading and drawing the graph go through a different parser and were unaffected, which is why the
window looked like it was working. On Linux, where hkxpack writes LF, none of it reproduced, and that
is why it shipped.

Two changes rather than one. The regex tolerates a trailing `\r`, and every read of unpacked XML now
goes through `HkxTextEdit.ReadXml`, which normalises to LF once at the door. The second matters
because every edit in here splices in text of its own: a file read as CRLF and spliced with LF is
half and half, and the regexes that put it back together have to agree with what is already in the
string.

Pinned by `WindowsLineEndingsStillEdit`, which builds the same graph twice, once with each line
ending, and asserts the field list, a field write, and a node connection on both.

## 2026-08-04, review pass before the beta

Four things a read of the session's own changes turned up, all of them things the canvas remembered
when it should not have:

**Opening a second file kept the first one's positions.** Everything the canvas holds is keyed by
object id, and the next file numbers its objects from one as well, so a node dragged in one file
pinned whichever node happened to hold that number in the next. The highlight, the filter and the
marks had the same problem. A load clears all of it now.

**A file with no text form left the last graph on screen.** The canvas is only refilled once a file
has been unpacked, so opening something that cannot be unpacked showed the previous file's nodes
under the new file's name.

**The tree marked no empty states on load.** It was built before the file was unpacked, so the
answer was always "none", and it was only ever right after something else forced a rebuild. It is
built again once the text form exists.

**Typing in the filter reparsed the file.** Working out which states are empty is a question about
the file, not about the filter, and it was being answered on every keystroke: seven megabytes,
six times, to type "Sprint". Six keystrokes now cost 444ms on the weapon behaviour rather than well
over a second. The properties panel also stopped being wiped by typing, which is the last thing you
want when the reason you are searching is the node whose fields are open.

## 2026-08-04, the search box works on the canvas, and the weapon graph is usable

**The filter box only ever drove the tree.** It sits above the tabs, so on the Graph tab typing in it
did nothing at all. It now filters whichever view is showing: matching nodes stay lit, everything else
dims, and a wire touching a match stays lit because where a match connects is the question being
asked. Nodes dim rather than disappear, since a node's place in the graph is most of what it tells
you. Enter moves the view onto the first match and selects it; typing alone does not yank the view
around.

**The canvas drew 400 nodes.** `WeaponBehavior.hkx` lays out 3978, so nine tenths of it was never
drawn and the search could not find a node that is plainly in the file. The cap is 4000 now. Wires
off screen are dropped before their geometry is built, which is what makes that affordable: ten full
redraws of all 3978 nodes measure 240ms.

**Nodes were drawn on top of each other.** A column placed its nodes at row number times *this* node's
height, and a node is as tall as its slot count, so anything shorter than its neighbour overlapped the
one below. Now each column keeps a running offset. On a small graph this was barely visible; on the
weapon graph it was most of why the canvas looked like a mess.

**Opening the weapon behaviour took about two minutes.** The Symbols tab asked "what references this
symbol" one symbol at a time, and each ask rescanned seven megabytes of text: 873 symbols, roughly 110
seconds of pure scanning. One pass builds the whole table now. Selecting a node also parsed the file
twice, once for the fields and once for the status line. The file opens in a couple of seconds.

## 2026-08-04, a new node lands where it was dropped

**Dragging a wire out to empty canvas put the node at the far end of the graph.** The canvas lays
nodes out by their depth from the root, and a node nothing points at yet has no depth, so it went into
a column of its own past everything else, nowhere near the cursor that asked for it. The drop point is
now carried through the menu and pinned before the canvas rebuilds, the same way a node dragged by
hand keeps its place.

**And it is wired into the slot the drag came from.** The slot was being collected and then thrown
away: the new node was attached to whatever happened to be selected, by whichever slot its class would
normally take, so dragging off a clip's `triggers` could hang a generator somewhere else entirely. A
drag now names the slot, and if that slot will not take the node it says so and leaves it unattached
rather than putting it somewhere it was not asked for.

## 2026-08-04, the fields are next to the node now, and one state at a time

**The properties panel was in the wrong tab.** Clicking a node on the canvas filled a panel that only
existed beside the tree, so the fields were built, correct, and invisible unless you switched tabs and
lost the node you were looking at. The panel is now a control rather than a loose stack, and there is
one beside the canvas as well as one beside the tree. Double clicking a node puts the caret in the
first box instead of nudging the node a pixel.

**An empty field could not be given a value.** hkxpack writes an empty string as a self closing tag,
and the reader only matched `<hkparam name="x">value</hkparam>`, so `animationBundleName` and every
other empty field was missing from the panel entirely. Both shapes are now read in one pass, so the
order fields appear in is still the order they sit in the file, and writing an empty value puts the
self closing form back rather than leaving `<hkparam name="x"></hkparam>` behind. Proved by editing
`PipboyBehavior.hkx` #98 through a real hkxpack repack and reading it back: the value survives, and
clearing it repacks clean. Arrays are excluded for free, since a `numelements` attribute sits between
the name and the slash.

**Highlighting one state.** Right click a node, "Highlight the paths of ...", and every wire not
touching it drops to half opacity while unrelated nodes dim to 40%. The lit wires are drawn in a
second pass so they sit on top of the dimmed ones rather than being crossed by them. Escape clears it.
A shipped graph draws a few hundred wires over each other and following one state through that was the
thing the canvas was worst at.

## 2026-08-04, variableBounds is positional after all

**The struct settles it.** The open question was what a short `variableBounds` array keys off, since
`MTBehavior` carries 19 entries against 67 variables and the entries do not all look right for the
variables they would land on. The answer is that it cannot key off anything: `hkbVariableBounds` is 8
bytes holding `min` at offset 0 and `max` at offset 4 and nothing else, read out of the class the
engine registers for it at startup rather than guessed. There is no field in it that could name a
variable, so position is the only key there can be.

So a short array means the variables past its end have no bound, and an unbounded variable inside it
is written `0..0`. Measured over the 531 vanilla files: 224 empty, 17 the same length as the variable
list, 87 shorter. In 85 of those 87 the last entry is a real bound rather than `0..0`, which is what a
trailing-trimmed positional array looks like.

Two statistical attempts to find a different key are recorded as having failed to separate anything:
scoring bounds against the type of the variable at each alignment gives 79.6% for positional and
79.7% for a one-place shift, and scoring against what the variable's name implies gives 33% and 38%.
Neither is evidence, which is why the struct layout is what this rests on.

**A removal was mishandling it, and worse than the ticket assumed.** Removing a variable took its
bound with it only when the array was full length. The audit it tested was taken after the name had
already been removed, so a file with three variables and two bounds looked parallel at that moment
and the bound was removed anyway; removing the last variable then tried to remove a bounds entry that
was never there and threw. Both are fixed by asking the only question that matters, whether the
removed index is inside the array.

Adding was already right and is unchanged: a new variable goes on the end, past a short array, so it
needs no entry.

Still not done, and now the only thing left on the ticket: nothing authors a bound. The window has no
way to set one.

## 2026-08-04, a plain build was silently read only

**The jar the editing layer needs was never copied next to the program.** Only the release zip
carried it, so anything run out of the build directory quietly dropped to read only: the tree still
drew, because that is read straight from the binary, and the Graph, Symbols, Chain and Animation tabs
were simply empty, because all four come from the unpacked text form. Save was off. It looked exactly
like a tool that does not work.

The build copies `tools/hkxpack-cli.jar` and both licence files to the output now, so a build and a
release behave the same way.

The message made it worse. It said editing needs a Java runtime, when Java was installed and present
on PATH the whole time and the jar was the missing half. It names which one is missing now, says the
four tabs are empty because of it and that the tree does not need it, says where to put the jar, and
is drawn in the warning colour rather than as muted text nobody reads. Save's own refusal was
similarly folded into one message and is now two.

## 2026-08-04, check graph marks the canvas

**A finding now points at a node instead of scrolling past in a status line.** Check graph outlines
the node it is about, red for an error and amber for a warning, with a soft halo outside the border so
it is still findable zoomed out where a one pixel edge is one pixel. The problem list under the canvas
names every finding, and clicking a row centres the view on that node and selects it, which is the
part that matters: the node that is wrong is almost always the one off screen.

Getting there needed the findings to know what they were about. Every one already started its location
with the object id, so a `Finding` now carries that id, taken from the text rather than threaded
through the forty odd places that build one. Errors beat warnings on the same node, or a node with one
of each would draw amber and read as something that can be left alone.

Measured rather than assumed: over the 531 vanilla behaviour files the checker produces 208 findings
and **all 208 can be placed on a node**. It was 197 before this. The last 11 were symbol index
references past the end of the declared list, which named the class and the member but not which of
the file's objects carried it, so the one fault nobody could locate was the one that needed locating
most. The scanner tracks the enclosing object now.

Marks are kept across rebuilds, so fixing one thing does not silently clear the rest of the list.

## 2026-08-04, the refusal now says which state and what to do

Save blocking a file the game cannot load is right, but the first version of that block only said how
many states were empty and told you to go and run Check graph. Being stopped without being told which
state, or what to do about it, is worse than not checking at all: it turns a two second fix into a
hunt through the tree.

It now names them, with the machine each one sits in, four at a time and a count for the rest, and
spells out both ways out: give the state a generator, or delete the state. Check graph's own wording
carries the same advice, so the two do not read differently for the same fault.

Nothing about when it refuses changed. Four checks hold the message to naming the state, naming its
machine, and offering both fixes, and all four fail if the message goes back to a bare count.

## 2026-08-04, the state resolution figure, measured over the whole game

**All 531 behaviour files, all 5329 states, nothing unresolved.** The README had been quoting a
subset, 314 files and 4881 states, because that was what happened to be extracted at the time, and
before that it quoted "5292 of 5323" from a document in another repository that nobody could check
from here. Both are replaced by a number this repo can reproduce.

    5329 states across 531 files
      0 with no generator
      0 pointing at an object not in the file
      15 generator classes

The 31 that supposedly did not resolve were never a reading failure. That figure came from
OpenCommonwealth's whole-library conversion run, where "understand" meant "map to a Godot animation
node", and its own numbers name the cause: 34 unmapped generators, all `BSBehaviorGraphSwapGenerator`
with a null `pDefaultGenerator`. Counting those here gives 34 out of 34, exactly, which is what
settles it. This reader parses every one; the Godot converter had nothing to point them at. The 31
was that 34 arrived at by subtraction against a different denominator.

`symrm states` is the new command, so the claim in the README is re-runnable rather than asserted.
It walks with `BehaviourGraphModel` and `StateEditor`, the same code the window uses, which is the
point: a separate script agreeing with itself proves nothing about the tool. An independent walk over
the raw XML was run alongside it and agrees on all four numbers.

## 2026-08-04, a state with no generator crashes the game

**Fallout 4 crashes while loading a graph that contains one**, so Save refuses to write the file
instead of warning about it. The tree and the graph still mark the state, and Check graph still names
it, but nothing reaches disk while one exists.

That was the open question on the ticket. Marking a state is easy; deciding whether an empty state is
a mistake worth blocking on was not, because no file with one had ever been in front of the game. It
has now. The Red Rocket garage door's `Closed` state had its generator link cleared through the
tool's own unlink and nothing else touched: 30 objects in, 30 objects out, same 7 states and 11
events as vanilla. Approaching the door takes the game down.

**It crashes on the load, not on entering the state**, which is the part worth keeping. The crash log
puts it in `BShkbUtils::GraphTraverser::Next` at `Fallout4.exe+0x1705DDF`, an access violation
reading address 0, under `LoadBehaviorHelper` → `BShkbAnimationGraph::InitImpl` →
`QueuedReference::BackgroundClone`, with `GenericBehaviors\SpecialCaseDoors\SpecialCaseDoors.hkx` on
the stack. The disassembly says why: the traverser pops each child a node reports off its own stack
and immediately reads that pointer's vtable to make a virtual call, with no null check anywhere on
the path. A null child is dereferenced as soon as the walk reaches it.

So reachability is beside the point. A state nothing can enter still kills the file, which is a
stronger rule than the one the tool was about to ship, and it is why the refusal does not ask whether
anything targets the state.

Unlink rather than delete on purpose. Deleting the orphan would also have exercised object removal
and renumbering, which are separately unproven, and a crash would then have had two candidates. The
only two crashes of this signature in the log are the two from this test; the one before it is an
unrelated CEF breakpoint from a week earlier.

The refusal and the mark both come from `GraphValidator.StatesWithNoGenerator`, so they cannot
disagree about what empty means, and five checks hold the refusal to saying what it is refusing and
why. Vanilla is unaffected: all 4881 states across 314 files have a generator, so a mark only ever
means an edit produced it.
## 2026-08-04, first edit to run in the game

**The Red Rocket gas station garage door was edited with this tool, loaded by Fallout 4, and did what
the edit asked.** It sat permanently half open, with no interaction needed, which is the whole point
of that particular test: a door cannot end up half open by accident, so the signal cannot be confused
with a broken mod.

The edit was three scalar values on one existing object, the `Closed` state's sequence generator:

    pSequence           Closed  ->  Opening
    eUseTimePercentage  NOT_USING_TIME_PERCENTAGE  ->  USING_TIME_PERCENTAGE
    fTimePercent        0.0  ->  0.5

The file keeps vanilla's 30 objects, 7 states, 11 events and byte size. So what the engine accepted is
a field value edit on an existing object, written here and repacked by hkxpack.

Everything before this was proven one step short of that: repack, read the binary back, count the
objects, run the validator. All of that says the file is well formed. None of it says the engine will
load it, and the README carried "none of it has been loaded by Fallout 4" as a standing caveat since
the tool was split out.

That caveat is now narrower, and only in one direction. Structural editing is still untested against
the game: adding a state, removing one, retargeting a transition and renumbering a symbol have never
been in front of it, and neither has `symrm door`'s additive edit. The `.bak` is still worth keeping.

## 2026-08-04, the Pip-Boy's unused variables

**`iTabSync` and `iCatSync` are declared and never used, by anything.** They looked like the obvious
drivers of the Pip-Boy's tab and category switching, and the Symbols tab showing an empty "Used by"
column for both read as the tool missing a route rather than as the answer.

Searched three places, case insensitively: the behaviour file binds neither, the 1.10.163 unpacked
binary contains neither byte sequence anywhere in 65 MB, and no vanilla Papyrus script mentions
either, across all 8570 entries of `Fallout4 - Misc.ba2` decompressed and searched. The same pass
finds `PlayAnimation` in 220 scripts, so the search works.

The contrast is what settles it. `fRadLevel` and `fRadioTune`, the two the file does bind, are both
literals in `.rdata` beside the Pip-Boy's INI settings, and `PipboyManager::SetInputGraphVariables`
passes them to `SetGraphVariableFloat` by name. The by-name mechanism exists and is in use for exactly
two of the four variables. The other two never appear.

So the tab switching is event driven, which the file states outright, and the Symbols tab's wording
was right all along: an empty column means nothing in this file reads it, not that the symbol is dead.
For these two it happens to be both.

Recorded with its own caveat: a name assembled at runtime, or one written by a mod, would slip past a
byte search of the binary. Neither is plausible here and neither can be ruled out without reading the
values from a running game.

## 2026-08-04, lossless scale

**The lossless scale path is confirmed against the engine.** It could not be checked against game
data, because no vanilla animation of that class carries a scale, so it was checked against
`hkaLosslessCompressedAnimation::getFrameTransform` in the 1.10.163 unpacked binary instead.

Every point agrees: the scale word array at `+0xb8`, static values at `+0xa8`, dynamic at `+0x98`,
stride as the dynamic array's length over the frame count at `+0xd8`, and the same `(offset << 2) |
type` packing that `::getType` and `::getOffset` apply, four fields to a 64 bit word. Dynamic indexing
is `offset + frame * stride`, frame major, the same trap that nearly shipped on translations.

The one that mattered most: what a clear word means. The engine prefills the output transform before
touching any of it, with translation 0, rotation identity and scale 1,1,1,1, from a constant at
0x143828480 that reads as four ones. So returning 1,1,1 for a clear scale is the engine's answer, not
a convenient default, and a scale falling back to 0 would have collapsed whatever it drives.

13 new checks hold the reader to those rules, including the field above bit 32 that hkxpack's XML
drops, so the packing cannot drift back to a guess. The README no longer calls this unproven, but it
still says plainly that no real file has ever exercised it.

## 2026-08-04, frame browser

**The animation tab answers the question a variable driven clip asks.** Type a
`userControlledTimeFraction`, and it says which frame that is, moves the page to it and marks the row.
Previously that mapping existed only in `symrm frames`, printed for five fixed fractions, which is not
much use when you are aiming a Pip-Boy needle at a pose. It now lives in `HkxAnimationData.FrameAt` so
the window and the harness share one implementation rather than two that can drift.

**A bone filter**, because a character animation has 95 tracks and reading one bone's motion should
not mean scrolling past 94 others. Filtering also expands what it finds, so a search lands on frames
rather than on a collapsed row.

Nonsense in the fraction box is refused and says so rather than aiming at something. Out of range is
clamped, since the value comes from a graph variable and wrapping to the other end of the clip would
be worse than pinning to the nearest.

Checked against real files: fraction 1 on `Idle_TrainTrain_Song05` lands on frame 3684 of 3684 and
jumps 13 pages to get there, and 0 and 1 land on the ends of every file tested.

## 2026-08-04, scale

**Animation scale is decoded, shown, and checked against real data.** It was being decoded all along
and then printed nowhere: the tab had columns for position and rotation only, and `symrm frames`
counted scales without ever showing one. A wrong value and a right one looked identical, which is not
a decode anyone should trust.

There is a Scale column now, and `frames` prints it, on the tracks that carry one. Almost every track
in the game is a flat 1,1,1, so printing all of them would bury the ones that are not.

Checked, rather than assumed. 130 of the 13133 vanilla spline compressed animations scale something,
none of them contains a zero, and the values are the shape authored data takes: the crow's
`PerchedIdle` folds both wings to exactly 0.4599 on all three axes, left and right identical. Those
float32s are in the file at 0x714 and 0x794, so the static branch is confirmed against the raw bytes
rather than against itself.

The lossless branch is still unproven and the README now says so plainly. All 856 vanilla lossless
animations leave both scale arrays empty with every scale word clear, so only the clear case has ever
run. It returns 1,1,1 there, which is correct, but nothing in the game exercises static or dynamic
scale. `symrm scale` is the sweep that produced these numbers.

## 2026-08-04, later still

**Expanding an event says what the file does with it.** Raised here, listened for here, or written
somewhere with no established direction, each naming the class and member rather than the struct that
carries it: every clip trigger and every alarm is an `hkbEventProperty`, so that name separates
nothing.

No verdict comes with it, which was the decision on the ticket. An event listened for with nothing in
the file sending it is the ordinary case, not a fault: 2912 of the 4799 events used across the 314
vanilla behaviour files look exactly like that, because Papyrus and the engine send them by name from
outside. A check would be wrong more often than right.

The role table was enumerated rather than recalled. Those files write an event index in 43 distinct
class and member pairs and all 43 are listed, so the only thing reporting as "referenced" on vanilla
data is `BSLimbCycleModifier`. Anything outside the table reports the same way instead of being
assigned a direction. `symrm events` reprints the measurement over a directory.

Found on the way: state enter and exit notify events were invisible. They sit inline in
`hkbStateMachineEventPropertyArray` with no class attribute of their own, so the reference walk never
saw them, in 2804 places across the vanilla corpus. That hid them from the Used by column and, worse,
from renumbering, so removing an event left every notify event above it pointing one too high. Both
are fixed.

## 2026-08-04, later

**Check graph now finds a state nothing can enter.** Being referenced and being reachable are
different questions for a state: a machine always lists its own states, so the unattached check could
never see one that no transition targets. That is what the door edit produced, and the checker had
nothing to say about it.

The ticket asked for this as an error, on the grounds that a dead state is always a mistake. Vanilla
says otherwise, so it ships as a warning. Swept over all 328 behaviours: 477 hits, dominated by
`RagdollAndGetUp`, the `SharedCore` wrapper state and `PairedState`. Those are entered by the game,
not by the graph, and nothing in the file describes how. Checked and ruled out on samples:
`startStateIdSelector` is null, `startStateId` is not variable bound, and all four of Havok's implicit
transition event ids are -1. Skipping machines that have no transitions at all, which are engine
driven by definition, takes it to 123 across 56 files. States named as a `toNestedStateId` target are
exempt too, since a parent machine can enter a nested state directly.

Two independent implementations of the reachability walk, one in the validator and one throwaway in
Python, agree on the same set, so the count is the data rather than a bug in the walk.

## 2026-08-04

Two checks that need something outside the single file, which is why the validator never had them.

**A clip's animation is now checked against the folder on disk.** Getting there meant fixing the
chain first: it read the animation list from `animationNames`, which is a Skyrim field. Fallout 4
puts them in `animationBundleNameData`, so the Chain tab's animation list had been empty for every
vanilla file it had ever been pointed at, and nothing downstream could have checked anything.

Swept over the whole of `Fallout4 - Animations.ba2`: 215 project roots, 328 behaviours, 111 clips
either missing their animation on disk or playing one the character does not declare. Those are real
rather than false alarms, so both are warnings and not errors. Shared behaviours reference per
creature animations that not every creature has, and some clips point at content that never shipped
in any form. Dogmeat's behaviour plays `Animations\WalkForward_B.hkt` and there is no such file.

**Save verifies the repack before overwriting anything.** hkxpack renumbers every object, so a
repack cannot be compared by id, but the object count and the multiset of class names have to come
back identical. They are compared now, on the file hkxpack actually produced, before the original is
touched. A short file is refused and named rather than written.

`symrm anims` and `symrm repack` run both from a clone. `anims` takes a directory to sweep every
project root beneath it, which is where the numbers above come from.

## cac7b09, 2026-07-30

Door graph editing, symbol removal and a validator. One squashed commit covering the session.

**Doors are driven by events, not variables.** Every animated door, lift, periscope and switch
checked declares no graph variables at all. They are state machines, and Papyrus sends the events:
`ObjectReference.PlayAnimation` takes the name of the event to send to the object's animation graph,
and 177 vanilla base scripts call it or `PlayAnimationAndWait`. The names line up on both sides, so
`DN151_DoorSeal` sending `StartOpen` and `Open` reaches a graph that declares exactly those. The
Pip-Boy pattern of binding a variable to `userControlledTimeFraction` is for gauges and dials, and is
the wrong tool for a door.

**The SpecialCaseDoors edit.** `symrm door` adds `StartOpen` and `StartClosed`, which that behaviour
does not have. `StartOpen` goes straight to the held `Opened` pose, the way `SwitchDoorExLarge01`
does it, so a door placed open is simply open rather than animating itself while the cell loads.
`StartClosed` plays its sequence and settles. No existing transition is retargeted, because those
event ids are shared by every door built on this behaviour. 30 objects, 7 states, 10 transitions and
11 events become 33, 8, 13 and 13.

An earlier version of that edit built a `StartOpening` state for `StartOpen` to enter. Once
`StartOpen` was pointed at the existing posed state instead, that state had nothing pointing at it
and duplicated the `Open` state the graph already had, so it is no longer created. The checker did
not catch it while it existed, which is filed as issue 12: an unreachable state is still a referenced
object, because the machine lists it.

**Also in this commit.** Nodes can be added from the graph view. Variables and events can be removed,
renumbering every reference above them. `AddVariable` now writes `variableBounds`, a fourth parallel
array it had been skipping. `GraphValidator` and the Check graph button. `tools/symrm`, the harness
that produces the numbers quoted here, so they can be re-run from a clone.

**Not verified in game.** Everything above is proven against hkxpack round trips and the validator.
No file produced by this tool has been loaded by Fallout 4.

Session notes for this work, including the reasoning that did not belong in commit messages, are
recorded outside the repository in the assistant's own store rather than here.
