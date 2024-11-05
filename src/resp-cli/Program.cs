
using System.CommandLine;
using System.Reflection;

// https://learn.microsoft.com/en-us/dotnet/standard/commandline/get-started-tutorial
RootCommand rootCommand = new RootCommand(
    description: "Connects to a RESP server to issue ad-hoc commands.");
rootCommand.AddOption(new Option<string>(name: "host", description: "The RESP endpoint to connect to.").AddAlias("h")
rootCommand.AddArgument(new Argument<string>()
var method = typeof(RespClient).GetMethod(nameof(RespClient.RunClient))!;
rootCommand.Add(new Option
{
   

})
//    .ConfigureFromMethod(method);
//rootCommand.Children["--input"].AddAlias("-i");
//rootCommand.Children["--output"].AddAlias("-o");
return await rootCommand.InvokeAsync(args);

static class RespClient
{
    public static void RunClient()
    {
        string? line;
        while ((line = ReadLine()) is not null)
        {
            Console.WriteLine("< " + line);
        }

        static string? ReadLine()
        {
            Console.Write("> ");
            return Console.ReadLine();
        }
    }

}
