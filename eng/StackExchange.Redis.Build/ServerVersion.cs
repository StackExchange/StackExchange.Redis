using System.Globalization;
using Microsoft.CodeAnalysis.Diagnostics;

namespace StackExchange.Redis.Build;

/// <summary>
/// The server version a suggestion needs, and the caller's declared minimum to compare it against.
/// </summary>
/// <remarks>
/// Only major/minor: server features land on minor boundaries, and the extra precision would be false anyway
/// (release candidates report as the *previous* minor with a high patch - 8.4 RC1 is 8.3.224 - so a patch
/// comparison would need the same RC fudging <c>RedisFeatures</c> does, for no benefit to a suggestion).
/// </remarks>
internal readonly struct ServerVersion
{
    /// <summary>The suggestion works on any server this library supports, so there is nothing to say.</summary>
    public static ServerVersion Any => default;

    public ServerVersion(int major, int minor)
    {
        Major = major;
        Minor = minor;
    }

    public int Major { get; }
    public int Minor { get; }

    /// <summary>Is this an actual requirement, as opposed to <see cref="Any"/>?</summary>
    public bool IsSpecified => Major != 0;

    /// <summary>Would a server at <paramref name="available"/> support a feature needing this version?</summary>
    public bool IsSatisfiedBy(ServerVersion available)
        => !IsSpecified
           || !available.IsSpecified // nothing declared: assume the newest, which is why the default shows all
           || available.Major > Major
           || (available.Major == Major && available.Minor >= Minor);

    /// <inheritdoc/>
    public override string ToString() => Major + "." + Minor;

    /// <summary>
    /// The minimum server version the project has declared, if any.
    /// </summary>
    /// <remarks>
    /// Two spellings, matching the two ways a consumer can reasonably configure an analyzer: an
    /// <c>.editorconfig</c>/<c>.globalconfig</c> entry, or an MSBuild property surfaced through
    /// <c>CompilerVisibleProperty</c>. Unset means show everything - a version-gated suggestion is still useful
    /// to someone who has not thought about server versions yet, and silence by default would hide the rule
    /// from exactly the people it is for.
    /// </remarks>
    public static ServerVersion FromOptions(AnalyzerOptions? options)
    {
        if (options is not null)
        {
            var global = options.AnalyzerConfigOptionsProvider.GlobalOptions;
            if (global.TryGetValue("redis.min_server_version", out var value) && TryParse(value, out var version))
            {
                return version;
            }

            // the MSBuild <RedisMinServerVersion> property, surfaced by the CompilerVisibleProperty declared in
            // the build/ props we ship; the build_property. prefix is how MSBuild properties arrive here
            if (global.TryGetValue("build_property.RedisMinServerVersion", out value) && TryParse(value, out version))
            {
                return version;
            }
        }

        return Any;
    }

    /// <summary>
    /// Parses "8", "8.4", "8.4.1" - anything past the minor is accepted and ignored.
    /// </summary>
    /// <remarks>
    /// Deliberately lenient: a value we cannot read is treated as "unset" and so shows everything, because
    /// silently hiding suggestions over a typo in a config value would be very hard to work out.
    /// </remarks>
    private static bool TryParse(string? text, out ServerVersion version)
    {
        version = Any;
        if (string.IsNullOrWhiteSpace(text)) return false;

        // invariant throughout: this is a version from a config file, not something a human typed in a locale
        var parts = text!.Trim().Split('.');
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var major) || major <= 0) return false;

        var minor = 0;
        if (parts.Length > 1 && !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minor)) return false;
        if (minor < 0) return false;

        version = new ServerVersion(major, minor);
        return true;
    }
}
