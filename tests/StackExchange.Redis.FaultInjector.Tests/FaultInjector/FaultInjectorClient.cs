using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// The fault injector's HTTP surface, as much of it as this suite needs.
/// </summary>
/// <remarks>
/// Deliberately the same shape go-redis, redis-py and node-redis wrap: <c>POST /action</c> returning an id and
/// <c>GET /action/{id}</c> to poll. They integrated independently and converged, so the contract is stable
/// enough to depend on, and their scenario documentation transfers to this client.
/// </remarks>
public sealed class FaultInjectorClient(Uri baseAddress) : IDisposable
{
    /// <remarks>
    /// No <see cref="HttpClient.Timeout"/>, deliberately. The injector *queues* actions, so a request can
    /// legitimately sit for many minutes when anything else is in flight - and a two-minute limit here failed
    /// twenty of twenty-six tests the first time the whole suite ran together, all of them reported as a
    /// cancelled HTTP request rather than as what they were. Bounding belongs to the caller's cancellation
    /// token and to <see cref="WaitForActionAsync"/>, which can tell "still working" from "wedged"; a blanket
    /// client timeout cannot.
    /// </remarks>
    private readonly HttpClient _http = new() { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>
    /// Statuses that mean "still going". Both of them, which is the point.
    /// </summary>
    /// <remarks>
    /// A job passes through <c>pending</c> *and* <c>running</c>, so a loop that waits only on <c>pending</c>
    /// returns while the work is still in flight - and then the test asserts against a cluster that has not
    /// finished changing. Documented as a trap in the console's notes; encoded here so it cannot be
    /// rediscovered.
    /// </remarks>
    private static readonly HashSet<string> PendingStatuses = new(StringComparer.OrdinalIgnoreCase) { "pending", "running", "in_progress" };

    private static readonly HashSet<string> FailedStatuses = new(StringComparer.OrdinalIgnoreCase) { "failed", "cancelled", "error" };

    public void Dispose() => _http.Dispose();

    /// <summary>
    /// Fires an action and returns its id, without waiting.
    /// </summary>
    public async Task<string> StartActionAsync(string type, object? parameters = null, CancellationToken cancellationToken = default)
    {
        var payload = new ActionRequest(type, parameters);
        using var response = await _http.PostAsJsonAsync("/action", payload, cancellationToken);
        await EnsureSuccessAsync(response, $"POST /action ({type})", cancellationToken);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return ReadActionId(document.RootElement)
            ?? throw new InvalidOperationException($"the injector accepted '{type}' but returned no action id: {document.RootElement}");
    }

    /// <summary>
    /// Fires an action and waits for it to finish, returning its final payload.
    /// </summary>
    public async Task<JsonElement> RunActionAsync(
        string type,
        object? parameters = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var id = await StartActionAsync(type, parameters, cancellationToken);
        return await WaitForActionAsync(id, timeout, cancellationToken);
    }

    /// <summary>
    /// Polls an action to completion.
    /// </summary>
    /// <remarks>
    /// The timeout is generous by default because these actions move data: a slot migration or a node coming
    /// out of maintenance mode is minutes, not seconds. A tight default here would produce failures that look
    /// like product bugs.
    /// </remarks>
    public async Task<JsonElement> WaitForActionAsync(
        string id,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(10));
        string? lastStatus = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var response = await _http.GetAsync($"/action/{id}", cancellationToken);
            await EnsureSuccessAsync(response, $"GET /action/{id}", cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var status = ReadStatus(document.RootElement);
            lastStatus = status ?? lastStatus;

            if (status is not null && !PendingStatuses.Contains(status))
            {
                if (FailedStatuses.Contains(status))
                {
                    throw new InvalidOperationException($"fault-injector action {id} ended as '{status}': {body}");
                }

                // clone: the JsonDocument is disposed with this scope, and callers keep the result
                return document.RootElement.Clone();
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"fault-injector action {id} was still '{lastStatus ?? "unknown"}' after {(timeout ?? TimeSpan.FromMinutes(10)).TotalSeconds:0}s");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    /// <summary>
    /// Discovers which triggers are valid for an effect.
    /// </summary>
    /// <remarks>
    /// Worth asking rather than hardcoding: the effect/trigger matrix is sparse - <c>maintenance_mode</c> only
    /// supports <c>remove-add</c> and <c>remove</c>, <c>failover</c> needs replication enabled - and some
    /// scenarios do not enumerate their triggers in the schema at all.
    /// </remarks>
    public async Task<JsonElement> GetValidTriggersAsync(string scenario, string effect, int clusterIndex = 0, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync($"/{scenario}?effect={Uri.EscapeDataString(effect)}&cluster_index={clusterIndex}", cancellationToken);
        await EnsureSuccessAsync(response, $"GET /{scenario}?effect={effect}", cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Runs a scenario's <c>setup</c> / <c>run</c> / <c>teardown</c> triple's individual legs.
    /// </summary>
    public async Task<JsonElement> PostScenarioAsync(
        string scenario,
        string? leg,
        IReadOnlyDictionary<string, string?> query,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var path = leg is null ? $"/{scenario}" : $"/{scenario}/{leg}";
        var separator = '?';
        foreach (var pair in query)
        {
            if (pair.Value is null) continue;
            path += $"{separator}{pair.Key}={Uri.EscapeDataString(pair.Value)}";
            separator = '&';
        }

        using var response = await _http.PostAsync(path, content: null, cancellationToken);
        await EnsureSuccessAsync(response, $"POST {path}", cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);

        // the scenario legs only *enqueue*; a caller that returns here is racing the work it asked for
        var id = ReadActionId(document.RootElement);
        return id is null ? document.RootElement.Clone() : await WaitForActionAsync(id, timeout, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string what, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        // include the body: the injector explains refusals there, and "400 Bad Request" alone has cost people
        // hours on the effect/trigger matrix
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"{what} failed: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
    }

    /// <summary>
    /// The id of a pollable action, if this response describes one.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>setup_id</c>: a scenario's setup returns a *handle* to state held in the injector,
    /// which is passed back on the run and teardown legs, and is not something <c>/action/{id}</c> knows about.
    /// Treating it as an action id polls a URL that does not exist.
    /// </remarks>
    private static string? ReadActionId(JsonElement element)
    {
        foreach (var name in new[] { "action_id", "id" })
        {
            if (element.TryGetProperty(name, out var value))
            {
                return value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.ToString(),
                    _ => null,
                };
            }
        }

        return null;
    }

    private static string? ReadStatus(JsonElement element)
        => element.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String
            ? status.GetString()
            : null;

    private sealed record ActionRequest(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("parameters")] object? Parameters);
}
