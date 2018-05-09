#if NETSTANDARD1_5
using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Strictly for compat and less #if defs on netstandard1.5
    /// </summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false)]
    internal class BrowsableAttribute : Attribute
    {
        public BrowsableAttribute(bool _) { }
    }
}
#endif