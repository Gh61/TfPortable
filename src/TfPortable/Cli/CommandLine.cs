using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TfPortable.Errors;

namespace TfPortable.Cli
{
    /// <summary>
    /// Parsed command line in tf.exe style: `[vc] command [itemspec ...] [/switch[:value]]`.
    /// Switches start with '/' or '-', are case-insensitive, and take their value after the first ':'.
    /// Spike version: the full parser (aliases per command, @file, versionspec, itemspec) comes in milestone 2.
    /// </summary>
    internal sealed class CommandLine
    {
        private static readonly Dictionary<string, string> ShortSwitches = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["R"] = "recursive",
            ["I"] = "noprompt",
            ["V"] = "version",
            ["F"] = "format",
            ["U"] = "user",
            ["W"] = "workspace",
            ["S"] = "collection",
            ["O"] = "owner",
            ["P"] = "force",
            ["N"] = "newname",
            ["K"] = "lock",
            ["Y"] = "login",
            ["M"] = "computer",
            ["X"] = "slotmode",
            ["?"] = "help",
            ["H"] = "help",
            ["server"] = "collection",
            ["v"] = "version",
        };

        /// <summary>Switches accepted by every command (SPEC.md "Globální přepínače").</summary>
        public static readonly string[] GlobalSwitches = { "collection", "workspace", "login", "noprompt", "format", "verbose", "help" };

        private static readonly Regex MsysConvertedPath = new Regex(@"^(?<root>[A-Za-z]:/(?:[^/]+/)*?)(?<name>[A-Za-z?]+)(?::(?<value>.*))?$", RegexOptions.Compiled);

        private readonly Dictionary<string, string?> switches = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        private CommandLine(string command, List<string> positionals)
        {
            Command = command;
            Positionals = positionals;
        }

        /// <summary>Canonical lower-case command name, or empty when no command was given.</summary>
        public string Command { get; }

        public IReadOnlyList<string> Positionals { get; }

        public IEnumerable<string> SwitchNames => switches.Keys;

        public bool Has(string name) => switches.ContainsKey(name);

        public string? Value(string name) => switches.TryGetValue(name, out var value) ? value : null;

        public bool Json => string.Equals(Value("format"), "json", StringComparison.OrdinalIgnoreCase);

        public static CommandLine Parse(string[] args, IEnumerable<string>? knownSwitches = null)
        {
            var known = new HashSet<string>(ShortSwitches.Values.Concat(GlobalSwitches), StringComparer.OrdinalIgnoreCase);
            if (knownSwitches != null)
            {
                foreach (string s in knownSwitches)
                {
                    known.Add(s);
                }
            }

            string command = string.Empty;
            var positionals = new List<string>();
            var result = new CommandLine(string.Empty, positionals);
            var switchesInOrder = new List<KeyValuePair<string, string?>>();

            int index = 0;
            if (args.Length > 0 && string.Equals(args[0], "vc", StringComparison.OrdinalIgnoreCase))
            {
                index = 1;
            }

            for (; index < args.Length; index++)
            {
                string arg = args[index];
                if (IsSwitch(arg))
                {
                    string body = arg.Substring(1);
                    int colon = body.IndexOf(':');
                    string name = colon >= 0 ? body.Substring(0, colon) : body;
                    string? value = colon >= 0 ? body.Substring(colon + 1) : null;
                    if (ShortSwitches.TryGetValue(name, out var longName) && (name.Length <= 2 || name.Equals("server", StringComparison.OrdinalIgnoreCase)))
                    {
                        name = longName;
                    }

                    switchesInOrder.Add(new KeyValuePair<string, string?>(name.ToLowerInvariant(), value));
                    continue;
                }

                DetectMsysConvertedSwitch(arg, known);

                if (command.Length == 0)
                {
                    command = arg.ToLowerInvariant();
                }
                else
                {
                    positionals.Add(arg);
                }
            }

            var parsed = new CommandLine(command, positionals);
            foreach (var pair in switchesInOrder)
            {
                parsed.switches[pair.Key] = pair.Value;
            }

            return parsed;
        }

        /// <summary>Throws TFP0002 for switches the command does not accept (exit 100, as the original).</summary>
        public void Validate(string toolName, params string[] allowed)
        {
            var set = new HashSet<string>(allowed.Concat(GlobalSwitches), StringComparer.OrdinalIgnoreCase);
            foreach (string name in switches.Keys)
            {
                if (!set.Contains(name))
                {
                    throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError,
                        $"Unknown switch '/{name}' for command '{Command}'.",
                        $"Valid switches: {string.Join(", ", set.OrderBy(s => s, StringComparer.Ordinal).Select(s => "/" + s))}. See '{toolName} help {Command}'.");
                }
            }
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
        private static void DetectMsysConvertedSwitch(string arg, HashSet<string> knownSwitches)
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
            if (!knownSwitches.Contains(name) && !ShortSwitches.ContainsKey(name))
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
