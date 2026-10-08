using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TfPortable.Errors;

namespace TfPortable.Cli
{
    /// <summary>
    /// Parsed command line in tf.exe style: `[vc] command [itemspec ...] [/switch[:value]]` (SPEC.md "Společné konvence CLI").
    /// Switches start with '/' or '-', are case-insensitive, take their value after the first ':' and are resolved
    /// against the command's switch table (<see cref="CommandCatalog"/>): long names, short forms (`-R`, `-I`, `-V`, ...),
    /// `/server` for `/collection`. `@file` arguments are replaced by the lines of the file. The optional `vc` prefix is skipped.
    /// </summary>
    internal sealed class CommandLine
    {
        private static readonly Regex MsysConvertedPath = new Regex(@"^(?<root>[A-Za-z]:/(?:[^/]+/)*?)(?<name>[A-Za-z?]+)(?::(?<value>.*))?$", RegexOptions.Compiled);

        private readonly Dictionary<string, string?> switches = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> switchOrder = new List<string>();

        private CommandLine(string rawCommand, CommandSpec? spec, List<string> positionals)
        {
            RawCommand = rawCommand;
            Spec = spec;
            Positionals = positionals;
        }

        /// <summary>The command as typed (lower-case), or empty when no command was given.</summary>
        public string RawCommand { get; }

        /// <summary>Catalog entry, or null when there is no command.</summary>
        public CommandSpec? Spec { get; }

        /// <summary>Canonical command name (`status` for `stat`), or empty when no command was given.</summary>
        public string Command => Spec?.Name ?? RawCommand;

        public IReadOnlyList<string> Positionals { get; }

        /// <summary>Canonical switch names in the order they were given.</summary>
        public IReadOnlyList<string> SwitchNames => switchOrder;

        public bool Has(string name) => switches.ContainsKey(name);

        public string? Value(string name) => switches.TryGetValue(name, out var value) ? value : null;

        /// <summary>Comma-separated values of a switch, trimmed; empty when the switch is absent.</summary>
        public string[] Values(string name)
        {
            string? value = Value(name);
            return string.IsNullOrEmpty(value)
                ? Array.Empty<string>()
                : value!.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0).ToArray();
        }

        public bool Json => string.Equals(Value("format"), CommandCatalog.Json, StringComparison.OrdinalIgnoreCase);

        /// <summary>`/?`, `/help` or `-H` was given.</summary>
        public bool HelpRequested => Has("help");

        /// <summary>
        /// Parses and validates. Throws TFP0001 (exit 2) for an unknown command, TFP0003 for another tf.exe command group,
        /// TFP0002 (exit 100) for an unknown switch, a missing or unexpected value, a value outside the allowed list,
        /// or a missing @file. The switches of an unknown command are not validated.
        /// </summary>
        public static CommandLine Parse(string[] args, string toolName = "tfp")
        {
            string[] expanded = ExpandArgumentFiles(args);
            var known = CommandCatalog.AllSwitchNames();

            int index = 0;
            if (expanded.Length > 0 && string.Equals(expanded[0], "vc", StringComparison.OrdinalIgnoreCase))
            {
                index = 1;
            }

            string rawCommand = string.Empty;
            CommandSpec? spec = null;
            var positionals = new List<string>();
            var rawSwitches = new List<KeyValuePair<string, string?>>();

            for (; index < expanded.Length; index++)
            {
                string arg = expanded[index];
                if (IsSwitch(arg))
                {
                    string body = arg.Substring(1);
                    int colon = body.IndexOf(':');
                    string name = colon >= 0 ? body.Substring(0, colon) : body;
                    string? value = colon >= 0 ? body.Substring(colon + 1) : null;
                    rawSwitches.Add(new KeyValuePair<string, string?>(name, value));
                    continue;
                }

                DetectMsysConvertedSwitch(arg, known);

                if (rawCommand.Length == 0)
                {
                    rawCommand = arg.ToLowerInvariant();
                    if (CommandCatalog.OtherGroups.Contains(rawCommand))
                    {
                        throw new TfpException(ErrorCodes.NotSupported, ExitCodes.GeneralError,
                            $"The command group '{rawCommand}' of tf.exe is not supported; {toolName} covers version control (tf vc) only.",
                            $"Run '{toolName} help' for the commands.");
                    }

                    spec = CommandCatalog.Find(rawCommand);
                }
                else
                {
                    positionals.Add(arg);
                }
            }

            var result = new CommandLine(rawCommand, spec, positionals);
            var table = SwitchTable(spec);
            bool help = rawSwitches.Any(s => IsHelpSwitch(s.Key));

            foreach (var raw in rawSwitches)
            {
                string name = raw.Key;
                if (IsHelpSwitch(name))
                {
                    result.Set("help", null);
                    continue;
                }

                SwitchSpec? switchSpec = Resolve(table, name);
                if (switchSpec == null)
                {
                    if (spec == null || help)
                    {
                        // Unknown command (TFP0001 is thrown by the caller) or help: keep the switch, do not judge it.
                        result.Set(name.ToLowerInvariant(), raw.Value);
                        continue;
                    }

                    throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError,
                        $"Unknown switch '/{name}' for command '{spec.Name}'.",
                        $"Valid switches: {string.Join(", ", table.Values.Distinct().Select(s => s.Usage).OrderBy(s => s, StringComparer.Ordinal))}. See '{toolName} help {spec.Name}'.");
                }

                if (!help)
                {
                    ValidateValue(spec!, switchSpec, raw.Value, toolName);
                }

                result.Set(switchSpec.Name, raw.Value);
            }

            return result;
        }

        private void Set(string name, string? value)
        {
            if (!switches.ContainsKey(name))
            {
                switchOrder.Add(name);
            }

            switches[name] = value;
        }

        private static bool IsHelpSwitch(string name) =>
            name == "?" || name.Equals("help", StringComparison.OrdinalIgnoreCase) || name.Equals("h", StringComparison.OrdinalIgnoreCase);

        /// <summary>Long names, short forms and `server` for the command; the global switches alone when the command is unknown.</summary>
        private static Dictionary<string, SwitchSpec> SwitchTable(CommandSpec? spec)
        {
            var table = new Dictionary<string, SwitchSpec>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<SwitchSpec> specs = spec == null ? CommandCatalog.GlobalSwitches : CommandCatalog.SwitchesOf(spec);
            foreach (var s in specs)
            {
                table[s.Name] = s;
            }

            foreach (var s in specs)
            {
                if (s.ShortName != null && !table.ContainsKey(s.ShortName))
                {
                    table[s.ShortName] = s;
                }
            }

            if (table.TryGetValue("collection", out var collection))
            {
                table["server"] = collection;
            }

            return table;
        }

        private static SwitchSpec? Resolve(Dictionary<string, SwitchSpec> table, string name) =>
            table.TryGetValue(name, out var s) ? s : null;

        private static void ValidateValue(CommandSpec command, SwitchSpec s, string? value, string toolName)
        {
            if (s.Value == SwitchValue.None && value != null)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError,
                    $"Switch '/{s.Name}' of command '{command.Name}' does not take a value ('{value}' given).",
                    $"Write '/{s.Name}' alone. See '{toolName} help {command.Name}'.");
            }

            if (s.Value == SwitchValue.Required && string.IsNullOrEmpty(value))
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError,
                    $"Switch '/{s.Name}' of command '{command.Name}' needs a value.",
                    $"Write '{s.Usage}'. See '{toolName} help {command.Name}'.");
            }

            if (!string.IsNullOrEmpty(value) && !s.Accepts(value!))
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError,
                    $"'{value}' is not a valid value of '/{s.Name}' for command '{command.Name}'.",
                    $"Valid values: {string.Join(", ", s.Values)}. See '{toolName} help {command.Name}'.");
            }
        }

        /// <summary>Replaces `@file` by the lines of the file (UTF-8, one argument per line, blank lines skipped).</summary>
        internal static string[] ExpandArgumentFiles(string[] args)
        {
            if (!args.Any(a => a.Length > 1 && a[0] == '@'))
            {
                return args;
            }

            var result = new List<string>();
            foreach (string arg in args)
            {
                if (arg.Length > 1 && arg[0] == '@')
                {
                    string path = arg.Substring(1);
                    if (!File.Exists(path))
                    {
                        throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError,
                            $"Argument file '{path}' does not exist.",
                            "Pass '@<file>' with a path relative to the current directory; the file lists one argument per line.", item: path);
                    }

                    foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.Length > 0)
                        {
                            result.Add(trimmed);
                        }
                    }
                }
                else
                {
                    result.Add(arg);
                }
            }

            return result.ToArray();
        }

        private static bool IsSwitch(string arg)
        {
            if (arg.Length < 2)
            {
                return false;
            }

            if (arg[0] != '/' && arg[0] != '-')
            {
                return false;
            }

            // "/" followed by a path separator or a server path is not a switch: "$/Project" never starts with '/' anyway,
            // and "-" followed by a digit could be a negative number (not used by tf.exe).
            return char.IsLetter(arg[1]) || arg[1] == '?';
        }

        /// <summary>
        /// Git Bash (MSYS) turns "/recursive" into "C:/Program Files/Git/recursive". Report that as TFP0002 instead of
        /// treating it as a path, but only when the prefix is a Git for Windows installation (SPEC.md, "Ochrana proti převodu cest").
        /// </summary>
        internal static void DetectMsysConvertedSwitch(string arg, HashSet<string> knownSwitches)
        {
            if (Environment.GetEnvironmentVariable("MSYSTEM") == null && Environment.GetEnvironmentVariable("EXEPATH") == null)
            {
                return;
            }

            var match = MsysConvertedPath.Match(arg);
            if (!match.Success)
            {
                return;
            }

            string root = match.Groups["root"].Value;
            string name = match.Groups["name"].Value;
            if (!knownSwitches.Contains(name))
            {
                return;
            }

            string exePath = Environment.GetEnvironmentVariable("EXEPATH") ?? string.Empty;
            bool looksLikeGitRoot = root.IndexOf("git", StringComparison.OrdinalIgnoreCase) >= 0
                || (exePath.Length > 0 && root.Replace('/', '\\').StartsWith(exePath.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase));
            if (!looksLikeGitRoot || !Directory.Exists(root.TrimEnd('/')))
            {
                return;
            }

            throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError,
                $"Argument '{arg}' looks like the switch '/{name}' converted to a path by Git Bash (MSYS).",
                $"Write switches with a dash in Git Bash ('-{name}'), or set MSYS_NO_PATHCONV=1.");
        }
    }
}
