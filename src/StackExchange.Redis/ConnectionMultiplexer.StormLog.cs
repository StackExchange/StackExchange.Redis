using System.Threading;

namespace StackExchange.Redis;

public partial class ConnectionMultiplexer
{
    internal int haveStormLog = 0;
    internal string? stormLogSnapshot;
    /// <summary>
    /// Limit at which to start recording unusual busy patterns (only one log will be retained at a time).
    /// Set to a negative value to disable this feature.
    /// </summary>
    public int StormLogThreshold { get; set; } = 15;

    /// <summary>
    /// Obtains the log of unusual busy patterns.
    /// </summary>
    public string? GetStormLog() => Volatile.Read(ref stormLogSnapshot);

    /// <summary>
    /// Resets the log of unusual busy patterns.
    /// </summary>
    public void ResetStormLog()
    {
        Interlocked.Exchange(ref stormLogSnapshot, null);
        Interlocked.Exchange(ref haveStormLog, 0);
    }
}
