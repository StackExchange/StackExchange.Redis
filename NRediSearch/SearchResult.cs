// .NET port of https://github.com/RedisLabs/JRediSearch/

using StackExchange.Redis;
using System.Collections.Generic;

namespace NRediSearch
{
    /// <summary>
    /// SearchResult encapsulates the returned result from a search query.
    /// It contains publically accessible fields for the total number of results, and an array of <see cref="Document"/>
    /// objects conatining the actual returned documents.
    /// </summary>
    public class SearchResult
    {
        public long TotalResults { get; }
        public List<Document> Documents { get; }


        internal SearchResult(RedisResult[] resp, bool hasContent, bool hasScores, bool hasPayloads)
        {
            // Calculate the step distance to walk over the results.
            // The order of results is id, score (if withScore), payLoad (if hasPayloads), fields
            int step = 1;
            int scoreOffset = 0;
            int contentOffset = 1;
            int payloadOffset = 0;
            if (hasScores)
            {
                step += 1;
                scoreOffset = 1;
                contentOffset += 1;
            }
            if (hasContent)
            {
                step += 1;
                if (hasPayloads)
                {
                    payloadOffset = scoreOffset + 1;
                    step += 1;
                    contentOffset += 1;
                }
            }

            // the first element is always the number of results
            TotalResults = (long)resp[0];
            var docs = new List<Document>((resp.Length - 1) / step);
            Documents = docs;
            for (int i = 1; i < resp.Length; i += step)
            {

                var id = (string)resp[i];
                double score = 1.0;
                byte[] payload = null;
                RedisValue[] fields = null;
                if (hasScores)
                {
                    score = (double)resp[i + scoreOffset];
                }
                if (hasPayloads)
                {
                    payload = (byte[])resp[i + payloadOffset];
                }

                if (hasContent)
                {
                    fields = (RedisValue[])resp[i + contentOffset];
                }

                docs.Add(Document.Load(id, score, payload, fields));
            }


        }
    }
}
