using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Helper for Array.ConvertAll() as it's missing on .Net Core.
    /// </summary>
    public static class ConvertHelper
    {
        /// <summary>
        /// Converts array of one type to an array of another type.
        /// </summary>
        /// <typeparam name="TInput">Input type</typeparam>
        /// <typeparam name="TOutput">Output type</typeparam>
        /// <param name="source">source</param>
        /// <param name="selector">selector</param>
        /// <returns></returns>
        public static TOutput[] ConvertAll<TInput, TOutput>(TInput[] source, Func<TInput, TOutput> selector)
        {
#if CORE_CLR
            TOutput[] arr = new TOutput[source.Length];
            for(int i = 0 ; i < arr.Length ; i++)
                arr[i] = selector(source[i]);
            return arr;
#else
            return Array.ConvertAll(source, item => selector(item));
#endif
        }
    }
}
