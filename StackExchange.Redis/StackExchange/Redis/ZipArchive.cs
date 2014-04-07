//#if NET40
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StackExchange.Redis
{
    internal enum CompressionLevel
    {
        Optimal = 0,
        Fastest = 1
    }

    internal enum ZipArchiveMode
    {
        Create = 1
    }

    internal class ZipArchive : IDisposable
    {
        private readonly Stream stream;

        public ZipArchive(Stream stream, ZipArchiveMode mode, bool leaveOpen)
        {

        }

        public ZipArchiveEntry CreateEntry(string entryName, CompressionLevel compressionLevel)
        {
            return null;
        }

        public void Dispose()
        {
            
        }
    }

    internal class ZipArchiveEntry
    {
        public Stream Open()
        {
            return null;
        }
    }
}
//#endif