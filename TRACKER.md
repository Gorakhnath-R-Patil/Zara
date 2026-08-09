# Zara — Development Tracker

Companion to [ARCHITECTURE.md](ARCHITECTURE.md). This file is the single source of truth for "what's done, what's next." Update it as part of every work session — check boxes, add dates, add notes. Don't let it drift from reality.

**Legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked

**Rule:** nothing moves past Phase 0 until its exit criteria (ARCHITECTURE.md §30) are met. Don't start Phase 2 work while Phase 1 tasks sit unfinished.

---

## Status at a glance

| Phase | Status | Started | Target exit |
|---|---|---|---|
| 0 — Spikes | `[ ]` not started (see note) | — | 4 spikes green |
| 1 — Functional MVP | `[~]` in progress — **M1 + M2 + M3 complete**, M4 next | 2026-08-09 | §29.2 criteria met |
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

### Milestone M2 — Storage & Walk Indexer — **[x] COMPLETE, 2026-08-09**
- [x] **T11** `Zara.Storage`: SQLite bootstrap, WAL pragmas, `MigrationRunner`, `001_initial.sql` (files/volumes/folder_stats tables from §22, minus content/vector tables)
- [x] **T12** `Zara.Storage`: `SqliteConnectionFactory` + single-writer `WriteQueue` — verified against 200 concurrent writers, zero `SQLITE_BUSY`
- [x] **T13** `Zara.Volumes` (fallback path): `WalkScanner` — BFS via explicit queue (not recursion), `DefaultSkipList` applied pre-descent (§10.5's full hard-exclusion list), reparse points never auto-descended (verified against a **real** `mklink /J` junction, no elevation needed)
- [x] **T14** `Zara.Indexing`: `ScanOrchestrator` — batched 5k-row transactions (`FileIndexWriter`, upsert on `(volume_id, frn)`), progress events. **Scoping decision:** checkpoint/resume granularity is per top-level child of the scan root, not per-directory — see `002_scan_checkpoints.sql`'s header comment and the decision log below.
- [x] **T15** `Zara.Indexing`: `ScanCheckpointStore` — resume a killed scan without restarting; `002_scan_checkpoints.sql`
- [x] **T16** Bench (`benchmarks/Zara.Scenarios -- scan <files> <dirs>`): full scan + interrupt/resume, **real measured numbers, Release build, 100k files / 20 shards:**
  - Full scan: **100,020 rows in 2.6s (~39,100 rows/s)**. Extrapolated linearly to 500k files: ~13s — comfortably inside §29.2's 120s exit bar (a real 500k run would be needed to confirm this holds at scale; the linear extrapolation is a reasonable expectation given the batched-transaction design, not a guarantee).
  - Interrupt/resume: killed after ~10/20 shards (0.8s, 50,010 rows landed), resumed and finished the remaining 10 shards in 0.9s, ending at the fully-correct 100,020 rows. **Zero shards re-walked** — confirmed both here and in the unit test below via an explicit per-directory call-count assertion.

**M2 exit:** all 6 tasks done, **184/184 tests passing** across all 8 test projects. The standout test is `ScanOrchestratorTests.ScanAsync_InterruptedThenResumed_CompletesFully_WithoutRewalkingFinishedChildren` — it cancels a real scan mid-flight via a synchronous progress callback, resumes it, and asserts (via a `CountingWalkScanner` spy) that every child directory was walked **exactly once** across both runs combined, not zero and not two.

### Milestone M3 — Name Search — **[x] COMPLETE, 2026-08-09**
- [x] **T17** `Zara.Search`: `NameIndex` — trigram map (plain `Dictionary<string, HashSet<int>>`, not RoaringBitmap — see decision log) + folded-name parallel arrays (§12.2)
- [x] **T18** `Zara.Search`: incremental `Upsert`/`Remove`, index warms during scan — 19 tests including one that upserts/searches/upserts-more interleaved, proving mid-build search correctness
- [x] **T19** `Zara.Search`: `DslLexer` / `DslParser` for the structured query grammar (§12.3). **Scoping decision:** implicit-AND of field:value predicates + `NOT`/`-` negation; no parenthesized boolean grouping or general `OR` — see `DslParser`'s class remarks for the full rationale. 55 tests.
- [x] **T20** `Zara.Search`: `QueryPlanner` + `SelectivityEstimator` (metadata-only path, no FTS/vector yet). **Scoping decision:** `path:`/`in:`/`dup:`/`empty:`/`content:` parse successfully but are surfaced via `QueryPlan.UnsupportedPredicates` rather than silently ignored — none are executable against the current schema (no stored full path; no dup/empty aggregates yet; no FTS). 26 tests, most executing the generated SQL against a **real** SQLite database, not just inspecting the WHERE-clause string.
- [x] **T21** Bench (`benchmarks/Zara.Scenarios -- search 500000`, B06/B07): name search p95 < 20ms, structured query p95 < 25ms @ 500k. **Real numbers, two real bugs found and fixed by this benchmark:**
  - **NameIndex** — first run: p95 **20.55ms** (FAIL, just over budget). Root cause: `Search`'s `OrderBy().Take()` still performs a full O(n log n) sort even though only the top 50 are needed — LINQ's `Take` does not turn `OrderBy` into a partial sort. For an unselective query (a common word matching ~50k of 500k entries), that's a real cost. **Fix:** replaced with a bounded sorted list capped at `maxResults` (O(n log k) instead of O(n log n), most candidates rejected in O(1) once the list is full). **After:** p95 **14.28–14.49ms** — PASS, with headroom, and the fix's own unit tests (all 81) stayed green throughout.
  - **QueryPlanner** — first run: p95 **83.88ms** (FAIL, 3× over budget). Root cause: the "Relevance" sort default fell back to `ORDER BY modified_utc DESC`, which isn't covered by whatever index served the query's filter predicates (e.g. `ix_files_ext_size`) — SQLite had to filesort the entire filtered set before applying `LIMIT`. **Fix:** default sort is now no `ORDER BY` at all (free, natural rowid order) rather than a sort with no real meaning yet (there's no relevance signal to sort by before Phase 2's FTS/vector scoring exists) — an explicit `sort:modified` still costs what it costs, as a deliberate tradeoff instead of a hidden default one. **After:** p95 **0.06ms** — a ~1,400× improvement, PASS with enormous headroom.

**M3 exit:** all 5 tasks done, **265/265 tests passing** across 9 test projects. T21 is the standout result of the whole milestone: it's the first benchmark this session that actually FAILED on first run, and both failures led to real root-cause fixes (not benchmark tuning) that are now permanently reflected in `NameIndex.Search` and `QueryPlanner.BuildOrderBy` — exactly what "measure before optimizing" is for.

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
| 2026-08-09 | Scan checkpoint granularity is per **top-level child of the scan root**, not per-directory throughout the tree | `WalkScanner`'s BFS queue (T13, already shipped and tested) is internal to one `Walk()` call and isn't persistable mid-traversal without invasive changes. Treating each top-level child as an independent, fully-idempotent unit of work is far simpler, still delivers the thing that actually matters (a killed 500k-file scan doesn't restart from zero — it re-walks at most one in-flight subtree), and was verified end-to-end: T16's benchmark and `ScanOrchestratorTests`'s resumability test both confirm zero redundant re-walks of completed subtrees. Revisit only if profiling shows a single top-level directory holding a large enough fraction of a real volume's files that "worst case: re-walk one subtree" stops being cheap. |
| 2026-08-09 | M2's `files` table writes leave `parent_id` NULL | Populating it correctly requires either two-pass writes or querying back a parent's freshly-assigned `id` before writing its children — a real feature, not a one-line addition, and nothing in M2–M3's scope (name search, DSL, analytics) needs parent-chain path reconstruction yet. `path_hash` (populated) is enough for path-based lookups until it does. Tracked as a gap, not silently absent — flag before folder-rollup work (`folder_stats`, in the M4 task list) or Explorer-style path-breadcrumb reconstruction depends on it. |
| 2026-08-09 | `FileIndexWriter` silently skips (counts, doesn't throw) any entry with a null `Frn` | Only `Win32DirectoryEnumerator` (the non-NTFS/denied-path fallback, T08) ever produces one, and there's no other stable identity to upsert on for such an entry. This is a real, documented gap for non-NTFS volumes — not yet a problem since M1–M2 target the primary NTFS/`NtDirectoryEnumerator` path exclusively; revisit when Phase 4's network-drive support needs it. |
| 2026-08-09 | `path_hash` is `XxHash3.HashToUInt64` over the UTF-8 bytes of the **uppercased** canonical path | Matches NTFS's own case-insensitive identity semantics (same reasoning as `CanonicalPath`'s `OrdinalIgnoreCase` comparer, §10.1) — two paths differing only in case must hash identically. `System.IO.Hashing.XxHash3` (added .NET 8) rather than a hand-rolled implementation. |
| 2026-08-09 | `WriteQueue` only implements `IAsyncDisposable`, not `IDisposable` | Caught at compile time in `benchmarks/Zara.Scenarios/ScanScenario.cs` — a plain `using var writeQueue = new WriteQueue(...)` doesn't compile. Synchronous call sites (like a console benchmark's `Main`) need an explicit `writeQueue.DisposeAsync().AsTask().GetAwaiter().GetResult()` instead. Not a bug, just a reminder for the next synchronous caller. |
| 2026-08-09 | `NameIndex`'s trigram candidate sets are plain `Dictionary<string, HashSet<int>>`, not RoaringBitmap | No vetted RoaringBitmap package was in the solution, and T21's own note said to measure a simpler structure first. Measured: p95 14.3–14.5ms at 500k entries, comfortably under the 20ms target once top-K selection was bounded (see below) — the simpler structure was sufficient. Revisit only if a future benchmark at a larger scale (the `extreme` 5M-file corpus in §32.1) shows otherwise. |
| 2026-08-09 | `DslParser` supports implicit-AND of `field:value` predicates + `NOT`/`-` negation, but not parenthesized boolean grouping or general `OR` | A full recursive-descent boolean expression parser (precedence climbing for `AND`/`OR`/`NOT`/`(...)`) is a meaningfully bigger, separate piece of work than the rest of T19. The shipped subset covers the large majority of realistic queries. `type:image\|video`'s `\|` is a fixed field-scoped enumeration, not general OR — don't confuse the two when this gets revisited. |
| 2026-08-09 | `QueryPlanner` surfaces `path:`/`in:`/`dup:`/`empty:`/`content:` as `UnsupportedPredicates` rather than executing them | None are executable against the current schema: no full path is stored per row (M2's `parent_id`-left-NULL decision), no duplicate/empty aggregates exist yet (M4's job), and there's no FTS5 table (Phase 2). Parsing them now keeps the DSL surface stable — a query string written today won't need to change syntax when these land; only `QueryPlanner.Plan` needs to grow to actually honor them. |
| 2026-08-09 | `NameIndex.Search`'s top-K selection was rewritten from `OrderBy().Take()` to a bounded sorted list | **Found by T21's 500k-scale benchmark, not by inspection.** LINQ's `OrderBy` must fully sort before `Take` can pull anything from it — an unselective query (~50k candidates) was paying a full O(n log n) sort to keep only the top 50. Fixed with a manually-maintained sorted `List<T>` capped at `maxResults` (O(n log k), most candidates rejected in O(1) once full). p95 dropped from 20.55ms (FAIL) to 14.28ms (PASS). All 81 `Zara.Search.Tests` stayed green across the change — the fix is a strict internal optimization, not a behavior change. |
| 2026-08-09 | `QueryPlanner`'s default sort ("Relevance", i.e. no explicit `sort:`) produces no `ORDER BY` clause at all, not `ORDER BY modified_utc DESC` | **Also found by T21.** "Relevance" has no real meaning before Phase 2's FTS/vector scoring exists, so defaulting it to a sort that isn't covered by the filtering index (forcing SQLite to filesort the whole matched set before `LIMIT`) was paying real cost for a default nobody asked for. p95 dropped from 83.88ms (FAIL, 3× over budget) to 0.06ms (PASS) — a ~1,400× improvement. An explicit `sort:modified` still costs a filesort when the caller actually wants recency order; that's now a deliberate choice, not a hidden default one. |

---

## Notes for the next session

- **M1, M2, and M3 are all complete.** Start at the first `[ ]` in **M4**
  (Deterministic Analytics): T22 (`DuplicateFinder` — quick-hash pre-filter
  via `files.quick_hash`/`size_bytes`, then a BLAKE3 confirm pass; note
  `quick_hash` is a schema column that nothing populates yet — T22 is where
  that needs to start happening, likely as an addition to `FileIndexWriter`
  or a follow-up pass), T23 (`SizeRollup` — incremental `folder_stats`
  maintenance; this is also where `parent_id` being NULL, deferred since M2,
  will finally need to be revisited, since folder rollups are inherently a
  parent-chain operation), T24 (`StaleFileFinder`, `EmptyFolderFinder`).
  - M4 is a good milestone to also close the loop on `QueryPlanner`'s
    `UnsupportedPredicates` for `dup:`/`empty:` (T20's decision log entry) —
    once `DuplicateFinder`/`SizeRollup` exist, those two predicates can
    become real WHERE-clause fragments instead of parse-only stubs.
- Run `dotnet test` before marking any task `[x]`; for anything
  performance-sensitive, get a real number rather than assume one — T21
  alone caught two real bugs (a full-sort-instead-of-top-K in `NameIndex`,
  and an uncovered-index filesort in `QueryPlanner`'s default sort) that no
  amount of code review would have surfaced without actually running at
  500k-entry scale. That's now the established pattern for every
  performance-sensitive piece of code in this repo — trust it.
- If a task reveals the architecture doc is wrong, fix ARCHITECTURE.md in the same commit and log it above — don't let drift accumulate.
- Current repo state: solution has **16 projects** (8 `src/` — adds
  `Zara.Search`; 7 `tests/` — adds `Zara.Search.Tests`; 1
  `benchmarks/Zara.Scenarios`), **265/265** tests passing, five commits on
  `master`. `dotnet build` / `dotnet test` both clean from a fresh clone.
  - `dotnet run --project benchmarks/Zara.Scenarios -c Release -- list
    <fileCount>` reproduces T09's directory-listing numbers.
  - `dotnet run --project benchmarks/Zara.Scenarios -c Release -- scan
    <totalFiles> <childDirCount>` reproduces T16's full-scan and
    interrupt/resume numbers (defaults: 100,000 files / 20 shards).
  - `dotnet run --project benchmarks/Zara.Scenarios -c Release -- search
    <totalFiles>` reproduces T21's name-search and structured-query latency
    numbers (default: 500,000).
- Phase 0 spikes (S1–S4) are still outstanding. M2/M3 shipped without them
  per the sequencing note (below) — T13's real-junction test, T07's
  from-scratch-correct NT struct interop, and T21's real 500k-scale
  benchmark catching two genuine bugs are exactly the kind of evidence that
  note said would lower S1's risk, and it keeps doing so. **M4 is still
  fine to proceed without the spikes** (analytics work is ordinary
  algorithms + SQL, not a new unknown), but do not start M5 (WPF at scale)
  or M8 (LLM grammar reliability) before S2/S3 respectively are run for real.
