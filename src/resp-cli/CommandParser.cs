namespace StackExchange.Redis;

public static class CommandParser
{
    public static string Parse(string value, out object[] args)
    {
        args = Array.Empty<object>();
        using var iter = Tokenize(value);
        if (iter.MoveNext())
        {
            var cmd = iter.Current;
            List<object>? list = null;
            while (iter.MoveNext())
            {
                (list ??= new()).Add(iter.Current);
            }
            if (list is not null) args = list.ToArray();
            return cmd;
        }
        return "";
    }

    private static IEnumerator<string> Tokenize(string value)
    {
        bool inQuote = false, prevWhitespace = true;
        int startIndex = -1;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            switch (c)
            {
                case '"' when inQuote: // end the current quoted string
                    yield return value.Substring(startIndex, i - startIndex);
                    startIndex = -1;
                    inQuote = false;
                    break;
                case '"' when startIndex < 0: // start a new quoted string
                    if (!prevWhitespace) UnableToParse();
                    inQuote = true;
                    startIndex = i + 1;
                    break;
                case '"':
                    UnableToParse();
                    break;
                default:
                    if (char.IsWhiteSpace(c))
                    {
                        if (startIndex >= 0 && !inQuote) // end non-quoted string
                        {
                            yield return value.Substring(startIndex, i - startIndex);
                            startIndex = -1;
                        }
                    }
                    else if (startIndex < 0) // start a new non-quoted token
                    {
                        if (!prevWhitespace) UnableToParse();

                        startIndex = i;
                    }
                    break;
            }
            prevWhitespace = !inQuote && char.IsWhiteSpace(c);
        }
        // anything left
        if (startIndex >= 0)
        {
            yield return value.Substring(startIndex, value.Length - startIndex);
        }

        static void UnableToParse() => throw new FormatException("Unable to parse input");
    }
}
