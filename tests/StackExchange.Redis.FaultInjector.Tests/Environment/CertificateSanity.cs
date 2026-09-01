using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// Checks that the environment's TLS material belongs to the cluster we are about to talk to.
/// </summary>
/// <remarks>
/// Environments get re-provisioned, and the certificates do not always follow. Observed 2026-09-01: the folder
/// held a server certificate for <c>*.marcgravell-test-46be1d08...</c> while the live cluster was
/// <c>marcgravell-test-e21cd75d...</c> - a leftover from a previous provision, three days older than the
/// <c>env_output.json</c> beside it.
/// <para>
/// Without this check, that presents as a TLS handshake failure, which reads like a client bug and is not one:
/// the certificate genuinely does not cover the name being dialled, and refusing it is correct
/// (<c>TrustIssuer</c> tolerates chain errors only, so a name mismatch fails - as it should). Detecting it here
/// turns half an hour of certificate archaeology into a skip that names both clusters, and it costs nothing
/// because it happens before a database is provisioned.
/// </para>
/// </remarks>
internal static class CertificateSanity
{
    /// <summary>
    /// Skips when the environment's certificates were issued for a different cluster.
    /// </summary>
    public static void RequireCertificatesMatchThisCluster(FaultInjectorEnvironment environment, Action<string> log)
    {
        var clusterName = environment.Cluster?.ClusterName;
        if (clusterName is null) return; // nothing to compare against; let the connect speak for itself

        // the server certificate the environment generated, if it left one behind
        var leafPath = Path.Combine(environment.ConfigDirectory.FullName, "redis.crt");
        if (!File.Exists(leafPath)) return;

        var leaf = X509CertificateLoader.LoadCertificateFromFile(leafPath);
        var names = leaf.GetNameInfo(X509NameType.DnsName, forIssuer: false) ?? "(none)";
        log($"environment server certificate covers '{names}'; cluster is '{clusterName}'");

        if (!CoversHost(names, clusterName))
        {
            Assert.Skip(
                $"the environment's TLS certificates were issued for '{names}' but this cluster is "
                + $"'{clusterName}' - they are left over from an earlier provision, so a TLS test here would "
                + "only be measuring the mismatch. Re-provision with certificate generation enabled to run it.");
        }
    }

    /// <summary>
    /// Whether a certificate name - possibly a wildcard - covers a host.
    /// </summary>
    private static bool CoversHost(string certificateName, string host)
    {
        if (certificateName.StartsWith("*.", StringComparison.Ordinal))
        {
            // a wildcard matches one label, so compare the parent domains
            var suffix = certificateName[1..]; // ".domain"
            var dot = host.IndexOf('.');
            var parent = dot >= 0 ? host[dot..] : host;
            return string.Equals(suffix, parent, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(certificateName, host, StringComparison.OrdinalIgnoreCase);
    }
}
