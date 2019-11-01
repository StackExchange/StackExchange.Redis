// .NET port of https://github.com/RedisLabs/JRediSearch/

using System.Collections.Generic;

namespace NRediSearch.Aggregation
{
    public class AggregationBuilder
    {
        private readonly List<string> args = new List<string>();

        public AggregationBuilder() : this("*")
        {
        }

        public AggregationBuilder(string query) => args.Add(query);

        private static void AddCommandLength(List<string> list, string command, int length)
        {
            list.Add(command);
            list.Add(length.ToString());
        }

        private static void AddCommandArguments(List<string> destination, string command, List<string> source)
        {
            AddCommandLength(destination, command, source.Count);
            destination.AddRange(source);
        }
    }
}
