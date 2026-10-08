using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TfPortable.IntegrationTests.Golden
{
    /// <summary>
    /// Normalization and data extraction of docs/golden-format.md §7–8, applied to the golden text and to tfp output alike.
    /// Message texts are never compared: the comparison works on exit code, stream emptiness and the data extracted here.
    /// </summary>
    public static class GoldenText
    {
        private static readonly Regex IsoDate = new Regex(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?([+-]\d{2}:\d{2}|Z)?", RegexOptions.Compiled);
        private static readonly Regex CzechLongDate = new Regex(@"(pondělí|úterý|středa|čtvrtek|pátek|sobota|neděle) \d{1,2}\. \S+ \d{4} \d{1,2}:\d{2}:\d{2}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex CzechShortDate = new Regex(@"\b\d{1,2}\. \d{1,2}\. \d{4}( \d{1,2}:\d{2}(:\d{2})?)?", RegexOptions.Compiled);
        private static readonly Regex ActivityId = new Regex(@"^\s*ActivityId:.*$", RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex ServerPath = new Regex(@"\$/[^\s;,:""']*[^\s;,:.""']", RegexOptions.Compiled);
        private static readonly Regex LocalPath = new Regex(@"(?:[A-Za-z]:\\|<(?:WORKSPACE|LOCAL)_[A-Z_]*ROOT>)[^\s:""']*[^\s:.""']", RegexOptions.Compiled);
        private static readonly Regex ChangesetNumber = new Regex(@"(?<=;C)\d+|(?<=\bC)\d+\b|(?<=Changeset:\s)\d+|(?<=Sada změn:\s)\d+|^\s*\d+(?=\s|\*|$)", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex DeletionId = new Regex(@"(?<=;X)\d+", RegexOptions.Compiled);
        private static readonly Regex TrailerCount = new Regex(@"(?<=\S:\s*)\d+(?=\s*(,|$))", RegexOptions.Compiled | RegexOptions.Multiline);

        /// <summary>Czech change words of the golden output → the English words tfp prints (§8.5).</summary>
        private static readonly (Regex Pattern, string English)[] ChangeWords =
        {
            (new Regex(@"přejmenování zdroje", RegexOptions.Compiled | RegexOptions.IgnoreCase), "source rename"),
            (new Regex(@"\bpřidat\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "add"),
            (new Regex(@"\bupravit\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "edit"),
            (new Regex(@"\bodstranit\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "delete"),
            (new Regex(@"\bpřejmenovat\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "rename"),
            (new Regex(@"\bvětev\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "branch"),
            (new Regex(@"\bsloučit\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "merge"),
            (new Regex(@"\bobnovit\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "undelete"),
            (new Regex(@"\bvrátit zpět\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "rollback"),
            (new Regex(@"\buzamknout\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "lock"),
        };

        private static readonly Regex ChangeType = new Regex(@"\b(add|edit|delete|rename|branch|merge|undelete|rollback|lock|encoding|property|source rename)\b", RegexOptions.Compiled);

        /// <summary>CRLF → LF, trailing whitespace removed, trailing blank lines removed, dates and activity ids neutralized (§7).</summary>
        public static string Normalize(string text, IReadOnlyDictionary<string, string>? placeholders = null)
        {
            string t = text.Replace("\r\n", "\n").Replace('\r', '\n');
            if (placeholders != null)
            {
                foreach (var pair in placeholders.Where(p => !string.IsNullOrEmpty(p.Key)).OrderByDescending(p => p.Key.Length))
                {
                    t = Regex.Replace(t, Regex.Escape(pair.Key), pair.Value, RegexOptions.IgnoreCase);
                    // Paths also appear in forward-slash form (§5, <WORKSPACE_ROOT>).
                    string forward = pair.Key.Replace('\\', '/');
                    if (forward != pair.Key)
                    {
                        t = Regex.Replace(t, Regex.Escape(forward), pair.Value, RegexOptions.IgnoreCase);
                    }
                }
            }

            t = ActivityId.Replace(t, string.Empty);
            t = IsoDate.Replace(t, "<DATE>");
            t = CzechLongDate.Replace(t, "<DATE>");
            t = CzechShortDate.Replace(t, "<DATE>");

            var lines = t.Split('\n').Select(l => l.TrimEnd()).ToList();
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            return string.Join("\n", lines);
        }

        /// <summary>Server paths, local paths and folder headers, in order (§8.3). Version and deletion suffixes are stripped.</summary>
        public static IReadOnlyList<string> Items(string normalized)
        {
            var items = new List<(int Index, string Value)>();
            foreach (Match m in ServerPath.Matches(normalized))
            {
                items.Add((m.Index, m.Value));
            }

            foreach (Match m in LocalPath.Matches(normalized))
            {
                items.Add((m.Index, m.Value.Replace('/', '\\')));
            }

            return items.OrderBy(i => i.Index).Select(i => i.Value).ToList();
        }

        /// <summary>Changeset numbers in order of appearance (§8.4): `;C75`, `C75`, `Changeset: 75`, table cells, plus `*` partial markers kept as `75*`.</summary>
        public static IReadOnlyList<string> ChangesetNumbers(string normalized)
        {
            var result = new List<string>();
            foreach (Match m in ChangesetNumber.Matches(normalized))
            {
                string value = m.Value.Trim();
                bool partial = m.Index + m.Length < normalized.Length && normalized[m.Index + m.Length] == '*';
                result.Add(partial ? value + "*" : value);
            }

            return result;
        }

        public static IReadOnlyList<string> DeletionIds(string normalized) => DeletionId.Matches(normalized).Cast<Match>().Select(m => m.Value).ToList();

        /// <summary>Change types in order (§8.5), Czech words mapped to English first.</summary>
        public static IReadOnlyList<string> ChangeTypes(string normalized)
        {
            string english = normalized;
            foreach (var (pattern, word) in ChangeWords)
            {
                english = pattern.Replace(english, word);
            }

            return ChangeType.Matches(english).Cast<Match>().Select(m => m.Value).ToList();
        }

        /// <summary>Trailer numbers such as `Changes: 9`, `Items: 19`, `detected changes: 2` (§8.6), in order.</summary>
        public static IReadOnlyList<int> Counts(string normalized)
        {
            var result = new List<int>();
            foreach (string line in normalized.Split('\n'))
            {
                if (line.StartsWith("$/", StringComparison.Ordinal) || line.Contains("\\"))
                {
                    continue;
                }

                foreach (Match m in TrailerCount.Matches(line))
                {
                    result.Add(int.Parse(m.Value));
                }
            }

            return result;
        }
    }

    /// <summary>Which checks of §8 a comparison applies.</summary>
    [Flags]
    public enum GoldenChecks
    {
        ExitCode = 1,
        Streams = 2,
        Items = 4,
        Changesets = 8,
        ChangeTypes = 16,
        Counts = 32,

        /// <summary>Exit code and stream emptiness only (help text and error messages are never compared).</summary>
        Shape = ExitCode | Streams,

        All = ExitCode | Streams | Items | Changesets | ChangeTypes | Counts,
    }

    /// <summary>Compares a golden case with a tfp result and lists every difference (empty = pass).</summary>
    public static class GoldenComparison
    {
        public static IReadOnlyList<string> Compare(GoldenCase golden, TfpResult actual, int expectedExitCode, GoldenChecks checks, IReadOnlyDictionary<string, string>? placeholders = null)
        {
            var differences = new List<string>();
            if (actual.TimedOut)
            {
                differences.Add("tfp timed out");
                return differences;
            }

            string goldenOut = GoldenText.Normalize(golden.Stdout);
            string goldenErr = GoldenText.Normalize(golden.Stderr);
            string actualOut = GoldenText.Normalize(actual.Stdout, placeholders);
            string actualErr = GoldenText.Normalize(actual.Stderr, placeholders);

            if (checks.HasFlag(GoldenChecks.ExitCode) && actual.ExitCode != expectedExitCode)
            {
                differences.Add($"exit code: expected {expectedExitCode} (golden {golden.ExitCode}), got {actual.ExitCode}");
            }

            if (checks.HasFlag(GoldenChecks.Streams))
            {
                if ((goldenOut.Length == 0) != (actualOut.Length == 0))
                {
                    differences.Add($"stdout: golden {(goldenOut.Length == 0 ? "empty" : "not empty")}, tfp {(actualOut.Length == 0 ? "empty" : "not empty")}");
                }

                if ((goldenErr.Length == 0) != (actualErr.Length == 0))
                {
                    differences.Add($"stderr: golden {(goldenErr.Length == 0 ? "empty" : "not empty")}, tfp {(actualErr.Length == 0 ? "empty" : "not empty")}");
                }
            }

            string goldenAll = goldenOut + "\n" + goldenErr;
            string actualAll = actualOut + "\n" + actualErr;

            if (checks.HasFlag(GoldenChecks.Items))
            {
                CompareLists(differences, "items", GoldenText.Items(goldenAll), GoldenText.Items(actualAll));
            }

            if (checks.HasFlag(GoldenChecks.Changesets))
            {
                CompareLists(differences, "changesets", GoldenText.ChangesetNumbers(goldenAll), GoldenText.ChangesetNumbers(actualAll));
                CompareLists(differences, "deletion ids", GoldenText.DeletionIds(goldenAll), GoldenText.DeletionIds(actualAll));
            }

            if (checks.HasFlag(GoldenChecks.ChangeTypes))
            {
                CompareLists(differences, "change types", GoldenText.ChangeTypes(goldenAll), GoldenText.ChangeTypes(actualAll));
            }

            if (checks.HasFlag(GoldenChecks.Counts))
            {
                CompareLists(differences, "counts", GoldenText.Counts(goldenAll).Select(c => c.ToString()).ToList(), GoldenText.Counts(actualAll).Select(c => c.ToString()).ToList());
            }

            if (differences.Count > 0)
            {
                differences.Add($"--- tfp stdout ---\n{actualOut}\n--- tfp stderr ---\n{actualErr}\n--- golden stdout ---\n{goldenOut}\n--- golden stderr ---\n{goldenErr}");
            }

            return differences;
        }

        private static void CompareLists(List<string> differences, string what, IReadOnlyList<string> expected, IReadOnlyList<string> actual)
        {
            if (!expected.SequenceEqual(actual, StringComparer.OrdinalIgnoreCase))
            {
                differences.Add($"{what}: golden [{string.Join(", ", expected)}], tfp [{string.Join(", ", actual)}]");
            }
        }
    }
}
