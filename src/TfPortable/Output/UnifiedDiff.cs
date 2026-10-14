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

        /// <summary>
        /// Pairs of line indexes (left, right) that the diff keeps as equal, in order. Lines of the right text that are in no
        /// pair are new; lines of the left text in no pair were removed. Used by `annotate` to carry line authorship forward.
        /// </summary>
        public static IReadOnlyList<KeyValuePair<int, int>> MatchingLines(string left, string right, DiffOptions options = DiffOptions.None)
        {
            var a = Lines(left, options);
            var b = Lines(right, options);
            return Myers(a.Keys, b.Keys).Where(e => e.Kind == EditKind.Equal).Select(e => new KeyValuePair<int, int>(e.AIndex, e.BIndex)).ToList();
        }

        /// <summary>The lines of a text as `difference` sees them (CR stripped), for `annotate`.</summary>
        public static string[] SplitLines(string text, DiffOptions options = DiffOptions.None) => Lines(text, options).Original;

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
                        case EditKind.Equal:
                            output.Add(" " + a.Original[edit.AIndex]);
                            if (a.MissingFinalNewline && edit.AIndex == a.Original.Length - 1)
                            {
                                output.Add(NoNewline);
                            }

                            break;
                        case EditKind.Delete:
                            output.Add("-" + a.Original[edit.AIndex]);
                            if (a.MissingFinalNewline && edit.AIndex == a.Original.Length - 1)
                            {
                                output.Add(NoNewline);
                            }

                            break;
                        case EditKind.Insert:
                            output.Add("+" + b.Original[edit.BIndex]);
                            if (b.MissingFinalNewline && edit.BIndex == b.Original.Length - 1)
                            {
                                output.Add(NoNewline);
                            }

                            break;
                    }
                }
            }

            return output;
        }

        /// <summary>`start,count` of a unified hunk; a zero-length side is written as `start,0` with start = the line before.</summary>
        private static string Range(int start, int count) => count == 0 ? $"{start},0" : count == 1 ? $"{start + 1}" : $"{start + 1},{count}";

        /// <summary>The marker of a last line without a line ending, as GNU diff prints it.</summary>
        private const string NoNewline = "\\ No newline at end of file";

        private sealed class Split
        {
            public Split(string[] original, string[] keys, bool missingFinalNewline)
            {
                Original = original;
                Keys = keys;
                MissingFinalNewline = missingFinalNewline;
            }

            /// <summary>Lines as displayed (without the line ending).</summary>
            public string[] Original { get; }

            /// <summary>Lines as compared: the `\r` of a CRLF ending stays unless /ignoreeol (/ignorespace keeps it).</summary>
            public string[] Keys { get; }

            public bool MissingFinalNewline { get; }
        }

        private static Split Lines(string text, DiffOptions options)
        {
            string[] lines = text.Length == 0 ? Array.Empty<string>() : text.Split('\n');
            bool missingFinalNewline = text.Length > 0 && !text.EndsWith("\n", StringComparison.Ordinal);
            if (lines.Length > 0 && !missingFinalNewline)
            {
                lines = lines.Take(lines.Length - 1).ToArray();
            }

            // tf.exe: /ignorespace ignores spaces and tabs but not the line ending, /ignoreeol ignores CRLF against LF but not a
            // missing final newline (golden difference/eol-change-ignorespace, no-final-newline-ignoreeol).
            bool ignoreEol = options.HasFlag(DiffOptions.IgnoreEol);
            var keys = lines.Select((l, i) =>
            {
                string k = l;
                if (ignoreEol)
                {
                    k = k.TrimEnd('\r');
                }

                if (options.HasFlag(DiffOptions.IgnoreSpace))
                {
                    k = new string(k.Where(c => c == '\r' || !char.IsWhiteSpace(c)).ToArray());
                }

                if (options.HasFlag(DiffOptions.IgnoreCase))
                {
                    k = k.ToUpperInvariant();
                }

                // A missing final newline is a difference of the last line, even with /ignoreeol.
                if (missingFinalNewline && i == lines.Length - 1)
                {
                    k += "\0";
                }

                return k;
            }).ToArray();
            var shown = lines.Select(l => l.EndsWith("\r", StringComparison.Ordinal) ? l.Substring(0, l.Length - 1) : l).ToArray();
            return new Split(shown, keys, missingFinalNewline);
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

        /// <summary>
        /// Linear-space Myers (middle snake, divide and conquer): O(N+M) memory, O((N+M)·D) time. Within a block of
        /// changes the deletes are listed before the inserts, as diff tools print them.
        /// </summary>
        private static List<Edit> MyersCore(string[] a, string[] b)
        {
            var edits = new List<Edit>();
            Recurse(a, b, 0, a.Length, 0, b.Length, edits);
            return Grouped(edits);
        }

        private static void Recurse(string[] a, string[] b, int a0, int a1, int b0, int b1, List<Edit> edits)
        {
            while (a0 < a1 && b0 < b1 && string.Equals(a[a0], b[b0], StringComparison.Ordinal))
            {
                edits.Add(new Edit { Kind = EditKind.Equal, AIndex = a0, BIndex = b0 });
                a0++;
                b0++;
            }

            int suffix = 0;
            while (a0 < a1 - suffix && b0 < b1 - suffix && string.Equals(a[a1 - 1 - suffix], b[b1 - 1 - suffix], StringComparison.Ordinal))
            {
                suffix++;
            }

            int a1s = a1 - suffix;
            int b1s = b1 - suffix;
            int n = a1s - a0;
            int m = b1s - b0;
            if (n == 0)
            {
                for (int j = b0; j < b1s; j++)
                {
                    edits.Add(new Edit { Kind = EditKind.Insert, AIndex = a0, BIndex = j });
                }
            }
            else if (m == 0)
            {
                for (int i = a0; i < a1s; i++)
                {
                    edits.Add(new Edit { Kind = EditKind.Delete, AIndex = i, BIndex = b0 });
                }
            }
            else
            {
                int d = MiddleSnake(a, a0, n, b, b0, m, out int x, out int y, out int u, out int v);
                if (d == 1)
                {
                    // One edit at the first position (the common prefix is gone); the rest is equal.
                    if (n > m)
                    {
                        edits.Add(new Edit { Kind = EditKind.Delete, AIndex = a0, BIndex = b0 });
                        for (int i = 0; i < m; i++)
                        {
                            edits.Add(new Edit { Kind = EditKind.Equal, AIndex = a0 + 1 + i, BIndex = b0 + i });
                        }
                    }
                    else
                    {
                        edits.Add(new Edit { Kind = EditKind.Insert, AIndex = a0, BIndex = b0 });
                        for (int i = 0; i < n; i++)
                        {
                            edits.Add(new Edit { Kind = EditKind.Equal, AIndex = a0 + i, BIndex = b0 + 1 + i });
                        }
                    }
                }
                else
                {
                    Recurse(a, b, a0, a0 + x, b0, b0 + y, edits);
                    for (int i = 0; i < u - x; i++)
                    {
                        edits.Add(new Edit { Kind = EditKind.Equal, AIndex = a0 + x + i, BIndex = b0 + y + i });
                    }

                    Recurse(a, b, a0 + u, a1s, b0 + v, b1s, edits);
                }
            }

            for (int i = 0; i < suffix; i++)
            {
                edits.Add(new Edit { Kind = EditKind.Equal, AIndex = a1s + i, BIndex = b1s + i });
            }
        }

        /// <summary>
        /// The middle snake of a[a0..a0+n) against b[b0..b0+m): the forward and the reverse search meet on one diagonal.
        /// Returns the edit distance D; (x, y)–(u, v) is the snake in local coordinates.
        /// </summary>
        private static int MiddleSnake(string[] a, int a0, int n, string[] b, int b0, int m, out int x, out int y, out int u, out int v)
        {
            int max = (n + m + 1) / 2 + 1;
            int delta = n - m;
            bool odd = (delta & 1) != 0;
            var vf = new int[2 * max + 3];
            var vb = new int[2 * max + 3];
            int off = max + 1;
            for (int d = 0; d <= max; d++)
            {
                for (int k = -d; k <= d; k += 2)
                {
                    int xx = k == -d || (k != d && vf[off + k - 1] < vf[off + k + 1]) ? vf[off + k + 1] : vf[off + k - 1] + 1;
                    int yy = xx - k;
                    int sx = xx;
                    int sy = yy;
                    while (xx < n && yy < m && string.Equals(a[a0 + xx], b[b0 + yy], StringComparison.Ordinal))
                    {
                        xx++;
                        yy++;
                    }

                    vf[off + k] = xx;
                    int rk = delta - k;
                    if (odd && rk >= -(d - 1) && rk <= d - 1 && xx + vb[off + rk] >= n)
                    {
                        x = sx;
                        y = sy;
                        u = xx;
                        v = yy;
                        return 2 * d - 1;
                    }
                }

                for (int k = -d; k <= d; k += 2)
                {
                    // The reverse search runs on the mirrored texts: x' = n - x, y' = m - y, k' = delta - k.
                    int xx = k == -d || (k != d && vb[off + k - 1] < vb[off + k + 1]) ? vb[off + k + 1] : vb[off + k - 1] + 1;
                    int yy = xx - k;
                    int sx = xx;
                    int sy = yy;
                    while (xx < n && yy < m && string.Equals(a[a0 + n - 1 - xx], b[b0 + m - 1 - yy], StringComparison.Ordinal))
                    {
                        xx++;
                        yy++;
                    }

                    vb[off + k] = xx;
                    int fk = delta - k;
                    if (!odd && fk >= -d && fk <= d && xx + vf[off + fk] >= n)
                    {
                        x = n - xx;
                        y = m - yy;
                        u = n - sx;
                        v = m - sy;
                        return 2 * d;
                    }
                }
            }

            throw new InvalidOperationException("The middle snake was not found.");
        }

        /// <summary>Within every run of changes: the deletes first, then the inserts, with the positions a hunk header needs.</summary>
        private static List<Edit> Grouped(List<Edit> edits)
        {
            var result = new List<Edit>(edits.Count);
            int i = 0;
            while (i < edits.Count)
            {
                if (edits[i].Kind == EditKind.Equal)
                {
                    result.Add(edits[i]);
                    i++;
                    continue;
                }

                int start = i;
                while (i < edits.Count && edits[i].Kind != EditKind.Equal)
                {
                    i++;
                }

                int aStart = int.MaxValue;
                int bStart = int.MaxValue;
                for (int j = start; j < i; j++)
                {
                    aStart = Math.Min(aStart, edits[j].AIndex);
                    bStart = Math.Min(bStart, edits[j].BIndex);
                }

                int deletes = 0;
                for (int j = start; j < i; j++)
                {
                    if (edits[j].Kind == EditKind.Delete)
                    {
                        result.Add(new Edit { Kind = EditKind.Delete, AIndex = edits[j].AIndex, BIndex = bStart });
                        deletes++;
                    }
                }

                for (int j = start; j < i; j++)
                {
                    if (edits[j].Kind == EditKind.Insert)
                    {
                        result.Add(new Edit { Kind = EditKind.Insert, AIndex = aStart + deletes, BIndex = edits[j].BIndex });
                    }
                }
            }

            return result;
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

    /// <summary>
    /// Decoding of item content (SPEC.md "Výstup a kódování"): a BOM wins; without one the bytes are UTF-8 when they are
    /// valid UTF-8, and the item's server encoding otherwise. tf.exe records the ANSI code page (windows-1250 on a Czech
    /// machine) for every BOM-less text file it adds, so the server encoding alone would turn UTF-8 sources into mojibake.
    /// </summary>
    internal static class TextContent
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        /// <summary>
        /// Decodes content. <paramref name="encodingName"/> is the item's server encoding, <paramref name="requestedEncoding"/>
        /// an explicit `/encoding:` that wins over the UTF-8 default (a BOM still wins over both). Null for binary content.
        /// </summary>
        public static string? Decode(byte[] bytes, string? encodingName, string? requestedEncoding = null)
        {
            if (requestedEncoding == "binary" || (requestedEncoding == null && encodingName == "binary"))
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

            // NUL bytes are valid UTF-8, so a BOM-less UTF-16 or UTF-32 item must not pass the UTF-8 attempt.
            if (requestedEncoding == null && !LooksBinary(bytes))
            {
                string? utf8 = AsUtf8(bytes);
                if (utf8 != null)
                {
                    return utf8;
                }
            }

            Encoding encoding;
            string? name = requestedEncoding ?? encodingName;
            try
            {
                encoding = string.IsNullOrEmpty(name) ? new UTF8Encoding(false) : Encoding.GetEncoding(name);
            }
            catch (ArgumentException)
            {
                encoding = new UTF8Encoding(false);
            }

            return encoding.GetString(bytes);
        }

        /// <summary>The bytes as a string when they are valid UTF-8 (BOM-less), else null.</summary>
        public static string? AsUtf8(byte[] bytes)
        {
            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }

        /// <summary>
        /// True for a text file without a BOM whose bytes are valid UTF-8: the file `add` records as `utf-8` instead of the
        /// client library's default, the ANSI code page. Binary files, files with a BOM and unreadable files are false.
        /// </summary>
        public static bool IsBomlessUtf8Text(string path)
        {
            try
            {
                if (!System.IO.File.Exists(path))
                {
                    return false;
                }

                // Streamed: the file is never loaded whole. The first buffer decides binary and BOM; the strict decoder keeps
                // the state of a sequence split across buffers, and the final flush rejects a truncated sequence at the end.
                using var stream = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
                byte[] buffer = new byte[8192];
                var decoder = StrictUtf8.GetDecoder();
                char[] chars = new char[buffer.Length + 1];
                bool first = true;
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (first)
                    {
                        first = false;
                        byte[] head = read == buffer.Length ? buffer : buffer.Take(read).ToArray();
                        if (LooksBinary(head) || HasBom(head))
                        {
                            return false;
                        }
                    }

                    decoder.GetChars(buffer, 0, read, chars, 0, false);
                }

                if (first)
                {
                    return false;
                }

                decoder.GetChars(buffer, 0, 0, chars, 0, true);
                return true;
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is DecoderFallbackException)
            {
                return false;
            }
        }

        private static bool HasBom(byte[] bytes) =>
            (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            || (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)));

        /// <summary>A local file read as text (BOM-aware, UTF-8 when valid, else the item's encoding); null when it looks binary.</summary>
        public static string? ReadLocal(string path, string? encodingName = null, string? requestedEncoding = null)
        {
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            if (LooksBinary(bytes))
            {
                return null;
            }

            return Decode(bytes, encodingName, requestedEncoding);
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
