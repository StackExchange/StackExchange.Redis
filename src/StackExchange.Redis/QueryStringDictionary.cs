using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace StackExchange.Redis
{
    internal class QueryStringDictionary : Dictionary<string, List<string>>
    {
        public QueryStringDictionary() : this(null) { }

        public QueryStringDictionary(string qs) : base(StringComparer.OrdinalIgnoreCase)
        {
            var trimmedQs = qs?.Trim().TrimStart('?');
            if (!string.IsNullOrEmpty(trimmedQs))
            {
                Init(trimmedQs);
            }
        }

        private void Init(string qs)
        {
            foreach (var pair in qs.Split(StringSplits.Ampersand).OrderBy(x => x))
            {
                var splited = pair.Split(StringSplits.Equal);
                if (splited.Length == 0)
                {
                    continue;
                }

                string key = string.Empty;
                string value = string.Empty;

                if (splited.Length > 0)
                {
                    key = WebUtility.UrlDecode(splited[0]);
                }
                if (splited.Length > 1)
                {
                    value = WebUtility.UrlDecode(splited[1]);
                }

                if (!ContainsKey(key))
                {
                    this[key] = new List<string>();
                }

                this[key].Add(value);
            }
        }

        public void AddIfNotNullOrEmpty(string key, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                Add(key, value);
            }
        }

        public void Add(string key, string value) => Add(key, new List<string> { value });

        public override string ToString() => ToString(false);

        public string ToString(bool encode = false)
        {
            var builder = new StringBuilder();

            foreach (var key in Keys.OrderBy(x => x))
            {
                var values = this[key];
                if (values != null && values.Any())
                {
                    foreach (var value in values.OrderBy(x => x))
                    {
                        var finalKey = encode ? WebUtility.UrlEncode(key) : key;
                        if (!string.IsNullOrEmpty(value))
                        {
                            var finalValue = encode ? WebUtility.UrlEncode(value) : value;
                            builder.Append($"{finalKey}={finalValue}&");
                        }
                        else
                        {
                            builder.Append($"{finalKey}&");
                        }
                    }
                }
            }

            if (builder.Length > 0)
            {
                return $"?{builder}".TrimEnd('&');
            }

            return string.Empty;
        }
    }
}
