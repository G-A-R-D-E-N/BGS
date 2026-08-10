# Expression Modifier Runtime Design

## Goal

Complete the remaining runtime-evaluation scope of work item 37: execute active Fallout 4 `hkbEvaluateExpressionModifier` assignments against the simulator's runtime variable map on each simulated time advance.

## Observed format and scope

The focused simulator preflight remains covered by the native regression suite: event routing, conditional variables, timed blends, clip-end events, and active-state tracking all execute before this change.

In the inspected Dogmeat assets, an `hkbEvaluateExpressionModifier` owns its expression array through `expressions`. The referenced `hkbExpressionDataArray` stores ordered `hkbExpressionData` records in `expressionsData`; each record's `expression` is the source text. The observed `assignmentVariableIndex` values are `-1`, and every expression target and source is already a declared graph variable. This implementation therefore uses expression names as the observed source of assignment identity and does not invent an index-based alternative.

The Dogmeat files contain 17 active assignment lines. They require assignment, numeric literals, names, unary signs, parentheses, arithmetic `+`, `-`, `*`, `/`, comparisons, `&&`, `||`, `!`, and the `clamp(value, low, high)` and `cond(test, whenTrue, whenFalse)` functions. This is the supported subset. It is deliberately not a general Havok scripting implementation.

## Runtime model

`Expression` becomes a single parser for both transition predicates and numeric assignment expressions. Its parsed tree gains arithmetic and function nodes. The existing truth evaluator preserves its three-valued condition semantics: malformed or unreadable transition conditions remain undecided, which means the simulator does not incorrectly hold a route back.

Numeric evaluation returns either a number or a precise refusal. An assignment can run only when its parse tree is an assignment, every read name resolves in the runtime variable map, the target is a declared variable, and the expression produces a finite number. Division by zero and unsupported syntax are refusals, not guessed values.

`GraphRun` discovers expression modifiers only while walking the active generator tree. It records their expression records in deterministic traversal and array order. On `Advance(seconds)`, after blend clocks advance and before clip triggers are sent, it writes the elapsed seconds to declared `fTimeStep` when present and evaluates each active record in order. An assignment is visible to the next record in the same tick. Re-entering a state rebuilds the active modifier list along with the active clip list.

Unsupported or unreadable expression records leave the variable map unchanged and are exposed as structured runtime stops that name the modifier, array position, source text, and refusal. The simulator continues with later expressions and other runtime behavior. This makes the limitation observable without pretending to know Havok's answer.

## UI and pose boundary

The existing runtime variable controls continue to read and set the same `GraphRun` variable map, so an evaluated output is available through `ValueOf`, the runtime variable picker, conditions, and existing debug consumers. The current skeleton viewport does not apply behavior-graph variables to bone transforms. This work therefore proves updated pose-control values such as `HeadXTwist` and `SpineXTwist`, but does not claim skeletal deformation or Havok pose rendering.

## Tests and acceptance

Tests are written before implementation in `tools/symrm/Tests.cs`. They cover parsing and evaluation of arithmetic and functions; sequential assignment; input changes; unknown names, zero division, and unsupported functions; active-only execution; and no effect on transition routing.

The corpus tool extends its expression reporting to parse the observed assignment subset and distinguish evaluated records from unsupported ones. Disposable acceptance copies of Dogmeat behavior assets prove that the shipped sources remain unchanged while `fTimeStep` and an input change drive real head or spine control variables during a simulated tick.

## Explicit boundaries

- No source HKX is written or mutated by a simulation run.
- No unsupported expression is assigned an invented value.
- No theoretical Havok grammar, event-expression behavior, property binding, or bone deformation is added without observed data.
- Existing condition behavior and state routing retain their current semantics.
