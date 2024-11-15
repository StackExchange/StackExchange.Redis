#nullable enable

using System;
using System.Buffers;
using RESPite.Resp.Commands;
using RESPite.Resp.Writers;

namespace RESPite.Resp.Client;

public partial class CommandFactory : IRespWriterFactory<Empty>
{
    IRespWriter<Empty> IRespWriterFactory<Empty>.CreateWriter(string command) => EmptyWriter.Factory.Create(command);

    private sealed class EmptyWriter(string command, ReadOnlySpan<byte> pinnedPrefix = default) : CommandWriter<Empty>(command, 0, pinnedPrefix)
    {
        public static class Factory
        {
            private static EmptyWriter? __DBSIZE, __FLUSHDB;

            public static EmptyWriter Create(string command) => command switch
            {
                "DBSIZE" => __DBSIZE ??= new(command, "*1\r\n$6\r\nDBSIZE\r\n"u8),
                "FLUSHDB" => __FLUSHDB ??= new(command, "*1\r\n$7\r\nFLUSHDB\r\n"u8),
                _ => new(command),
            };
        }

        protected override IRespWriter<Empty> Create(string command) => Factory.Create(command);

        public override void Write(in Empty request, ref RespWriter writer)
        {
            writer.WriteRaw(CommandAndArgCount);
        }

        public override void Write(in Empty request, IBufferWriter<byte> target)
        {
            RespWriter writer = new(target);
            writer.WriteRaw(CommandAndArgCount);
            writer.Flush();
        }
    }
}

public partial class CommandFactory : IRespWriterFactory<SimpleString>
{
    IRespWriter<SimpleString> IRespWriterFactory<SimpleString>.CreateWriter(string command) => SimpleStringWriter.Factory.Create(command);

    private sealed class SimpleStringWriter(string command, ReadOnlySpan<byte> pinnedPrefix = default) : CommandWriter<SimpleString>(command, 1, pinnedPrefix)
    {
        public static class Factory
        {
            private static SimpleStringWriter? __GET, __TYPE;

            public static SimpleStringWriter Create(string command) => command switch
            {
                "GET" => __GET ??= new(command, "*2\r\n$3\r\nGET\r\n"u8),
                "TYPE" => __TYPE ??= new(command, "*2\r\n$4\r\nTYPE\r\n"u8),
                _ => new(command),
            };
        }

        protected override IRespWriter<SimpleString> Create(string command) => Factory.Create(command);

        public override void Write(in SimpleString request, ref RespWriter writer)
        {
            writer.WriteRaw(CommandAndArgCount);
            writer.WriteBulkString(request);
        }

        public override void Write(in SimpleString request, IBufferWriter<byte> target)
        {
            RespWriter writer = new(target);
            writer.WriteRaw(CommandAndArgCount);
            writer.WriteBulkString(request);
            writer.Flush();
        }
    }
}

public partial class CommandFactory : IRespWriterFactory<string>
{
    IRespWriter<string> IRespWriterFactory<string>.CreateWriter(string command) => StringWriter.Factory.Create(command);

    private sealed class StringWriter(string command, ReadOnlySpan<byte> pinnedPrefix = default) : CommandWriter<string>(command, 1, pinnedPrefix)
    {
        public static class Factory
        {
            private static StringWriter? __INFO;

            public static StringWriter Create(string command) => command switch
            {
                "INFO" => __INFO ??= new(command, "*2\r\n$4\r\nINFO\r\n"u8),
                _ => new(command),
            };
        }

        protected override IRespWriter<string> Create(string command) => Factory.Create(command);

        public override void Write(in string request, ref RespWriter writer)
        {
            writer.WriteRaw(CommandAndArgCount);
            writer.WriteBulkString(request);
        }

        public override void Write(in string request, IBufferWriter<byte> target)
        {
            RespWriter writer = new(target);
            writer.WriteRaw(CommandAndArgCount);
            writer.WriteBulkString(request);
            writer.Flush();
        }
    }
}


public partial class CommandFactory : IRespWriterFactory<(SimpleString, SimpleString)>
{
    IRespWriter<(SimpleString, SimpleString)> IRespWriterFactory<(SimpleString, SimpleString)>.CreateWriter(string command) => SimpleStringSimpleStringWriter.Factory.Create(command);

    private sealed class SimpleStringSimpleStringWriter(string command, ReadOnlySpan<byte> pinnedPrefix = default) : CommandWriter<(SimpleString, SimpleString)>(command, 2, pinnedPrefix)
    {
        public static class Factory
        {
            private static SimpleStringSimpleStringWriter? __SET;

            public static SimpleStringSimpleStringWriter Create(string command) => command switch
            {
                "SET" => __SET ??= new(command, "*3\r\n$3\r\nSET\r\n"u8),
                _ => new(command),
            };
        }

        protected override IRespWriter<(SimpleString, SimpleString)> Create(string command) => Factory.Create(command);

        public override void Write(in (SimpleString, SimpleString) request, ref RespWriter writer)
        {
            writer.WriteRaw(CommandAndArgCount);
            writer.WriteBulkString(request.Item1);
            writer.WriteBulkString(request.Item2);
        }

        public override void Write(in (SimpleString, SimpleString) request, IBufferWriter<byte> target)
        {
            RespWriter writer = new(target);
            writer.WriteRaw(CommandAndArgCount);
            writer.WriteBulkString(request.Item1);
            writer.WriteBulkString(request.Item2);
            writer.Flush();
        }
    }
}

public partial class CommandFactory : IRespWriterFactory<(SimpleString, int)>
{
    IRespWriter<(SimpleString, int)> IRespWriterFactory<(SimpleString, int)>.CreateWriter(string command) => SimpleStringInt32Writer.Factory.Create(command);

    private sealed class SimpleStringInt32Writer(string command, ReadOnlySpan<byte> pinnedPrefix = default) : CommandWriter<(SimpleString, int)>(command, 2, pinnedPrefix)
    {
        public static class Factory
        {
            private static SimpleStringInt32Writer? __LINDEX;

            public static SimpleStringInt32Writer Create(string command) => command switch
            {
                "LINDEX" => __LINDEX ??= new(command, "*3\r\n$6\r\nLINDEX\r\n"u8),
                _ => new(command),
            };
        }

        protected override IRespWriter<(SimpleString, int)> Create(string command) => Factory.Create(command);

        public override void Write(in (SimpleString, int) request, ref RespWriter writer)
        {
            writer.WriteRaw(CommandAndArgCount);
            writer.WriteBulkString(request.Item1);
            writer.WriteBulkString(request.Item2);
        }

        public override void Write(in (SimpleString, int) request, IBufferWriter<byte> target)
        {
            RespWriter writer = new(target);
            writer.WriteRaw(CommandAndArgCount);
            writer.WriteBulkString(request.Item1);
            writer.WriteBulkString(request.Item2);
            writer.Flush();
        }
    }
}


public partial class CommandFactory : IRespWriterFactory<(SimpleString, int, int)>
{
    IRespWriter<(SimpleString, int, int)> IRespWriterFactory<(SimpleString, int, int)>.CreateWriter(string command) => SimpleStringInt32Int32Writer.Factory.Create(command);

    private sealed class SimpleStringInt32Int32Writer(string command, ReadOnlySpan<byte> pinnedPrefix = default) : CommandWriter<(SimpleString, int, int)>(command, 3, pinnedPrefix)
    {
        public static class Factory
        {
            private static SimpleStringInt32Int32Writer? __SUBSTR, __GETRANGE;

            public static SimpleStringInt32Int32Writer Create(string command) => command switch
            {
                "SUBSTR" => __SUBSTR ??= new(command, "*4\r\n$6\r\nSUBSTR\r\n"u8),
                "GETRANGE" => __GETRANGE ??= new(command, "*4\r\n$8\r\nGETRANGE\r\n"u8),
                _ => new(command),
            };
        }

        protected override IRespWriter<(SimpleString, int, int)> Create(string command) => Factory.Create(command);

        public override void Write(in (SimpleString, int, int) request, ref RespWriter writer)
        {
            writer.WriteRaw(CommandAndArgCount);
            writer.WriteBulkString(request.Item1);
            writer.WriteBulkString(request.Item2);
            writer.WriteBulkString(request.Item3);
        }

        public override void Write(in (SimpleString, int, int) request, IBufferWriter<byte> target)
        {
            RespWriter writer = new(target);
            writer.WriteRaw(CommandAndArgCount);
            writer.WriteBulkString(request.Item1);
            writer.WriteBulkString(request.Item2);
            writer.WriteBulkString(request.Item3);
            writer.Flush();
        }
    }
}

public partial class CommandFactory : IRespWriterFactory<(SimpleString, int, SimpleString)>
{
    IRespWriter<(SimpleString, int, SimpleString)> IRespWriterFactory<(SimpleString, int, SimpleString)>.CreateWriter(string command) => SimpleStringInt32SimpleStringWriter.Factory.Create(command);

    private sealed class SimpleStringInt32SimpleStringWriter(string command, ReadOnlySpan<byte> pinnedPrefix = default) : CommandWriter<(SimpleString, int, SimpleString)>(command, 3, pinnedPrefix)
    {
        public static class Factory
        {
            private static SimpleStringInt32SimpleStringWriter? __SETEX;

            public static SimpleStringInt32SimpleStringWriter Create(string command) => command switch
            {
                "SETEX" => __SETEX ??= new(command, "*4\r\n$5\r\nSETEX\r\n"u8),
                _ => new(command),
            };
        }

        protected override IRespWriter<(SimpleString, int, SimpleString)> Create(string command) => Factory.Create(command);

        public override void Write(in (SimpleString, int, SimpleString) request, ref RespWriter writer)
        {
            writer.WriteRaw(CommandAndArgCount);
            writer.WriteBulkString(request.Item1);
            writer.WriteBulkString(request.Item2);
            writer.WriteBulkString(request.Item3);
        }

        public override void Write(in (SimpleString, int, SimpleString) request, IBufferWriter<byte> target)
        {
            RespWriter writer = new(target);
            writer.WriteRaw(CommandAndArgCount);
            writer.WriteBulkString(request.Item1);
            writer.WriteBulkString(request.Item2);
            writer.WriteBulkString(request.Item3);
            writer.Flush();
        }
    }
}
