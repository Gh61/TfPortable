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
    }
}
