namespace Resp;

public readonly struct Void
{
    private static readonly Void _shared = default;
    public static ref readonly Void Instance => ref _shared;
}
