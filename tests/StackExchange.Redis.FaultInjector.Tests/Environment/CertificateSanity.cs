using System;
using System.Collections.Generic;
using System.Linq;
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
        var names = ReadDnsNames(leaf);
        log($"environment server certificate covers [{string.Join(", ", names)}]; cluster is '{clusterName}'");

        if (!names.Any(name => CoversClusterEndpoints(name, clusterName)))
        {
            Assert.Skip(
                $"the environment's TLS certificates cover [{string.Join(", ", names)}] but this cluster is "
                + $"'{clusterName}' - they are left over from an earlier provision, so a TLS test here would "
                + "only be measuring the mismatch. Re-provision with certificate generation enabled to run it.");
        }
    }

    /// <summary>
    /// Every DNS name a certificate carries, not just the first.
    /// </summary>
    /// <remarks>
    /// <c>GetNameInfo</c> returns one name, which is not enough: these certificates carry both a wildcard and
    /// the bare cluster name, and which one comes back decides the answer. Reading the SAN extension properly is
    /// the difference between a check that works and one that skips a perfectly good environment - as the first
    /// version of this did.
    /// </remarks>
    private static List<string> ReadDnsNames(X509Certificate2 certificate)
    {
        foreach (var extension in certificate.Extensions)
        {
            if (extension is X509SubjectAlternativeNameExtension san)
            {
                return [.. san.EnumerateDnsNames()];
            }
        }

        var subject = certificate.GetNameInfo(X509NameType.DnsName, forIssuer: false);
        return subject is null ? [] : [subject];
    }

    /// <summary>
    /// Whether a certificate name covers the endpoints of a given cluster.
    /// </summary>
    /// <remarks>
    /// The hosts actually dialled are *database* endpoints - <c>redis-13500.&lt;cluster&gt;</c> - so a wildcard
    /// whose parent is the cluster name is exactly right, even though (correctly) it does not match the bare
    /// cluster name itself. An exact SAN for the cluster name also counts, since that is what the REST API is
    /// reached by.
    /// </remarks>
    private static bool CoversClusterEndpoints(string certificateName, string clusterName)
    {
        if (certificateName.StartsWith("*.", StringComparison.Ordinal))
        {
            return string.Equals(certificateName[2..], clusterName, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(certificateName, clusterName, StringComparison.OrdinalIgnoreCase);
    }
}
