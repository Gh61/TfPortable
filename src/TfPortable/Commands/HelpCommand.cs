using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using TfPortable.Cli;
using TfPortable.Errors;
using TfPortable.Output;

namespace TfPortable.Commands
{
    /// <summary>
    /// `tfp help`, `tfp` with no arguments, `tfp help <command>` and `tfp <command> /?`: the command catalog or the
    /// switches of one command, from <see cref="CommandCatalog"/> (SPEC.md "Rozšíření pro AI", docs/json-schema.md `help`).
    /// </summary>
    internal sealed class HelpCommand : ICommand
    {
        public string Name => "help";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            string? target = commandLine.Command == Name
                ? (commandLine.Positionals.Count > 0 ? commandLine.Positionals[0] : null)
                : commandLine.Command;

            if (string.IsNullOrEmpty(target))
            {
                output.Lines(CatalogLines(toolName));
                output.Success(new JObject { ["commands"] = new JArray(CommandCatalog.Commands.Select(CommandJson).Cast<object>().ToArray()) });
                return ExitCodes.Success;
            }

            var spec = CommandCatalog.Find(target!);
            if (spec == null)
            {
                throw UnknownCommand(target!, toolName);
            }

            output.Lines(CommandLines(spec, toolName));
            output.Success(new JObject { ["commands"] = new JArray(CommandJson(spec)) });
            return ExitCodes.Success;
        }

        /// <summary>TFP0001 (exit 2) with the list of commands, shared with the dispatcher.</summary>
        public static TfpException UnknownCommand(string name, string toolName)
        {
            string commands = string.Join(", ", CommandCatalog.Commands.Where(c => c.Status != CommandStatus.OutOfScope).Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal));
            return new TfpException(ErrorCodes.UnknownCommand, ExitCodes.UnknownCommand,
                $"Unknown command '{name}'.",
                $"Commands: {commands}. Run '{toolName} help' for details.");
        }

        internal static IEnumerable<string> CatalogLines(string toolName)
        {
            yield return $"{toolName} {VersionCommand.ToolVersion()} - portable TFVC client (PAT sign-in, no dialogs, JSON output).";
            yield return $"Usage: {toolName} [vc] <command> [itemspec ...] [/switch[:value]] [@argumentfile]";
            yield return string.Empty;

            foreach (var line in Section("Commands (tf.exe compatible):", CommandCatalog.Commands.Where(c => c.Phase == 1)))
            {
                yield return line;
            }

            foreach (var line in Section("Tool commands:", CommandCatalog.Commands.Where(c => c.Phase == null && c.Status != CommandStatus.OutOfScope)))
            {
                yield return line;
            }

            foreach (var line in Section("Phase 2 (planned):", CommandCatalog.Commands.Where(c => c.Phase == 2)))
            {
                yield return line;
            }

            foreach (var line in Section("Phase 3 (planned):", CommandCatalog.Commands.Where(c => c.Phase == 3 && c.Status == CommandStatus.Planned)))
            {
                yield return line;
            }

            yield return "Not available in this build (planned for a later phase or never offered; use Visual Studio or tf.exe):";
            yield return "  " + string.Join(", ", CommandCatalog.Commands.Where(c => c.Status == CommandStatus.OutOfScope).Select(c => c.Name));
            yield return string.Empty;
            yield return "Global switches: " + string.Join(" ", CommandCatalog.GlobalSwitches.Select(s => s.Usage));
            yield return "Switches accept '/' or '-' (use '-' in Git Bash) and are case-insensitive; values follow ':' (e.g. -format:json).";
            yield return $"'{toolName} help <command>' or '{toolName} <command> /?' lists the switches of a command; '{toolName} help /format:json' is the machine-readable catalog.";
            yield return "Differences from tf.exe: docs/compat.md. Commands marked (planned) are not available in this build.";
        }

        private static IEnumerable<string> Section(string title, IEnumerable<CommandSpec> commands)
        {
            var list = commands.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
            if (list.Count == 0)
            {
                yield break;
            }

            yield return title;
            int width = list.Max(c => NameWithAliases(c).Length);
            foreach (var c in list)
            {
                string status = c.Status == CommandStatus.Planned ? " (planned)" : string.Empty;
                yield return $"  {NameWithAliases(c).PadRight(width)}  {c.Summary}{status}";
            }

            yield return string.Empty;
        }

        private static string NameWithAliases(CommandSpec c) => c.Aliases.Length == 0 ? c.Name : $"{c.Name} ({string.Join(", ", c.Aliases)})";

        internal static IEnumerable<string> CommandLines(CommandSpec spec, string toolName)
        {
            yield return $"{NameWithAliases(spec)} - {spec.Summary}";
            if (spec.Status == CommandStatus.OutOfScope)
            {
                yield return $"Not supported by {toolName} (TFP0003); use Visual Studio or the original tf.exe.";
                yield break;
            }

            var switches = CommandCatalog.SwitchesOf(spec).ToList();
            string usage = string.Join(" ", switches.Where(s => !IsGlobal(s)).Select(s => "[" + s.Usage + "]"));
            yield return $"Usage: {toolName} {spec.Name} {spec.Usage} {usage}".TrimEnd().Replace("  ", " ");
            yield return string.Empty;
            yield return "Switches:";
            int width = switches.Max(s => SwitchLabel(s).Length);
            foreach (var s in switches)
            {
                string extra = s.Extension ? " (TfPortable extension)" : string.Empty;
                yield return $"  {SwitchLabel(s).PadRight(width)}  {s.Summary}{extra}";
            }

            yield return string.Empty;
            yield return spec.Status == CommandStatus.Planned
                ? $"Status: planned for phase {spec.Phase}, not available in this build (TFP0003)."
                : "Status: available in this build.";
        }

        private static bool IsGlobal(SwitchSpec s) => CommandCatalog.GlobalSwitches.Any(g => ReferenceEquals(g, s));

        private static string SwitchLabel(SwitchSpec s) => s.ShortName == null ? s.Usage : $"{s.Usage} (-{s.ShortName})";

        internal static JObject CommandJson(CommandSpec c)
        {
            return new JObject
            {
                ["name"] = c.Name,
                ["aliases"] = new JArray(c.Aliases.Cast<object>().ToArray()),
                ["phase"] = c.Phase,
                ["supported"] = c.Status == CommandStatus.Implemented,
                ["status"] = c.Status == CommandStatus.Implemented ? "implemented" : c.Status == CommandStatus.Planned ? "planned" : "out-of-scope",
                ["summary"] = c.Summary,
                ["switches"] = new JArray(CommandCatalog.SwitchesOf(c).Select(s => new JObject
                {
                    ["name"] = s.Name,
                    ["short"] = s.ShortName,
                    ["value"] = s.Value.ToString().ToLowerInvariant(),
                    ["values"] = new JArray(s.Values.Cast<object>().ToArray()),
                    ["summary"] = s.Summary,
                    ["extension"] = s.Extension,
                }).Cast<object>().ToArray()),
            };
        }
    }

    /// <summary>`tfp version`: TfPortable and client library versions, for bug reports.</summary>
    internal sealed class VersionCommand : ICommand
    {
        public string Name => "version";

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
