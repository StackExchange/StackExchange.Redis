#if NETCORE
using System;
#endif
using System.Text.RegularExpressions;

namespace StackExchange.Redis
{
    internal static class InternalRegexCompiledOption
    {
        private static readonly RegexOptions RegexCompiledOption;

        static InternalRegexCompiledOption()
        {
#if NETCORE
            if (!Enum.TryParse("Compiled", out RegexCompiledOption))
                RegexCompiledOption = RegexOptions.None;
#else
            RegexCompiledOption = RegexOptions.Compiled;
#endif
        }

        /// <summary>
        /// Gets the default <see cref="RegexOptions"/> to use.
        /// <see cref="System.Text.RegularExpressions.RegexOptions.Compiled"/> option isn't available yet for dnxcore50.
        /// This returns <see cref="System.Text.RegularExpressions.RegexOptions.Compiled"/> if it is supported; 
        /// <see cref="System.Text.RegularExpressions.RegexOptions.None"/> otherwise.
        /// </summary>
        public static RegexOptions Default
        {
            get
            {
                return RegexCompiledOption;
            }
        }
    }
}
