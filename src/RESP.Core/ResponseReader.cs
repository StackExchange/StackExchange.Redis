// using System.Buffers;
// using RESPite.Messages;
//
// namespace Resp;
//
// /// <summary>
// /// Base implementation for RESP writers that do not depend on the request parameter.
// /// </summary>
// public abstract class ResponseReader<TResponse> : IReader<Empty, TResponse>, IRespReader<Empty, TResponse>
// {
//     TResponse IReader<Empty, TResponse>.Read(in Empty request, in ReadOnlySequence<byte> content)
//         => Read(content);
//
//     /// <summary>
//     /// Read a raw RESP payload.
//     /// </summary>
//     public virtual TResponse Read(scoped in ReadOnlySequence<byte> content)
//     {
//         var reader = new RespReader(in content);
//         reader.MoveNext();
//         return Read(ref reader);
//     }
//
//     /// <summary>
//     /// Read a RESP payload via the <see cref="RespReader"/> API.
//     /// </summary>
//     public virtual TResponse Read(ref RespReader reader)
//         => throw new NotSupportedException("A " + nameof(Read) + " overload must be overridden");
//
//     TResponse IRespReader<Empty, TResponse>.Read(in Empty request, ref RespReader reader)
//         => Read(ref reader);
// }
//
// /// <summary>
// /// Base implementation for RESP writers that do depend on the request parameter.
// /// </summary>
// public abstract class ResponseReader<TRequest, TResponse> : IReader<TRequest, TResponse>, IRespReader<TRequest, TResponse>
// {
//     /// <summary>
//     /// Read a raw RESP payload.
//     /// </summary>
//     public virtual TResponse Read(in TRequest request, in ReadOnlySequence<byte> content)
//     {
//         var reader = new RespReader(in content);
//         reader.MoveNext();
//         return Read(in request, ref reader);
//     }
//
//     /// <summary>
//     /// Read a RESP payload via the <see cref="RespReader"/> API.
//     /// </summary>
//     public virtual TResponse Read(in TRequest request, ref RespReader reader)
//         => throw new NotSupportedException("A " + nameof(Read) + " overload must be overridden");
// }
