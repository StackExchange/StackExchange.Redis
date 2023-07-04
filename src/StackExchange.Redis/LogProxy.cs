using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace StackExchange.Redis;

internal sealed class LogProxy : IDisposable
{
    public static LogProxy? TryCreate(TextWriter? writer, ILogger? logger)
        => writer is null && logger is null
        ? null
        : new LogProxy(writer, logger);

    private TextWriter? _log;
    private ILogger? _logger;
    internal static Action<string> NullWriter = _ => { };
    public object SyncLock => this;

    private LogProxy(TextWriter? log, ILogger? logger)
    {
        _log = log;
        _logger = logger;
    }

    public void LogInfo(string? message = null)
    {
        var msg = $"{DateTime.UtcNow:HH:mm:ss.ffff}: {message}";
        if (_log != null) // note: double-checked
        {
            lock (SyncLock)
            {
                _log?.WriteLine(msg);
            }
        }
        _logger?.LogInformation(msg);
    }

    public void LogTrace(string message)
    {
        var msg = $"{DateTime.UtcNow:HH:mm:ss.ffff}: {message}";
        if (_log != null) // note: double-checked
        {
            lock (SyncLock)
            {
                _log?.WriteLine(msg);
            }
        }
        _logger?.LogTrace(msg);
    }

    public void LogError(Exception? ex, string message)
    {
        var msg = $"{DateTime.UtcNow:HH:mm:ss.ffff}: {message}";
        if (_log != null) // note: double-checked
        {
            lock (SyncLock)
            {
                _log?.WriteLine(msg);
            }
        }
        _logger?.LogError(ex, msg);
    }

    public override string ToString()
    {
        string? s = null;
        if (_log != null)
        {
            lock (SyncLock)
            {
                s = _log?.ToString();
            }
        }
        return s ?? base.ToString() ?? string.Empty;
    }

    public void Dispose()
    {
        if (_log != null) // note: double-checked
        {
            lock (SyncLock)
            {
                _log = null;
            }
        }
        _logger = null;
    }
}
