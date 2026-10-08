using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TfPortable.Cli;
using TfPortable.Commands;
using TfPortable.Settings;
using Xunit;

namespace TfPortable.Tests
{
    /// <summary>Protection levels (SPEC.md "Úrovně ochrany"): the catalog map, the settings file and the gate in Program.</summary>
    public sealed class ProtectionTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "tfp-tests-" + Guid.NewGuid().ToString("N"));
        private readonly Func<string> previousPath = ToolSettings.PathProvider;
        private readonly Func<bool> previousInteractive = ProtectionCommand.InteractiveProvider;
        private readonly Func<string?> previousReadLine = ProtectionCommand.ReadLineProvider;

        public ProtectionTests()
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "settings.json");
            ToolSettings.PathProvider = () => path;
        }

        public void Dispose()
        {
            ToolSettings.PathProvider = previousPath;
            ProtectionCommand.InteractiveProvider = previousInteractive;
            ProtectionCommand.ReadLineProvider = previousReadLine;
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }

        private static (int Exit, string Out, string Err) Run(params string[] args)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            int exit = Program.Run(args, stdout, stderr, "tfp");
            return (exit, stdout.ToString(), stderr.ToString());
        }

        [Fact]
        public void Every_catalog_command_has_a_level_and_the_map_names_only_catalog_commands()
        {
            foreach (var spec in CommandCatalog.Commands)
            {
                int level = ProtectionLevels.Of(spec.Name);
                Assert.InRange(level, ProtectionLevels.ReadOnly, ProtectionLevels.Everything);
            }

            foreach (string name in ProtectionLevels.KnownCommands)
            {
                Assert.NotNull(CommandCatalog.Find(name));
            }
        }

        [Fact]
        public void Reads_are_level_1_and_writes_are_level_2_or_3()
        {
            Assert.Equal(1, ProtectionLevels.Of("status"));
            Assert.Equal(1, ProtectionLevels.Of("shelvesets"));
            Assert.Equal(1, ProtectionLevels.Of("protection"));
            Assert.Equal(2, ProtectionLevels.Of("checkin"));
            Assert.Equal(2, ProtectionLevels.Of("lock"));
            Assert.Equal(3, ProtectionLevels.Of("destroy"));
            Assert.Equal(3, ProtectionLevels.Of("workspace"));
        }

        [Fact]
        public void A_switch_can_raise_the_level_and_merge_candidate_lowers_it()
        {
            var checkin = CommandLine.Parse(new[] { "checkin", "/comment:x", "/override:why" });
            Assert.Equal((3, "override"), ProtectionLevels.Required(checkin.Spec!, checkin));

            var plain = CommandLine.Parse(new[] { "checkin", "/comment:x" });
            Assert.Equal((2, (string?)null), ProtectionLevels.Required(plain.Spec!, plain));

            var candidate = CommandLine.Parse(new[] { "merge", "/candidate", "$/a", "$/b" });
            Assert.Equal((1, (string?)null), ProtectionLevels.Required(candidate.Spec!, candidate));

            var undoForeign = CommandLine.Parse(new[] { "undo", "/workspace:other;owner", "$/a" });
            Assert.Equal((3, "workspace"), ProtectionLevels.Required(undoForeign.Spec!, undoForeign));
        }

        [Fact]
        public void Settings_default_to_level_2_and_round_trip_through_the_file()
        {
            Assert.False(File.Exists(ToolSettings.FilePath));
            Assert.Equal(2, ToolSettings.Load().ProtectionLevel);

            new ToolSettings { ProtectionLevel = 3 }.Save();
            Assert.Equal(3, ToolSettings.Load().ProtectionLevel);
            Assert.Equal(3, (int)JObject.Parse(File.ReadAllText(ToolSettings.FilePath))["protectionLevel"]!);
        }

        [Fact]
        public void An_invalid_level_in_the_file_is_TFP0004()
        {
            File.WriteAllText(ToolSettings.FilePath, "{ \"protectionLevel\": 7 }");
            var ex = Assert.Throws<TfPortable.Errors.TfpException>(() => ToolSettings.Load());
            Assert.Equal("TFP0004", ex.Code);

            File.WriteAllText(ToolSettings.FilePath, "not json");
            Assert.Equal("TFP0004", Assert.Throws<TfPortable.Errors.TfpException>(() => ToolSettings.Load()).Code);
        }

        [Fact]
        public void A_command_above_the_level_is_TFP0004_with_the_spec_wording()
        {
            new ToolSettings { ProtectionLevel = 1 }.Save();
            var (exit, stdout, stderr) = Run("add", "file.txt");
            Assert.Equal(100, exit);
            Assert.Equal(string.Empty, stdout);
            Assert.StartsWith("ERROR TFP0004: Command 'add' needs protection level 2. The current level is 1.", stderr);
            Assert.Contains("Raise it with 'tfp protection set 2' in an interactive terminal.", stderr);
            Assert.DoesNotContain(";", stderr);

            var (exitJson, json, _) = Run("add", "file.txt", "/format:json");
            Assert.Equal(100, exitJson);
            Assert.Equal("TFP0004", (string)JObject.Parse(json)["errors"]![0]!["code"]!);
        }

        [Fact]
        public void A_level_1_command_runs_without_reading_the_settings_file()
        {
            File.WriteAllText(ToolSettings.FilePath, "not json");
            var (exit, _, _) = Run("help", "status");
            Assert.Equal(0, exit);
        }

        [Fact]
        public void Protection_without_arguments_shows_the_level()
        {
            var (exit, stdout, _) = Run("protection");
            Assert.Equal(0, exit);
            Assert.Contains("Protection level: 2 (everyday work)", stdout);

            var (_, json, _) = Run("protection", "/format:json");
            var data = JObject.Parse(json)["data"]!;
            Assert.Equal(2, (int)data["protectionLevel"]!);
            Assert.Equal(ToolSettings.FilePath, (string)data["settingsPath"]!);
        }

        [Fact]
        public void Protection_set_refuses_a_non_interactive_run_and_bad_arguments()
        {
            ProtectionCommand.InteractiveProvider = () => false;
            var (exit, _, stderr) = Run("protection", "set", "3");
            Assert.Equal(100, exit);
            Assert.StartsWith("ERROR TFP0004: The protection level is changed in an interactive terminal", stderr);
            Assert.Equal(2, ToolSettings.Load().ProtectionLevel);

            var (exit2, _, stderr2) = Run("protection", "set", "9");
            Assert.Equal(100, exit2);
            Assert.StartsWith("ERROR TFP0002:", stderr2);

            var (exit3, _, stderr3) = Run("protection", "raise");
            Assert.Equal(100, exit3);
            Assert.StartsWith("ERROR TFP0002:", stderr3);
        }

        [Fact]
        public void Protection_set_raises_only_with_a_typed_confirmation_and_lowers_without_one()
        {
            ProtectionCommand.InteractiveProvider = () => true;
            ProtectionCommand.ReadLineProvider = () => "no";
            var (exit, _, stderr) = Run("protection", "set", "3");
            Assert.Equal(100, exit);
            Assert.StartsWith("ERROR TFP0004: The confirmation did not match.", stderr);
            Assert.Equal(2, ToolSettings.Load().ProtectionLevel);

            ProtectionCommand.ReadLineProvider = () => "3";
            var (exit2, stdout2, _) = Run("protection", "set", "3");
            Assert.Equal(0, exit2);
            Assert.Contains("Protection level set to 3 (everything).", stdout2);
            Assert.Equal(3, ToolSettings.Load().ProtectionLevel);

            ProtectionCommand.ReadLineProvider = () => throw new InvalidOperationException("lowering must not ask");
            var (exit3, json, _) = Run("protection", "set", "1", "/format:json");
            Assert.Equal(0, exit3);
            var data = JObject.Parse(json)["data"]!;
            Assert.Equal(1, (int)data["protectionLevel"]!);
            Assert.Equal(3, (int)data["previous"]!);
            Assert.Equal(1, ToolSettings.Load().ProtectionLevel);
        }

        [Fact]
        public void Help_and_whoami_show_the_level()
        {
            var (_, stdout, _) = Run("help", "checkin");
            Assert.Contains("Protection level: 2 (everyday work)", stdout);

            var (_, json, _) = Run("help", "destroy", "/format:json");
            Assert.Equal(3, (int)JObject.Parse(json)["data"]!["commands"]![0]!["protectionLevel"]!);

            var lines = WhoamiCommand.Render(new Context.IdentityInfo("Name", "user@example.com", "id"), "https://dev.azure.com/org", null, null, "C:\\work", 1).ToList();
            Assert.Contains("Protection : 1 (read-only)", lines);
        }
    }
}
