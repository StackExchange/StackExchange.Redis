namespace RESPite;

public abstract class RespCommandMap
{
    /// <summary>
    /// Apply any remapping to the command.
    /// </summary>
    /// <param name="command">The command requested.</param>
    /// <returns>The remapped command; this can be the original command, a remapped command, or an empty instance if the command is not available.</returns>
    public abstract ReadOnlySpan<byte> Map(ReadOnlySpan<byte> command);

    /// <summary>
    /// Indicates whether the specified command is available.
    /// </summary>
    public virtual bool IsAvailable(ReadOnlySpan<byte> command)
        => Map(command).Length != 0;

    public static RespCommandMap Default { get; } = new DefaultRespCommandMap();

    private sealed class DefaultRespCommandMap : RespCommandMap
    {
        public override ReadOnlySpan<byte> Map(ReadOnlySpan<byte> command) => command;
        public override bool IsAvailable(ReadOnlySpan<byte> command) => true;
    }
}
