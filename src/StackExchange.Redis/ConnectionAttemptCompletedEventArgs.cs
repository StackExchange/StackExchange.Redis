using System;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace StackExchange.Redis;

/// <summary>
/// Describes the outcome of a single physical connection attempt; see <see cref="ConfigurationOptions.ConnectionAttemptCompleted"/>.
/// </summary>
/// <remarks>
/// Each physical connection attempt (initial connect or reconnect, interactive or subscription) reports exactly one outcome:
/// success once the Redis handshake has completed, or failure if the attempt ends before that point. A connection that fails
/// <em>after</em> it was established is a disconnect, not a failed attempt, and does not report here.
/// </remarks>
public sealed class ConnectionAttemptCompletedEventArgs : EventArgs, ICompletable
{
    private readonly EventHandler<ConnectionAttemptCompletedEventArgs>? handler;
    private readonly object sender;
    private readonly string _physicalName;

    internal ConnectionAttemptCompletedEventArgs(
        EventHandler<ConnectionAttemptCompletedEventArgs>? handler,
        object sender,
        EndPoint? endPoint,
        ConnectionType connectionType,
        bool isSuccess,
        ConnectionAttemptStage stage,
        ConnectionFailureType failureType,
        Exception? exception,
        X509Certificate? clientCertificate,
        string? tlsHostName,
        ServerCertificateCheck? serverCertificateCheck,
        long sequenceNumber,
        DateTime completedTimeUtc,
        string? physicalName)
    {
        this.handler = handler;
        this.sender = sender;
        EndPoint = endPoint;
        ConnectionType = connectionType;
        IsSuccess = isSuccess;
        Stage = stage;
        FailureType = failureType;
        Exception = exception;
        TlsHostName = tlsHostName;
        SequenceNumber = sequenceNumber;
        CompletedTimeUtc = completedTimeUtc;
        if (serverCertificateCheck is not null)
        {
            ServerCertificatePolicyErrors = serverCertificateCheck.PolicyErrors;
            ServerCertificateChainStatus = serverCertificateCheck.ChainStatus;
            ServerCertificateAccepted = serverCertificateCheck.Accepted;
        }
        _physicalName = physicalName ?? GetType().Name;

        // Snapshot rather than retain: this is delivered on a worker after the attempt has finished, by which time
        // whoever supplied the certificate may have disposed it - and we have no business holding it (or its key).
        // This runs on the connection's own path, so each read is guarded: a certificate disposed while the attempt
        // was in flight (plausible mid-rotation) throws when read, and observing must never affect the connection.
        if (clientCertificate is not null)
        {
            ClientCertificateSubject = TryRead(clientCertificate, static c => c.Subject);
            ClientCertificateIssuer = TryRead(clientCertificate, static c => c.Issuer);
            ClientCertificateThumbprint = TryRead(clientCertificate, static c => c.GetCertHashString());
            ClientCertificateThumbprintSha256 = TryRead(clientCertificate, GetSha256Thumbprint);
        }
    }

    private static string? TryRead(X509Certificate certificate, Func<X509Certificate, string?> read)
    {
        try
        {
            return read(certificate);
        }
        catch
        {
            return null; // e.g. disposed; the identity is then simply unknown
        }
    }

    private static string GetSha256Thumbprint(X509Certificate certificate)
    {
#if NET8_0_OR_GREATER
        // RawDataMemory is a view over the certificate's own (cached) encoding, unlike RawData / GetRawCertData(),
        // which copy on every call; the span is only used for the duration of the hash, never retained
        ReadOnlySpan<byte> raw = certificate is X509Certificate2 cert2
            ? cert2.RawDataMemory.Span
            : certificate.GetRawCertData(); // base type only: no non-copying accessor
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(raw, hash);
        return Convert.ToHexString(hash); // upper-case, matching Thumbprint
#else
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(certificate.GetRawCertData())).Replace("-", "");
#endif
    }

    /// <summary>
    /// Identifies this outcome among those reported by the same multiplexer: it increases with every outcome, in the order they
    /// were recorded. Handlers are invoked on worker threads, so they can observe outcomes out of order, or concurrently; use this
    /// (rather than arrival order) to tell which outcome is the more recent.
    /// </summary>
    public long SequenceNumber { get; }

    /// <summary>
    /// When this outcome was recorded (UTC).
    /// </summary>
    public DateTime CompletedTimeUtc { get; }

    /// <summary>
    /// Gets the server endpoint of the attempt.
    /// </summary>
    public EndPoint? EndPoint { get; }

    /// <summary>
    /// Gets the connection-type of the attempt.
    /// </summary>
    public ConnectionType ConnectionType { get; }

    /// <summary>
    /// Whether the attempt completed the Redis handshake.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// How far the attempt got: <see cref="ConnectionAttemptStage.Established"/> on success, otherwise the stage during which it failed.
    /// </summary>
    public ConnectionAttemptStage Stage { get; }

    /// <summary>
    /// The type of failure, or <see cref="ConnectionFailureType.None"/> on success.
    /// </summary>
    public ConnectionFailureType FailureType { get; }

    /// <summary>
    /// The host name the library asked TLS to authenticate for this attempt (sent as SNI, and used to validate the server
    /// certificate), or null if the library did not perform TLS. This can differ from <see cref="EndPoint"/>, for example when
    /// <see cref="ConfigurationOptions.SslHost"/> is configured or a host name is inferred for an address endpoint.
    /// </summary>
    /// <remarks>When <c>SslClientAuthenticationOptions</c> is used, this is its <c>TargetHost</c>, as supplied by the caller.</remarks>
    public string? TlsHostName { get; }

    /// <summary>
    /// The errors the platform found when validating the server certificate, if the library observed that validation; null otherwise
    /// (for example: TLS was not used, the handshake failed before validation, or <c>SslClientAuthenticationOptions</c> supplied its
    /// own validation callback).
    /// </summary>
    /// <remarks>Non-empty errors do not imply rejection: a configured <see cref="ConfigurationOptions.CertificateValidation"/> callback
    /// may accept the certificate regardless; see <see cref="ServerCertificateAccepted"/>.</remarks>
    public SslPolicyErrors? ServerCertificatePolicyErrors { get; }

    /// <summary>
    /// The combined status flags of the server certificate chain, under the same conditions as <see cref="ServerCertificatePolicyErrors"/>;
    /// this gives the detail behind <see cref="SslPolicyErrors.RemoteCertificateChainErrors"/> (for example, an untrusted root or an expired certificate).
    /// </summary>
    public X509ChainStatusFlags? ServerCertificateChainStatus { get; }

    /// <summary>
    /// Whether the server certificate was accepted (by the configured validation callback, or by the platform default when none is
    /// configured), under the same conditions as <see cref="ServerCertificatePolicyErrors"/>.
    /// </summary>
    public bool? ServerCertificateAccepted { get; }

    /// <summary>
    /// Gets the exception if available (this can be null, and is always null on success).
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// The subject of the client certificate used for this attempt, if any.
    /// </summary>
    /// <remarks>
    /// <para>If the TLS handshake completed, this is the certificate actually sent to the server (null if the server did not ask for one).
    /// If the handshake itself failed, this is the certificate returned by <see cref="ConfigurationOptions.CertificateSelection"/>, when that
    /// callback ran; a certificate supplied via <c>SslClientAuthenticationOptions</c> is only identified after a completed handshake.</para>
    /// <para>This is only known when the library performs TLS itself; it is always null when a tunnel supplies the transport.</para>
    /// <para>The same applies to the other <c>ClientCertificate*</c> members. Any of them may also be null if the certificate could
    /// not be read (for example, because it was disposed while the attempt was in flight).</para>
    /// <para>Certificate subjects and issuers can contain tenant or personal identifiers; consider that before logging them.</para>
    /// </remarks>
    public string? ClientCertificateSubject { get; }

    /// <summary>
    /// The issuer of the client certificate used for this attempt, if any.
    /// </summary>
    public string? ClientCertificateIssuer { get; }

    /// <summary>
    /// The SHA-1 thumbprint (upper-case hex, as <see cref="X509Certificate2.Thumbprint"/>) of the client certificate used for this attempt, if any.
    /// </summary>
    /// <remarks>This identifies a certificate; it is not suitable as a basis for trust decisions, given SHA-1's weaknesses. Prefer
    /// <see cref="ClientCertificateThumbprintSha256"/> where possible.</remarks>
    public string? ClientCertificateThumbprint { get; }

    /// <summary>
    /// The SHA-256 thumbprint (upper-case hex) of the client certificate used for this attempt, if any.
    /// </summary>
    public string? ClientCertificateThumbprintSha256 { get; }

    void ICompletable.AppendStormLog(StringBuilder sb) =>
        sb.Append("event, connection-attempt: ").Append(EndPoint != null ? Format.ToString(EndPoint) : "n/a");

    bool ICompletable.TryComplete(bool isAsync) => ConnectionMultiplexer.TryCompleteHandler(handler, sender, this, isAsync);

    /// <summary>
    /// Returns the physical name of the connection.
    /// </summary>
    public override string ToString() => _physicalName;
}

internal sealed class ServerCertificateCheck(SslPolicyErrors policyErrors, X509ChainStatusFlags chainStatus, bool accepted)
{
    public SslPolicyErrors PolicyErrors { get; } = policyErrors;
    public X509ChainStatusFlags ChainStatus { get; } = chainStatus;
    public bool Accepted { get; } = accepted;
}
