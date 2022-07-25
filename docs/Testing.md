Testing
===

Welcome to documentation for the `StackExchange.Redis` test suite!

Supported platforms:
- Windows (all tests)
- Other .NET-supported platforms (.NET Core tests)

The unit and integration tests here are fairly straightforward. There are 2 primary steps:
1. Start the servers

This can be done either by installing Docker and running `docker compose up` in the `tests\RedisConfigs` folder or by running the `start-all` script in the same folder. Docker is the preferred method.

2. Run the tests

Tests default to `127.0.0.1` as their server, however you can override any of the test IPs/hostnames and ports by placing a `TestConfig.json` in the `StackExchange.Redis.Tests\` folder. This file is intentionally in `.gitignore` already, as it's for *your* personal overrides. This is useful for testing local or remote servers, different versions, various ports, etc.

You can find all the JSON properties at [TestConfig.cs](https://github.com/StackExchange/StackExchange.Redis/blob/main/tests/StackExchange.Redis.Tests/Helpers/TestConfig.cs). An example override (everything not specified being a default) would look like this:
```json
{
  "RunLongRunning": true,
  "PrimaryServer": "192.168.0.42",
  "PrimaryPort": 12345
}
```
<sub>Note: if a server isn't specified, the related tests should be skipped as inconclusive.</sub>

### Instructions for Windows
The tests are run (by default) as part of the build. You can simply run this in the repository root:
```cmd
.\build.cmd -BuildNumber local
```