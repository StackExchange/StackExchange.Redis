using Garnet;

try
{
    using var server = new GarnetServer(args);
    server.Start();

    Thread.Sleep(Timeout.Infinite);
    return 0; // never reached
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Unable to initialize server due to exception: {ex.Message}");
    return -1;
}
