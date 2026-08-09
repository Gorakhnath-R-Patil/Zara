# Zara — Development Tracker

Companion to [ARCHITECTURE.md](ARCHITECTURE.md). This file is the single source of truth for "what's done, what's next." Update it as part of every work session — check boxes, add dates, add notes. Don't let it drift from reality.

**Legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked

**Rule:** nothing moves past Phase 0 until its exit criteria (ARCHITECTURE.md §30) are met. Don't start Phase 2 work while Phase 1 tasks sit unfinished.

---

## Status at a glance

| Phase | Status | Started | Target exit |
|---|---|---|---|
| 0 — Spikes | `[~]` in progress | 2026-08-09 | 4 spikes green |
| 1 — Functional MVP | `[ ]` not started | — | §29.2 criteria met |
| 2 — Content & speed | `[ ]` not started | — | — |
| 3 — Controlled operations | `[ ]` not started | — | — |
| 4 — Windows integration | `[ ]` not started | — | — |
| 5 — Hardening | `[ ]` not started | — | — |

---

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

- [x] **T01** Repo scaffold: solution, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore`, git init
- [x] **T02** `Zara.Core`: `CanonicalPath`, `FileId`, `VolumeRef`, `FileEntry` domain types
- [ ] **T03** `Zara.Core`: `Result<T>` / `ZaraError` / `ErrorCode` — shared error model
- [x] **T04** `Zara.Filesystem`: `PathCanonicalizer` (extended-length prefix, `GetFinalPathNameByHandle`)
- [x] **T05** `Zara.Filesystem`: `PathValidator` + the 200-case adversarial test suite (§27.1 PATH section)
- [ ] **T06** `Zara.Filesystem`: `KnownFolders` wrapper (`SHGetKnownFolderPath`)
- [ ] **T07** `Zara.Filesystem`: `DirectoryEnumerator` via `NtQueryDirectoryFile` (P/Invoke, pooled buffers)
- [ ] **T08** `Zara.Filesystem` fallback: `FindFirstFileEx` enumerator for non-NTFS/denied paths
- [ ] **T09** Benchmark: 100k-file directory listing < 400ms, < 20MB allocated (`benchmarks/`)
- [ ] **T10** Architecture test (`NetArchTest`): illegal project references fail the build
- [ ] **M1 exit** All of the above green; `dotnet test` clean on fresh clone

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

---

## Notes for the next session

- Start at the first `[ ]` in M1, top to bottom — dependencies are ordered.
- Run `dotnet test` before marking any task `[x]`.
- If a task reveals the architecture doc is wrong, fix ARCHITECTURE.md in the same commit and log it above — don't let drift accumulate.
