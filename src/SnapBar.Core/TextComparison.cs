using System.Globalization;
using System.Text;

namespace SnapBar.Core;

public sealed record ComparisonSpan(string Text, bool Changed);
public sealed record TextComparison(IReadOnlyList<ComparisonSpan> Original, IReadOnlyList<ComparisonSpan> Result)
{
    // Bound both allocation and work. Large edits fall back to one changed middle region.
    public static TextComparison Create(string original, string result)
    {
        string[] Elements(string text)
        {
            var items = new List<string>(); var iterator = StringInfo.GetTextElementEnumerator(text);
            while (iterator.MoveNext()) items.Add(iterator.GetTextElement());
            return items.ToArray();
        }
        var a = Elements(original); var b = Elements(result);
        var left = new List<ComparisonSpan>(); var right = new List<ComparisonSpan>();
        void Add(List<ComparisonSpan> spans, string value, bool changed)
        {
            if (value.Length > 0) spans.Add(new(value, changed));
        }
        IReadOnlyList<ComparisonSpan> Merge(List<ComparisonSpan> spans)
        {
            var merged = new List<ComparisonSpan>(); var text = new StringBuilder(); bool? changed = null;
            foreach (var span in spans)
            {
                if (changed != null && changed != span.Changed)
                { merged.Add(new(text.ToString(), changed.Value)); text.Clear(); }
                changed = span.Changed; text.Append(span.Text);
            }
            if (changed != null) merged.Add(new(text.ToString(), changed.Value));
            return merged;
        }
        var prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;
        var suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[^(suffix + 1)] == b[^(suffix + 1)]) suffix++;
        var commonStart = string.Concat(a.Take(prefix)); Add(left, commonStart, false); Add(right, commonStart, false);
        var n = a.Length - prefix - suffix; var m = b.Length - prefix - suffix;
        if ((long)n * m > 1_000_000 || n + m > 12000)
        {
            Add(left, string.Concat(a.Skip(prefix).Take(n)), true); Add(right, string.Concat(b.Skip(prefix).Take(m)), true);
        }
        else
        {
            var lengths = new int[n + 1, m + 1];
            for (var x = n - 1; x >= 0; x--)
                for (var y = m - 1; y >= 0; y--)
                    lengths[x, y] = a[prefix + x] == b[prefix + y] ? lengths[x + 1, y + 1] + 1 : Math.Max(lengths[x + 1, y], lengths[x, y + 1]);
            var i = 0; var j = 0;
            while (i < n || j < m)
            {
                if (i < n && j < m && a[prefix + i] == b[prefix + j])
                { Add(left, a[prefix + i++], false); Add(right, b[prefix + j++], false); }
                else if (i < n && (j == m || lengths[i + 1, j] >= lengths[i, j + 1])) Add(left, a[prefix + i++], true);
                else Add(right, b[prefix + j++], true);
            }
        }
        var commonEnd = string.Concat(a.Skip(a.Length - suffix)); Add(left, commonEnd, false); Add(right, commonEnd, false);
        return new(Merge(left), Merge(right));
    }
}
