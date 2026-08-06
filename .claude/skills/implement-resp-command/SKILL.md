---
name: implement-resp-command
description: Add a new Redis/RESP command (or overload) to StackExchange.Redis end-to-end — enum, interfaces, RedisDatabase implementation, ResultProcessor, public-API tracking, the ResultProcessor + RoundTrip unit tests, and TransactionAnalyzer coverage where the command replaces a transaction. Use when asked to "add/implement/support a Redis command", wire up a new RESP command, expose a server feature on IDatabase/IDatabaseAsync, or add a result processor.
---

# Implement a new RESP command

This walks through adding a command to **StackExchange.Redis** (the `src/StackExchange.Redis` client). Read `AGENTS.md` first — especially **Public API tracking → Backwards compatibility is paramount** and **Architecture**. Do every step; the build and the API analyzer will fail loudly if you skip the wiring, but the *tests* are what prove the command actually works.

Use an existing, similarly-shaped command as your template (e.g. `StringGet`/`GET` for a simple key command, `StreamAutoClaim`/`XAUTOCLAIM` for a structured aggregate reply). Grep `RedisDatabase.cs` for one and mirror it.

## Source the command's spec first

Before writing anything, get the command's exact argument order and reply shape — you need it for the `Message` (request bytes) and the `ResultProcessor` (reply parsing), and the round-trip test asserts both precisely.

- **Existing / released commands** are described in two authoritative places (substitute the command name, lower-case):
  - **Server source, JSON spec** — e.g. `https://github.com/redis/redis/blob/unstable/src/commands/xdelex.json`. This is the most precise: argument tokens/order/optionality, `arity`, key specs, and the **`write`/`readonly` command flags** (which directly tell you the `IsPrimaryOnly` classification) plus, often, a `reply_schema`.
  - **HTML docs** — e.g. `https://redis.io/docs/latest/commands/xdelex/`. More readable, with reply examples.
  - (For non-Redis targets the equivalents are the Valkey/Garnet/etc. source and docs — but the wire command is usually identical.)
- **Module commands** (RediSearch `FT.*`, RedisJSON `JSON.*`, RedisTimeSeries `TS.*`, RedisBloom, …) live in each module's own repo, usually as a single aggregated `commands.json` (e.g. RediSearch: `https://github.com/RediSearch/RediSearch/blob/master/commands.json`) rather than core Redis's one-file-per-command layout. Use it the same way for argument/reply shape. **But module commands are generally handled by separate companion libraries (e.g. [NRedisStack](https://github.com/redis/NRedisStack)), not core StackExchange.Redis** — so usually you won't add them here at all; ad-hoc use goes through the generic `Execute`/`ExecuteAsync(string command, …)` → `RedisResult` API. If you *do* wire one as first-class, note the wire token is dotted (`FT.SEARCH`) and a C# enum member name can't contain `.`; the token for a member whose name isn't a valid identifier is supplied via the `[AsciiHash("FT.SEARCH")]` override — see `eng/StackExchange.Redis.Build/AsciiHash.md`. Confirm that a first-class typed binding is actually intended before following the enum steps below.
- **New / unreleased commands** may not be in either yet. In that case **ask the user for the spec** — the exact argument order and a concrete sample request/reply (RESP bytes if possible) — rather than guessing; the round-trip and ResultProcessor tests are only as correct as that sample.
- **RESP2 vs RESP3:** the reply (and occasionally argument handling) can differ subtly between protocols — e.g. a map/`%` vs a flat `*` array, a double/`,` vs a bulk-string number, or added attributes. The JSON `reply_schema` sometimes distinguishes them. Capture **both** forms and handle them in the `ResultProcessor` (and cover both in the unit tests).

## Steps

1. **Add the command name to the `RedisCommand` enum** — `src/StackExchange.Redis/Enums/RedisCommand.cs`. The enum member name *is* the wire token (`CommandMap` serializes it via `command.ToString()`), so name it exactly as Redis expects (e.g. `GETEX`, `XAUTOCLAIM`). Keep the existing alphabetical grouping.
   - **Then classify it in `IsPrimaryOnly`** (the `switch` in the same file). That switch is **exhaustive** — its `default` *throws* `ArgumentOutOfRangeException` (*"Every RedisCommand must be defined in Message.IsPrimaryOnly…"*) at runtime for any unlisted command, so this is not optional. Put writes/mutations in the primary-only list; pure reads fall through to the replica-eligible branch. Getting it wrong mis-routes the command (e.g. a write sent to a replica).

2. **Declare the method on the interfaces** — `src/StackExchange.Redis/Interfaces/IDatabase.cs` *and* `IDatabaseAsync.cs` (or the `.Arrays.cs` / `.VectorSets.cs` partials when relevant). Always provide both sync and async.
   - **Back-compat:** never add an optional parameter to an existing shipped method (binary break → `MissingMethodException`). Add a new **overload** instead; see `AGENTS.md`.
   - **Additive-overload trick (avoids ambiguity):** if the shipped method has an all-optional tail (e.g. `Foo(key, int? count = null, CommandFlags flags = CommandFlags.None)`), a new overload that just appends more optional params (`Foo(key, int? count = null, int? extra = null, CommandFlags flags = ...)`) is **ambiguous** for existing calls like `Foo(key, 5)` — both candidates substitute a default, so neither is "better" (compile error `CS0121`, and the analyzer flags `RS0026`). The fix: **make the existing overload's parameters non-optional (remove the `= ...` defaults) and let the new overload carry all the optionals.** Removing a default is binary-safe (the IL signature is unchanged) and source-safe (old calls simply rebind to the new all-optional overload, which is functionally identical). Do this consistently across the interface *and* every implementor (`RedisDatabase`, `KeyPrefixed`/`KeyPrefixedDatabase`), and update the changed signatures in **`PublicAPI.Shipped.txt`** (optional→required is an edit-in-place there; the new all-optional overload goes in `PublicAPI.Unshipped.txt`). Wrap the new overload in `#pragma warning disable RS0026` since the analyzer still flags competing optional-param overloads even when they're unambiguous. Watch for the "richest" existing overload living in a sibling partial (e.g. `IDatabaseAsync.VectorSets.cs`).
   - **Implement the new member on every `IDatabase`/`IDatabaseAsync` implementor**, or the build breaks. Chiefly `KeyspaceIsolation/KeyPrefixedDatabase.cs` — and there it must prefix keys via `ToInner(key)`; a stub that forwards without prefixing compiles but **silently breaks keyspace isolation** for the new command. If the command should also be usable in batches/transactions, add it to `IBatch`/`ITransaction` and their implementations (`RedisBatch`/`RedisTransaction`/`KeyPrefixedBatch`) too.

3. **Implement in `RedisDatabase.cs`** (next to the template you picked). The standard shape:
   ```csharp
   public RedisValue StringGet(RedisKey key, CommandFlags flags = CommandFlags.None)
   {
       var msg = Message.Create(Database, flags, RedisCommand.GET, key);
       return ExecuteSync(msg, ResultProcessor.RedisValue);
   }
   public Task<RedisValue> StringGetAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
   {
       var msg = Message.Create(Database, flags, RedisCommand.GET, key);
       return ExecuteAsync(msg, ResultProcessor.RedisValue);
   }
   ```
   For argument shapes `Message.Create` doesn't cover (optional tokens, variadic args, multiple round-trips), write a private `Message` subclass overriding `WriteImpl` (search `RedisDatabase.cs` for `: Message` and `GetStringGetExMessage` for examples), or an `IMultiMessage`.

4. **Pick or write the `ResultProcessor<T>`** — `src/StackExchange.Redis/ResultProcessor.cs`. Reuse an existing one if the reply shape matches (`RedisValue`, `RedisValueArray`, `Int64`, `Boolean`, `Lease`, …). Otherwise add a nested `internal sealed class XProcessor : ResultProcessor<T>` overriding `SetResult(PhysicalConnection, Message, ref RespReader)` to parse the reply with the `RespReader`, and expose it as a `public static readonly` field. Handle RESP2 vs RESP3 and older-server reply variants here.

5. **New result types** go in `src/StackExchange.Redis/APITypes/` (mirror `StreamAutoClaimResult` etc.).

6. **Update public-API tracking** — add every new public member to `PublicAPI.Unshipped.txt` (and the `net6.0/` subfolder if the API only exists on newer TFMs). The build error tells you the exact line. See `AGENTS.md`.

7. **Write the two unit-test layers** (below). These run with **no external server**, so they're the fast, reliable proof of correctness — write them even if you also add live integration tests.

8. **Gate pre-release server features** behind `[Experimental(Experiments.Server_8_x)]` when appropriate (see `src/RESPite/Shared/Experiments.cs`).

9. **Ask whether the command is an *atomic composition*** — does it do in one round-trip what callers currently write a `MULTI`/`WATCH` transaction (or several queued commands) to achieve? A surprising number of new commands are exactly that: `GETDEL`, `GETEX`, `HGETDEL`, `SMOVE`, `SET ... NX/GET/IFEQ`, `SMISMEMBER`, every `M*`/variadic form. If yes, teach `TransactionAnalyzer` about it, or the people who would benefit most never find out it exists — see the section below.

## If the command replaces a transaction

`eng/StackExchange.Redis.Build/TransactionAnalyzer.cs` ships inside the package and tells consumers when a transaction they wrote is now one command. A new atomic command that isn't added there is invisible: the analyzer keeps quiet about exactly the code your command was written to replace. This is cheap to do at the time and nobody comes back for it later.

Work out which shape the command replaces, and add a row to the matching table in that file:

| The transaction it replaces | Table | Rule |
|---|---|---|
| one `AddCondition` + one write, where the command now takes that condition as an argument | `Map`, family A | SER300 |
| one `AddCondition` + one write, where a *newer* command subsumes both | `Map`, family B | SER301 |
| one `AddCondition` + one write, where the write's own return value already answers the condition | `Map`, family C | SER302 |
| two different queued commands | `MapPair` | SER303 |
| the same command queued N times, now a variadic overload | `MapVariadic` | SER304 |

Beyond the suggestion text, a row states as much of the following as its table has columns for:

- **The server version the *suggestion* needs** — not the one the flagged code needs. Use the same `RedisFeatures` constant the live integration test gates on, and `ServerVersion.Any` where the form predates anything realistically in service (saying "requires 2.6 or later" is noise). This is what lets a project declaring `<RedisMinServerVersion>` see only what it can act on.
- **A coverage set** — the parameter names the suggestion still carries. Anything the caller wrote that isn't in it makes the rule stay quiet, because a rewrite that silently drops an argument is worse than no suggestion: N x `StringSet(key, value, expiry)` is not `MSET`, and "helpfully" collapsing it makes the keys permanent. State names *kept*, never names dropped, so that a parameter added to an overload later fails safe. `CommandFlags` is exempt globally. Family C passes `null` meaning "everything", because it keeps the command as written and deletes only the condition.
- **Whether the same member or field has to match**, not just the same key (`Map`'s `SameMember`, `MapVariadic`'s `RequiresMember`). A condition about member `"a"` says nothing about a write to member `"b"`, and collapsing the two drops a real guard.
- **Order, where the commands are not commutative** (`MapPair`). `SET ... GET` returns the value from *before* the write; `SET` clears any TTL, so `StringSet` + `KeyExpire` is `SET ... EX` while the reverse is not. Map one direction and pin the other with a negative test.
- **Which way the keys go** (`MapVariadic`'s `ManyKeys`). `SADD` takes one key and many values, so N calls must be on the *same* key; `MSET`/`DEL` take many keys, so those must be on *different* ones. A mapping in the wrong direction suggests a command that does something else entirely.

**Write down what you decided *not* to map, and why.** The near-misses are the dangerous part and the comments in those tables are load-bearing: `ListRightPop` + `ListLeftPush` is not `LMOVE` (inside a transaction the pop's result is an unresolved `Task`, so the pushed value is a different one), N x `ListLeftPop` is not `LMPOP` (which pops from the first non-empty key, not from each). If you talk yourself out of a mapping, leave the reasoning where the next person will hit it.

Then:

- **Tests** in `tests/StackExchange.Redis.Build.Tests/` — a positive in `SER30x.cs`, and the negatives that matter in `DetectionShape.cs`. The negatives are the point: they are correct code a keener analyzer would suggest breaking, in a diagnostic shipped to every consumer. If your mapping needs the same key, the same member, a particular order, or the absence of an argument, there is a test for each, or the constraint isn't real.
- **A row in `docs/rules/SER30x.md`**, since every message links to that page for the caveats it can't carry itself.
- **A new rule ID** (rather than a row in an existing table) additionally needs a descriptor in `Diagnostics.cs` and an entry in `AnalyzerReleases.Unshipped.md`; IDs are a public contract once released, because consumers put them in `NoWarn`.

## Tests — the two layers that matter

### ResultProcessor unit test (parsing in isolation)

Proves your `ResultProcessor` turns raw RESP bytes into the right typed value. Add a class under `tests/StackExchange.Redis.Tests/ResultProcessorUnitTests/` deriving `ResultProcessorUnitTest`; feed handcrafted RESP wire strings to `Execute(resp, ResultProcessor.X)` and assert on the result; use `ExecuteUnexpected(resp, ...)` for replies that must fail. Model it on `ResultProcessorUnitTests/StreamAutoClaim.cs`:

```csharp
public class MyCommand(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Fact]
    public void Basic_Success()
    {
        var resp = "*2\r\n$3\r\n0-0\r\n*0\r\n"; // hand-built RESP reply
        var result = Execute(resp, ResultProcessor.MyCommand);
        Assert.Equal("0-0", result.NextStartId.ToString());
    }

    [Fact]
    public void WrongShape_Failure() => ExecuteUnexpected("$5\r\nhello\r\n", ResultProcessor.MyCommand);
}
```
Cover the cases that actually bite: RESP2 **and** RESP3 forms, empty arrays, null (`$-1`/`*-1`), older-server reply shapes (e.g. a 2-element vs 3-element reply across versions), and at least one malformed reply via `ExecuteUnexpected`.

### RoundTrip unit test (full write + read, still no server)

Proves the command **serializes to the exact bytes** Redis expects *and* parses back correctly, exercising `Message.WriteTo` + the command-map. Add to `tests/StackExchange.Redis.Tests/RoundTripUnitTests/` using `TestConnection.ExecuteAsync(message, processor, requestResp, responseResp, ...)`, which asserts the outbound RESP equals `requestResp` and then feeds `responseResp` back through the processor. See `RoundTripUnitTests/AdhocMessageRoundTrip.cs`:

```csharp
[Theory(Timeout = 1000)]
[InlineData("hello", "*2\r\n$4\r\nECHO\r\n$5\r\nhello\r\n")]
public async Task MyCommand_RoundTrips(string payload, string requestResp)
{
    var msg = /* build the Message exactly as RedisDatabase does */;
    var result = await TestConnection.ExecuteAsync(msg, ResultProcessor.MyCommand, requestResp, ":5\r\n", log: log);
    Assert.Equal(5, result.AsInt32());
}
```
Verify the precise outbound bytes (length prefixes included), and ideally that command-map **rename** and **disable** behave (the `MapMode` pattern in that file).

### Optional: live integration test

Only if you need to prove behavior against a real server — these need the docker Redis topology (see `AGENTS.md → Testing topology`). An **absent** server is skipped automatically by the test infrastructure, so you don't write code for that.

What you *do* need to handle for a new command is **server version**: most new commands are new server features, and the test must skip as inconclusive on servers too old to support them. Use the `require:` parameter when creating the connection — it connects and auto-skips when the live server is below the threshold:

```csharp
await using var conn = Create(require: RedisFeatures.v7_4_0_rc1);
var db = conn.GetDatabase();
// ... exercise the command ...
```
Pick the `RedisFeatures.vX_Y_Z` constant matching the version that introduced the command (see `HashFieldTests.cs` / `CopyTests.cs` for the pattern). If your command needs a version threshold that doesn't exist yet, add the constant to `RedisFeatures`. This keeps the suite green across the range of server versions CI and contributors run against.

The in-process managed server (`toys/StackExchange.Redis.Server`) may also need a handler if integration tests run against it.

## Before finishing

- `dotnet build Build.csproj -c Release /p:CI=true` — analyzers + `TreatWarningsAsErrors` must pass (this catches a missing `PublicAPI.Unshipped.txt` entry).
- `dotnet test tests/StackExchange.Redis.Tests/StackExchange.Redis.Tests.csproj -f net10.0 --filter "FullyQualifiedName~MyCommand"` — runs your new unit tests without any server.
- `dotnet test tests/StackExchange.Redis.Build.Tests/StackExchange.Redis.Build.Tests.csproj` — if you touched `TransactionAnalyzer`. Also needs no server, and takes seconds.
- Double-check no shipped signature changed (back-compat).
