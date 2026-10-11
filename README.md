# Zara

A local-first, AI-assisted file manager for Windows 11, built on .NET 9 / C#.

Most of an "AI file manager" is really an indexing and retrieval problem. Zara does the heavy lifting (filtering, sizing, deduplication, ranking, path safety) in deterministic code. The AI has a narrow job: translate fuzzy natural language into a precise, validated structured query using a local model (Gemma 3 4B via Ollama). The MVP performs no AI write operations.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the full design and [TRACKER.md](TRACKER.md) for detailed progress and the decision log.

## Status

Early development. The Phase 1 backend is largely complete and tested; **there is no UI yet**, so this is currently a set of libraries plus a background Engine process, not an app you can launch.

| Area | Status |
|---|---|
| Filesystem layer, path canonicalization and validation | Done |
| SQLite storage, resumable scanner | Done |
| Name search, query DSL, query planner | Done |
| Duplicates, folder sizes, stale/empty analytics | Done |
| Shell file operations, operation journal, crash recovery, undo | Mostly done (delete-undo missing) |
| Engine process + gRPC over named pipes + client | Done |
| Natural-language to query compiler (Ollama) | Done |
| Resource governor, security policy engine | Done |
| WPF UI | Not started |
| Content extraction, embeddings, agent (Phases 2+) | Not started |

538 tests pass across 12 test projects.

## Highlights

- Directory listing of 100k files in about 100 ms via `NtQueryDirectoryFile`.
- Full scan of 100k files in about 2.6 s, resumable after interruption.
- Name search p95 about 14 ms at 500k entries.
- Two-process design: the Engine can crash without taking the UI down.
- Path validation that re-checks containment after following junctions and symlinks.

## Requirements

- Windows 11
- .NET SDK (targets `net9.0-windows`)
- Optional: [Ollama](https://ollama.com) with `gemma3:4b` for the LLM-dependent tests. Those tests skip themselves if Ollama is unreachable.

## Build and test

```
dotnet build Zara.slnx
dotnet test
```

The `Zara.EngineClient.Tests` and `Zara.Ai.Tests` suites are slow (real spawned processes and live LLM calls), so the full run takes a few minutes.

## Benchmarks

```
dotnet run --project benchmarks/Zara.Scenarios -c Release -- list <fileCount>
dotnet run --project benchmarks/Zara.Scenarios -c Release -- scan <totalFiles> <childDirCount>
dotnet run --project benchmarks/Zara.Scenarios -c Release -- search <totalFiles>
dotnet run --project benchmarks/Zara.Scenarios -c Release -- golden
```

## Running the Engine

```
Zara.Engine.exe --pipe-name=... --db-path=... --scan-root=...
```

## Roadmap

Next up: have the Engine use the policy-gated executor (the gate itself, `PolicyGatedShellOperations`, is built and tested), then validate WPF at scale (Spike S2) and build the shell UI. Later phases add content search, embeddings, an agent layer, and Explorer integration.
