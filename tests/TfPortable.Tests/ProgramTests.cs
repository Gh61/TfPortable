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
            Assert.False(string.IsNullOrWhiteSpace(Program.GetVersion()));
        }

        [Fact]
        public void Exit_codes_match_the_specification()
        {
            Assert.Equal(0, Program.ExitSuccess);
            Assert.Equal(2, Program.ExitUnknownCommand);
            Assert.Equal(100, Program.ExitGeneralError);
        }
    }
}
