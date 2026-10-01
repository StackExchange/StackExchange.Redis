using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Messages;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Captures what was sent and answers from a script: the executor almost every context test wants.
/// </summary>
/// <remarks>
/// <para>
/// <b>This was twenty-eight near-identical private classes</b>, which is how it came to be written: they
/// differed only in which of <see cref="Sent"/> and <see cref="Flags"/> they happened to expose, and four
/// of them wanted nothing but a count. Converting the executor to an abstract base meant editing all
/// twenty-eight by hand, which is the sort of thing that argues for itself.
/// </para>
/// <para>
/// <b>Replies clamp to the last entry</b>, so a test that sends more than it scripted keeps getting the
/// final reply rather than running off the end. That was the behaviour in every copy, and a surprising
/// number of tests rely on it - a scan that pages until the cursor is zero, say, does not want to count
/// its own round trips.
/// </para>
/// <para>
/// <b>Not sealed</b>: a handful of tests need something this cannot express - a delay, a fault, a gate, a
/// capability - and deriving is the cheap way to add one thing without re-stating the rest.
/// </para>
/// </remarks>
/// <param name="replies">The replies, in order; the last one repeats.</param>
internal class FakeExecutor(params string[] replies) : RespExecutorBase
{
    private readonly object _sync = new();
    private int _next;

    /// <summary>Every frame sent, with <c>CRLF</c> shown as <c>|</c> so an assertion reads as one line.</summary>
    public List<string> Sent { get; } = [];

    /// <summary>The flags each request carried, for the tests that assert what reached the wire.</summary>
    public List<CommandFlags> Flags { get; } = [];

    /// <summary>The keys the writer marked, captured at send time; only when <see cref="CaptureKeys"/>.</summary>
    /// <remarks>
    /// Opt-in because it costs a <see cref="KeyRange"/> array per send, and because the request is
    /// recycled afterwards - so a test that wants the keys has to have them taken here or not at all.
    /// </remarks>
    public List<string> Keys { get; } = [];

    /// <summary>Whether to record <see cref="Keys"/>; off by default.</summary>
    public bool CaptureKeys { get; set; }

    /// <summary>How many sends have happened.</summary>
    public int Sends
    {
        get
        {
            lock (_sync) return Sent.Count;
        }
    }

    /// <summary>Whether anything has been sent at all.</summary>
    public bool HasSent => Sends != 0;

    public override int Database => 0;

    /// <remarks>
    /// Locked, because a few tests drive this from more than one thread - stale-while-revalidate refreshes
    /// in the background while the caller is served - and a list that tore under that would fail somewhere
    /// else entirely.
    /// </remarks>
    public override RespPayload Send(in RespRequest request)
    {
        string reply;
        lock (_sync)
        {
            Sent.Add(Text(request.Span));
            Flags.Add(request.Flags);
            if (CaptureKeys) RecordKeys(in request);
            reply = replies[Math.Min(_next++, replies.Length - 1)];
        }

        OnSent(in request);
        return RespPayload.Create(Encoding.UTF8.GetBytes(reply));
    }

    public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
        => new(Send(request));

    /// <summary>Called after each send, for a derived executor that needs to do one more thing.</summary>
    /// <param name="request">The request that was just sent.</param>
    protected virtual void OnSent(in RespRequest request)
    {
    }

    /// <summary>A frame as a single readable line.</summary>
    public static string Text(ReadOnlySpan<byte> value)
        => Encoding.UTF8.GetString(value.ToArray()).Replace("\r\n", "|");

    private void RecordKeys(in RespRequest request)
    {
        var count = request.KeyCount;
        if (count <= 0) return;

        var ranges = new KeyRange[count];
        if (request.TryGetKeys(ranges) != count) return;

        foreach (var range in ranges) Keys.Add(Encoding.UTF8.GetString(request.GetKey(in range).ToArray()));
    }
}
