using TfPortable.Cli;
using TfPortable.Output;

namespace TfPortable.Commands
{
    internal interface ICommand
    {
        /// <summary>Canonical name (also the JSON "command" field).</summary>
        string Name { get; }

        /// <summary>Switches the command accepts, in addition to the global ones.</summary>
        string[] Switches { get; }

        /// <summary>Runs the command and returns the exit code. Errors are thrown as TfpException or mapped by the caller.</summary>
        int Run(CommandLine commandLine, OutputWriter output, string toolName);
    }
}
