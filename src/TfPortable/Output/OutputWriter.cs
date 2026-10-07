using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TfPortable.Errors;

namespace TfPortable.Output
{
    /// <summary>Text and JSON output (SPEC.md "Výstup a kódování", docs/json-schema.md).</summary>
    internal sealed class OutputWriter
    {
        private readonly TextWriter stdout;
        private readonly TextWriter stderr;
        private readonly List<JObject> warnings = new List<JObject>();

        public OutputWriter(bool json, string command, TextWriter stdout, TextWriter stderr)
        {
            Json = json;
            Command = command;
            this.stdout = stdout;
            this.stderr = stderr;
        }

        public bool Json { get; }

        public string Command { get; }

        public string? Collection { get; set; }

        public string? Workspace { get; set; }

        public JObject? Truncated { get; set; }

        public void Line(string text = "")
        {
            if (!Json)
            {
                stdout.WriteLine(text);
            }
        }

        public void Warning(string message, string? code = null, string? hint = null)
        {
            warnings.Add(Message(code, null, message, hint, null, null));
            if (!Json)
            {
                stderr.WriteLine(code == null ? $"WARNING: {message}" : $"WARNING {code}: {message}");
            }
        }

        /// <summary>Writes the JSON envelope for a successful command (no-op in text mode).</summary>
        public void Success(JObject data, int exitCode = ExitCodes.Success)
        {
            if (Json)
            {
                stdout.WriteLine(Envelope(exitCode, data, new JArray()).ToString(Formatting.Indented));
            }
        }

        /// <summary>Writes the error in text form to stderr, or as the JSON envelope to stdout.</summary>
        public void Error(TfpException error)
        {
            if (Json)
            {
                var errors = new JArray(Message(error.Code, error.TfCode, error.Message, error.Hint, error.Item, error.ActivityId));
                stdout.WriteLine(Envelope(error.ExitCode, new JObject(), errors).ToString(Formatting.Indented));
                return;
            }

            string code = error.TfCode == null ? error.Code : $"{error.Code} [{error.TfCode}]";
            stderr.WriteLine($"ERROR {code}: {error.Message}");
            if (!string.IsNullOrEmpty(error.ServerMessage) && !string.Equals(error.ServerMessage, error.Message, StringComparison.Ordinal))
            {
                stderr.WriteLine($"  Server message: {error.ServerMessage!.Trim()}");
            }

            if (!string.IsNullOrEmpty(error.Hint))
            {
                stderr.WriteLine($"  Next step: {error.Hint}");
            }

            if (!string.IsNullOrEmpty(error.ActivityId))
            {
                stderr.WriteLine($"  ActivityId: {error.ActivityId}");
            }
        }

        public static string IsoDate(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:sszzz");

        public static string IsoDate(DateTime value) => IsoDate(new DateTimeOffset(value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Local) : value.ToLocalTime()));

        private JObject Envelope(int exitCode, JObject data, JArray errors)
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

        private static JObject Message(string? code, string? tfCode, string message, string? hint, string? item, string? activityId)
        {
            return new JObject
            {
                ["code"] = code,
                ["tfCode"] = tfCode,
                ["message"] = message,
                ["hint"] = hint,
                ["item"] = item,
                ["activityId"] = activityId,
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
}
