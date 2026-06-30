# AGENTS.md

Guidance for AI coding agents working in this repository. Claude Code reads this via a `CLAUDE.md` that imports it.

## What this is

StackExchange.Redis — a high-performance .NET client for RESP servers (Redis, Valkey, Garnet, Azure Managed Redis, AWS ElastiCache, etc.). This is the **v3** line (`VersionPrefix` 3.0, current branch work on `marc/agents`), whose defining change is that the low-level IO core has been extracted into a separate **RESPite** library that StackExchange.Redis now sits on top of.

## Solution layout

- `src/StackExchange.Redis/` — the client library (the NuGet package). Public surface: `ConnectionMultiplexer`, `IDatabase`/`IDatabaseAsync`, `IServer`, `ISubscriber`, `ITransaction`/`IBatch`, `ConfigurationOptions`, `RedisValue`/`RedisKey`/`RedisResult`.
- `src/RESPite/` — standalone low-level RESP protocol library (separate package, `Marc Gravell` copyright). Owns wire-level parsing/writing: `RespReader`, `RespFrameScanner`, `RespPrefix`, buffer pooling (`CycleBuffer`, `MemoryTrackedPool`). StackExchange.Redis depends on it via `ProjectReference`. RESPite has no dependency on StackExchange.Redis.
- `eng/StackExchange.Redis.Build/` — a Roslyn analyzer/source-generator project that every other project references `OutputItemType="Analyzer"`. It generates code (e.g. `AsciiHashGenerator`) and enforces project-specific rules. Not shipped.
- `tests/` — `StackExchange.Redis.Tests` (xUnit v3, the main integration suite), `RESPite.Tests`, `*.Benchmarks` (BenchmarkDotNet), and `RedisConfigs` (server configs + docker compose, see Testing).
- `toys/` — runnable samples and an in-process RESP server (`StackExchange.Redis.Server`, used by tests as a managed fake server), Kestrel-hosted server, console tools.
- `docs/` — published documentation site source (markdown). `docs/ReleaseNotes.md` is the changelog (frozen at 3.0 — from v3 onward release notes live in GitHub Releases).

`Build.csproj` is a `Microsoft.Build.Traversal` project that references everything under `eng/src/tests/toys` — build/test/pack it to act on the whole repo. `StackExchange.Redis.slnx` is the IDE solution (XML SLNX format, not classic `.sln`).

## Build, test, pack

```bash
# Build everything (CI uses Release)
dotnet build Build.csproj -c Release /p:CI=true

# Full local build + start servers + run tests (Windows-oriented, also build.cmd)
pwsh ./build.ps1 -StartServers

# Start the Redis test servers in the background (preferred; see "Testing topology")
docker compose --file tests/RedisConfigs/docker-compose.yml up -d --wait

# Run the main test suite against one target framework (fastest inner loop)
dotnet test tests/StackExchange.Redis.Tests/StackExchange.Redis.Tests.csproj -c Release -f net10.0

# Run a single test class / method (xUnit v3 + Microsoft.Testing.Platform)
dotnet test tests/StackExchange.Redis.Tests/StackExchange.Redis.Tests.csproj -f net10.0 --filter "FullyQualifiedName~ClassName.MethodName"

# Pack the library
dotnet pack src/StackExchange.Redis/StackExchange.Redis.csproj --no-build -c Release /p:Packing=true
```

- SDK is pinned (`global.json`, `allowPrerelease: false`); CI installs the 6/8/10 runtimes. `LangVersion` is 14.
- `TreatWarningsAsErrors=true` everywhere and `Features=strict` — warnings fail the build. Analyzers (StyleCop + the custom `eng` analyzer + PublicApiAnalyzers) run as part of the build.
- Library multi-targets `net461;netstandard2.0;net472;net6.0;net8.0;net10.0`. Conditional compile symbols: `VECTOR_SAFE` (all but net461), `UNIX_SOCKET` (net6.0+). The test project targets `net481;net8.0;net10.0`; `BUILD_CURRENT` is defined on the newest TFM (disables some parallelism for brittle tests).

## Public API tracking (important — easy to trip over)

Both shipped libraries use `Microsoft.CodeAnalysis.PublicApiAnalyzers`. Any change to the public surface fails the build until you update the API text files:

- `src/StackExchange.Redis/PublicAPI/PublicAPI.{Shipped,Unshipped}.txt` (and the `net6.0/` subfolder for APIs that only exist on newer TFMs — each folder is effectively `NET_X_Y_OR_GREATER`).
- `src/RESPite/PublicAPI/...` likewise (with `net8.0/`).

Add new members to `PublicAPI.Unshipped.txt`. The build error message tells you the exact line to add.

### Backwards compatibility is paramount

This library is heavily used and referenced across the .NET ecosystem, so **hard breaks to shipped public APIs are extremely discouraged** — especially binary breaks that surface as `MissingMethodException`/`MissingFieldException` at runtime for callers compiled against an older version. Note that source-compatible changes can still be binary breaks: **adding an optional parameter to an existing method changes its signature** and breaks already-compiled callers, so do not do it. The same applies to changing parameter/return types, renaming members, or removing them.

Prefer additive, non-breaking patterns instead:
- **Add a new overload** rather than modifying an existing method's signature; use `[OverloadResolutionPriority(...)]` to steer the compiler toward the preferred overload when several would otherwise be ambiguous.
- **Deprecate, don't delete**: mark the old member `[Obsolete(...)]` (keeping it functional) and point callers at the replacement.
- When unsure whether a change is breaking, treat it as breaking and reach for an overload — or raise it for human review.

## Experimental APIs

Newer features (especially pre-release server APIs) are typically gated behind `[Experimental(...)]` diagnostic IDs defined in `src/RESPite/Shared/Experiments.cs` (`SER001`–`SER006`, e.g. `Respite = "SER004"`, version-gated server features `Server_8_4/8_6/8_8`). These IDs are in the root `NoWarn` list so consuming them internally doesn't error; docs live under `docs/exp/`.

## Architecture (the big picture)

Request flow, roughly outer → inner:

1. **`ConnectionMultiplexer`** (split across many `ConnectionMultiplexer.*.cs` partials) is the root object — one per logical Redis deployment, meant to be shared/long-lived. It owns endpoints, configuration, pub/sub, sentinel logic, and server selection.
2. **`IDatabase` / `RedisDatabase`** (`RedisDatabase.cs`, ~6k lines) is the command surface. Each command builds a **`Message`** and hands it to the multiplexer with a **`ResultProcessor<T>`** that knows how to parse the reply into the typed result. `Message.cs` and `ResultProcessor.cs` are the two hubs to understand command implementation — to add/modify a command, you create the message + pick/extend a result processor.
3. **`ServerEndPoint`** represents one physical server; **`PhysicalBridge`** manages the queue/backlog and connection lifecycle for a server; **`PhysicalConnection`** is the actual socket + read/write loop. This is where pipelining and the backlog policy live (see `docs/PipelinesMultiplexers.md`).
4. **RESPite** does the byte-level RESP framing beneath `PhysicalConnection` — scanning frames off the buffer (`RespFrameScanner`), reading values (`RespReader`, a `ref struct` with many `.cs` partials), and pooled buffers.

Cross-cutting: `CommandMap` (command renaming/disabling per server type), `ServerType`/cluster slot routing (`ClusterConfiguration`, `ServerSelectionStrategy`), `CommandFlags` (sync/async, fire-and-forget, replica preference), keyspace isolation (`KeyspaceIsolation/`), profiling (`Profiling/`), and maintenance events (`Maintenance/`). RESP3 push/attribute support is reflected in both the reader and result processors.

Partial-class file naming is heavily used: `Foo.cs` + `Foo.Bar.cs` are one type (the csproj wires `DependentUpon`). When editing a type, check for sibling `Foo.*.cs` files.

## Testing topology

Many tests are pure unit tests, or run against the in-process managed test server (`toys/StackExchange.Redis.Server`) and need no external Redis at all. The rest are **integration** tests that talk to a real server.

The integration suite needs a **full local Redis topology**, not a single server. Bring it up with docker compose:

```bash
cd tests/RedisConfigs && docker compose up -d --wait
```

Expected servers (defaults in `tests/StackExchange.Redis.Tests/Helpers/TestConfig.cs`):
- `6379` primary, `6380` replica (standalone tests use `6379,6380`)
- `6382`/`6383` failover pair, `6381`/`6384` secure/TLS
- `7000`-`7005` cluster nodes
- `7010`/`7011` + `26379`-`26381` sentinel

Tests skip as *inconclusive* when their required server is absent (e.g. cluster tests skip with "Unable to connect to server"). Override hosts/ports for local runs with a `tests/StackExchange.Redis.Tests/TestConfig.json` (gitignored). A stray container squatting on `6379` is a common failure: it makes the primary reachable but leaves no replica/cluster, so replica/cluster tests fail or skip — clear it before bringing the compose up.

To probe these servers ad hoc, the local user may have `resp-cli` installed — a `dotnet` global tool that is functionally similar to `redis-cli` (same basic flags: `-p`, `-a`, `-n`, `--tls`, `-3`). Prefer `resp-cli` when it's available; fall back to `redis-cli` otherwise.

## Agent skills

Repo-specific [Agent Skills](https://agentskills.io/home) (the portable `SKILL.md` open standard) live under `.claude/skills/`:

- `implement-resp-command` — add a new RESP command to StackExchange.Redis end-to-end (enum, interfaces, `RedisDatabase`, `ResultProcessor`, public-API tracking, and the ResultProcessor + RoundTrip unit tests).
- `summarize-database` — profile a live (often production) RESP database by sampling: discover key patterns, where data lives by count and size, and what the values are. Read-only.

That path is where Claude Code discovers them; the files themselves are tool-agnostic, so if your agent reads skills from a different directory (Codex uses `.agents/skills/`, etc.), point it at this folder or copy the skill across.

## Conventions

- Code style is enforced via `.editorconfig` + `Shared.ruleset` + StyleCop; 4-space indent, BOM + final newline on `.cs`, `System.*` usings first, no redundant `this.`. Build will fail on violations.
- `InternalsVisibleTo` exposes internals to the test/benchmark/server projects, so tests reach into internal types directly.
- `docs/` markdown is the user-facing documentation; update it for user-visible behavior changes.
