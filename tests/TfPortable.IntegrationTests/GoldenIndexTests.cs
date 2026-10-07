using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace TfPortable.IntegrationTests
{
    /// <summary>
    /// Integrity of tests/golden: every index entry points to an existing case file whose header
    /// agrees with the index. Runs without a server. The golden tests of milestones 3 and 4 build
    /// on the same loader (docs/golden-format.md).
    /// </summary>
    public class GoldenIndexTests
    {
        private static readonly Regex HeaderLine = new Regex(@"^# (?<key>[a-z-]+): (?<value>.*)$", RegexOptions.Compiled);

        public static IEnumerable<object[]> IndexFiles()
        {
            foreach (string file in Directory.GetFiles(TestEnvironment.GoldenRoot, "index*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                yield return new object[] { Path.GetFileName(file) };
            }
        }

        [Theory]
        [MemberData(nameof(IndexFiles))]
        public void Every_case_file_exists_and_matches_its_index_entry(string indexFileName)
        {
            string indexPath = Path.Combine(TestEnvironment.GoldenRoot, indexFileName);
            var index = JObject.Parse(File.ReadAllText(indexPath, Encoding.UTF8));
            var cases = (JArray?)index["cases"];
            Assert.NotNull(cases);
            Assert.NotEmpty(cases!);

            foreach (JObject c in cases!.Cast<JObject>())
            {
                string relative = (string)c["file"]!;
                string path = Path.Combine(TestEnvironment.GoldenRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(path), $"{indexFileName}: missing case file {relative}");

                var header = ReadHeader(path);
                Assert.Equal($"{(string)c["group"]!}/{(string)c["name"]!}", header["case"]);
                Assert.Equal((string)c["command"]!, header["command"]);
                Assert.Equal(((int)c["exitCode"]!).ToString(), header["exit"]);
                Assert.Equal(((bool)c["timedOut"]!).ToString().ToLowerInvariant(), header["timed-out"]);
            }
        }

        [Fact]
        public void Golden_files_contain_no_unredacted_personal_data()
        {
            // Values the step 0 redaction replaced; see docs/golden-format.md §5. Only placeholders may remain.
            var forbidden = new Regex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+|\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b|[A-Za-z]:\\Users\\(?!<USER>)", RegexOptions.Compiled);
            var offenders = new List<string>();

            foreach (string file in Directory.GetFiles(TestEnvironment.GoldenRoot, "*.txt", SearchOption.AllDirectories))
            {
                foreach (string line in File.ReadLines(file, Encoding.UTF8))
                {
                    if (forbidden.IsMatch(line))
                    {
                        offenders.Add($"{Path.GetFileName(Path.GetDirectoryName(file))}/{Path.GetFileName(file)}: {line.Trim()}");
                    }
                }
            }

            Assert.Empty(offenders);
        }

        [IntegrationFact]
        public void Test_collection_is_configured()
        {
            // Placeholder for the server-backed golden tests of milestones 3 and 4. Skipped without secrets.
            Assert.StartsWith("https://", TestEnvironment.Collection, StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, string> ReadHeader(string path)
        {
            var header = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in File.ReadLines(path, Encoding.UTF8))
            {
                if (line.StartsWith("==== ", StringComparison.Ordinal))
                {
                    break;
                }

                var m = HeaderLine.Match(line);
                if (m.Success)
                {
                    header[m.Groups["key"].Value] = m.Groups["value"].Value;
                }
            }

            return header;
        }
    }
}
