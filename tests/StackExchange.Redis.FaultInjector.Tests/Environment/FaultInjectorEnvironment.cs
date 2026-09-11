using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace StackExchange.Redis.FaultInjector.Tests;

/// <summary>
/// Everything this suite needs to reach a real deployment, discovered from one directory.
/// </summary>
/// <remarks>
/// One path is the whole configuration, deliberately. That directory is the one mounted into the fault
/// injector as <c>/app/config</c> and the one <c>docker compose up</c> is run from, so it already holds the
/// compose file, the cluster credentials (<c>env_output.json</c>), the CA certificate, and - once databases
/// exist - <c>endpoints.json</c>. Asking for anything else would mean the person who provisioned the
/// environment has to hand-carry facts into the test run, which is the error-prone part.
/// <para>
/// Point at it with <c>SER_FI_CONFIG_DIR</c>; <c>FI_CONSOLE_CONFIG_DIR</c> is honoured as a fallback so this
/// runs against the same directory as the fault-injector console with no extra setup.
/// </para>
/// </remarks>
public sealed class FaultInjectorEnvironment
{
    private const string ConfigDirVariable = "SER_FI_CONFIG_DIR";
    private const string ConsoleConfigDirVariable = "FI_CONSOLE_CONFIG_DIR";
    private const string InjectorUrlVariable = "FAULT_INJECTION_API_URL";
    private const string EnabledVariable = "E2E_SCENARIO_TESTS";
    private const string DefaultInjectorUrl = "http://127.0.0.1:20324";

    /// <summary>
    /// The environment for this run, or <c>null</c> when none is configured.
    /// </summary>
    public static FaultInjectorEnvironment? Current { get; } = Discover();

    /// <summary>
    /// Why there is no environment, for a skip message that says something useful.
    /// </summary>
    public static string? UnavailableReason { get; private set; }

    private FaultInjectorEnvironment(DirectoryInfo configDirectory, Uri injectorUrl)
    {
        ConfigDirectory = configDirectory;
        InjectorUrl = injectorUrl;
    }

    public DirectoryInfo ConfigDirectory { get; }

    public Uri InjectorUrl { get; }

    /// <summary>
    /// The CA certificate that signs the proxy certificates, for <see cref="ConfigurationOptions.TrustIssuer(string)"/>.
    /// </summary>
    /// <remarks>
    /// The certificates are self-signed per environment, so trusting the issuer is how a TLS test validates
    /// anything at all. Note what this is *not*: a switch that disables validation. A test that cannot find the
    /// CA fails rather than falling back to trusting everything, because a TLS test that silently stops
    /// checking identity is worse than no TLS test - it reports success for the one thing it exists to catch.
    /// </remarks>
    public string? CertificateAuthorityPath { get; private set; }

    /// <summary>
    /// Cluster credentials for the REST API on port 9443, when <c>env_output.json</c> carries them.
    /// </summary>
    /// <remarks>
    /// Needed for the facts <c>endpoints.json</c> does not carry - notably <c>oss_cluster</c> and the
    /// advertised endpoint type - which matter because they decide which notification family a database can
    /// even emit. Tests that provision their own database know what they asked for and need none of this.
    /// </remarks>
    public ClusterCredentials? Cluster { get; private set; }

    private static FaultInjectorEnvironment? Discover()
    {
        var path = Environment.GetEnvironmentVariable(ConfigDirVariable)
            ?? Environment.GetEnvironmentVariable(ConsoleConfigDirVariable);

        if (string.IsNullOrWhiteSpace(path))
        {
            UnavailableReason = $"no fault-injector environment: set {ConfigDirVariable} to the directory holding env_output.json";
            return null;
        }

        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
        {
            // configured but wrong is a mistake worth reporting, not a reason to quietly do nothing
            UnavailableReason = $"{ConfigDirVariable} points at '{path}', which does not exist";
            return null;
        }

        var url = Environment.GetEnvironmentVariable(InjectorUrlVariable);
        var injectorUrl = Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed : new Uri(DefaultInjectorUrl);

        var result = new FaultInjectorEnvironment(directory, injectorUrl)
        {
            CertificateAuthorityPath = FindCertificateAuthority(directory),
        };
        result.Cluster = ReadClusterCredentials(directory);
        return result;
    }

    /// <summary>
    /// Whether the caller actually meant to run destructive tests against a real deployment.
    /// </summary>
    /// <remarks>
    /// Two gates rather than one, and they mean different things. Without a config directory there is nothing
    /// to talk to, which is the ordinary state for everybody else and skips. With a directory but without
    /// <c>E2E_SCENARIO_TESTS=true</c> we also skip, because these tests create and delete databases and nobody
    /// should trip that by running the full traversal. What must *not* happen is a third state where the
    /// environment is configured, meant, and broken, yet the run still reports success - see
    /// <see cref="FaultInjectorFixture"/>.
    /// </remarks>
    public static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnabledVariable), "true", StringComparison.OrdinalIgnoreCase);

    private static string? FindCertificateAuthority(DirectoryInfo directory)
    {
        // names seen across the environment templates, most specific first
        foreach (var candidate in new[] { "ca.pem", "ca.crt", "proxy_cert.pem", "redislabs_ca.pem" })
        {
            var path = Path.Combine(directory.FullName, candidate);
            if (File.Exists(path)) return path;
        }

        return null;
    }

    private static ClusterCredentials? ReadClusterCredentials(DirectoryInfo directory)
    {
        var path = Path.Combine(directory.FullName, "env_output.json");
        if (!File.Exists(path)) return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));

            // two schemas in the wild: single-cluster templates put outputs at the top level, and the AWS
            // multi-cluster template nests them under .clusters.value[N]. Both are handled because which one
            // you get is a property of the template somebody chose, not of anything a test can control.
            var root = document.RootElement;
            if (root.TryGetProperty("clusters", out var clusters)
                && clusters.TryGetProperty("value", out var values)
                && values.ValueKind == JsonValueKind.Array
                && values.GetArrayLength() > 0)
            {
                root = values[0];
            }

            var name = ReadValue(root, "cluster_name") ?? ReadValue(root, "name");
            var user = ReadValue(root, "username") ?? ReadValue(root, "cluster_username");
            var password = ReadValue(root, "password") ?? ReadValue(root, "cluster_password");

            return name is null || user is null || password is null ? null : new ClusterCredentials(name, user, password);
        }
        catch (Exception)
        {
            // a malformed env_output.json is not fatal here: only the tests that need REST enrichment care,
            // and they report it themselves rather than failing every test in the suite at discovery time
            return null;
        }
    }

    /// <summary>
    /// Reads a property that may be a bare value or a terraform-style <c>{ "value": ... }</c> wrapper.
    /// </summary>
    private static string? ReadValue(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("value", out var inner)) value = inner;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    public sealed record ClusterCredentials(string ClusterName, string Username, string Password)
    {
        public Uri RestUrl => new($"https://{ClusterName}:9443");

        /// <summary>Never let the password reach a log; the endpoints are real and reachable.</summary>
        public override string ToString() => $"{Username}@{ClusterName}";
    }
}
