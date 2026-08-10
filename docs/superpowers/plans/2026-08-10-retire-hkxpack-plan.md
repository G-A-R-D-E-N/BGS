# Retire hkxpack Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the native C# HKX pipeline the only supported application path, then permanently remove the external packer only after corpus lifecycle proof.

**Architecture:** Phase A keeps the existing fallback implementation isolated behind the unadvertised `BGS_ENABLE_LEGACY_PACKER=1` developer environment switch. The normal application, packages, documentation, and smoke tests use `NativeXml`, `NativeGraphModel`, and `NativeSave` exclusively. Phase B is blocked until the corpus lifecycle gate proves supported files can be opened, edited, written natively, reloaded, validated, and rendered without the fallback.

**Tech Stack:** .NET 8, Avalonia, native HKX packfile reader and writer, xUnit, symrm, uismoke.

## Global Constraints

- Do not change HKX parsing, binary layout rules, serialization semantics, or native-save behavior.
- Do not expose the legacy path in the normal user interface or documentation.
- Do not package the JAR or require Java in Phase A.
- Unsupported native edits must not write the original file.
- Keep Phase A private to GitLab. Do not publish the retained fallback source or JAR to public GitHub.
- Run the four repository verification gates before committing.

---

### Task 1: Make native the only supported application path

**Files:**
- Modify: `app/MainWindow.cs`
- Modify: `app/Program.cs`
- Modify: `src/Hkx/ProjectChain.cs`
- Modify: `src/Hkx/ProjectCheck.cs`
- Modify: `src/Hkx/HkxTextEdit.cs`
- Test: `tools/uismoke/Smoke.cs`

**Interfaces:**
- Consumes: `NativeXml.From(byte[])`, `NativeGraphModel.From(PackfileObjects)`, `NativeSave.Compare(string, string)`, and `NativeSave.Apply(string, NativeSave.Plan)`.
- Produces: an application where normal loading, comparison, validation, and saving do not require Java or a JAR.

- [ ] **Step 1: Write failing smoke assertions**

Assert that the normal window contains no Java picker and that the representative behavior loads into editable native XML. The Java picker assertion must fail before the UI removal.

- [ ] **Step 2: Remove normal fallback use**

Remove Java setup controls and make native XML the normal load, compare, project-chain, project-check, and save path. If `NativeSave.Compare` refuses an edit, report the refusal and leave the source file unchanged.

- [ ] **Step 3: Retain an internal-only escape hatch**

Keep the legacy invocation code behind `BGS_ENABLE_LEGACY_PACKER=1`. The switch must default to off, have no UI control, and require an externally supplied Java launcher and JAR path.

- [ ] **Step 4: Run focused smoke verification**

Run: `/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj`

Expected: PASS with no Java control and native-loaded editor data.

### Task 2: Stop shipping the external packer

**Files:**
- Modify: `app/BehaviourStudio.csproj`
- Modify: `tools/uismoke/uismoke.csproj`
- Modify: `README.md`
- Modify: `CHANGELOG.md`
- Modify: `memory.md`
- Modify: `THIRD_PARTY_NOTICES.md`

**Interfaces:**
- Consumes: Task 1 native application behavior.
- Produces: normal build and release outputs without a JAR, Java instructions, or public fallback claims.

- [ ] **Step 1: Write a failing package assertion**

Add a check that the application and uismoke project files do not copy the JAR into their output. It must fail while either csproj includes it.

- [ ] **Step 2: Remove packaging and user-facing references**

Remove JAR and licence copy items, Java user interface wording, and documentation that describes Java or hkxpack as supported. Keep the retained source artifact and its applicable notice until Phase B.

- [ ] **Step 3: Verify clean shipping inputs**

Run: `rg -n -i -g '!bin/**' -g '!obj/**' 'hkxpack-cli\.jar|Find Java|Java runtime' app README.md CHANGELOG.md tools/uismoke`

Expected: no normal application, packaging, or user-facing matches.

### Task 3: Add the native lifecycle retirement gate

**Files:**
- Modify: `tools/symrm/Program.cs`
- Modify: `tools/symrm/Tests.cs`
- Test: `tools/tests/BehaviourGraph.Tests.csproj`

**Interfaces:**
- Consumes: representative corpus paths and `NativeSave` operations supported by the editor.
- Produces: a `symrm lifecycle` command that reports each file type through open, edit, native save, reload, validate, and render.

- [ ] **Step 1: Write a failing lifecycle test**

Add a test that invokes the lifecycle command over the checked-in representative corpus and expects one result for every supported file class. It must fail before the command is implemented.

- [ ] **Step 2: Implement the lifecycle command without fallback calls**

For each representative supported behavior and animation, use a reversible supported edit, apply `NativeSave`, reopen the generated file, run `GraphValidator` where applicable, and build the graph or pose renderer. Report every stage and fail the command if any supported lifecycle stage fails.

- [ ] **Step 3: Run the lifecycle gate**

Run: `/home/ricky/.dotnet/dotnet run --project tools/symrm/symrm.csproj -- lifecycle dist/examples`

Expected: every supported representative passes open, edit, native save, reload, validate, and render.

### Task 4: Phase B deletion gate

**Files:**
- Delete only after Task 3 passes across the supported corpus: `tools/hkxpack-cli.jar`, `tools/hkxpack-LICENSE.txt`, `tools/apache-2.0.txt`, `tools/no-java.sh`, and remaining legacy Java/JAR code.

**Interfaces:**
- Consumes: a passing lifecycle report and all normal verification gates.
- Produces: a permanent native-only repository suitable for public GitHub publication.

- [ ] **Step 1: Review the lifecycle report**

Confirm every supported corpus representative completed all six lifecycle stages with no legacy switch enabled.

- [ ] **Step 2: Delete retained fallback only after approval**

Remove source artifacts, external-process code, legacy commands, and third-party notices specific to the retired packer. Confirm no tracked files or source references remain.

- [ ] **Step 3: Full verification and public publish**

Run:

```bash
/home/ricky/.dotnet/dotnet test tools/tests/BehaviourGraph.Tests.csproj
/home/ricky/.dotnet/dotnet run --project tools/symrm/symrm.csproj -- test
/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj
/home/ricky/.dotnet/dotnet run --project tools/uismoke/uismoke.csproj -- dist/examples/Dogmeat/Behaviors/DogmeatDefault.hkx
```

Then replace GitHub history with the clean Phase B source only.
