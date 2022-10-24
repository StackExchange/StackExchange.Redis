using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace StackExchange.Redis;

internal sealed class LogProxy : IDisposable
{
    public static LogProxy? TryCreate(TextWriter writer, ConfigurationOptions options)
        => writer == null && options?.Logger == null
        ? null
        : new LogProxy(writer, options?.Logger);

    private TextWriter? _log;
    private ILogger _logger;
    internal static Action<string> NullWriter = _ => { };
    public object SyncLock => this;

    private LogProxy(TextWriter log, ILogger logger)
    {
        _log = log;
        _logger = logger;
    }

    public void WriteLine()
    {
        if (_log is not null) // note: double-checked
        {
            lock (SyncLock)
            {
                _log?.WriteLine();
            }
        }
    }

    public void LogInfo(string message = null)
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

    public void LogError(Exception ex, string message)
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

    public void WriteLine(string prefix, string message)
    {
        if (_log != null) // note: double-checked
        {
            lock (SyncLock)
            {
                _log?.WriteLine($"{DateTime.UtcNow:HH:mm:ss.ffff}: {prefix}{message}");
            }
        }
    }

    public override string ToString()
    {
        string s = null;
        if (_log != null)
        {
            lock (SyncLock)
            {
                s = _log?.ToString();
            }
        }
        return s ?? base.ToString();
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
