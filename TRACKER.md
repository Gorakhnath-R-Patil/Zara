# Zara — Development Tracker

Companion to [ARCHITECTURE.md](ARCHITECTURE.md). This file is the single source of truth for "what's done, what's next." Update it as part of every work session — check boxes, add dates, add notes. Don't let it drift from reality.

**Legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked

**Rule:** nothing moves past Phase 0 until its exit criteria (ARCHITECTURE.md §30) are met. Don't start Phase 2 work while Phase 1 tasks sit unfinished.

---

## Status at a glance

| Phase | Status | Started | Target exit |
|---|---|---|---|
| 0 — Spikes | `[ ]` not started (see note) | — | 4 spikes green |
| 1 — Functional MVP | `[~]` in progress — M1 underway | 2026-08-09 | §29.2 criteria met |
| 2 — Content & speed | `[ ]` not started | — | — |
| 3 — Controlled operations | `[ ]` not started | — | — |
| 4 — Windows integration | `[ ]` not started | — | — |
| 5 — Hardening | `[ ]` not started | — | — |

---

> **Note on sequencing:** M1's path layer (T01/T02/T04/T05/T10) was built ahead
> of the Phase 0 spikes. That's a deliberate, narrow exception, not an
> abandonment of the gate: the path/canonicalization work is well-understood
> .NET + Win32 (low technical risk, and it's a dependency of everything else
> including the spikes' own test harnesses), whereas S1–S4 exist specifically
> to de-risk the *unknowns* — MFT parsing, WPF at 1M rows, Gemma 3 4B grammar
> reliability, sqlite-vec filtered KNN speed. **Do not start M2 (SQLite/walk
> indexer) or go further into M5 (WPF grid) or M8 (LLM) before S1–S4 are
> green** — those milestones are exactly where a red spike would force a
> rework, and the whole point of Phase 0 is to find that out cheaply.

## Phase 0 — Spikes (risk validation, throwaway code OK)

| # | Spike | Question | Status | Result |
|---|---|---|---|---|
| S1 | MFT read | Can C# parse the MFT? 1M records < 15s? | `[ ]` | |
| S2 | WPF scale | Does a virtualized grid hold 1M rows smoothly? | `[ ]` | |
| S3 | Grammar decoding | Does Gemma 3 4B (via Ollama, JSON-schema `format`) hit ≥95% schema-valid / ≥85% semantically-correct / p95<1.5s? | `[ ]` | |
| S4 | sqlite-vec | Filtered int8 KNN over 200k vectors < 50ms? | `[ ]` | |

**Exit:** all four green, or the architecture doc amended to reflect what a red spike forced us to change.

---

## Phase 1 — Functional MVP

### Milestone M1 — Foundation & Filesystem
*Goal: enumerate/display a directory faster than Explorer, on a canonical, tested path layer.*

- [x] **T01** Repo scaffold: solution, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore`, git init — 2026-08-09
- [x] **T02** `Zara.Core`: `CanonicalPath`, `FileId`, `VolumeRef`, `FileEntry` domain types — 2026-08-09
- [ ] **T03** `Zara.Core`: `Result<T>` / `ZaraError` / `ErrorCode` — shared error model
- [x] **T04** `Zara.Filesystem`: `PathCanonicalizer` (extended-length prefix, `GetFinalPathNameByHandle`) — 2026-08-09. Hand-rolled `DllImport` interop (`Interop/NativeMethods.cs`), not CsWin32 — see decision log.
- [x] **T05** `Zara.Filesystem`: `PathValidator` + adversarial test suite (§27.1 PATH section) — 2026-08-09. 74 tests in `Zara.Filesystem.Tests` across `PathSyntaxTests` (pure, no I/O), `PathCanonicalizerTests`, `PathValidatorTests` (real temp-filesystem integration). Caught and fixed a real bug: `\\?\`-prefixed paths bypass OS `..`-normalization, so `CanonicalizeExisting` now normalizes via `Path.GetFullPath` before prefixing.
- [ ] **T06** `Zara.Filesystem`: `KnownFolders` wrapper (`SHGetKnownFolderPath`)
- [ ] **T07** `Zara.Filesystem`: `DirectoryEnumerator` via `NtQueryDirectoryFile` (P/Invoke, pooled buffers)
- [ ] **T08** `Zara.Filesystem` fallback: `FindFirstFileEx` enumerator for non-NTFS/denied paths
- [ ] **T09** Benchmark: 100k-file directory listing < 400ms, < 20MB allocated (`benchmarks/`)
- [x] **T10** Architecture test (`NetArchTest`): illegal project references fail the build — 2026-08-09. `Zara.ArchitectureTests` enforces the §8.2 table for the two projects that exist; commented stubs mark where to extend it per future milestone.
- [ ] **M1 exit** All of the above green; `dotnet test` clean on fresh clone — **74+6+2=82/82 tests passing so far; T03/T06/T07/T08/T09 remain before M1 exits**

### Milestone M2 — Storage & Walk Indexer
- [ ] **T11** `Zara.Storage`: SQLite bootstrap, WAL pragmas, `MigrationRunner`, `001_initial.sql` (files/volumes/folder_stats tables from §22, minus content/vector tables)
- [ ] **T12** `Zara.Storage`: `SqliteConnectionFactory` + single-writer `WriteQueue`
- [ ] **T13** `Zara.Volumes` (fallback path): `WalkScanner` — BFS traversal, skip-list applied pre-descent, reparse-point guard
- [ ] **T14** `Zara.Indexing`: `ScanOrchestrator` — batched 5k-row transactions, checkpointing, progress events
- [ ] **T15** `Zara.Indexing`: `ScanCheckpointStore` — resume a killed scan without restarting
- [ ] **T16** Bench: `small` corpus (100k files) full scan wall time + resumability under kill -9

### Milestone M3 — Name Search
- [ ] **T17** `Zara.Search`: `NameIndex` — trigram map + roaring bitmap + folded-name arena (§12.2)
- [ ] **T18** `Zara.Search`: incremental `Upsert`/`Remove`, index warms during scan (search usable mid-scan)
- [ ] **T19** `Zara.Search`: `DslLexer` / `DslParser` for the structured query grammar (§12.3)
- [ ] **T20** `Zara.Search`: `QueryPlanner` + `SelectivityEstimator` (metadata-only path, no FTS/vector yet)
- [ ] **T21** Bench B06/B07: name search p95 < 20ms, structured query p95 < 25ms @ 500k files

### Milestone M4 — Deterministic Analytics
- [ ] **T22** `DuplicateFinder` — quick-hash pre-filter → BLAKE3 confirm
- [ ] **T23** `SizeRollup` — incremental `folder_stats` maintenance (dirty-flag propagation on change)
- [ ] **T24** `StaleFileFinder`, `EmptyFolderFinder`

### Milestone M5 — WPF Shell
- [ ] **T25** `Zara.App` skeleton: window chrome, Fluent theme, dark/light follow-OS
- [ ] **T26** `VirtualizingFileGrid` control — container recycling, spike-S2-validated approach
- [ ] **T27** `DirectoryViewModel` + windowed `VirtualizingCollection` backed by the index
- [ ] **T28** Navigation: breadcrumbs, back/forward/up, sidebar (known folders, drive usage bars)
- [ ] **T29** Keyboard-complete interaction (arrow nav, type-ahead, Tab regions, F2, Del)
- [ ] **T30** `CommandBarView` + `SyntaxProbe` (structured DSL vs. bare token vs. NL routing, §8.3)

### Milestone M6 — Operations & Journal
- [ ] **T31** `Zara.Filesystem.Shell`: `IFileOperation` wrapper (`ShellFileOperations`) — copy/move/rename/delete-to-Recycle-Bin
- [ ] **T32** `IOperationJournal` + `operations`/`operation_items` schema + write protocol (§19.3)
- [ ] **T33** Crash-recovery: replay `status='executing'` entries on Engine start (§19.4)
- [ ] **T34** Undo stack (50 deep, 24h expiry) + `OperationPreviewDialog`
- [ ] **T35** Property test: `undo(op(fs)) == fs` byte-identical, for move/copy/rename/delete

### Milestone M7 — Two-Process Split
- [ ] **T36** `Zara.Contracts`: `.proto` definitions (search/index/journal/admin — ai.proto in M8)
- [ ] **T37** `Zara.Engine`: Generic Host, named-pipe gRPC server, owner-SID-only DACL
- [ ] **T38** `Zara.App`: `EngineProcessManager` (spawn, job-object kill-on-close, backoff reconnect, degraded-mode banner)
- [ ] **T39** Move Storage/Indexing/Search into the Engine process; App talks only via `EngineClient`
- [ ] **T40** Failure-mode tests: kill Engine mid-search → App keeps browsing (§28 #3)

### Milestone M8 — Query Compiler (first AI feature)
- [ ] **T41** `Zara.Ai`: `ILlmProvider` + `OllamaProvider` (JSON-schema `format`, `keep_alive=30m`)
- [ ] **T42** `IntentRouter`: DSL/token short-circuit + ~40 pattern-matched intents before any LLM call
- [ ] **T43** `QueryCompiler`: schema (§14.3), prompt builder with static-prefix caching, semantic cache
- [ ] **T44** Post-generation validation (schema → semantic → clamping) + confidence-gated clarification
- [ ] **T45** `QueryChipEditor` UI — editable, removable, re-runs instantly
- [ ] **T46** Golden-set harness (100 queries) wired into CI; router LLM-bypass-rate metric ≥75%

### Milestone M9 — MVP Hardening
- [ ] **T47** Diagnostics page (§26.2 metrics rendered locally)
- [ ] **T48** ResourceGovernor v1 (foreground/idle/battery states; `PROCESS_MODE_BACKGROUND_BEGIN`)
- [ ] **T49** Full security suite (§27.1) green in CI
- [ ] **T50** 72h soak test; §29.2 exit criteria all verified; **dogfood for 2 weeks**

> Tasks beyond T50 (Phase 2+: MFT/USN scanner, content extraction, embeddings, hybrid ranking,
> agent/tool system, Explorer shell extension, network drives) are deliberately not itemized yet —
> per ARCHITECTURE.md §5/§29, detailing them now would be planning past a phase gate that hasn't
> opened. Expand Phase 2 into tasks here once M9 exits.

---

## Decision log

*Append-only. One line per non-obvious call made during implementation that isn't already in ARCHITECTURE.md.*

| Date | Decision | Why |
|---|---|---|
| 2026-08-09 | Target `net9.0-windows` (not `net10.0-windows`) despite SDK 10 being installed | .NET 9 is the LTS release the architecture doc was written against; WPF/WindowsAppSDK third-party package support lags on brand-new TFMs. Revisit when 10 is LTS-equivalent and the ecosystem catches up. |
| 2026-08-09 | Hand-rolled `[DllImport]` for `CreateFileW`/`GetFinalPathNameByHandleW` instead of CsWin32 codegen | `LibraryImport` (source-generated) doesn't support `StringBuilder` marshalling, which `GetFinalPathNameByHandleW` needs; classic `DllImport` is a well-trodden, easy-to-audit pattern for this handful of calls. `Microsoft.Windows.CsWin32` stays in `Directory.Packages.props` for when a larger Win32/COM surface (shell interop at M6) makes generated bindings worth it. |
| 2026-08-09 | `dotnet new sln` produced `Zara.slnx` (the new XML solution format), not `Zara.sln` | Default in the .NET 9/10 SDK; works identically with `dotnet build`/`test`/`sln add`. No action needed unless a tool in the chain later requires the classic format. |
| 2026-08-09 | `CanonicalizeExisting` normalizes via `Path.GetFullPath` before applying the `\\?\` prefix | A `\\?\`-prefixed path is passed to Win32 verbatim — the OS does NOT collapse `.`/`..` segments in it (that's the tradeoff for bypassing `MAX_PATH`). Prefixing a raw path containing `..` and calling `CreateFileW` fails whenever an intermediate segment doesn't itself exist on disk, even though the fully-resolved target does. Found by `Validate_DeepDotDotTraversal_ResolvesAndIsCheckedAgainstRoot` in T05's test suite. |

---

## Notes for the next session

- Start at the first `[ ]` in M1, top to bottom — dependencies are ordered.
  Next up: **T03** (`Result<T>`/`ZaraError`), then **T06** (`KnownFolders`),
  then **T07** (`DirectoryEnumerator`) — T07 is the one to slow down for, it's
  the performance-critical piece M1's exit criteria (§29.2 #1–2) hinge on.
- Run `dotnet test` before marking any task `[x]`.
- If a task reveals the architecture doc is wrong, fix ARCHITECTURE.md in the same commit and log it above — don't let drift accumulate.
- Current repo state: `Zara.sln`(x) has 5 projects (`Zara.Core`, `Zara.Filesystem`,
  `Zara.Core.Tests`, `Zara.Filesystem.Tests`, `Zara.ArchitectureTests`), 82/82
  tests passing, one commit on `master`. `dotnet build` / `dotnet test` both
  clean from a fresh clone.
- Phase 0 spikes (S1–S4) are still outstanding — see the sequencing note
  above M1's checklist. Fit them in before M2/M5/M8 go deep.
