using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Reruns the full <see cref="HashImportTests"/> suite with high-integrity mode enabled, exercising the coexistence of
/// the fire-and-forget <c>HIMPORT PREPARE</c> injected into the write path (which is not high-integrity) with the
/// per-command high-integrity checksum framing applied to the following <c>HIMPORT SET</c>s on the same connection.
/// </summary>
public class HighIntegrityHashImportTests(ITestOutputHelper output, SharedConnectionFixture fixture) : HashImportTests(output, fixture)
{
    internal override bool HighIntegrity => true;
}
