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

        // every connection there is; see RespNewCore.SetLibraryName
        NewCoreIfCreated?.SetLibraryName(libName);
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
