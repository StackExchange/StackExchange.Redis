namespace RESPite.Messages;

/// <summary>
/// When implemented by a <see cref="IRespParser{TResponse}"/> or <see cref="IRespParser{TState,TResponse}"/>,
/// indicates that the reader should not be pre-initialized to the first node - which would otherwise
/// consume attributes and errors.
/// </summary>
public interface IRespMetadataParser
{
}
