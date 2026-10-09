using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TfPortable.Commands;
using TfPortable.Errors;
using Xunit;

namespace TfPortable.Tests
{
    public class ProgramTests
    {
        [Fact]
        public void Assembly_is_named_tfp()
        {
            Assert.Equal("tfp", typeof(Program).Assembly.GetName().Name);
        }

        [Fact]
        public void Version_is_not_empty()
        {
            Assert.False(string.IsNullOrWhiteSpace(VersionCommand.ToolVersion()));
        }

        [Fact]
        public void Exit_codes_match_the_specification()
        {
            Assert.Equal(0, ExitCodes.Success);
            Assert.Equal(1, ExitCodes.PartialSuccess);
            Assert.Equal(2, ExitCodes.UnknownCommand);
            Assert.Equal(100, ExitCodes.GeneralError);
            Assert.Equal(110, ExitCodes.Authentication);
            Assert.Equal(120, ExitCodes.WorkspaceNotFound);
        }

        private static (int Exit, string Out, string Err) Run(params string[] args)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            int exit = Program.Run(args, stdout, stderr, "tfp");
            return (exit, stdout.ToString(), stderr.ToString());
        }

        [Fact]
        public void No_arguments_prints_the_catalog_with_exit_0()
        {
            var (exit, stdout, stderr) = Run();
            Assert.Equal(0, exit);
            Assert.Contains("Commands (tf.exe compatible):", stdout);
            Assert.Contains("status (stat)", stdout);
            Assert.Equal(string.Empty, stderr);
        }

        [Fact]
        public void Help_of_a_command_and_the_question_mark_switch()
        {
            var (exit, stdout, _) = Run("help", "status");
            Assert.Equal(0, exit);
            Assert.Contains("Usage: tfp status", stdout);

            var (exit2, stdout2, _) = Run("status", "/?", "/bogus");
            Assert.Equal(0, exit2);
            Assert.Contains("Usage: tfp status", stdout2);

            var (exit3, stdout3, _) = Run("vc", "help", "edit");
            Assert.Equal(0, exit3);
            Assert.Contains("checkout (edit, co) - ", stdout3);
        }

        [Fact]
        public void Help_json_lists_the_catalog()
        {
            var (exit, stdout, stderr) = Run("help", "/format:json");
            Assert.Equal(0, exit);
            Assert.Equal(string.Empty, stderr);
            var envelope = JObject.Parse(stdout);
            Assert.True((bool)envelope["ok"]!);
            Assert.Equal("help", (string)envelope["command"]!);
            JsonAssert.Null(envelope["collection"]!);
            var commands = (JArray)envelope["data"]!["commands"]!;
            Assert.Contains(commands, c => (string)c["name"]! == "status" && (bool)c["supported"]!);
            Assert.Contains(commands, c => (string)c["name"]! == "checkin" && (bool)c["supported"]!);
            Assert.Contains(commands, c => (string)c["name"]! == "destroy" && (bool)c["supported"]!);
            Assert.Contains(commands, c => (string)c["name"]! == "proxy" && !(bool)c["supported"]!);

            var (_, single, _) = Run("help", "history", "/format:json");
            Assert.Single((JArray)JObject.Parse(single)["data"]!["commands"]!);
        }

        [Fact]
        public void Unknown_command_exits_2_on_stderr()
        {
            var (exit, stdout, stderr) = Run("nosuchcommand");
            Assert.Equal(2, exit);
            Assert.Equal(string.Empty, stdout);
            Assert.StartsWith("ERROR TFP0001: Unknown command 'nosuchcommand'.", stderr);
            Assert.Contains("Next step: Commands: ", stderr);
        }

        [Fact]
        public void Unknown_switch_exits_100_on_stderr()
        {
            var (exit, stdout, stderr) = Run("status", "/nosuchswitch");
            Assert.Equal(100, exit);
            Assert.Equal(string.Empty, stdout);
            Assert.StartsWith("ERROR TFP0002: Unknown switch '/nosuchswitch' for command 'status'.", stderr);
        }

        [Fact]
        public void Never_offered_commands_and_groups_are_TFP0005_with_exit_100()
        {
            var (exit, _, stderr) = Run("proxy", "/configure");
            Assert.Equal(100, exit);
            Assert.StartsWith("ERROR TFP0005: Command 'proxy' is not part of tfp: it configures a TFVC proxy server", stderr);

            var (exit2, _, stderr2) = Run("configure", "$/a");
            Assert.Equal(100, exit2);
            Assert.Contains("never shows a dialog", stderr2);

            var (exit3, _, stderr3) = Run("git", "permission");
            Assert.Equal(100, exit3);
            Assert.StartsWith("ERROR TFP0005:", stderr3);
        }

        [Fact]
        public void Errors_in_json_mode_are_one_envelope_on_stdout()
        {
            var (exit, stdout, stderr) = Run("nosuchcommand", "/format:json");
            Assert.Equal(2, exit);
            Assert.Equal(string.Empty, stderr);
            var envelope = JObject.Parse(stdout);
            Assert.False((bool)envelope["ok"]!);
            Assert.Equal(2, (int)envelope["exitCode"]!);
            Assert.Equal("TFP0001", (string)envelope["errors"]![0]!["code"]!);

            var (exit2, stdout2, _) = Run("status", "/format:json", "/nosuchswitch");
            Assert.Equal(100, exit2);
            var envelope2 = JObject.Parse(stdout2);
            Assert.Equal("status", (string)envelope2["command"]!);
            Assert.Equal("TFP0002", (string)envelope2["errors"]![0]!["code"]!);
        }

        [Fact]
        public void Global_switch_errors_without_a_command_do_not_crash()
        {
            var (exit, _, stderr) = Run("/format:xml");
            Assert.Equal(100, exit);
            Assert.StartsWith("ERROR TFP0002: 'xml' is not a valid value of '/format'.", stderr);

            var (exit2, _, stderr2) = Run("nosuchcommand", "/format:xml");
            Assert.Equal(2, exit2);
            Assert.StartsWith("ERROR TFP0001:", stderr2);

            var (exit3, _, stderr3) = Run("/noprompt:yes");
            Assert.Equal(100, exit3);
            Assert.StartsWith("ERROR TFP0002:", stderr3);
        }

        [Fact]
        public void A_pat_passed_to_a_secret_switch_is_never_echoed()
        {
            var (exit, stdout, stderr) = Run("login", "/collection:https://dev.azure.com/contoso", "/pat:abcdefghijklmnop1234");
            Assert.Equal(100, exit);
            Assert.DoesNotContain("abcdefghijklmnop1234", stdout + stderr);
            Assert.Contains("the value given is not a valid value of '/pat'", stderr);

            var (exit2, stdout2, stderr2) = Run("version", "/login:user,secretpat123", "/verbose:x");
            Assert.Equal(100, exit2);
            Assert.DoesNotContain("secretpat123", stdout2 + stderr2);
        }

        [Fact]
        public void Format_json_inside_an_argument_file_switches_the_envelope_on()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllLines(path, new[] { "/format:json" });
                var (exit, stdout, _) = Run("version", "@" + path);
                Assert.Equal(0, exit);
                Assert.Equal("version", (string)JObject.Parse(stdout)["command"]!);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Version_prints_tool_and_client_library()
        {
            var (exit, stdout, _) = Run("version");
            Assert.Equal(0, exit);
            Assert.StartsWith("tfp " + VersionCommand.ToolVersion(), stdout);
            Assert.Contains("Microsoft.TeamFoundationServer.ExtendedClient", stdout);

            var (_, json, _) = Run("version", "-F:json");
            var envelope = JObject.Parse(json);
            Assert.Equal("version", (string)envelope["command"]!);
            Assert.NotNull(envelope["data"]!["clientLibraries"]!["Microsoft.TeamFoundationServer.ExtendedClient"]);
        }

        [Fact]
        public void Invalid_login_switch_is_TFP0101()
        {
            var error = Assert.Throws<TfpException>(() => Context.ContextResolver.ParseLoginSwitch("user,", "tfp"));
            Assert.Equal(ErrorCodes.PatMissing, error.Code);
            Assert.Equal("secret", Context.ContextResolver.ParseLoginSwitch("user,secret", "tfp"));
            Assert.Equal("secret", Context.ContextResolver.ParseLoginSwitch("secret", "tfp"));
        }

        [Fact]
        public void Every_catalog_command_has_help_text()
        {
            foreach (var spec in Cli.CommandCatalog.Commands)
            {
                var (exit, stdout, _) = Run("help", spec.Name);
                Assert.Equal(0, exit);
                Assert.Contains(spec.Name, stdout);
            }
        }
    }
}
