using System;
using System.IO;
using System.Linq;
using TfPortable.Cli;
using TfPortable.Errors;
using Xunit;

namespace TfPortable.Tests
{
    public class CommandLineTests
    {
        [Theory]
        [InlineData("/recursive")]
        [InlineData("-recursive")]
        [InlineData("/Recursive")]
        [InlineData("-R")]
        [InlineData("/R")]
        [InlineData("-r")]
        public void Switches_accept_slash_and_dash_and_short_forms(string arg)
        {
            var line = CommandLine.Parse(new[] { "status", arg });
            Assert.Equal("status", line.Command);
            Assert.True(line.Has("recursive"));
            Assert.Empty(line.Positionals);
        }

        [Fact]
        public void Value_follows_the_first_colon()
        {
            var line = CommandLine.Parse(new[] { "whoami", "-collection:https://dev.azure.com/contoso", "/format:JSON" });
            Assert.Equal("https://dev.azure.com/contoso", line.Value("collection"));
            Assert.True(line.Json);
        }

        [Fact]
        public void Server_is_an_alias_of_collection_and_I_of_noprompt()
        {
            var line = CommandLine.Parse(new[] { "status", "/server:https://x", "-I", "-S:https://y" });
            Assert.Equal("https://y", line.Value("collection"));
            Assert.True(line.Has("noprompt"));
        }

        [Fact]
        public void Vc_prefix_is_skipped()
        {
            var line = CommandLine.Parse(new[] { "vc", "changeset", "81", "-noprompt" });
            Assert.Equal("changeset", line.Command);
            Assert.Equal(new[] { "81" }, line.Positionals);
            Assert.True(line.Has("noprompt"));
        }

        [Theory]
        [InlineData("stat", "status")]
        [InlineData("edit", "checkout")]
        [InlineData("co", "checkout")]
        [InlineData("del", "delete")]
        [InlineData("rm", "delete")]
        [InlineData("move", "rename")]
        [InlineData("ren", "rename")]
        [InlineData("diff", "difference")]
        [InlineData("info", "properties")]
        [InlineData("hist", "history")]
        [InlineData("lv", "localversions")]
        [InlineData("STATUS", "status")]
        public void Aliases_resolve_to_the_canonical_command(string alias, string canonical)
        {
            var line = CommandLine.Parse(new[] { alias });
            Assert.Equal(canonical, line.Command);
            Assert.Equal(alias.ToLowerInvariant(), line.RawCommand);
        }

        [Fact]
        public void Paths_with_forward_slashes_are_positionals()
        {
            var line = CommandLine.Parse(new[] { "view", "D:/Projects/App/BaseController.cs", "-version:C12" });
            Assert.Equal(new[] { "D:/Projects/App/BaseController.cs" }, line.Positionals);
            Assert.Equal("C12", line.Value("version"));
        }

        [Fact]
        public void Server_paths_are_positionals()
        {
            var line = CommandLine.Parse(new[] { "dir", "$/TfsPortableTestRepo/Main", "/recursive" });
            Assert.Equal(new[] { "$/TfsPortableTestRepo/Main" }, line.Positionals);
        }

        [Fact]
        public void Unknown_switch_is_TFP0002_with_exit_100_and_lists_the_valid_ones()
        {
            var error = Assert.Throws<TfpException>(() => CommandLine.Parse(new[] { "status", "/nosuchswitch" }));
            Assert.Equal(ErrorCodes.InvalidSwitch, error.Code);
            Assert.Equal(ExitCodes.GeneralError, error.ExitCode);
            Assert.Contains("/recursive", error.Hint);
            Assert.Contains("/format:brief|detailed|json", error.Hint);
        }

        [Fact]
        public void Short_form_of_another_commands_switch_is_unknown()
        {
            // -X is /slotmode of history; status has no such switch.
            var error = Assert.Throws<TfpException>(() => CommandLine.Parse(new[] { "status", "-X" }));
            Assert.Equal(ErrorCodes.InvalidSwitch, error.Code);
        }

        [Fact]
        public void Value_outside_the_allowed_list_is_TFP0002()
        {
            var error = Assert.Throws<TfpException>(() => CommandLine.Parse(new[] { "status", "/format:bogus" }));
            Assert.Equal(ErrorCodes.InvalidSwitch, error.Code);
            Assert.Equal(ExitCodes.GeneralError, error.ExitCode);
            Assert.Contains("brief, detailed, json", error.Hint);

            // A value tf.exe knows and TfPortable does not support (yet) is TFP0006, not a typo.
            var unsupported = Assert.Throws<TfpException>(() => CommandLine.Parse(new[] { "status", "/format:xml" }));
            Assert.Equal(ErrorCodes.SwitchNotSupported, unsupported.Code);
            Assert.StartsWith("Value 'xml' of '/format' for command 'status' is not supported by tfp yet.", unsupported.Message);
            Assert.Contains("brief, detailed, json", unsupported.Hint);
        }

        [Fact]
        public void Missing_required_value_is_TFP0002()
        {
            var error = Assert.Throws<TfpException>(() => CommandLine.Parse(new[] { "history", "$/x", "/version" }));
            Assert.Equal(ErrorCodes.InvalidSwitch, error.Code);
            Assert.Contains("needs a value", error.Message);
        }

        [Fact]
        public void Unexpected_value_is_TFP0002()
        {
            var error = Assert.Throws<TfpException>(() => CommandLine.Parse(new[] { "status", "/recursive:yes" }));
            Assert.Equal(ErrorCodes.InvalidSwitch, error.Code);
            Assert.Contains("does not take a value", error.Message);
        }

        [Fact]
        public void Global_switches_are_always_allowed()
        {
            var line = CommandLine.Parse(new[] { "status", "/noprompt", "/format:json", "/collection:https://x", "/workspace:W;me", "/login:u,p", "/verbose" });
            Assert.Equal(new[] { "noprompt", "format", "collection", "workspace", "login", "verbose" }, line.SwitchNames);
        }

        [Fact]
        public void Format_json_is_valid_for_every_command()
        {
            foreach (var spec in CommandCatalog.Commands.Where(c => c.Status != CommandStatus.OutOfScope))
            {
                var line = CommandLine.Parse(new[] { spec.Name, "/format:json" });
                Assert.True(line.Json, spec.Name);
            }
        }

        [Fact]
        public void Empty_arguments_give_an_empty_command()
        {
            var line = CommandLine.Parse(new string[0]);
            Assert.Equal(string.Empty, line.Command);
            Assert.Null(line.Spec);
        }

        [Fact]
        public void Unknown_command_keeps_its_name_and_switches_for_the_dispatcher()
        {
            var line = CommandLine.Parse(new[] { "nosuchcommand", "/whatever", "/format:json" });
            Assert.Null(line.Spec);
            Assert.Equal("nosuchcommand", line.RawCommand);
            Assert.True(line.Json);
        }

        [Theory]
        [InlineData("/?")]
        [InlineData("-?")]
        [InlineData("/help")]
        [InlineData("-H")]
        [InlineData("-h")]
        public void Help_switch_is_recognized_and_disables_validation(string arg)
        {
            var line = CommandLine.Parse(new[] { "status", arg, "/bogus" });
            Assert.True(line.HelpRequested);
        }

        [Fact]
        public void Other_command_groups_are_TFP0005()
        {
            var error = Assert.Throws<TfpException>(() => CommandLine.Parse(new[] { "git", "permission" }));
            Assert.Equal(ErrorCodes.NotOffered, error.Code);
            Assert.Equal(ExitCodes.GeneralError, error.ExitCode);
        }

        [Fact]
        public void Comma_separated_values_are_split()
        {
            var line = CommandLine.Parse(new[] { "history", "$/x", "/changetype:add, edit,delete" });
            Assert.Equal(new[] { "add", "edit", "delete" }, line.Values("changetype"));
            Assert.Empty(line.Values("user"));
        }

        [Fact]
        public void Last_occurrence_of_a_switch_wins()
        {
            var line = CommandLine.Parse(new[] { "history", "$/x", "/stopafter:5", "/stopafter:9" });
            Assert.Equal("9", line.Value("stopafter"));
            Assert.Single(line.SwitchNames);
        }

        [Fact]
        public void Argument_file_lines_become_arguments()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllLines(path, new[] { "$/Project/a.cs", "", "  $/Project/b.cs  ", "/recursive" });
                var line = CommandLine.Parse(new[] { "status", "@" + path, "/format:json" });
                Assert.Equal(new[] { "$/Project/a.cs", "$/Project/b.cs" }, line.Positionals);
                Assert.True(line.Has("recursive"));
                Assert.True(line.Json);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Missing_argument_file_is_TFP0002()
        {
            var error = Assert.Throws<TfpException>(() => CommandLine.Parse(new[] { "status", "@no-such-file.txt" }));
            Assert.Equal(ErrorCodes.InvalidSwitch, error.Code);
            Assert.Equal("no-such-file.txt", error.Item);
        }

        [Fact]
        public void Msys_converted_switch_is_reported_when_the_git_root_exists()
        {
            string root = Path.Combine(Path.GetTempPath(), "tfp-msys-test-git");
            Directory.CreateDirectory(root);
            string? msystem = Environment.GetEnvironmentVariable("MSYSTEM");
            try
            {
                Environment.SetEnvironmentVariable("MSYSTEM", "MINGW64");
                string arg = root.Replace('\\', '/') + "/recursive";
                var error = Assert.Throws<TfpException>(() => CommandLine.DetectMsysConvertedSwitch(arg, CommandCatalog.AllSwitchNames()));
                Assert.Equal(ErrorCodes.InvalidSwitch, error.Code);
                Assert.Contains("-recursive", error.Hint);

                // A converted /login:user,pat never echoes the value.
                var login = Assert.Throws<TfpException>(() => CommandLine.DetectMsysConvertedSwitch(root.Replace('\\', '/') + "/login:user,secretpat", CommandCatalog.AllSwitchNames()));
                Assert.DoesNotContain("secretpat", login.Message + login.Hint);

                // A real path under the same root that is not a switch name passes.
                CommandLine.DetectMsysConvertedSwitch(root.Replace('\\', '/') + "/readme.txt", CommandCatalog.AllSwitchNames());
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSYSTEM", msystem);
                Directory.Delete(root);
            }
        }

        [Fact]
        public void Msys_detection_is_off_outside_git_bash()
        {
            string? msystem = Environment.GetEnvironmentVariable("MSYSTEM");
            string? exePath = Environment.GetEnvironmentVariable("EXEPATH");
            try
            {
                Environment.SetEnvironmentVariable("MSYSTEM", null);
                Environment.SetEnvironmentVariable("EXEPATH", null);
                CommandLine.DetectMsysConvertedSwitch("C:/Program Files/Git/recursive", CommandCatalog.AllSwitchNames());
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSYSTEM", msystem);
                Environment.SetEnvironmentVariable("EXEPATH", exePath);
            }
        }
    }
}
