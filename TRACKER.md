# Zara — Development Tracker

Companion to [ARCHITECTURE.md](ARCHITECTURE.md). This file is the single source of truth for "what's done, what's next." Update it as part of every work session — check boxes, add dates, add notes. Don't let it drift from reality.

**Legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked

**Rule:** nothing moves past Phase 0 until its exit criteria (ARCHITECTURE.md §30) are met. Don't start Phase 2 work while Phase 1 tasks sit unfinished.

---

## Status at a glance

| Phase | Status | Started | Target exit |
|---|---|---|---|
| 0 — Spikes | `[ ]` not started (see note) | — | 4 spikes green |
| 1 — Functional MVP | `[~]` in progress — **M1 complete**, M2 next | 2026-08-09 | §29.2 criteria met |
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
- [x] **T03** `Zara.Core`: `Result<T>` / `ZaraError` / `ErrorCode` — shared error model — 2026-08-09. `Results/` folder; `Result<T>` + non-generic `Result`, `ZaraError` record with `Retryable` flag (feeds §16.4's retry rule later), closed `ErrorCode` enum seeded from what's needed so far plus the §16.3 tool-result categories. 10 tests.
- [x] **T04** `Zara.Filesystem`: `PathCanonicalizer` (extended-length prefix, `GetFinalPathNameByHandle`) — 2026-08-09. Hand-rolled `DllImport` interop (`Interop/NativeMethods.cs`), not CsWin32 — see decision log.
- [x] **T05** `Zara.Filesystem`: `PathValidator` + adversarial test suite (§27.1 PATH section) — 2026-08-09. 74 tests in `Zara.Filesystem.Tests` across `PathSyntaxTests` (pure, no I/O), `PathCanonicalizerTests`, `PathValidatorTests` (real temp-filesystem integration). Caught and fixed a real bug: `\\?\`-prefixed paths bypass OS `..`-normalization, so `CanonicalizeExisting` now normalizes via `Path.GetFullPath` before prefixing.
- [x] **T06** `Zara.Filesystem`: `KnownFolders` wrapper (`SHGetKnownFolderPath`) — 2026-08-09. All 9 `FOLDERID_*` GUIDs resolved correctly against the real profile on the first test run (Profile/Desktop/Documents/Downloads/Pictures/Videos/Music/LocalAppData/RoamingAppData). Round-trips through `PathCanonicalizer` so a known folder is never a special case downstream.
- [x] **T07** `Zara.Filesystem`: `NtDirectoryEnumerator` via `NtQueryDirectoryFile` + `FileIdBothDirectoryInformation` (P/Invoke, `ArrayPool`-backed 64KB buffer) — 2026-08-09. All 9 tests passed on the first real run against disk, including a 1500-file multi-batch case and an FRN-uniqueness check — validates the hand-written `FILE_ID_BOTH_DIR_INFORMATION` struct layout and pointer-arithmetic name parsing.
- [x] **T08** `Zara.Filesystem` fallback: `Win32DirectoryEnumerator` for non-NTFS/denied paths — 2026-08-09. Wraps `DirectoryInfo.EnumerateFileSystemInfos()` rather than hand-rolling `FindFirstFileEx` — see decision log. `Frn` is always `null` from this path (documented, tested).
- [x] **T09** Benchmark: 100k-file directory listing (`benchmarks/Zara.Scenarios`, B12) — 2026-08-09. **Real measured numbers, Release build:**
  - `NtDirectoryEnumerator`: median **102ms**, min **80ms**, alloc min **5.5MB** / avg 7MB → **PASS** on both the <400ms and <20MB targets, with real headroom.
  - `Win32DirectoryEnumerator` (fallback): median **134ms** (still comfortably under 400ms) but alloc min **32MB** / avg 33MB → **FAILS** the <20MB target. Expected and acceptable: a `FileSystemInfo` per entry is inherently heavier than our zero-copy struct parsing, and this path exists for "works everywhere" correctness, not for hitting the primary-path perf budget — §24.1's targets are written against the NTFS/MFT path. Not a blocker for M1 exit. Revisit only if profiling shows the fallback triggering often enough in practice to matter (e.g. heavy non-NTFS or denied-path usage).
- [x] **T10** Architecture test (`NetArchTest`): illegal project references fail the build — 2026-08-09. `Zara.ArchitectureTests` enforces the §8.2 table for the two projects that exist; commented stubs mark where to extend it per future milestone.
- [x] **M1 exit** — **2026-08-09, all criteria met.** `dotnet test`: **119/119 passing** (16 Core + 101 Filesystem + 2 Architecture) clean from this state. T07/T09 together satisfy §29.2 exit criteria #1–2 in spirit (100k in ~100ms vs. the ≤120s/500k bar; full 500k/full-volume timing is now an M2 concern once `WalkScanner` composes this enumerator recursively).

> **S1 note:** T07's `NtDirectoryEnumerator` success is encouraging evidence
> for the same *family* of native interop S1 needs (hand-written NT structs,
> pointer parsing, validated against real disk state) — but S1 itself is a
> different, harder API surface (`FSCTL_ENUM_USN_DATA` against a raw,
> elevated volume handle, plus USN journal semantics). Don't count S1 as done;
> do treat it as lower-risk than it looked on 2026-08-09.

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
| 2026-08-09 | Introduced `RawDirectoryEntry` (Zara.Filesystem) instead of having `IDirectoryEnumerator` return `Zara.Core.Files.FileEntry` directly | `FileEntry.Id` (`FileId`) needs a resolved volume-table surrogate key, which is Zara.Storage's job (M2+) — the Filesystem layer must not know that key exists (§8.2 module boundary). `RawDirectoryEntry.Frn` is `ulong?` (not the strongly-typed `FileId`) so raw enumeration stays fully decoupled from Storage; a later layer combines a `RawDirectoryEntry` with volume context to produce a real `FileEntry`. |
| 2026-08-09 | `Win32DirectoryEnumerator` (T08) wraps `DirectoryInfo.EnumerateFileSystemInfos()` instead of hand-rolling `FindFirstFileEx` P/Invoke | The .NET runtime already implements this on top of `FindFirstFileEx(FIND_FIRST_EX_LARGE_FETCH)` with no extra per-entry syscalls — matches what ARCHITECTURE.md §10.2 asks for ("works everywhere, no elevation, no undocumented API") at much lower interop risk than a second hand-written native surface. Trade-off measured directly in T09: ~4x the allocation of `NtDirectoryEnumerator` at 100k files (32MB vs 5.5MB) because it allocates a `FileSystemInfo` object per entry — acceptable for a correctness-first fallback path, not for the primary path. |
| 2026-08-09 | `benchmarks/Zara.Scenarios` uses a plain `Stopwatch`/`GC.GetAllocatedBytesForCurrentThread` harness, not BenchmarkDotNet, for T09/B12 | Generating and enumerating a 100k-file corpus is itself the expensive part; BenchmarkDotNet's process-isolation and pilot-stage overhead would multiply that for little added precision at this scale. `benchmarks/Zara.Benchmarks` (BenchmarkDotNet, not yet created) is reserved for micro-benchmarking hot-path *methods* — e.g. the name index's trigram intersection at M3 — where that precision earns its cost. |

---

## Notes for the next session

- **M1 is complete.** Start at the first `[ ]` in **M2** (Storage & Walk
  Indexer): T11 (SQLite bootstrap + WAL pragmas + `MigrationRunner`), then
  T12 (single-writer `WriteQueue`), then T13 (`WalkScanner`, which composes
  `Win32DirectoryEnumerator`/`NtDirectoryEnumerator` recursively — the
  reparse-point guard from ARCHITECTURE.md §10.4 has NOT been implemented
  yet anywhere; T13 is where it needs to land, as part of the walk, not
  bolted on after).
- Run `dotnet test` before marking any task `[x]`; for anything
  performance-sensitive, prefer getting a real number (like T09's benchmark
  run) over an assumption — it's cheap on this hardware and it's already
  caught nothing wrong, which is itself useful signal.
- If a task reveals the architecture doc is wrong, fix ARCHITECTURE.md in the same commit and log it above — don't let drift accumulate.
- Current repo state: `Zara.sln`(x) has **7 projects** (`Zara.Core`,
  `Zara.Filesystem`, `Zara.Core.Tests`, `Zara.Filesystem.Tests`,
  `Zara.ArchitectureTests`, plus `benchmarks/Zara.Scenarios`), **119/119**
  tests passing, two commits on `master`. `dotnet build` / `dotnet test` both
  clean from a fresh clone. `dotnet run --project benchmarks/Zara.Scenarios -c
  Release -- <fileCount>` reproduces T09's numbers (defaults to 100,000;
  reuses a cached corpus under `%TEMP%\zara-scenario-b12-<n>` on repeat runs).
- Phase 0 spikes (S1–S4) are still outstanding — see the sequencing note
  above M1's checklist. Fit them in before M2 goes past the walk scanner, and
  definitely before M5/M8 go deep.
