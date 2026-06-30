---
name: implement-resp-command
description: Add a new Redis/RESP command (or overload) to StackExchange.Redis end-to-end — enum, interfaces, RedisDatabase implementation, ResultProcessor, public-API tracking, and the ResultProcessor + RoundTrip unit tests. Use when asked to "add/implement/support a Redis command", wire up a new RESP command, expose a server feature on IDatabase/IDatabaseAsync, or add a result processor.
---

# Implement a new RESP command

This walks through adding a command to **StackExchange.Redis** (the `src/StackExchange.Redis` client). Read `AGENTS.md` first — especially **Public API tracking → Backwards compatibility is paramount** and **Architecture**. Do every step; the build and the API analyzer will fail loudly if you skip the wiring, but the *tests* are what prove the command actually works.

Use an existing, similarly-shaped command as your template (e.g. `StringGet`/`GET` for a simple key command, `StreamAutoClaim`/`XAUTOCLAIM` for a structured aggregate reply). Grep `RedisDatabase.cs` for one and mirror it.

## Steps

1. **Add the command name to the `RedisCommand` enum** — `src/StackExchange.Redis/Enums/RedisCommand.cs`. The enum member name *is* the wire token (`CommandMap` serializes it via `command.ToString()`), so name it exactly as Redis expects (e.g. `GETEX`, `XAUTOCLAIM`). Keep the existing alphabetical grouping.
   - **Then classify it in `IsPrimaryOnly`** (the `switch` in the same file). That switch is **exhaustive** — its `default` *throws* `ArgumentOutOfRangeException` (*"Every RedisCommand must be defined in Message.IsPrimaryOnly…"*) at runtime for any unlisted command, so this is not optional. Put writes/mutations in the primary-only list; pure reads fall through to the replica-eligible branch. Getting it wrong mis-routes the command (e.g. a write sent to a replica).

2. **Declare the method on the interfaces** — `src/StackExchange.Redis/Interfaces/IDatabase.cs` *and* `IDatabaseAsync.cs` (or the `.Arrays.cs` / `.VectorSets.cs` partials when relevant). Always provide both sync and async.
   - **Back-compat:** never add an optional parameter to an existing shipped method (binary break → `MissingMethodException`). Add a new **overload** instead; see `AGENTS.md`.
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
- Double-check no shipped signature changed (back-compat).
