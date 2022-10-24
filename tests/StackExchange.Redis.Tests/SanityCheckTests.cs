using System;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Reflection.Metadata;
using Xunit;

namespace StackExchange.Redis.Tests;

public sealed class SanityChecks
{
    /// <summary>
    /// Ensure we don't reference System.ValueTuple as it causes issues with .NET Full Framework
    /// </summary>
    /// <remarks>
    /// Modified from <see href="https://github.com/ltrzesniewski/InlineIL.Fody/blob/137e8b57f78b08cdc3abdaaf50ac01af50c58759/src/InlineIL.Tests/AssemblyTests.cs#L14"/>.
    /// Thanks Lucas Trzesniewski!
    /// </remarks>
    [Fact]
    public void ValueTupleNotReferenced()
    {
        using var fileStream = File.OpenRead(typeof(RedisValue).Assembly.Location);
        using var peReader = new PEReader(fileStream);
        var metadataReader = peReader.GetMetadataReader();

        foreach (var typeRefHandle in metadataReader.TypeReferences)
        {
            var typeRef = metadataReader.GetTypeReference(typeRefHandle);
            if (metadataReader.GetString(typeRef.Namespace) == typeof(ValueTuple).Namespace)
            {
                var typeName = metadataReader.GetString(typeRef.Name);
                Assert.DoesNotContain(nameof(ValueTuple), typeName);
            }
        }
    }
}
