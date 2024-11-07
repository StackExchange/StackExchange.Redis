using System.CommandLine;
using System.CommandLine.Parsing;
using StackExchange.Redis;

Option<string> hostOption = new(
    aliases: ["--host", "-h"],
    description: "The host of the RESP server.");
hostOption.SetDefaultValue("127.0.0.1");

Option<int> portOption = new(
    aliases: ["--port", "-p"],
    description: "The port of the RESP server.");
portOption.SetDefaultValue(6379);

Option<bool> guiOption = new(
    aliases: ["--gui"],
    description: "Use GUI mode.")
{
    Arity = ArgumentArity.Zero,
};

RootCommand rootCommand = new(description: "Connects to a RESP server to issue ad-hoc commands.")
{
    hostOption,
    portOption,
    guiOption,
};

rootCommand.SetHandler(
    async (string host, int port, bool gui) =>
    {
        var config = Utils.BuildOptions(host, port);
        await using var muxer = await ConnectionMultiplexer.ConnectAsync(config);
        if (gui)
        {
            RespDesktop.Run(muxer);
        }
        else
        {
            await RespClient.RunClient(muxer);
        }
    },
    hostOption,
    portOption,
    guiOption);
return await rootCommand.InvokeAsync(args);
