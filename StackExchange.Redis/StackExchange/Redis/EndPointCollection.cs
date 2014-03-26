using System;
using System.Collections.ObjectModel;
using System.Net;

namespace StackExchange.Redis
{
    /// <summary>
    /// A list of endpoints
    /// </summary>
    public sealed class EndPointCollection : Collection<EndPoint>
    {
        /// <summary>
        /// Format an endpoint
        /// </summary>
        public static string ToString(EndPoint endpoint)
        {
            return Format.ToString(endpoint);
        }

        /// <summary>
        /// Attempt to parse a string into an EndPoint
        /// </summary>
        public static EndPoint TryParse(string endpoint)
        {
            return Format.TryParseEndPoint(endpoint);
        }
        /// <summary>
        /// Adds a new endpoint to the list
        /// </summary>
        public void Add(string hostAndPort)
        {
            var endpoint = Format.TryParseEndPoint(hostAndPort);
            if (endpoint == null) throw new ArgumentException();
            Add(endpoint);
        }

        /// <summary>
        /// Adds a new endpoint to the list
        /// </summary>
        public void Add(string host, int port)
        {
            Add(Format.ParseEndPoint(host, port));
        }

        /// <summary>
        /// Adds a new endpoint to the list
        /// </summary>
        public void Add(IPAddress host, int port)
        {
            Add(new IPEndPoint(host, port));
        }



        /// <summary>
        /// See Collection&lt;T&gt;.InsertItem()
        /// </summary>
        protected override void InsertItem(int index, EndPoint item)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (Contains(item)) throw new ArgumentException("EndPoints must be unique", "item");
            base.InsertItem(index, item);
        }
        /// <summary>
        /// See Collection&lt;T&gt;.SetItem()
        /// </summary>
        protected override void SetItem(int index, EndPoint item)
        {
            if (item == null) throw new ArgumentNullException("item");
            int existingIndex = IndexOf(item);
            if (existingIndex >= 0 && existingIndex != index) throw new ArgumentException("EndPoints must be unique", "item");
            base.SetItem(index, item);
        }
    }

}
