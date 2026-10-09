using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TfPortable.Errors;

namespace TfPortable.Output
{
    /// <summary>
    /// Text and JSON output (SPEC.md "Výstup a kódování", "Chybové hlášky"; docs/json-schema.md). In text mode data go
    /// to stdout and warnings and errors to stderr as they happen; in JSON mode everything is collected into one
    /// envelope written by <see cref="Success"/> or <see cref="Error"/>. <see cref="Collection"/> and <see cref="Workspace"/>
    /// are filled by the context resolver as soon as they are known, so they appear even in error envelopes.
    /// </summary>
    internal sealed class OutputWriter
    {
        private readonly TextWriter stdout;
        private readonly TextWriter stderr;
        private readonly List<JObject> warnings = new List<JObject>();
        private readonly List<JObject> itemErrors = new List<JObject>();

        public OutputWriter(bool json, string command, TextWriter stdout, TextWriter stderr)
        {
            Json = json;
            Command = command;
            this.stdout = stdout;
            this.stderr = stderr;
        }

        public bool Json { get; }

        /// <summary>Canonical command name; set when the parser resolves it.</summary>
        public string Command { get; set; }

        public string? Collection { get; set; }

        public string? Workspace { get; set; }

        public JObject? Truncated { get; set; }

        /// <summary>Number of per-item errors reported so far (they make the exit code 1, SPEC.md "Pravidla").</summary>
        public int ItemErrorCount => itemErrors.Count;

        public void Line(string text = "")
        {
            if (!Json)
            {
                stdout.WriteLine(text);
            }
        }

        public void Lines(IEnumerable<string> lines)
        {
            foreach (string line in lines)
            {
                Line(line);
            }
        }

        public void Warning(string message, string? code = null, string? hint = null, string? item = null)
        {
            warnings.Add(Message(code, null, message, hint, item, null));
            if (!Json)
            {
                stderr.WriteLine(code == null ? $"WARNING: {message}" : $"WARNING {code}: {message}");
                if (!string.IsNullOrEmpty(hint))
                {
                    stderr.WriteLine($"  Next step: {hint}");
                }
            }
        }

        /// <summary>
        /// An error of one item that does not stop the command (SPEC.md "Pravidla"): printed to stderr at once in text
        /// mode, collected into the envelope in JSON mode. The command finishes with exit code 1 through <see cref="Success"/>.
        /// </summary>
        public void ItemError(TfpException error)
        {
            itemErrors.Add(Message(error.Code, error.TfCode, error.Message, error.Hint, error.Item, error.ActivityId, error.ServerMessage));
            if (!Json)
            {
                WriteErrorText(error);
            }
        }

        /// <summary>Writes the JSON envelope for a finished command (no-op in text mode). Item errors force exit code 1.</summary>
        public void Success(JObject data, int exitCode = ExitCodes.Success)
        {
            if (exitCode == ExitCodes.Success && itemErrors.Count > 0)
            {
                exitCode = ExitCodes.PartialSuccess;
            }

            if (Json)
            {
                stdout.WriteLine(Envelope(exitCode, data, new JArray(itemErrors.Cast<object>().ToArray())).ToString(Formatting.Indented));
            }
        }

        /// <summary>Writes the fatal error in text form to stderr, or as the JSON envelope to stdout.</summary>
        public void Error(TfpException error)
        {
            if (Json)
            {
                var errors = new JArray(itemErrors.Cast<object>().ToArray())
                {
                    Message(error.Code, error.TfCode, error.Message, error.Hint, error.Item, error.ActivityId, error.ServerMessage),
                };
                stdout.WriteLine(Envelope(error.ExitCode, new JObject(), errors).ToString(Formatting.Indented));
                return;
            }

            WriteErrorText(error);
        }

        /// <summary>The envelope that <see cref="Success"/> would write, for tests and for commands that need to inspect it.</summary>
        public JObject Envelope(int exitCode, JObject data, JArray errors)
        {
            return new JObject
            {
                ["ok"] = exitCode == ExitCodes.Success,
                ["command"] = Command,
                ["exitCode"] = exitCode,
                ["collection"] = Collection,
                ["workspace"] = Workspace,
                ["data"] = data,
                ["truncated"] = Truncated,
                ["warnings"] = new JArray(warnings.Cast<object>().ToArray()),
                ["errors"] = errors,
            };
        }

        public static string IsoDate(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:sszzz");

        /// <summary>ISO 8601 with the local offset; null for a default/empty date (e.g. a detected change that has no creation date).</summary>
        public static string? IsoDate(DateTime value)
        {
            if (value == default || value == DateTime.MinValue || value == DateTime.MaxValue)
            {
                return null;
            }

            try
            {
                return IsoDate(new DateTimeOffset(value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Local) : value.ToLocalTime()));
            }
            catch (ArgumentOutOfRangeException)
            {
                // Out of the DateTimeOffset range in this time zone (dates near MinValue in UTC+ zones).
                return null;
            }
        }

        /// <summary>Text form of an error (SPEC.md "Textový formát (stderr)").</summary>
        public static IEnumerable<string> ErrorLines(TfpException error)
        {
            string code = error.TfCode == null ? error.Code : $"{error.Code} [{error.TfCode}]";
            yield return $"ERROR {code}: {error.Message}";
            if (!string.IsNullOrEmpty(error.ServerMessage) && !string.Equals(error.ServerMessage, error.Message, StringComparison.Ordinal))
            {
                yield return $"  Server message: {error.ServerMessage!.Trim()}";
            }

            if (!string.IsNullOrEmpty(error.Hint))
            {
                yield return $"  Next step: {error.Hint}";
            }

            if (!string.IsNullOrEmpty(error.ActivityId))
            {
                yield return $"  ActivityId: {error.ActivityId}";
            }
        }

        private void WriteErrorText(TfpException error)
        {
            foreach (string line in ErrorLines(error))
            {
                stderr.WriteLine(line);
            }
        }

        private static JObject Message(string? code, string? tfCode, string message, string? hint, string? item, string? activityId, string? serverMessage = null)
        {
            return new JObject
            {
                ["code"] = code,
                ["tfCode"] = tfCode,
                ["message"] = message,
                ["hint"] = hint,
                ["item"] = item,
                ["activityId"] = activityId,
                ["serverMessage"] = serverMessage,
            };
        }
    }

    /// <summary>Fixed-width table in the style of tf.exe: header, dashes, rows; the last column is never truncated.</summary>
    internal sealed class TextTable
    {
        private readonly string[] headers;
        private readonly List<string[]> rows = new List<string[]>();

        public TextTable(params string[] headers)
        {
            this.headers = headers;
        }

        public int Count => rows.Count;

        public void Add(params string[] cells) => rows.Add(cells);

        public IEnumerable<string> Render()
        {
            int[] widths = headers.Select(h => h.Length).ToArray();
            foreach (var row in rows)
            {
                for (int i = 0; i < widths.Length - 1; i++)
                {
                    widths[i] = Math.Max(widths[i], row[i].Length);
                }
            }

            yield return Format(headers, widths);
            yield return string.Join(" ", widths.Select((w, i) => new string('-', i == widths.Length - 1 ? Math.Max(w, MaxRowLength(i)) : w)));
            foreach (var row in rows)
            {
                yield return Format(row, widths);
            }
        }

        private int MaxRowLength(int column) => rows.Count == 0 ? headers[column].Length : Math.Max(headers[column].Length, rows.Max(r => r[column].Length));

        private static string Format(string[] cells, int[] widths)
        {
            var parts = new string[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                parts[i] = i == cells.Length - 1 ? cells[i] : cells[i].PadRight(widths[i]);
            }

            return string.Join(" ", parts).TrimEnd();
        }
    }

    /// <summary>
    /// Text helpers of SPEC.md "Tvar textového výstupu": grouped listings (folder header, items, blank line between
    /// groups) and detail blocks (`Label: value` lines aligned on the widest label, indented multi-line text).
    /// </summary>
    internal static class TextBlocks
    {
        /// <summary>Items grouped under their folder: `<folder>:` then one line per item, groups separated by a blank line.</summary>
        public static IEnumerable<string> Grouped(IEnumerable<KeyValuePair<string, string>> folderAndLine)
        {
            string? current = null;
            foreach (var pair in folderAndLine)
            {
                if (!string.Equals(pair.Key, current, StringComparison.OrdinalIgnoreCase))
                {
                    if (current != null)
                    {
                        yield return string.Empty;
                    }

                    yield return pair.Key + ":";
                    current = pair.Key;
                }

                yield return pair.Value;
            }
        }

        /// <summary>`Label: value` lines with the values aligned after the widest label; a null value prints an empty value.</summary>
        public static IEnumerable<string> Fields(IEnumerable<KeyValuePair<string, string?>> fields)
        {
            var list = fields.ToList();
            int width = list.Count == 0 ? 0 : list.Max(f => f.Key.Length);
            foreach (var field in list)
            {
                yield return $"{(field.Key + ":").PadRight(width + 1)} {field.Value}".TrimEnd();
            }
        }

        /// <summary>Multi-line text indented by two spaces, as the comment of a changeset block.</summary>
        public static IEnumerable<string> Indented(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                yield break;
            }

            foreach (string line in text!.Replace("\r\n", "\n").Split('\n'))
            {
                yield return "  " + line.TrimEnd();
            }
        }

        /// <summary>The 79-dash separator line of tf.exe detail listings.</summary>
        public static string Separator => new string('-', 79);
    }
}
