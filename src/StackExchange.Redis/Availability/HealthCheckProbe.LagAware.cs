using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RESPite;

namespace StackExchange.Redis.Availability;

public abstract partial class HealthCheckProbe
{
    /// <summary>
    /// Ask the Redis Enterprise database availability API whether the member's database is available and,
    /// for an Active-Active member, caught up; see <see cref="LagAwareOptions"/>. Runs once per member.
    /// </summary>
    /// <remarks>
    /// Healthy on HTTP 200; unhealthy on 503 (unavailable or lagging) or 404 (no such database);
    /// <see cref="HealthCheckResult.Inconclusive"/> when the question could not be asked (the REST API
    /// unreachable, credentials rejected, the database not found by discovery), so that a management-plane
    /// problem is never mistaken for the database being down. Intended as a
    /// <see cref="ConnectionGroupMember.FailbackHealthCheck"/>.
    /// </remarks>
    [Experimental(Experiments.LagAwareFailover, UrlFormat = Experiments.UrlFormat)]
    public static HealthCheckProbe LagAware(LagAwareOptions options) => new LagAwareProbe(options);

    internal sealed class LagAwareProbe : HealthCheckProbe
    {
        private const int DefaultRestPort = 9443;

        private readonly Func<CancellationToken, ValueTask<NetworkCredential>> _credentials;
        private readonly Uri? _restEndpoint;
        private readonly int? _databaseId;
        private readonly LagCheckMode _mode;
        private readonly TimeSpan? _tolerance;
        private readonly HttpClient _http;

        // per (REST endpoint, database endpoint): one probe can serve every member of a group
        private readonly ConcurrentDictionary<string, Discovered> _discovered = new(StringComparer.OrdinalIgnoreCase);

        private sealed class Discovered
        {
            public int? Uid;
            public bool? ActiveActive;
        }

        public LagAwareProbe(LagAwareOptions options)
        {
            if (options is null) throw new ArgumentNullException(nameof(options));
            _credentials = options.Credentials ?? throw new ArgumentException("Credentials are required.", nameof(options));
            if (options.LagTolerance is { } tolerance && tolerance < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(options), tolerance, "LagTolerance must not be negative.");
            }

            _restEndpoint = options.RestEndpoint;
            _databaseId = options.DatabaseId;
            _mode = options.LagCheck;
            _tolerance = options.LagTolerance;
            var handler = options.HttpMessageHandlerFactory?.Invoke() ?? CreateHandler(options.CertificateValidation, options.ClientCertificate);
            _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }; // bounded per call instead
        }

        public override HealthCheckProbeScope Scope => HealthCheckProbeScope.Member;

        internal static HttpMessageHandler CreateHandler(RemoteCertificateValidationCallback? validation, X509Certificate2? clientCertificate)
        {
#if NET
            // redirects point at a node's internal address, so following one only ever hangs; report it instead
            var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2), AllowAutoRedirect = false };
            if (validation is not null) handler.SslOptions.RemoteCertificateValidationCallback = validation;
            if (clientCertificate is not null) handler.SslOptions.ClientCertificates = [clientCertificate];
            return handler;
#elif NET461
            if (validation is not null || clientCertificate is not null)
            {
                throw new PlatformNotSupportedException("Custom certificate validation and client certificates for the REST API require .NET Framework 4.7.1 or later.");
            }
            return new HttpClientHandler { AllowAutoRedirect = false };
#else
            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            if (validation is not null)
            {
                handler.ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) => validation(request, certificate, chain, errors);
            }
            if (clientCertificate is not null)
            {
                handler.ClientCertificateOptions = ClientCertificateOption.Manual;
                handler.ClientCertificates.Add(clientCertificate);
            }
            return handler;
#endif
        }

        public override async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context)
        {
            var multiplexer = context.Server.Multiplexer;
            var endpoints = (multiplexer as IInternalConnectionMultiplexer)?.RawConfig.EndPoints;
            var configured = endpoints is { Count: > 0 } ? endpoints[0] : null;
            if (!Format.TryGetHostPort(configured, out var host, out var port))
            {
                return Inconclusive(multiplexer, "(none)", "the member has no configured endpoint");
            }

            var rest = _restEndpoint ?? new UriBuilder(Uri.UriSchemeHttps, ClusterHost(host, port.Value), DefaultRestPort).Uri;
            var key = $"{rest}|{host}:{port}";
            var known = _discovered.GetOrAdd(key, static _ => new Discovered());

            // finish inside the probe timeout, so that "could not ask" is reported as such rather than as the
            // Unhealthy a timed-out probe is otherwise given
            using var cts = new CancellationTokenSource(Budget(context.ProbeTimeout));
            try
            {
                var credential = await _credentials(cts.Token).ConfigureAwait(false);
                var auth = new AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credential.UserName}:{credential.Password}")));

                int? uid = _databaseId ?? known.Uid;
                if (uid is null)
                {
                    // filtered: an unfiltered /v1/bdbs carries database passwords, even for db_viewer
                    using var list = await GetAsync(rest, "v1/bdbs?fields=uid,endpoints", auth, cts.Token).ForAwait();
                    if (list.StatusCode != HttpStatusCode.OK) return Inconclusive(multiplexer, rest, Describe("database discovery", list));
                    uid = FindUid(await list.Content.ReadAsStringAsync().ForAwait(), host, port!.Value);
                    if (uid is null) return Inconclusive(multiplexer, rest, $"no database on this cluster has the endpoint {host}:{port}");
                    known.Uid = uid;
                }

                bool lag = _mode switch
                {
                    LagCheckMode.Enabled => true,
                    LagCheckMode.Disabled => false,
                    _ => known.ActiveActive ?? false,
                };
                if (_mode is LagCheckMode.Auto && known.ActiveActive is null)
                {
                    using var bdb = await GetAsync(rest, $"v1/bdbs/{uid}?fields=uid,crdt", auth, cts.Token).ForAwait();
                    if (bdb.StatusCode == HttpStatusCode.NotFound) return Forget(key);
                    if (bdb.StatusCode != HttpStatusCode.OK) return Inconclusive(multiplexer, rest, Describe("database lookup", bdb));
                    lag = IsActiveActive(await bdb.Content.ReadAsStringAsync().ForAwait());
                    known.ActiveActive = lag;
                }

                var path = $"v1/bdbs/{uid}/availability";
                if (lag)
                {
                    path += "?extend_check=lag";
                    if (_tolerance is { } tolerance) path += $"&availability_lag_tolerance_ms={(long)tolerance.TotalMilliseconds}";
                }

                using var response = await GetAsync(rest, path, auth, cts.Token).ForAwait();
                return response.StatusCode switch
                {
                    HttpStatusCode.OK => HealthCheckResult.Healthy,
                    HttpStatusCode.ServiceUnavailable => HealthCheckResult.Unhealthy, // unavailable, or lagging
                    HttpStatusCode.NotFound => Forget(key),
                    _ => Inconclusive(multiplexer, rest, Describe("availability", response)),
                };
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or IOException)
            {
                return Inconclusive(multiplexer, rest, ex is OperationCanceledException ? "timed out" : ex.Message);
            }
        }

        // Database endpoints are named "redis-{port}.{cluster fqdn}"; ask the cluster name, not the database's. Any
        // node answers the availability routes itself, but the others (discovery, the crdt lookup) are answered
        // only by the master: other nodes redirect to its *internal* address, unreachable from outside the
        // cluster's network. The cluster name resolves to the master. A host that does not follow the pattern
        // is used as given.
        internal static string ClusterHost(string host, int port)
        {
            var prefix = $"redis-{port}.";
            return host.Length > prefix.Length && host.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? host.Substring(prefix.Length)
                : host;
        }

        // the database has gone (or the configured uid is wrong): unhealthy, and rediscover next time
        private HealthCheckResult Forget(string key)
        {
            _discovered.TryRemove(key, out _);
            return HealthCheckResult.Unhealthy;
        }

        private static string Describe(string what, HttpResponseMessage response)
            => response.Headers.Location is { } location
                ? $"{what} answered {(int)response.StatusCode}, redirecting to {location} (set RestEndpoint to an address that answers directly)"
                : $"{what} answered {(int)response.StatusCode}";

        private static TimeSpan Budget(TimeSpan probeTimeout)
        {
            var budget = TimeSpan.FromTicks(probeTimeout.Ticks / 10 * 9);
            return budget > TimeSpan.Zero ? budget : TimeSpan.FromMilliseconds(1);
        }

        private async Task<HttpResponseMessage> GetAsync(Uri rest, string path, AuthenticationHeaderValue auth, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(rest, path));
            request.Headers.Authorization = auth;
            return await _http.SendAsync(request, cancellationToken).ForAwait();
        }

        private static HealthCheckResult Inconclusive(IConnectionMultiplexer multiplexer, object rest, string reason)
        {
            (multiplexer as ConnectionMultiplexer)?.Logger?.LogWarningLagAwareInconclusive(rest.ToString() ?? "", reason);
            return HealthCheckResult.Inconclusive;
        }

        // GET /v1/bdbs?fields=uid,endpoints => [{"uid":1,"endpoints":[{"dns_name":"redis-1.example","port":1,"addr":["10.0.0.1"]}]}]
        internal static int? FindUid(string json, string host, int port)
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var bdb in document.RootElement.EnumerateArray())
            {
                if (!bdb.TryGetProperty("uid", out var uid) || !uid.TryGetInt32(out var id)) continue;
                if (!bdb.TryGetProperty("endpoints", out var endpoints) || endpoints.ValueKind != JsonValueKind.Array) continue;
                foreach (var endpoint in endpoints.EnumerateArray())
                {
                    if (!endpoint.TryGetProperty("port", out var p) || !p.TryGetInt32(out var endpointPort) || endpointPort != port) continue;
                    if (endpoint.TryGetProperty("dns_name", out var dns) && string.Equals(dns.GetString(), host, StringComparison.OrdinalIgnoreCase)) return id;
                    if (endpoint.TryGetProperty("addr", out var addrs) && addrs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var addr in addrs.EnumerateArray())
                        {
                            if (string.Equals(addr.GetString(), host, StringComparison.OrdinalIgnoreCase)) return id;
                        }
                    }
                }
            }

            return null;
        }

        // GET /v1/bdbs/{uid}?fields=uid,crdt => {"crdt":true,"uid":1}
        internal static bool IsActiveActive(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("crdt", out var crdt)
                && crdt.ValueKind == JsonValueKind.True;
        }
    }
}
