using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
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

    public ClusterRestClient(FaultInjectorEnvironment.ClusterCredentials credentials, string? certificateAuthorityPath)
    {
        var handler = new HttpClientHandler();

        if (certificateAuthorityPath is not null)
        {
            // The cluster's management certificate is self-signed per environment, like the proxy ones. Pin to
            // the CA in the config directory rather than accepting anything: this channel carries credentials.
            var issuer = X509CertificateLoader.LoadCertificateFromFile(certificateAuthorityPath);
            handler.ServerCertificateCustomValidationCallback = (_, certificate, chain, _) =>
            {
                if (certificate is null || chain is null) return false;
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(issuer);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                return chain.Build(certificate);
            };
        }

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
