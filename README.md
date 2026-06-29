# NEXUS — Multi-Agent Coding Orchestrator

> One control tower. Many agents. No chaos.

NEXUS is a cross-platform desktop app (.NET 8 / Avalonia) that orchestrates multiple AI coding-agent CLIs working in parallel on one codebase. This repository contains **Milestone 0** — a walking skeleton that proves the architecture end-to-end using a stub coordinator and agent (no real CLI required).

---

## Running

**Prerequisites:** .NET 8 SDK

```bash
# Clone and build
git clone <repo>
cd nexus
dotnet build

# Run tests
dotnet test

# Launch the app
dotnet run --project src/Nexus.App
```

The app creates `.nexus/state.db` (SQLite, WAL mode) in the application's base directory on first launch. Kill and relaunch — open tasks reload automatically from the database.

---

## Usage (Milestone 0)

1. Type a task description in the input bar and click **Submit**.
2. A stub coordinator decomposes it into 2 tasks: `auth` (no dependencies) and `booking` (depends on `auth`).
3. Watch the kanban board: `auth` moves Pending → Running → Done first; `booking` stays Pending until `auth` completes (dependency ordering enforced).
4. Progress updates render live without freezing the UI.
5. Kill the app mid-run and relaunch — open tasks are restored from SQLite.

---

## Architecture

```
┌─────────────────── NEXUS.exe (Avalonia .NET 8) ──────────────────┐
│  Task Input  ──►  StubCoordinator  ──►  NexusOrchestrator         │
│                                              │                     │
│               ChannelWriter<StateEvent>  ◄───┘  (IEventSink)      │
│                      ├─ channel_critical  (bounded, no drop)       │
│                      └─ channel_progress  (bounded, DropOldest)   │
│                                ▼                                   │
│              Single consumer loop (ONLY SQLite writer)             │
│                      ├─ TaskRepository  (Dapper + SQLite)          │
│                      ├─ AgentRepository (Dapper + SQLite)          │
│                      └─ UI notification callbacks                  │
│                                ▼                                   │
│              MainWindowViewModel  ──►  Avalonia UI (Dispatcher)    │
│              [Pending | Running | Done | Failed] + Agent list      │
└───────────────────────────────────────────────────────────────────┘
                               ▼
                         StubAdapter
                   (simulates work, emits progress)
```

**Single-writer invariant:** The only path to SQLite is through the channel consumer loop in `AppHost.HandleEventAsync`. No UI code, no adapter, no MCP code writes the database directly.

---

## Project Structure

```
Nexus.sln
src/
  Nexus.App/        Avalonia UI — Views, ViewModels, AppHost
  Nexus.Core/       Domain model, adapters, pipeline, orchestrator, parsing
  Nexus.Mcp/        MCP ingestion stub (real server in M1)
  Nexus.State/      SQLite + Dapper repositories
tests/
  Nexus.Core.Tests/ Unit tests — StdoutSanitizer (6 tests, all pass)
```

---

## Milestone 0 Scope

| Built | |
|---|---|
| Avalonia MVVM UI | Kanban board + agent status panel |
| EventPipeline | Two-channel single-writer architecture |
| StubCoordinator | Canned 2-task decomposition (auth + booking) |
| StubAdapter | Simulates agent work with progress ticks over ~2s |
| SQLite state store | WAL mode, crash-recovery on startup |
| StdoutSanitizer | ANSI strip + delimiter + brace-count, 6 unit tests |
| Dependency ordering | booking waits for auth (FR-15) |

| Not built (M1+) | |
|---|---|
| Real CLI adapters | opencode, agy, claude-code |
| MCP wire protocol | `ModelContextProtocol` NuGet server |
| Git shadow repo + worktrees | Agent isolation |
| Heartbeat watchdog | Orphan detection + retry |
| Dev-edit guard | Conflict detection on merge |
| Compile/test quality gate | javac + mvn test verification |
| API-key keychain | OS credential store |
| Agent install/download | GUI-driven CLI setup |

---

## Suggested Milestone 1

**MCP server + first real adapter (opencode)**

1. Replace `McpStub` with a real `ModelContextProtocol` server hosting the 7 tools (`register`, `heartbeat`, `report_progress`, `publish_contract`, `read_contract`, `report_done`, `report_failed`).
2. Implement `OpenCodeAdapter` — spawn `opencode run --agent build` as a child process, read stdout with `BeginOutputReadLine`, parse JSON via `StdoutSanitizer`.
3. Add heartbeat watchdog: mark agents `Orphaned` after 90s silence, retry up to 3 times.
4. Wire process lifecycle: Job Objects on Windows, `prctl(PR_SET_PDEATHSIG)` on Linux for zombie cleanup.
