# Expression Modifier Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Evaluate the active Fallout 4 expression-modifier assignments required by work item 37 without changing HKX source data or existing transition semantics.

**Architecture:** Extend `Expression` with a numeric AST and evaluator while retaining its existing three-valued predicate API. `GraphRun` collects expressions only from active `hkbEvaluateExpressionModifier` nodes and applies them in traversal and array order during `Advance`. `symrm` tests use hand-built graphs for deterministic semantics and disposable Dogmeat copies for real-asset acceptance.

**Tech Stack:** C# 12, .NET 8, native HKX parser, Avalonia desktop application, `symrm` regression harness.

## Global Constraints

- Support only grammar observed in the inspected Fallout 4 Dogmeat expressions: assignment, numeric literals, names, unary signs, parentheses, arithmetic `+ - * /`, comparisons, `&& || !`, `clamp`, and `cond`.
- Simulations must never write, mutate, or replace an HKX file.
- Unreadable input, zero division, unknown names, and unsupported syntax must leave runtime values unchanged and be reported precisely.
- Preserve existing transition-condition semantics: unknown conditions do not hold transitions back.
- Evaluate only expression modifiers on the active generator path, in deterministic generator traversal and `expressionsData` array order.
- Do not claim that expression values deform the skeleton; current scope is runtime control values.

---

## File Structure

- `src/Hkx/Expression.cs`: expression tokenization, parsing, numeric evaluation, and existing predicate evaluation.
- `src/Hkx/GraphRun.cs`: active expression modifier discovery, per-tick execution, runtime diagnostics, and public inspection surface.
- `tools/symrm/Tests.cs`: expression and graph-run regression fixtures.
- `tools/symrm/Program.cs`: corpus measurement for parsed and unsupported expression-modifier records.
- `docs/superpowers/specs/2026-08-10-expression-modifier-runtime-design.md`: approved behavior boundary and observed data basis.

### Task 1: Add numeric expression evaluation

**Files:**
- Modify: `src/Hkx/Expression.cs`
- Test: `tools/symrm/Tests.cs`

**Interfaces:**
- Produces: `Expression.Numeric(Parsed parsed, Func<string, double?> value)` returning a numeric result and a refusal string when no number can be produced.
- Produces: assignment parsing for `target = expression`, arithmetic precedence, `clamp(value, low, high)`, and `cond(test, trueValue, falseValue)`.
- Consumes: the existing `Expression.Parse` and `Expression.Evaluate` condition APIs without changing their observable result.

- [ ] **Step 1: Write the failing parser and evaluator checks**

  Add `AnExpressionAssignmentCanDoTheArithmeticWeShip` to the `Tests.All` list and define it beside `AConditionSaysWhatItSays`:

  ```csharp
  var variables = new Dictionary<string, double>
  {
      ["Speed"] = 8, ["Gain"] = 0.5, ["Limit"] = 3,
  };
  var parsed = Expression.Parse("Out = clamp(Speed * Gain + 1, -Limit, Limit)");
  CheckTrue("the assignment parses", parsed.Ok && parsed.IsAssignment);
  Check("the expression produces a number", 3d,
        Expression.Numeric(parsed, name => variables.GetValueOrDefault(name))!.Value);
  ```

  Add equivalent assertions for `cond(Speed > 5, 7, 9)`, sequentially readable names, unknown function `lerp`, and division by zero.

- [ ] **Step 2: Run the focused check and confirm it fails for missing numeric evaluation**

  Run:

  ```bash
  /home/ricky/.dotnet/dotnet run --project tools/symrm/symrm.csproj -- test
  ```

  Expected: the new check fails because `Expression.Numeric` does not exist or cannot parse arithmetic and function calls. Existing checks may still run first.

- [ ] **Step 3: Implement the smallest complete observed grammar**

  Add AST records for binary arithmetic and calls. Replace the single `Compare` parsing level with precedence levels in this order: assignment, logical-or, logical-and, comparison, addition/subtraction, multiplication/division, unary, primary. Parse function arguments between parentheses and reject a function name or arity outside `clamp` and `cond`.

  Implement `Numeric` recursively. It must:

  ```csharp
  // Names are resolved only through the supplied runtime map.
  // "cond" evaluates its predicate with existing three-valued truth semantics.
  // A non-true cond predicate takes the false arm only when it is False;
  // Unknown returns a refusal rather than inventing an arm.
  // Division by zero and non-finite output return a refusal.
  ```

  Keep `Evaluate(Parsed, value)` restricted to non-assignment predicates. Its existing `Unknown` behavior remains unchanged.

- [ ] **Step 4: Run the focused regression and confirm the new behavior passes**

  Run:

  ```bash
  /home/ricky/.dotnet/dotnet run --project tools/symrm/symrm.csproj -- test
  ```

  Expected: `AnExpressionAssignmentCanDoTheArithmeticWeShip` passes and the suite has 0 failures.

- [ ] **Step 5: Commit the parser slice**

  ```bash
  git add src/Hkx/Expression.cs tools/symrm/Tests.cs
  git commit -m "feat(graph): evaluate expression assignments"
  ```

### Task 2: Run active expression modifiers every simulation tick

**Files:**
- Modify: `src/Hkx/GraphRun.cs`
- Modify: `tools/symrm/Tests.cs`

**Interfaces:**
- Consumes: `Expression.Numeric`, `BehaviourGraphModel`, active generator traversal, and graph variable names.
- Produces: `GraphRun.ExpressionFailures` with modifier ID, expression array index, source text, and refusal.
- Produces: evaluated graph variables from `GraphRun.ValueOf` after `Advance(seconds)`.

- [ ] **Step 1: Write the failing graph-run fixture**

  Add `AnActiveExpressionModifierUpdatesRuntimeVariables` and an XML fixture containing a state generator path with an `hkbModifierGenerator`, a real `hkbEvaluateExpressionModifier`, one `hkbExpressionDataArray`, and two ordered `hkbExpressionData` entries:

  ```xml
  <hkparam name="expression">fTimeStep = 99</hkparam>
  <hkparam name="expression">HeadXTwist = clamp(HeadXTwist + Input * fTimeStep, -2, 2)</hkparam>
  ```

  Initialise `Input=0.5` and `HeadXTwist=0`, call `run.Advance(0.1f)`, and assert `fTimeStep == 0.1` and `HeadXTwist == 0.05`. Change `Input` through `run.Set`, advance again, and assert the output changes. Add a transition event to the fixture and assert it still routes after expression evaluation.

  Add a second inactive modifier branch whose assignment writes a sentinel. Assert the sentinel is unchanged. Add an unsupported `lerp` expression and assert its target stays unchanged while `ExpressionFailures` names the exact source line.

- [ ] **Step 2: Run the focused check and confirm it fails because GraphRun does not execute modifiers**

  Run:

  ```bash
  /home/ricky/.dotnet/dotnet run --project tools/symrm/symrm.csproj -- test
  ```

  Expected: the output variable remains at its original value and no expression failure surface exists yet.

- [ ] **Step 3: Implement active discovery and deterministic execution**

  In `GraphRun`, introduce an internal active-expression record containing the modifier object ID, array index, source text, and parsed expression. When `Enter` encounters `hkbEvaluateExpressionModifier`, resolve its `expressions` reference and append `expressionsData` rows in source order. Reset this list alongside `_playing` during `Rebuild`.

  At the beginning of `Advance(seconds)` after rejecting non-positive intervals:

  ```csharp
  if (_variableTypes.ContainsKey("fTimeStep")) _variables["fTimeStep"] = seconds;
  foreach (var expression in _activeExpressions)
      Apply(expression); // update _variables immediately on success
  ```

  `Apply` accepts only a parsed assignment to a declared variable and a finite numeric result. It appends an `ExpressionFailure` on refusal, deduplicated by modifier and array index for the active span, and continues to the next expression. Do not create names absent from `_variableTypes`.

- [ ] **Step 4: Run the focused regression and confirm runtime behavior passes**

  Run:

  ```bash
  /home/ricky/.dotnet/dotnet run --project tools/symrm/symrm.csproj -- test
  ```

  Expected: ordered assignment, changing input, active-only execution, graceful unsupported handling, and event routing checks pass with 0 suite failures.

- [ ] **Step 5: Commit the runtime slice**

  ```bash
  git add src/Hkx/GraphRun.cs tools/symrm/Tests.cs
  git commit -m "feat(graph): run active expression modifiers"
  ```

### Task 3: Measure real data and perform disposable acceptance

**Files:**
- Modify: `tools/symrm/Program.cs`
- Modify: `tools/symrm/Tests.cs`

**Interfaces:**
- Consumes: `Expression.Parse`, `Expression.Numeric`, and native `PackfileObjects` expression arrays.
- Produces: `symrm conditions` totals for expression modifier records that parse, evaluate from initial values, or are unsupported, with concise example refusals.

- [ ] **Step 1: Write the failing corpus reporting expectation**

  Add a `Conditions`-style helper test using the existing expression fixture. It must assert that a parsed assignment is counted separately from a predicate and that an unsupported function is reported rather than counted as evaluated.

- [ ] **Step 2: Run the test and confirm the current reporting has no evaluation totals**

  Run:

  ```bash
  /home/ricky/.dotnet/dotnet run --project tools/symrm/symrm.csproj -- test
  ```

  Expected: the test fails because `Conditions` only lists raw modifier lines.

- [ ] **Step 3: Add observed-subset reporting without changing simulator behavior**

  In `Program.Conditions`, parse each `hkbExpressionData.expression`. Count assignment records whose targets and source names are in that file's variable table, and evaluate them against a copy of the table in array order. Report unsupported records with the parser or evaluator refusal. Keep existing condition totals and exit behavior intact.

- [ ] **Step 4: Run focused regression and real-asset measurement**

  Run:

  ```bash
  /home/ricky/.dotnet/dotnet run --project tools/symrm/symrm.csproj -- test
  /home/ricky/.dotnet/dotnet run --project tools/symrm/symrm.csproj -- conditions dist/examples/Dogmeat
  ```

  Expected: all regression checks pass, and Dogmeat reports all 17 expression lines with their supported or unsupported state.

- [ ] **Step 5: Run disposable real-HKX acceptance**

  Create a temporary directory using `mktemp -d`, copy `dist/examples/Dogmeat/Behaviors/DogmeatRoot.hkx` and `DogmeatDefault.hkx` into it with their needed sibling assets, and record SHA-256 hashes of the originals before and after. Run `symrm conditions` on the copy and a small temporary native harness that loads the copied graph, advances it, changes an input, advances again, and prints `HeadXTwist` or `SpineXTwist`. Delete only the explicitly created temporary directory after the hashes match.

  Expected: sources are byte-identical, values change after the tick and input change, no graph mutation occurs, and unsupported records do not crash the run.

- [ ] **Step 6: Commit the measurement slice**

  ```bash
  git add tools/symrm/Program.cs tools/symrm/Tests.cs
  git commit -m "test(graph): measure expression modifier coverage"
  ```

### Task 4: Run final project verification

**Files:**
- Verify only.

- [ ] **Step 1: Run native, unit, UI, and build gates**

  Run:

  ```bash
  /home/ricky/.dotnet/dotnet run --project tools/symrm/symrm.csproj -- test
  /home/ricky/.dotnet/dotnet test tools/tests/BehaviourGraph.Tests.csproj
  /home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj -- dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx --event
  /home/ricky/.dotnet/dotnet build app/BehaviourStudio.csproj
  git diff --check
  ```

  Expected: every command exits 0; report exact check/test totals and any environmental skips separately from test failures.

- [ ] **Step 2: Inspect delivery state**

  Run:

  ```bash
  git status --short --branch
  git log --oneline gitlab/main..HEAD
  ```

  Expected: only intended commits and files are present. Do not push, update, or close the work item without the user's separate authorization.

## Plan Self-Review

- Spec coverage: Tasks 1 and 2 implement the observed grammar, active-only cadence, deterministic assignment, safe refusals, and runtime-value boundary. Task 3 provides corpus and disposable-real-HKX evidence. Task 4 provides project gates.
- Placeholder scan: no deferred behavior or unspecified error handling remains; supported grammar, commands, and expected outputs are explicit.
- Type consistency: `Expression.Numeric` is the parser-to-runner numeric API; `GraphRun.ExpressionFailures` is the runner-to-UI/debug diagnostic API. No task relies on an undefined production interface.
