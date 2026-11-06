namespace Fratak_Shop.Core.Utilities;

/// <summary>Small string-distance helpers for "did you mean" suggestions.</summary>
public static class Fuzzy
{
    /// <summary>Damerau–Levenshtein (optimal string alignment) distance, stops early above <paramref name="max"/>.</summary>
    public static int Distance(string a, string b, int max = int.MaxValue)
    {
        if (a == b)
            return 0;
        if (Math.Abs(a.Length - b.Length) > max)
            return max + 1;
        if (a.Length == 0)
            return b.Length;
        if (b.Length == 0)
            return a.Length;

        var previous2 = new int[b.Length + 1];
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMin = current[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var value = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    value = Math.Min(value, previous2[j - 2] + 1);
                current[j] = value;
                rowMin = Math.Min(rowMin, value);
            }

            if (rowMin > max)
                return max + 1;

            (previous2, previous, current) = (previous, current, previous2);
        }

        return previous[b.Length];
    }

    /// <summary>Allowed typos for a word of this length (short words must be almost exact).</summary>
    public static int Tolerance(int length) => length switch
    {
        <= 3 => 0,
        <= 5 => 1,
        _ => 2,
    };
}
