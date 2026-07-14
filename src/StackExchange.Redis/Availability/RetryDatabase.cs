using System;
using System.Diagnostics;

namespace StackExchange.Redis.Availability;

[Conditional("DEBUG")]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false)]
internal sealed class AutoDatabaseAttribute : Attribute
{
}

[AutoDatabase]
internal partial class RetryDatabase : IDatabase
{
    public int Database => throw new NotImplementedException();
    public IConnectionMultiplexer Multiplexer => throw new NotImplementedException();
}
