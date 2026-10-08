using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TfPortable.Output
{
    /// <summary>Comparison options of `difference` (`/ignorespace`, `/ignoreeol`, `/ignorecase`).</summary>
    [Flags]
    internal enum DiffOptions
    {
        None = 0,
        IgnoreSpace = 1,
        IgnoreEol = 2,
        IgnoreCase = 4,
    }

    /// <summary>
    /// Line-based diff (Myers' O(ND) algorithm) rendered as a unified diff with three lines of context, in the shape
    /// tf.exe prints: `--- <left label>`, `+++ <right label>`, `@@ -a,b +c,d @@` hunks. Own implementation so that the
    /// output does not depend on the client library's diff classes.
    /// </summary>
    internal static class UnifiedDiff
    {
        private const int Context = 3;

        /// <summary>True when the two texts are equal under the options.</summary>
        public static bool AreEqual(string left, string right, DiffOptions options)
        {
            var a = Lines(left, options);
            var b = Lines(right, options);
            return a.Keys.SequenceEqual(b.Keys, StringComparer.Ordinal);
        }

        /// <summary>The unified diff lines (empty when the texts are equal).</summary>
        public static IReadOnlyList<string> Render(string left, string right, string leftLabel, string rightLabel, DiffOptions options = DiffOptions.None)
        {
            var a = Lines(left, options);
            var b = Lines(right, options);
            var edits = Myers(a.Keys, b.Keys);
            if (edits.All(e => e.Kind == EditKind.Equal))
            {
                return Array.Empty<string>();
            }

            var output = new List<string> { "--- " + leftLabel, "+++ " + rightLabel };
            foreach (var hunk in Hunks(edits))
            {
                int aStart = hunk.AStart;
                int aCount = hunk.Edits.Count(e => e.Kind != EditKind.Insert);
                int bStart = hunk.BStart;
                int bCount = hunk.Edits.Count(e => e.Kind != EditKind.Delete);
                output.Add($"@@ -{Range(aStart, aCount)} +{Range(bStart, bCount)} @@");
                foreach (var edit in hunk.Edits)
                {
                    switch (edit.Kind)
                    {
                        case EditKind.Equal: output.Add(" " + a.Original[edit.AIndex]); break;
                        case EditKind.Delete: output.Add("-" + a.Original[edit.AIndex]); break;
                        case EditKind.Insert: output.Add("+" + b.Original[edit.BIndex]); break;
                    }
                }
            }

            return output;
        }

        /// <summary>`start,count` of a unified hunk; a zero-length side is written as `start,0` with start = the line before.</summary>
        private static string Range(int start, int count) => count == 0 ? $"{start},0" : count == 1 ? $"{start + 1}" : $"{start + 1},{count}";

        private sealed class Split
        {
            public Split(string[] original, string[] keys)
            {
                Original = original;
                Keys = keys;
            }

            public string[] Original { get; }

            public string[] Keys { get; }
        }

        private static Split Lines(string text, DiffOptions options)
        {
            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Length == 0 ? Array.Empty<string>() : normalized.Split('\n');
            if (lines.Length > 0 && normalized.EndsWith("\n", StringComparison.Ordinal))
            {
                lines = lines.Take(lines.Length - 1).ToArray();
            }

            var keys = lines.Select(l =>
            {
                string k = l;
                if (options.HasFlag(DiffOptions.IgnoreSpace))
                {
                    k = new string(k.Where(c => !char.IsWhiteSpace(c)).ToArray());
                }
                else if (options.HasFlag(DiffOptions.IgnoreEol))
                {
                    k = k.TrimEnd('\r');
                }

                if (options.HasFlag(DiffOptions.IgnoreCase))
                {
                    k = k.ToUpperInvariant();
                }

                return k;
            }).ToArray();
            return new Split(lines, keys);
        }

        private enum EditKind
        {
            Equal,
            Delete,
            Insert,
        }

        private struct Edit
        {
            public EditKind Kind;
            public int AIndex;
            public int BIndex;
        }

        private sealed class Hunk
        {
            public int AStart;
            public int BStart;
            public List<Edit> Edits = new List<Edit>();
        }

        /// <summary>
        /// Edit script as equal/delete/insert steps. The common prefix and suffix are matched first, as tf.exe does: a block
        /// inserted in front of a repeated closing line (`}`) is reported before that line, not after it.
        /// </summary>
        private static List<Edit> Myers(string[] a, string[] b)
        {
            int prefix = 0;
            while (prefix < a.Length && prefix < b.Length && string.Equals(a[prefix], b[prefix], StringComparison.Ordinal))
            {
                prefix++;
            }

            int suffix = 0;
            while (suffix < a.Length - prefix && suffix < b.Length - prefix && string.Equals(a[a.Length - 1 - suffix], b[b.Length - 1 - suffix], StringComparison.Ordinal))
            {
                suffix++;
            }

            var edits = new List<Edit>();
            for (int i = 0; i < prefix; i++)
            {
                edits.Add(new Edit { Kind = EditKind.Equal, AIndex = i, BIndex = i });
            }

            foreach (var edit in MyersCore(a.Skip(prefix).Take(a.Length - prefix - suffix).ToArray(), b.Skip(prefix).Take(b.Length - prefix - suffix).ToArray()))
            {
                edits.Add(new Edit { Kind = edit.Kind, AIndex = edit.AIndex + prefix, BIndex = edit.BIndex + prefix });
            }

            for (int i = 0; i < suffix; i++)
            {
                edits.Add(new Edit { Kind = EditKind.Equal, AIndex = a.Length - suffix + i, BIndex = b.Length - suffix + i });
            }

            return edits;
        }

        /// <summary>Myers' greedy algorithm with a trace; returns the edit script as equal/delete/insert steps.</summary>
        private static List<Edit> MyersCore(string[] a, string[] b)
        {
            int n = a.Length;
            int m = b.Length;
            int max = n + m;
            var v = new int[2 * max + 2];
            var trace = new List<int[]>();
            int offset = max;
            bool found = false;

            for (int d = 0; d <= max && !found; d++)
            {
                trace.Add((int[])v.Clone());
                for (int k = -d; k <= d; k += 2)
                {
                    int x;
                    if (k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]))
                    {
                        x = v[offset + k + 1];
                    }
                    else
                    {
                        x = v[offset + k - 1] + 1;
                    }

                    int y = x - k;
                    while (x < n && y < m && string.Equals(a[x], b[y], StringComparison.Ordinal))
                    {
                        x++;
                        y++;
                    }

                    v[offset + k] = x;
                    if (x >= n && y >= m)
                    {
                        found = true;
                        break;
                    }
                }
            }

            // Backtrack.
            var edits = new List<Edit>();
            int px = n, py = m;
            for (int d = trace.Count - 1; d >= 0; d--)
            {
                var vd = trace[d];
                int k = px - py;
                int prevK;
                if (k == -d || (k != d && vd[offset + k - 1] < vd[offset + k + 1]))
                {
                    prevK = k + 1;
                }
                else
                {
                    prevK = k - 1;
                }

                int prevX = vd[offset + prevK];
                int prevY = prevX - prevK;
                while (px > prevX && py > prevY)
                {
                    px--;
                    py--;
                    edits.Add(new Edit { Kind = EditKind.Equal, AIndex = px, BIndex = py });
                }

                if (d > 0)
                {
                    if (px == prevX)
                    {
                        py--;
                        edits.Add(new Edit { Kind = EditKind.Insert, AIndex = px, BIndex = py });
                    }
                    else
                    {
                        px--;
                        edits.Add(new Edit { Kind = EditKind.Delete, AIndex = px, BIndex = py });
                    }
                }
            }

            edits.Reverse();
            return edits;
        }

        private static IEnumerable<Hunk> Hunks(List<Edit> edits)
        {
            Hunk? current = null;
            int equalRun = 0;
            for (int i = 0; i < edits.Count; i++)
            {
                var edit = edits[i];
                if (edit.Kind == EditKind.Equal)
                {
                    if (current != null)
                    {
                        equalRun++;
                        current.Edits.Add(edit);
                        if (equalRun > 2 * Context)
                        {
                            // Close the hunk: keep the first Context equal lines, drop the rest.
                            current.Edits.RemoveRange(current.Edits.Count - (equalRun - Context), equalRun - Context);
                            yield return current;
                            current = null;
                            equalRun = 0;
                        }
                    }

                    continue;
                }

                if (current == null)
                {
                    current = new Hunk();
                    int from = Math.Max(0, i - Context);
                    for (int j = from; j < i; j++)
                    {
                        current.Edits.Add(edits[j]);
                    }

                    current.AStart = current.Edits.Count > 0 ? current.Edits[0].AIndex : edit.AIndex;
                    current.BStart = current.Edits.Count > 0 ? current.Edits[0].BIndex : edit.BIndex;
                }

                equalRun = 0;
                current.Edits.Add(edit);
            }

            if (current != null)
            {
                if (equalRun > Context)
                {
                    current.Edits.RemoveRange(current.Edits.Count - (equalRun - Context), equalRun - Context);
                }

                yield return current;
            }
        }
    }

    /// <summary>Decoding of item content by the server's encoding name (SPEC.md: `view` re-encodes to UTF-8).</summary>
    internal static class TextContent
    {
        /// <summary>Decodes with the item's encoding; a BOM wins over the name. Null for binary items.</summary>
        public static string? Decode(byte[] bytes, string? encodingName)
        {
            if (encodingName == "binary")
            {
                return null;
            }

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }

            Encoding encoding;
            try
            {
                encoding = string.IsNullOrEmpty(encodingName) ? new UTF8Encoding(false) : Encoding.GetEncoding(encodingName);
            }
            catch (ArgumentException)
            {
                encoding = new UTF8Encoding(false);
            }

            return encoding.GetString(bytes);
        }

        /// <summary>A local file read as text (BOM-aware, UTF-8 by default); null when it looks binary.</summary>
        public static string? ReadLocal(string path)
        {
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            if (LooksBinary(bytes))
            {
                return null;
            }

            return Decode(bytes, null);
        }

        public static bool LooksBinary(byte[] bytes)
        {
            int limit = Math.Min(bytes.Length, 8000);
            for (int i = 0; i < limit; i++)
            {
                if (bytes[i] == 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
