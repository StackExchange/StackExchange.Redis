using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// The cluster's own REST API on port 9443, for the few facts the fault injector does not expose.
/// </summary>
/// <remarks>
/// Reads only. Anything that *changes* state goes through the injector so it is recorded as a job with an
/// action id - which is also how the console works, and it means a scenario can be reconstructed afterwards
/// from the injector's history rather than from somebody's memory.
/// </remarks>
public sealed class ClusterRestClient : IDisposable
{
    private readonly HttpClient _http;

    public ClusterRestClient(FaultInjectorEnvironment.ClusterCredentials credentials, DirectoryInfo configDirectory)
    {
        // The cluster's management (cnm) certificate is minted at bootstrap and is self-signed - its issuer is
        // itself, not any CA shipped in the config directory. `ca.crt` there signs the RESP-plane proxy
        // certificates (a different, coincidentally-named CA), so chain-building the cnm cert against it fails
        // even when it is the right environment. There is nowhere to read the cnm cert's expected fingerprint
        // from ahead of time (env_output.json does not carry it), so this pins on first use and persists the
        // fingerprint next to the rest of the environment's files - the same trust model as an SSH known_hosts
        // entry. A later run against the same environment must present the identical certificate; a changed
        // fingerprint fails loudly rather than silently accepting a new identity for a channel that carries
        // credentials. Keyed by host: multi-cluster templates put several clusters behind one directory.
        var pinPath = Path.Combine(configDirectory.FullName, $"cluster-cert.{credentials.ClusterName}.sha256");
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                if (certificate is null) return false;
                var actual = Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256));

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
            },
        };

        _http = new HttpClient(handler) { BaseAddress = credentials.RestUrl, Timeout = TimeSpan.FromSeconds(30) };
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.Username}:{credentials.Password}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
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

}
