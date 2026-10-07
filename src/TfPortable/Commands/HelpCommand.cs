using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using TfPortable.Cli;
using TfPortable.Errors;
using TfPortable.Output;

namespace TfPortable.Commands
{
    /// <summary>`tfp help [command]` and `tfp` with no arguments. Spike version: lists what this build implements.</summary>
    internal sealed class HelpCommand : ICommand
    {
        private readonly Func<ICommand[]> commands;

        public HelpCommand(Func<ICommand[]> commands)
        {
            this.commands = commands;
        }

        public string Name => "help";

        public string[] Switches => Array.Empty<string>();

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            var all = commands();
            output.Line($"{toolName} {VersionCommand.ToolVersion()} - portable TFVC client (spike build, milestone 1).");
            output.Line();
            output.Line("Commands in this build:");
            foreach (var command in all.OrderBy(c => c.Name, StringComparer.Ordinal))
            {
                string switches = string.Join(" ", command.Switches.Concat(CommandLine.GlobalSwitches).Distinct().Select(s => "/" + s));
                output.Line($"  {command.Name,-10} {switches}");
            }

            output.Line();
            output.Line("Switches accept '/' or '-' (use '-' in Git Bash). Values follow ':' e.g. -format:json.");
            output.Line("Full command set: docs/SPEC.md. Differences from tf.exe: docs/compat.md.");

            output.Success(new JObject
            {
                ["commands"] = new JArray(all.Select(c => new JObject
                {
                    ["name"] = c.Name,
                    ["aliases"] = new JArray(),
                    ["supported"] = true,
                    ["switches"] = new JArray(c.Switches.Concat(CommandLine.GlobalSwitches).Distinct().Select(s => new JObject { ["name"] = s }).Cast<object>().ToArray()),
                }).Cast<object>().ToArray()),
            });
            return ExitCodes.Success;
        }
    }

    /// <summary>`tfp version`: TfPortable and client library versions, for bug reports.</summary>
    internal sealed class VersionCommand : ICommand
    {
        public string Name => "version";

        public string[] Switches => Array.Empty<string>();

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            string client = ClientLibraryVersion();
            output.Line($"{toolName} {ToolVersion()}");
            output.Line($"Microsoft.TeamFoundationServer.ExtendedClient {client}");
            output.Line($".NET Framework {Environment.Version} on {Environment.OSVersion}");
            output.Success(new JObject
            {
                ["version"] = typeof(VersionCommand).Assembly.GetName().Version.ToString(),
                ["informationalVersion"] = ToolVersion(),
                ["clientLibraries"] = new JObject { ["Microsoft.TeamFoundationServer.ExtendedClient"] = client },
                ["runtime"] = $".NET Framework {Environment.Version}",
            });
            return ExitCodes.Success;
        }

        internal static string ToolVersion()
        {
            var assembly = typeof(VersionCommand).Assembly;
            string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return string.IsNullOrEmpty(informational) ? assembly.GetName().Version.ToString() : informational!;
        }

        internal static string ClientLibraryVersion()
        {
            var assembly = typeof(Microsoft.TeamFoundation.VersionControl.Client.VersionControlServer).Assembly;
            string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
            return string.IsNullOrEmpty(informational) ? assembly.GetName().Version.ToString() : informational!;
        }
    }
}
