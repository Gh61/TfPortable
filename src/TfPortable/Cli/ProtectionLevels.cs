using System;
using System.Collections.Generic;
using System.Linq;
using TfPortable.Errors;

namespace TfPortable.Cli
{
    /// <summary>
    /// The protection level every command and some switches require (SPEC.md "Úrovně ochrany"): 1 reads only, 2 is the
    /// everyday work in the workspace and on the server, 3 is everything including irreversible and foreign changes.
    /// The level the user allows lives in <see cref="Settings.ToolSettings"/>; a command above it is TFP0004.
    /// </summary>
    internal static class ProtectionLevels
    {
        public const int ReadOnly = 1;
        public const int Work = 2;
        public const int Everything = 3;

        private static readonly Dictionary<string, int> Commands = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // Level 1: nothing changes, apart from `view /output` writing a local file and the local PAT store.
            ["status"] = ReadOnly, ["history"] = ReadOnly, ["changeset"] = ReadOnly, ["view"] = ReadOnly, ["difference"] = ReadOnly,
            ["dir"] = ReadOnly, ["properties"] = ReadOnly, ["localversions"] = ReadOnly, ["workfold"] = ReadOnly, ["workspaces"] = ReadOnly,
            ["labels"] = ReadOnly, ["branches"] = ReadOnly, ["merges"] = ReadOnly, ["annotate"] = ReadOnly, ["folderdiff"] = ReadOnly,
            ["shelvesets"] = ReadOnly, ["help"] = ReadOnly, ["version"] = ReadOnly, ["whoami"] = ReadOnly, ["login"] = ReadOnly,
            ["logout"] = ReadOnly, ["protection"] = ReadOnly, ["msdn"] = ReadOnly, ["resolvepath"] = ReadOnly,
            // Level 2: pending changes, the workspace on disk and the everyday writes to the server.
            ["add"] = Work, ["checkout"] = Work, ["delete"] = Work, ["rename"] = Work, ["undo"] = Work, ["get"] = Work,
            ["resolve"] = Work, ["reconcile"] = Work, ["undelete"] = Work, ["merge"] = Work, ["branch"] = Work, ["rollback"] = Work,
            ["unshelve"] = Work, ["checkin"] = Work, ["shelve"] = Work, ["label"] = Work, ["unlabel"] = Work, ["lock"] = Work,
            // Level 3: irreversible or administrative.
            ["destroy"] = Everything, ["permission"] = Everything, ["workspace"] = Everything, ["proxy"] = Everything,
            ["configure"] = Everything, ["property"] = Everything, ["certificates"] = Everything,
        };

        /// <summary>Switches that need more than their command: `command/switch` → level.</summary>
        private static readonly Dictionary<string, int> Switches = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["undo/workspace"] = Everything,
            ["label/delete"] = Everything,
            ["shelve/delete"] = Everything,
            ["workfold/map"] = Work,
            ["workfold/unmap"] = Work,
            ["workfold/cloak"] = Work,
            ["workfold/decloak"] = Work,
        };

        public static string Name(int level) => level == ReadOnly ? "read-only" : level == Work ? "everyday work" : "everything";

        /// <summary>The level of a command by its canonical name; every catalog command has one (unit test).</summary>
        public static int Of(string command)
        {
            if (!Commands.TryGetValue(command, out int level))
            {
                throw new InvalidOperationException($"The command '{command}' has no protection level.");
            }

            return level;
        }

        /// <summary>The level a parsed command line needs and the switch that raised it, if any.</summary>
        public static (int Level, string? Switch) Required(CommandSpec spec, CommandLine commandLine)
        {
            int level = Of(spec.Name);
            string? raisedBy = null;

            // `merge /candidate` only lists changesets; the merge itself (phase 2) pends changes.
            if (string.Equals(spec.Name, "merge", StringComparison.OrdinalIgnoreCase) && commandLine.Has("candidate"))
            {
                level = ReadOnly;
            }

            foreach (string name in commandLine.SwitchNames)
            {
                if (Switches.TryGetValue(spec.Name + "/" + name, out int switchLevel) && switchLevel > level)
                {
                    level = switchLevel;
                    raisedBy = name;
                }
            }

            return (level, raisedBy);
        }

        /// <summary>TFP0004 for a command or switch above the current level (SPEC.md wording).</summary>
        public static TfpException Blocked(string command, string? switchName, int required, int current, string toolName)
        {
            string what = switchName == null ? $"Command '{command}'" : $"Switch '/{switchName}' of '{command}'";
            return new TfpException(ErrorCodes.ProtectionLevel, ExitCodes.GeneralError,
                $"{what} needs protection level {required}. The current level is {current}.",
                $"Raise it with '{toolName} protection set {required}' in an interactive terminal.");
        }

        /// <summary>Names of every command the map knows (for the catalog test).</summary>
        public static IEnumerable<string> KnownCommands => Commands.Keys;
    }
}
