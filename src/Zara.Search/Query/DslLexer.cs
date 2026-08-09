using System.Text;

namespace Zara.Search.Query;

/// <summary>
/// Splits a raw query string into whitespace-separated tokens, keeping a
/// double-quoted phrase (e.g. <c>content:"kafka consumer"</c>) as one token
/// even though it contains a space. Deliberately not a general tokenizer —
/// no escape sequences, no nested quotes; the DSL doesn't need them yet.
/// </summary>
internal static class DslLexer
{
    public static List<string> Tokenize(string query)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        foreach (char c in query)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                current.Append(c);
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}
