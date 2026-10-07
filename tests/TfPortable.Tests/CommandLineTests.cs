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
            var line = CommandLine.Parse(new[] { "status", "/server:https://x", "-I" });
            Assert.Equal("https://x", line.Value("collection"));
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
        public void Unknown_switch_is_TFP0002_with_exit_100()
        {
            var line = CommandLine.Parse(new[] { "status", "/nosuchswitch" });
            var error = Assert.Throws<TfpException>(() => line.Validate("tfp", "recursive"));
            Assert.Equal(ErrorCodes.InvalidSwitch, error.Code);
            Assert.Equal(ExitCodes.GeneralError, error.ExitCode);
        }

        [Fact]
        public void Global_switches_are_always_allowed()
        {
            var line = CommandLine.Parse(new[] { "status", "/noprompt", "/format:json", "/collection:https://x" });
            line.Validate("tfp", "recursive");
        }

        [Fact]
        public void Empty_arguments_give_an_empty_command()
        {
            var line = CommandLine.Parse(new string[0]);
            Assert.Equal(string.Empty, line.Command);
        }
    }
}
