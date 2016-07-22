using System.Text.RegularExpressions;
#if CORE_CLR
using System;
#endif

namespace StackExchange.Redis
{
    /// <summary>
    /// Credits to Sam Harwell https://github.com/dotnet/corefx/issues/340#issuecomment-120749951
    /// </summary>
    internal static class InternalRegexCompiledOption
    {
        static InternalRegexCompiledOption()
        {
#if CORE_CLR
            RegexOptions tmp; 
            if (!Enum.TryParse("Compiled", out tmp))
                tmp = RegexOptions.None;
            Default = tmp;
#else
            Default = RegexOptions.Compiled;
#endif
        }

        /// <summary>
        /// Gets the default <see cref="RegexOptions"/> to use.
        /// <see cref="System.Text.RegularExpressions.RegexOptions.Compiled"/> option isn't available yet for dnxcore50.
        /// This returns <see cref="System.Text.RegularExpressions.RegexOptions.Compiled"/> if it is supported; 
        /// <see cref="System.Text.RegularExpressions.RegexOptions.None"/> otherwise.
        /// </summary>
        public static RegexOptions Default { get; }
    }
}
