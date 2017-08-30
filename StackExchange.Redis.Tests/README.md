## Test Suite

Welcome to the `StackExchange.Redis` test suite!

Supported platforms:
- Windows

...that's it. For now. I'll add Docker files for the instances soon, unless someone's willing to get to it first. The tests (for `netcoreapp`) can run multi-platform

The unit and integration tests here are fairly straightforward. There are 2 primary steps:
1. Start the servers
2. Run the tests

Tests default to `127.0.0.1` as their server, however you can override any of the test IPs/Hostnames and ports by placing a `TestConfig.json` in the `StackExchange.Redis.Tests\` folder. This file is intentionally in `.gitignore` already, as it's for *your* personal overrides. This is useful for testing local or remote servers, different versions, various ports, etc.

You can find all the JSON properties at [TestConfig.cs](#TODO Link). An example override (everything not specified being a default) would look like this:
```json
{
  "RunLongRunning": true,
  "MasterServer": "192.168.0.42",
  "MasterPort": 12345
}
```
<sub>Note: if a server isn't specified, the related tests should be skipped as inconclusive.</sub>

### Instructions for Windows
The tests are run (by default) as part of the build. You can simply run this in the repository root:
```cmd
.\build.cmd -BuildNumber local
```

To specifically run the tests with far more options, from the repository root:
```cmd
dotnet build
.\RedisConfigs\start-for-tests.cmd
cd StackExchange.Redis.Tests
dotnet xunit
```