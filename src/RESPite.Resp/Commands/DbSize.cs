using RESPite.Messages;

namespace RESPite.Resp.Commands;

/// <summary>
/// Retrieves the number of keys in the current database.
/// </summary>
public readonly partial struct DbSize : IRespCommand<DbSize, int>
{
    private static readonly DbSize _command = default;

    /// <summary>
    /// A shared reusable instance of this command.
    /// </summary>
    public static ref readonly DbSize Command => ref _command;

    private static readonly UnsafeRawCommand<DbSize> _writer = new("*1\r\n$6\r\nDBSIZE\r\n"u8);

    IWriter<DbSize> IRespCommand<DbSize, int>.Writer => _writer;

    IReader<Empty, int> IRespCommand<DbSize, int>.Reader => RespReaders.Int32;
}

#if NET8_0_OR_GREATER
public partial struct DbSize : ISharedRespCommand<DbSize, int> { }
#endif
