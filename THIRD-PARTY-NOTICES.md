# Third-party notices

StackExchange.Redis and RESPite are released under the MIT license; see [LICENSE](LICENSE).
The notes below concern third-party projects and materials referenced by, or incorporated
into, this repository.

## Redis and other RESP servers

The Redis project (https://redis.io/) is independent of this client library and is licensed
separately. Redis 7.2 and earlier are licensed under the three-clause BSD license; Redis 7.4
and later are available under the Redis Source Available License v2 (RSALv2) or the Server
Side Public License v1 (SSPLv1), and Redis 8.0 and later additionally under the GNU Affero
General Public License v3 (AGPLv3). The current terms are published at
https://redis.io/legal/licenses/

Other RESP-compatible servers that this library can connect to (Valkey, Garnet, Dragonfly,
KeyDB, Azure Managed Redis, AWS ElastiCache, and others) are separate projects and products,
each licensed by its own maintainer or vendor.

## Redis documentation

The IntelliSense (XML documentation) comments on the command methods draw on the Redis command
documentation, originally published by the "redis-doc" project, which is licensed under the
Creative Commons Attribution-ShareAlike 4.0 International license:
https://github.com/redis/redis-doc (archived; see its COPYRIGHT and LICENSE files).

The current Redis documentation lives at https://redis.io/docs/latest/ and is maintained at
https://github.com/redis/docs, whose newer content is licensed under Creative Commons
Attribution-NonCommercial-ShareAlike 4.0 International; see that repository's LICENSE file.

## Code incorporated from other projects

The following source files contain code copied or adapted from other projects; the original
license notices are preserved in the files themselves.

- `src/RESPite/Shared/NullableHacks.cs` - nullable-annotation attributes for down-level target
  frameworks, from dotnet/runtime (`NullableAttributes.cs`); .NET Foundation, MIT license.
- `src/StackExchange.Redis/TaskExtensions.cs` (`TimeoutAfter`) - adapted from
  `TaskTimeoutExtensions.cs` in dotnet/corefx; .NET Foundation, MIT license.
- `src/StackExchange.Redis/Format.cs` (`TryParseEndPoint`) - adapted from `IPEndPointParser`
  in aspnet/BasicMiddleware (Microsoft.AspNetCore.HttpOverrides); .NET Foundation,
  Apache License 2.0 (https://www.apache.org/licenses/LICENSE-2.0).

## Test-only material

The test project includes `tests/StackExchange.Redis.Tests/Helpers/redis-sharp.cs`, a copy of
redis-sharp by Miguel de Icaza (https://github.com/migueldeicaza/redis-sharp), Copyright 2010
Novell, Inc., licensed under the new BSD license. It is used only as a comparison
implementation in tests and is not part of the release binaries.
