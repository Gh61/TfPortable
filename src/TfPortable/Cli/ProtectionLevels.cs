using System;
using System.Collections.Generic;
using System.Linq;
using TfPortable.Errors;

namespace TfPortable.Cli
{
    /// <summary>
    /// The protection level every command and some switches require (SPEC.md "Úrovně ochrany"): 0 reads only (nothing on
    /// disk either), 1 changes nothing on the server (the work in the own workspace that stays local until a checkin), 2 is
    /// the everyday write to the server, 3 is everything including irreversible and foreign changes.
    /// The level the user allows lives in <see cref="Settings.ToolSettings"/>; a command above it is TFP0004.
    /// </summary>
    internal static class ProtectionLevels
    {
        public const int ReadOnly = 0;
        public const int Local = 1;
        public const int Work = 2;
        public const int Everything = 3;

        private static readonly Dictionary<string, int> Commands = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // Level 0: reads only; nothing is written to the workspace or to disk (`view /output` and `property /output`
            // raise to level 1). The PAT store (`login`, `logout`) and the settings file (`protection set`) are the tool's own.
            ["status"] = ReadOnly, ["history"] = ReadOnly, ["changeset"] = ReadOnly, ["view"] = ReadOnly, ["difference"] = ReadOnly,
            ["dir"] = ReadOnly, ["properties"] = ReadOnly, ["localversions"] = ReadOnly, ["workfold"] = ReadOnly, ["workspaces"] = ReadOnly,
            ["labels"] = ReadOnly, ["branches"] = ReadOnly, ["merges"] = ReadOnly, ["annotate"] = ReadOnly, ["folderdiff"] = ReadOnly,
            ["shelvesets"] = ReadOnly, ["help"] = ReadOnly, ["version"] = ReadOnly, ["whoami"] = ReadOnly, ["login"] = ReadOnly,
            ["logout"] = ReadOnly, ["protection"] = ReadOnly, ["docs"] = ReadOnly, ["resolvepath"] = ReadOnly, ["property"] = ReadOnly,
            ["permission"] = ReadOnly, ["workspace"] = ReadOnly,
            // Level 1: nothing changes on the server. The work in the own workspace: pending changes (undone with `undo`), the
            // files on disk (`get`, `resolve`, `reconcile`) and a shelveset restored into it. A switch that writes to the server
            // (`/lock:checkin|checkout`, `unshelve /move`) raises the level.
            ["add"] = Local, ["checkout"] = Local, ["delete"] = Local, ["rename"] = Local, ["undo"] = Local, ["get"] = Local,
            ["resolve"] = Local, ["reconcile"] = Local, ["undelete"] = Local, ["merge"] = Local, ["branch"] = Local, ["rollback"] = Local,
            ["unshelve"] = Local,
            // Level 2: the everyday writes to the server.
            ["checkin"] = Work, ["shelve"] = Work, ["label"] = Work, ["unlabel"] = Work, ["lock"] = Work,
            // Level 3: irreversible or administrative (permission and workspace list at level 1; their write switches raise the level).
            ["destroy"] = Everything, ["proxy"] = Everything, ["configure"] = Everything, ["certificates"] = Everything,
        };

        /// <summary>Switches that need more than their command: `command/switch` → level.</summary>
        private static readonly Dictionary<string, int> Switches = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["undo/workspace"] = Everything,
            ["lock/workspace"] = Everything,
            ["label/delete"] = Everything,
            ["shelve/delete"] = Everything,
            ["workfold/map"] = Work,
            ["workfold/unmap"] = Work,
            ["workfold/cloak"] = Work,
            ["workfold/decloak"] = Work,
            ["changeset/comment"] = Work,
            ["changeset/notes"] = Work,
            ["unshelve/move"] = Work,
            ["view/output"] = Local,
            ["property/output"] = Local,
            ["property/setvalues"] = Local,
            ["property/deletevalues"] = Local,
            ["property/deleteall"] = Local,
            // `/lock:checkin|checkout` writes a lock to the server as `lock` does; `/lock:none` (the default) does not count.
            ["add/lock"] = Work,
            ["checkout/lock"] = Work,
            ["delete/lock"] = Work,
            ["rename/lock"] = Work,
            ["merge/lock"] = Work,
            ["branch/lock"] = Work,
            ["rollback/lock"] = Work,
            ["undelete/lock"] = Work,
            ["property/lock"] = Work,
            ["permission/allow"] = Everything,
            ["permission/deny"] = Everything,
            ["permission/remove"] = Everything,
            ["permission/inherit"] = Everything,
            ["workspace/new"] = Everything,
            ["workspace/delete"] = Everything,
            ["workspace/newname"] = Everything,
            ["workspace/comment"] = Everything,
            ["workspace/computer"] = Everything,
            ["workspace/permission"] = Everything,
            ["workspace/filetime"] = Everything,
        };

        public static string Name(int level) => level == ReadOnly ? "read-only" : level == Local ? "no server writes" : level == Work ? "everyday work" : "everything";

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

            // `merge /candidate` only lists changesets; the merge itself pends changes in the workspace.
            if (string.Equals(spec.Name, "merge", StringComparison.OrdinalIgnoreCase) && commandLine.Has("candidate"))
            {
                level = ReadOnly;
            }

            foreach (string name in commandLine.SwitchNames)
            {
                if (string.Equals(name, "lock", StringComparison.OrdinalIgnoreCase) && !string.Equals(spec.Name, "lock", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(commandLine.Value("lock"), "none", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

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
