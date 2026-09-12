# Havok tagfile reader evidence

This slice salvages the tagfile reader from the legacy BGS sweep without merging that branch's obsolete UI and runtime integration.

## Measured scope

The original corpus work used Fallout 76 character assets. Those files identify themselves as Havok SDK `20150100`, which is Havok 2015.1.0. This is not evidence for Havok 2018 and does not complete issue #115.

The measured packed-integer forms are one through five bytes wide. The five-byte form carries a full 32-bit value and is required for values such as the `0x7FFFFFFF` sentinel. The TYPE section supplies type names, field names, template arguments, and layout bodies so the file can describe class layouts without a separate Fallout 4 class table.

## Fail-closed boundary

TNAM and TBOD are independent sections. A packed integer that begins inside one of those sections must fit entirely inside that same section. Reading continuation bytes from the next sibling section would turn unrelated section bytes into type or layout data.

The proof tests intentionally place 2-, 3-, 4-, and 5-byte packed encodings at the final byte of TNAM with a following sibling section. They also cover TBOD. The initial proof commit preserves the legacy unbounded reader so these regressions must fail before the production fix is applied.
