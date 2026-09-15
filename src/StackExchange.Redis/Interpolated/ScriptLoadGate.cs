using System.Text;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// The <see cref="IRespPreambleGate"/> for a <c>SCRIPT LOAD</c>: needed only while the endpoint this
    /// write landed on is not believed to hold the script.
    /// </summary>
    /// <param name="script">The Lua source, which is how the endpoint's belief is keyed.</param>
    /// <param name="hash">The script's SHA1 as lower-case hex.</param>
    /// <remarks>
    /// <b>Per endpoint, not per connection</b>, because the server's script cache is server-wide: a script
    /// loaded on one connection is available on the next, and the existing path has always recorded it that
    /// way. The reconnect and restart cases are already handled by the <c>RunId</c> check that flushes this
    /// belief when a server's identity changes underneath it.
    /// </remarks>
    internal sealed class ScriptLoadGate(string script, string hash) : IRespPreambleGate
    {
        private byte[]? _asciiHash;

        public bool IsNeeded(PhysicalConnection connection)
            // no endpoint to ask is not "already loaded" - send it, and let the redundant load be harmless
            => connection.BridgeCouldBeNull?.ServerEndPoint is not { } endpoint || !endpoint.IsScriptLoaded(script);

        public void OnEstablished(PhysicalConnection connection)
            // the ASCII of the hex, matching what GetScriptHash hands back to the classic path; computed
            // once per script rather than per call, since this object outlives both
            => connection.BridgeCouldBeNull?.ServerEndPoint?.AddScript(
                script, _asciiHash ??= Encoding.ASCII.GetBytes(hash));
    }
}
