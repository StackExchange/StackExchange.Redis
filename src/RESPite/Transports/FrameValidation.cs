namespace RESPite.Transports;

/// <summary>
/// Controls when frame validation occurs.
/// </summary>
public enum FrameValidation
{
    /// <summary>
    /// Frame validation disabled.
    /// </summary>
    Disabled = 0,

    /// <summary>
    /// Frame validation enabled in debug builds only.
    /// </summary>
    Debug = 1,

    /// <summary>
    /// Frame validation enabled.
    /// </summary>
    Enabled = 2,
}
