using TfPortable.Cli;
using TfPortable.Output;

namespace TfPortable.Commands
{
    internal interface ICommand
    {
        /// <summary>Canonical name; must match an entry of <see cref="CommandCatalog"/>, which owns the switch table.</summary>
        string Name { get; }

        /// <summary>Runs the command and returns the exit code. Errors are thrown as TfpException or mapped by the caller.</summary>
        int Run(CommandLine commandLine, OutputWriter output, string toolName);
    }
}
