using StackExchange.Redis.Gui;
using Terminal.Gui;

namespace StackExchange.Redis;

internal class RespDesktop
{
    public static void Run(string host, int port, bool tls, string? user, string? pass, bool resp3)
    {
        Application.Init();

        try
        {
            using var window = new RespDesktopWindow(host, port, tls, user, pass, resp3);
            Application.Run(window);
        }
        finally
        {
            Application.Shutdown();
        }
    }
}
