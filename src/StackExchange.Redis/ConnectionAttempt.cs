using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace StackExchange.Redis;

/// <summary>
/// One physical connection attempt, as <see cref="ConfigurationOptions.ConnectionAttemptCompleted"/> reports it: how
/// far it got, what TLS saw, and exactly one outcome.
/// </summary>
/// <remarks>
/// <para>
/// <b>The v3 core kept this on <c>PhysicalConnection</c></b> (#3257); this core has no such object until the attempt
/// has succeeded, so the attempt carries its own record through the transport factory and the handshake instead. The
/// semantics are main's, unchanged: success once the Redis handshake completes; failure at whatever stage was
/// reached; an attempt abandoned by the library reported as <see cref="ConnectionFailureType.ConnectionDisposed"/>,
/// one that ran out of time as <see cref="ConnectionFailureType.UnableToConnect"/> with the timeout.
/// </para>
/// <para>
/// <b>Only created when someone is listening</b>, so an application that does not subscribe pays nothing - not even
/// the certificate observation, which wraps the TLS callbacks.
/// </para>
/// </remarks>
internal sealed class ConnectionAttempt(ConnectionMultiplexer multiplexer, EndPoint endpoint, ConnectionType connectionType)
{
    private int _reported;
    private X509Certificate? _clientCertificate;
    private ServerCertificateCheck? _serverCertificateCheck;
    private volatile ConnectionAttemptStage _stage = ConnectionAttemptStage.Connect;

    /// <summary>A tracker for one attempt, or <see langword="null"/> when nobody is listening.</summary>
    internal static ConnectionAttempt? Begin(ConnectionMultiplexer multiplexer, EndPoint endpoint, ConnectionType connectionType)
        => multiplexer.RawConfig.ConnectionAttemptCompletedHandler is null ? null : new(multiplexer, endpoint, connectionType);

    /// <summary>How far the attempt has got.</summary>
    internal ConnectionAttemptStage Stage
    {
        get => _stage;
        set => _stage = value;
    }

    /// <summary>The host TLS was asked to authenticate (and sent as SNI); null when the library did not perform TLS.</summary>
    internal string? TlsHostName { get; set; }

    /// <summary>The certificate the stream reports as sent, once the TLS handshake has completed.</summary>
    internal void CaptureSentCertificate(SslStream ssl)
    {
        // after a successful handshake the stream knows the certificate actually *sent*, which supersedes what the
        // callback selected: the callback can run even when the server never asks for one, and options-supplied
        // certificates never pass through our callback at all
        try
        {
            _clientCertificate = ssl.LocalCertificate;
        }
        catch
        {
            // best effort only
        }
    }

    /// <summary>Capture what was selected, so that a failed handshake can still say which certificate it used.</summary>
    internal LocalCertificateSelectionCallback ObserveClientCertificate(LocalCertificateSelectionCallback selector)
        => (sender, targetHost, localCertificates, remoteCertificate, acceptableIssuers) =>
        {
            var selected = selector(sender, targetHost, localCertificates, remoteCertificate, acceptableIssuers);
            _clientCertificate = selected;
            return selected;
        };

    /// <summary>
    /// Capture what the platform thought of the server certificate, and what was decided: <see cref="SslStream"/> does
    /// not expose it afterwards, and an <see cref="AuthenticationException"/> only describes it in message text.
    /// </summary>
    internal RemoteCertificateValidationCallback ObserveServerCertificate(RemoteCertificateValidationCallback? validator)
        => (sender, certificate, chain, sslPolicyErrors) =>
        {
            var chainStatus = X509ChainStatusFlags.NoError;
            if (chain is not null)
            {
                foreach (var status in chain.ChainStatus)
                {
                    chainStatus |= status.Status;
                }
            }

            // with no callback configured, this is the platform default: accept only a certificate without errors
            var accepted = validator is null ? sslPolicyErrors == SslPolicyErrors.None : validator(sender, certificate, chain, sslPolicyErrors);
            Volatile.Write(ref _serverCertificateCheck, new ServerCertificateCheck(sslPolicyErrors, chainStatus, accepted));
            return accepted;
        };

    /// <summary>
    /// Supplying a validation callback (only to observe) changes the platform's message from describing the errors to
    /// "rejected by the provided callback"; restore that detail, rather than lose it by observing.
    /// </summary>
    internal AuthenticationException Redescribe(AuthenticationException exception)
        => Volatile.Read(ref _serverCertificateCheck) is { Accepted: false } check
            ? new AuthenticationException($"The remote certificate is invalid: {check.PolicyErrors} (chain status: {check.ChainStatus}).", exception)
            : exception;

    /// <summary>The handshake completed: the connection is established.</summary>
    internal void Succeeded(string? physicalName) => Report(true, ConnectionFailureType.None, null, physicalName);

    /// <summary>The attempt ended without a connection.</summary>
    internal void Failed(Exception exception, string? physicalName)
        => Report(false, Classify(exception), exception, physicalName);

    /// <summary>
    /// The server refused the credentials during the handshake: <see cref="ConnectionAttemptStage.Handshake"/> plus
    /// <see cref="ConnectionFailureType.AuthenticationFailure"/>, the classification main documents for a wrong password.
    /// </summary>
    /// <remarks>
    /// Reported as a failure although this core keeps the connection - deliberately, for parity with v3's
    /// fire-and-forget AUTH: connecting succeeds with the refusal recorded, and commands then fail with the server's
    /// own words (see <c>RespHandshake</c>). The attempt did not authenticate, which is what this event is asked.
    /// </remarks>
    internal void AuthenticationRefused(Exception exception, string? physicalName)
        => Report(false, ConnectionFailureType.AuthenticationFailure, exception, physicalName);

    /// <summary>The library gave up on the attempt in order to retry or to shut down, which says nothing about the server.</summary>
    internal void Abandoned(string? physicalName)
        => Report(
            false,
            ConnectionFailureType.ConnectionDisposed,
            new RedisConnectionException(ConnectionFailureType.ConnectionDisposed, CommandFlags.None, "The connection attempt was abandoned so that it could be retried"),
            physicalName);

    /// <summary>The attempt outlived its connect timeout.</summary>
    internal void TimedOut(Exception timeout, string? physicalName)
        => Report(false, ConnectionFailureType.UnableToConnect, timeout, physicalName);

    /// <summary>
    /// The failure type main's <c>PhysicalConnection.IdentifyFailureType</c> would give, plus what this core's own
    /// exceptions already say about themselves.
    /// </summary>
    private ConnectionFailureType Classify(Exception exception)
    {
        if (exception is AggregateException { InnerException: { } inner }) exception = inner;
        return exception switch
        {
            RedisConnectionException rce when rce.FailureType != ConnectionFailureType.None => rce.FailureType,
            AuthenticationException => ConnectionFailureType.AuthenticationFailure,
            EndOfStreamException or ObjectDisposedException => ConnectionFailureType.SocketClosed,
            SocketException when _stage == ConnectionAttemptStage.Connect => ConnectionFailureType.UnableToConnect,
            SocketException or IOException => ConnectionFailureType.SocketFailure,
            _ => _stage == ConnectionAttemptStage.Connect ? ConnectionFailureType.UnableToConnect : ConnectionFailureType.InternalFailure,
        };
    }

    private void Report(bool isSuccess, ConnectionFailureType failureType, Exception? exception, string? physicalName)
    {
        if (Interlocked.CompareExchange(ref _reported, 1, 0) != 0) return;
        var clientCertificate = Interlocked.Exchange(ref _clientCertificate, null); // nothing to gain from keeping it

        // this runs on the connection's own path, including establishing a healthy connection, so nothing that
        // merely *observes* the attempt may be allowed to throw into it
        try
        {
            multiplexer.OnConnectionAttemptCompleted(
                endpoint,
                connectionType,
                isSuccess,
                isSuccess ? ConnectionAttemptStage.Established : _stage,
                failureType,
                exception,
                clientCertificate,
                TlsHostName,
                Volatile.Read(ref _serverCertificateCheck),
                physicalName);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
    }
}
