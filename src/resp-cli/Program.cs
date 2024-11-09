using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net.Security;
using System.Net.Sockets;
using StackExchange.Redis;

Option<string> hostOption = new(
    aliases: ["--host", "-h"],
    description: "Server hostname");
hostOption.SetDefaultValue("127.0.0.1");

Option<int> portOption = new(
    aliases: ["--port", "-p"],
    description: "Server port");
portOption.SetDefaultValue(6379);

Option<bool> guiOption = new(
    aliases: ["--gui"],
    description: "Use GUI mode")
{
    Arity = ArgumentArity.Zero,
};

Option<string?> userOption = new(
    aliases: ["--user"],
    description: "Username (requires --pass)");

Option<string?> passOption = new(
    aliases: ["--pass", "-a"],
    description: "Password to use when connecting to the server (or RESPCLI_AUTH environment variable");

Option<bool> tlsOption = new(
    aliases: ["--tls"],
    description: "Establish a secure TLS connection")
{
    Arity = ArgumentArity.Zero,
};

Option<bool> resp3Option = new(
    aliases: ["-3"],
    description: "Start session in RESP3 protocol mode")
{
    Arity = ArgumentArity.Zero,
};

RootCommand rootCommand = new(description: "Connects to a RESP server to issue ad-hoc commands.")
{
    hostOption,
    portOption,
    guiOption,
    userOption,
    passOption,
    tlsOption,
    resp3Option,
};

rootCommand.SetHandler(
    async (string host, int port, bool gui, string? user, string? pass, bool tls, bool resp3) =>
    {
        try
        {
            if (string.IsNullOrEmpty(pass))
            {
                pass = Environment.GetEnvironmentVariable("RESPCLI_AUTH");
            }

            var ep = Utils.BuildEndPoint(host, port);
            if (gui)
            {
                RespDesktop.Run(host, port, tls, user, pass, resp3);
            }
            else
            {
                using var conn = await Utils.ConnectAsync(host, port, tls, Console.WriteLine);
                if (conn is not null)
                {
                    var handshake = Utils.GetHandshake(user, pass, resp3);
                    await RespClient.RunClient(conn, handshake);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
        }
    },
    hostOption,
    portOption,
    guiOption,
    userOption,
    passOption,
    tlsOption,
    resp3Option);
return await rootCommand.InvokeAsync(args);
