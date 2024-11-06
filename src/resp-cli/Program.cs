using System.CommandLine;
using System.CommandLine.Parsing;

Option<string> hostOption = new(
    aliases: new[] { "--host", "-h" },
    description: "The host of the RESP server.");
hostOption.SetDefaultValue("127.0.0.1");

Option<int> portOption = new(
    aliases: new[] { "--port", "-p" },
    description: "The port of the RESP server.");
portOption.SetDefaultValue(6379);

RootCommand rootCommand = new(description: "Connects to a RESP server to issue ad-hoc commands.")
{
    hostOption,
    portOption,
};

rootCommand.SetHandler(
    (string host, int port) => RespClient.RunClient(host, port),
    hostOption,
    portOption);
return await rootCommand.InvokeAsync(args);
