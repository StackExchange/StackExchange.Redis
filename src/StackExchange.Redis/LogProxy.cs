using System;
using System.IO;

namespace StackExchange.Redis;

internal sealed class LogProxy : IDisposable
{
    public static LogProxy? TryCreate(TextWriter? writer)
        => writer == null ? null : new LogProxy(writer);

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
    private TextWriter? _log;
    internal static Action<string> NullWriter = _ => { };

    public object SyncLock => this;
    private LogProxy(TextWriter log) => _log = log;
    public void WriteLine()
    {
        if (_log != null) // note: double-checked
        {
            lock (SyncLock)
            {
                _log?.WriteLine();
            }
        }
    }
    public void WriteLine(string? message = null)
    {
        if (_log != null) // note: double-checked
        {
            lock (SyncLock)
            {
                _log?.WriteLine($"{DateTime.UtcNow:HH:mm:ss.ffff}: {message}");
            }
        }
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
    public void Dispose()
    {
        if (_log != null) // note: double-checked
        {
            lock (SyncLock) { _log = null; }
        }
    }
}
