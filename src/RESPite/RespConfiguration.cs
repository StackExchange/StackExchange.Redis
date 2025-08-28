namespace RESPite;

/// <summary>
/// Over-arching configuration for a RESP system.
/// </summary>
public class RespConfiguration
{
    private static readonly TimeSpan DefaultSyncTimeout = TimeSpan.FromSeconds(10);

    public static RespConfiguration Default { get; } = new(
        RespCommandMap.Default, [], DefaultSyncTimeout, NullServiceProvider.Instance);

    public static Builder Create() => default; // for discoverability

    public struct Builder // intentionally mutable
    {
        public TimeSpan? SyncTimeout { get; set; }
        public IServiceProvider? ServiceProvider { get; set; }
        public RespCommandMap? CommandMap { get; set; }
        public object? KeyPrefix { get; set; } // can be a string or byte[]

        public Builder(RespConfiguration? source)
        {
            if (source is not null)
            {
                CommandMap = source.RespCommandMap;
                SyncTimeout = source.SyncTimeout;
                KeyPrefix = source.KeyPrefix.ToArray();
                ServiceProvider = source.ServiceProvider;
                // undo defaults
                if (ReferenceEquals(CommandMap, RespCommandMap.Default)) CommandMap = null;
                if (ReferenceEquals(ServiceProvider, NullServiceProvider.Instance)) ServiceProvider = null;
            }
        }

        public RespConfiguration Create()
        {
            byte[] prefix = KeyPrefix switch
            {
                null => [],
                string { Length: 0 } => [],
                string s => Encoding.UTF8.GetBytes(s),
                byte[] { Length: 0 } => [],
                byte[] b => b.AsSpan().ToArray(), // create isolated copy for mutability reasons
                _ => throw new ArgumentException("KeyPrefix must be a string or byte[]", nameof(KeyPrefix)),
            };

            if (prefix.Length == 0 & SyncTimeout is null & CommandMap is null & ServiceProvider is null) return Default;

            return new(
                CommandMap ?? RespCommandMap.Default,
                prefix,
                SyncTimeout ?? DefaultSyncTimeout,
                ServiceProvider ?? NullServiceProvider.Instance);
        }
    }

    private RespConfiguration(
        RespCommandMap respCommandMap,
        byte[] keyPrefix,
        TimeSpan syncTimeout,
        IServiceProvider serviceProvider)
    {
        RespCommandMap = respCommandMap;
        SyncTimeout = syncTimeout;
        _keyPrefix = (byte[])keyPrefix.Clone(); // create isolated copy
        ServiceProvider = serviceProvider;
    }

    private readonly byte[] _keyPrefix;
    public IServiceProvider ServiceProvider { get; }
    public RespCommandMap RespCommandMap { get; }
    public TimeSpan SyncTimeout { get; }
    public ReadOnlySpan<byte> KeyPrefix => _keyPrefix;

    public Builder AsBuilder() => new(this);

    private sealed class NullServiceProvider : IServiceProvider
    {
        public static readonly NullServiceProvider Instance = new();
        private NullServiceProvider() { }
        public object? GetService(Type serviceType) => null;
    }

    internal T? GetService<T>() where T : class
        => ServiceProvider.GetService(typeof(T)) as T;
}
