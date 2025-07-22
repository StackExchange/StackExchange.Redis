namespace StackExchange.Redis
{
    /// <summary>
    /// <a href="https://en.wikipedia.org/wiki/Bitwise_operation">Bitwise operators</a>
    /// </summary>
    public enum Bitwise
    {
        /// <summary>
        /// <a href="https://en.wikipedia.org/wiki/Bitwise_operation#AND">And</a>
        /// </summary>
        And,

        /// <summary>
        /// <a href="https://en.wikipedia.org/wiki/Bitwise_operation#OR">Or</a>
        /// </summary>
        Or,

        /// <summary>
        /// <a href="https://en.wikipedia.org/wiki/Bitwise_operation#XOR">Xor</a>
        /// </summary>
        Xor,

        /// <summary>
        /// <a href="https://en.wikipedia.org/wiki/Bitwise_operation#NOT">Not</a>
        /// </summary>
        Not,

        /// <summary>
        /// DIFF operation: members of X that are not members of any of Y1, Y2, ...
        /// Equivalent to X ∧ ¬(Y1 ∨ Y2 ∨ ...)
        /// </summary>
        Diff,

        /// <summary>
        /// DIFF1 operation: members of one or more of Y1, Y2, ... that are not members of X
        /// Equivalent to ¬X ∧ (Y1 ∨ Y2 ∨ ...)
        /// </summary>
        Diff1,

        /// <summary>
        /// ANDOR operation: members of X that are also members of one or more of Y1, Y2, ...
        /// Equivalent to X ∧ (Y1 ∨ Y2 ∨ ...)
        /// </summary>
        AndOr,

        /// <summary>
        /// ONE operation: members of exactly one of X1, X2, ...
        /// For two bitmaps this is equivalent to XOR. For more than two bitmaps,
        /// this returns bits that are set in exactly one of the input bitmaps.
        /// </summary>
        One,
    }
}
