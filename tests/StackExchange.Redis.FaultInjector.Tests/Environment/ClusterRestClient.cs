using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// The cluster's own REST API on port 9443, for the few facts the fault injector does not expose.
/// </summary>
/// <remarks>
/// Reads, plus one deliberate exception: <see cref="SetCrdtSyncAsync"/>, because the injector has no action for
/// it and it is the narrowest reliable way to make an Active-Active member fall behind. Everything else that
/// *changes* state goes through the injector so it is recorded as a job with an action id - which is also how
/// the console works, and it means a scenario can be reconstructed afterwards from the injector's history rather
/// than from somebody's memory.
/// </remarks>
public sealed class ClusterRestClient : IDisposable
{
    private readonly HttpClient _http;

    public ClusterRestClient(FaultInjectorEnvironment.ClusterCredentials credentials, DirectoryInfo configDirectory)
    {
        var validate = PinnedCertificateValidation(credentials, configDirectory);
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) => validate(request, certificate, chain, errors),
        };

        _http = new HttpClient(handler) { BaseAddress = credentials.RestUrl, Timeout = TimeSpan.FromSeconds(30) };
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.Username}:{credentials.Password}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
    }

    /// <summary>
    /// Validates a cluster's management certificate by pinning it on first use.
    /// </summary>
    /// <remarks>
    /// The cluster's management (cnm) certificate is minted at bootstrap and is self-signed - its issuer is
    /// itself, not any CA shipped in the config directory. <c>ca.crt</c> there signs the RESP-plane proxy
    /// certificates (a different, coincidentally-named CA), so chain-building the cnm cert against it fails even
    /// when it is the right environment. There is nowhere to read the cnm cert's expected fingerprint from ahead
    /// of time (env_output.json does not carry it), so this pins on first use and persists the fingerprint next
    /// to the rest of the environment's files - the same trust model as an SSH known_hosts entry. A later run
    /// against the same environment must present the identical certificate; a changed fingerprint fails loudly
    /// rather than silently accepting a new identity for a channel that carries credentials. Keyed by cluster:
    /// multi-cluster templates put several clusters behind one directory, and every node of one cluster presents
    /// the same certificate. Shared with the lag-aware probe under test, so both trust the same anchor.
    /// </remarks>
    public static RemoteCertificateValidationCallback PinnedCertificateValidation(
        FaultInjectorEnvironment.ClusterCredentials credentials, DirectoryInfo configDirectory)
    {
        var pinPath = Path.Combine(configDirectory.FullName, $"cluster-cert.{credentials.ClusterName}.sha256");
        return (_, certificate, _, _) =>
        {
            if (certificate is null) return false;
            var actual = Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));

            if (File.Exists(pinPath))
            {
                var expected = File.ReadAllText(pinPath).Trim();
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                {
                    throw new AuthenticationException(
                        $"cluster management certificate at {credentials.RestUrl} does not match the pinned " +
                        $"fingerprint in {pinPath} (expected {expected}, got {actual}). If this environment " +
                        "was re-provisioned, delete that file and re-run to pin the new certificate.");
                }

                return true;
            }

            // trust on first use: nothing in the config directory names the expected fingerprint ahead of
            // time, so the only available anchor is "whatever this environment presents the first time we
            // ask", pinned from here on.
            File.WriteAllText(pinPath, actual);
            return true;
        };
    }

    public void Dispose() => _http.Dispose();

    /// <summary>
    /// Every database on the cluster, as (bdb_id, name).
    /// </summary>
    public async Task<List<(int BdbId, string Name)>> ListDatabasesAsync()
    {
        using var response = await _http.GetAsync("/v1/bdbs?fields=uid,name");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var results = new List<(int, string)>();
        foreach (var bdb in document.RootElement.EnumerateArray())
        {
            if (bdb.TryGetProperty("uid", out var uid) && bdb.TryGetProperty("name", out var name)
                && uid.TryGetInt32(out var id) && name.GetString() is { } text)
            {
                results.Add((id, text));
            }
        }

        return results;
    }

    /// <summary>
    /// The uid of the named database on <em>this</em> cluster; uids are not shared across the clusters of an
    /// Active-Active database, so always look up per cluster.
    /// </summary>
    public async Task<int> FindDatabaseUidAsync(string name)
    {
        foreach (var (uid, candidate) in await ListDatabasesAsync())
        {
            if (string.Equals(candidate, name, StringComparison.Ordinal)) return uid;
        }

        throw new InvalidOperationException($"no database named '{name}' on {_http.BaseAddress}");
    }

    /// <summary>
    /// Pauses or resumes sync <em>into</em> this cluster's member of an Active-Active database
    /// (<c>crdt_sync</c>: <c>paused</c> or <c>enabled</c>), waiting until the database reports it and is active.
    /// </summary>
    /// <remarks>
    /// Measured as narrow and reliably undone: one member, no network change, active again within seconds.
    /// Callers must resume in a <c>finally</c>.
    /// </remarks>
    public async Task SetCrdtSyncAsync(int uid, string value)
    {
        using (var put = await _http.PutAsJsonAsync($"/v1/bdbs/{uid}", new Dictionary<string, object> { ["crdt_sync"] = value }))
        {
            put.EnsureSuccessStatusCode();
        }

        for (int i = 0; i < 60; i++)
        {
            using var response = await _http.GetAsync($"/v1/bdbs/{uid}?fields=crdt_sync,status");
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (document.RootElement.GetProperty("crdt_sync").GetString() == value
                && document.RootElement.GetProperty("status").GetString() == "active")
            {
                return;
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException($"bdb {uid} on {_http.BaseAddress} did not reach crdt_sync={value}");
    }
}
