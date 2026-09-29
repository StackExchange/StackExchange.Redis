using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace StackExchange.Redis;

public partial class ConnectionMultiplexer
{
    private readonly HashSet<string> _libraryNameSuffixHash = new();
    private string _libraryNameSuffixCombined = "";

    /// <inheritdoc cref="IConnectionMultiplexer.AddLibraryNameSuffix(string)" />
    public void AddLibraryNameSuffix(string suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix)) return; // trivial

        // sanitize and re-check
        suffix = ServerEndPoint.ClientInfoSanitize(suffix ?? "").Trim();
        if (string.IsNullOrWhiteSpace(suffix)) return; // trivial

        lock (_libraryNameSuffixHash)
        {
            if (!_libraryNameSuffixHash.Add(suffix)) return; // already cited; nothing to do

            _libraryNameSuffixCombined = "-" + string.Join("-", _libraryNameSuffixHash.OrderBy(_ => _));
        }

        // if we get here, we *actually changed something*; we can retroactively fixup the connections
        var libName = GetFullLibraryName(); // note this also checks SetClientLibrary
        if (string.IsNullOrWhiteSpace(libName) || !CommandMap.IsAvailable(RedisCommand.CLIENT)) return; // disabled on no lib name

        // the other core's connections, which IServer.Execute below does not reach: it reaches whichever
        // core IServer is on, and while both exist that is one of the two. See RespNewCore.SetLibraryName.
        NewCoreIfCreated?.SetLibraryName(libName);

        // Sent through the pipeline rather than through IServer.Execute, which is what this used to do.
        // IServer is a ROUTING abstraction and the routing has moved: under the engine flag its commands go
        // on the other core's socket, so a retro-fix issued that way named the wrong connection and left the
        // one it was trying to fix unnamed. What this wants is "this endpoint's interactive connection", which
        // is a ServerEndPoint and not an IServer.
        foreach (var endpoint in GetServerSnapshot())
        {
            try
            {
                // note we can only fixup the *interactive* channel; that's tolerable here
                if (!endpoint.IsConnected) continue;

                // best effort only
                var msg = Message.Create(
                    -1,
                    CommandFlags.FireAndForget,
                    RedisCommand.CLIENT,
                    RedisLiterals.SETINFO,
                    RedisLiterals.lib_name,
                    libName.AsRedisValue());
                msg.SetInternalCall();
                ExecuteSyncImpl(msg, ResultProcessor.DemandOK, endpoint);
            }
            catch (Exception ex)
            {
                // if an individual server trips, that's fine - best effort; note we're using
                // F+F here anyway, so we don't *expect* any failures
                Debug.WriteLine(ex.Message);
            }
        }
    }

    internal string GetFullLibraryName()
    {
        var config = RawConfig;
        if (!config.SetClientLibrary) return ""; // disabled

        var libName = config.LibraryName;
        if (string.IsNullOrWhiteSpace(libName))
        {
            // defer to provider if missing (note re null vs blank; if caller wants to disable
            // it, they should set SetClientLibrary to false, not set the name to empty string)
            libName = config.Defaults.LibraryName;
        }

        libName = ServerEndPoint.ClientInfoSanitize(libName);
        // if no primary name, return nothing, even if suffixes exist
        if (string.IsNullOrWhiteSpace(libName)) return "";

        return libName + Volatile.Read(ref _libraryNameSuffixCombined);
    }
}
