# Unreachable State Check Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make issue #50's unreachable-state warning follow the state-machine entry paths and nested-target validity that Fallout 4's shipped files actually use.

**Architecture:** `StateEditor.TransitionRow` becomes the single parsed transition shape, including priority and flags. `symrm nesting` counts the three nested-target categories directly from machine-scoped rows. `GraphValidator.CheckReachableStates` then seeds or suppresses reachability per machine and resolves flagged nested targets only into the machine under the entered state.

**Tech Stack:** .NET 8, C#, the in-memory `symrm` regression harness, the 453-file `dist/examples` corpus.

## Global Constraints

- Measure the real files before changing code. The recorded baseline is 453 parsed files, 181 transitions, 15 carrying `0x2000`, 0 nonzero nested targets without `0x2000`, and 2 zero nested targets with `0x2000`.
- Test first and verify each test fails for the intended missing behavior.
- Mutation-test every new assertion by temporarily breaking the production branch it covers and confirming the test fails.
- Keep `GraphAuthor.PointsAt` and issues #51, #46, #37, #28, #19, and #8 out of scope.
- Run all four user-specified gates before every commit.

---

### Task 1: Surface transition priority and flags

**Files:**
- Modify: `src/Hkx/StateEditor.cs`
- Modify: `tools/symrm/Tests.cs`
- Modify: `tools/symrm/Program.cs`

**Interfaces:**
- Consumes: `StateEditor.Transitions(BehaviourGraphModel, string)` and each transition struct's `priority` and `flags` text.
- Produces: `TransitionRow.Priority`, `TransitionRow.Flags`, and `TransitionRow.HasFlag(int)`.

- [ ] **Step 1: Write a failing `TransitionRowsCarryPriorityAndFlags` regression using a literal transition with priority 7 and flags 8192.**

- [ ] **Step 2: Run only `TransitionRowsCarryPriorityAndFlags` and verify it fails because `TransitionRow` does not expose those values.**

- [ ] **Step 3: Parse `priority` and `flags` into the row and add a bitwise `HasFlag(int)` helper.**

- [ ] **Step 4: Make `symrm nesting` count `0x2000`, nonzero-without-flag, and zero-with-flag directly from the rows owned by each machine.**

- [ ] **Step 5: Run the focused regression and `symrm nesting dist/examples`; verify 15, 0, and 2.**

- [ ] **Step 6: Mutation-check by making `HasFlag` return false, verify the focused regression fails, restore it, and verify green.**

### Task 2: Correct machine entry and nested-target reachability

**Files:**
- Modify: `src/Hkx/GraphValidator.cs`
- Modify: `src/Hkx/StateRoutes.cs`
- Modify: `tools/symrm/Tests.cs`

**Interfaces:**
- Consumes: `TransitionRow.HasFlag(0x2000)`, `startStateMode`, `startStateIdSelector`, `transitionToNextHigherStateEventId`, `transitionToNextLowerStateEventId`, state `enable`, and `StateRoutes.MachineUnder`.
- Produces: machine-scoped reachable-state seeds and flagged nested targets, with warnings only for states that cannot be entered through paths represented by this file.

- [ ] **Step 1: Extend `AnUnreachableStateIsReported` with failing literal cases for random start, sync/chooser/selector entry, next-higher and next-lower events, and nested state 0 scoped to its actual child machine.**

- [ ] **Step 2: Run only `AnUnreachableStateIsReported` and verify each new assertion fails for the intended old reachability rule.**

- [ ] **Step 3: Seed random mode with every enabled state; skip claims for sync, chooser, selector, or next-state-event machines where the file does not determine entry.**

- [ ] **Step 4: Replace the file-wide nonzero nested-id set with machine-scoped targets resolved from transitions carrying `0x2000`, including nested state 0.**

- [ ] **Step 5: Run the focused regression and verify green.**

- [ ] **Step 6: Mutation-check the random-mode branch, one external-entry branch, the `0x2000` test, the nested target zero case, and machine scoping; confirm the regression goes red for each mutation and restore after each.**

- [ ] **Step 7: Run `symrm check` on the available unpacked corpus if present and compare warning counts; do not claim a count if the complete XML corpus is absent.**

### Task 3: Verify and commit issue #50

**Files:**
- Modify: `docs/superpowers/plans/2026-08-09-unreachable-state-check.md` only to mark completed steps if useful.

**Interfaces:**
- Consumes: completed Tasks 1 and 2.
- Produces: one scoped Conventional Commit for issue #50.

- [ ] **Step 1: Review `git diff` and confirm no supplied untracked file or unrelated issue is included.**

- [ ] **Step 2: Run `dotnet test tools/tests/BehaviourGraph.Tests.csproj` and require 111 passed.**

- [ ] **Step 3: Run `dotnet run --project tools/symrm/symrm.csproj -- test` and require 1024 checks.**

- [ ] **Step 4: Run `dotnet run --project tools/uismoke/uismoke.csproj` and require 66 checks.**

- [ ] **Step 5: Run `dotnet run --project tools/uismoke/uismoke.csproj -- dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx` and require 264 checks.**

- [ ] **Step 6: Stage only the owned source, tests, harness, and plan files. Commit with a Conventional Commit subject under 50 characters, a body explaining the measured data and validator correction, and `Closes #50` in the footer.**
