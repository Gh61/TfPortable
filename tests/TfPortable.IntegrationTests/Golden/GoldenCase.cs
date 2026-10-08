using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace TfPortable.IntegrationTests.Golden
{
    /// <summary>One captured case of the original tf.exe (docs/golden-format.md §2–3).</summary>
    public sealed class GoldenCase
    {
        private static readonly Regex HeaderLine = new Regex(@"^# (?<key>[a-z-]+): (?<value>.*)$", RegexOptions.Compiled);

        private GoldenCase(string group, string name, string command, int exitCode, bool timedOut, string file, IReadOnlyDictionary<string, string> header, string stdout, string stderr)
        {
            Group = group;
            Name = name;
            Command = command;
            ExitCode = exitCode;
            TimedOut = timedOut;
            File = file;
            Header = header;
            Stdout = stdout;
            Stderr = stderr;
        }

        public string Group { get; }

        public string Name { get; }

        /// <summary>`group/name`, as in `# case:`.</summary>
        public string Id => $"{Group}/{Name}";

        /// <summary>The command as typed, placeholders applied, starting with `tf `.</summary>
        public string Command { get; }

        /// <summary>The arguments after `tf ` (what tfp.exe receives), placeholders not yet applied.</summary>
        public string Arguments => Command.StartsWith("tf ", StringComparison.Ordinal) ? Command.Substring(3).Trim() : Command.Trim();

        public int ExitCode { get; }

        public bool TimedOut { get; }

        public string File { get; }

        public IReadOnlyDictionary<string, string> Header { get; }

        /// <summary>`# cwd:` of the capture, with placeholders.</summary>
        public string Cwd => Header.TryGetValue("cwd", out var cwd) ? cwd : "<WORKSPACE_ROOT>";

        public string Stdout { get; }

        public string Stderr { get; }

        public override string ToString() => Id;

        /// <summary>Loads every case of an index file, in capture order.</summary>
        public static IReadOnlyList<GoldenCase> LoadIndex(string indexFileName)
        {
            string indexPath = Path.Combine(TestEnvironment.GoldenRoot, indexFileName);
            var index = JObject.Parse(System.IO.File.ReadAllText(indexPath, Encoding.UTF8));
            return ((JArray)index["cases"]!).Cast<JObject>().Select(Load).ToList();
        }

        /// <summary>Loads one case by `group/name` from any `index*.json`: `index.json`, then `index-extra.json` (the first extra run), then the suffixed runs in order.</summary>
        public static GoldenCase Load(string id)
        {
            var indexFiles = Directory.GetFiles(TestEnvironment.GoldenRoot, "index*.json")
                .Select(Path.GetFileName)
                .OrderBy(f => f == "index.json" ? 0 : f == "index-extra.json" ? 1 : 2)
                .ThenBy(f => f, StringComparer.Ordinal);
            foreach (string indexFile in indexFiles)
            {
                var found = LoadIndex(indexFile!).FirstOrDefault(c => c.Id == id);
                if (found != null)
                {
                    return found;
                }
            }

            throw new InvalidOperationException($"Golden case {id} is not in any index*.json");
        }

        private static GoldenCase Load(JObject entry)
        {
            string relative = (string)entry["file"]!;
            string path = Path.Combine(TestEnvironment.GoldenRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            string text = System.IO.File.ReadAllText(path, Encoding.UTF8);

            const string StdoutMarker = "==== stdout ====";
            const string StderrMarker = "==== stderr ====";
            int stdoutAt = text.IndexOf(StdoutMarker, StringComparison.Ordinal);
            int stderrAt = text.IndexOf(StderrMarker, StringComparison.Ordinal);
            if (stdoutAt < 0 || stderrAt < stdoutAt)
            {
                throw new InvalidOperationException($"Golden file {relative} has no stdout/stderr sections");
            }

            var header = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in text.Substring(0, stdoutAt).Split('\n'))
            {
                var m = HeaderLine.Match(line.TrimEnd('\r'));
                if (m.Success)
                {
                    header[m.Groups["key"].Value] = m.Groups["value"].Value;
                }
            }

            string stdout = StripOneLineBreak(text.Substring(stdoutAt + StdoutMarker.Length, stderrAt - stdoutAt - StdoutMarker.Length));
            string stderr = StripOneLineBreak(text.Substring(stderrAt + StderrMarker.Length));

            return new GoldenCase((string)entry["group"]!, (string)entry["name"]!, (string)entry["command"]!, (int)entry["exitCode"]!,
                (bool)entry["timedOut"]!, relative, header, stdout, stderr);
        }

        /// <summary>The sections start after the marker's line break and end with the line break that joined them (§2).</summary>
        private static string StripOneLineBreak(string section)
        {
            if (section.StartsWith("\r\n", StringComparison.Ordinal))
            {
                section = section.Substring(2);
            }
            else if (section.StartsWith("\n", StringComparison.Ordinal))
            {
                section = section.Substring(1);
            }

            if (section.EndsWith("\r\n", StringComparison.Ordinal))
            {
                section = section.Substring(0, section.Length - 2);
            }
            else if (section.EndsWith("\n", StringComparison.Ordinal))
            {
                section = section.Substring(0, section.Length - 1);
            }

            return section;
        }
    }
}
