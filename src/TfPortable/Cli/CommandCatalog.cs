using System;
using System.Collections.Generic;
using System.Linq;

namespace TfPortable.Cli
{
    /// <summary>Whether a switch takes a value after ':'.</summary>
    internal enum SwitchValue
    {
        None,
        Optional,
        Required,
    }

    /// <summary>Where a command stands in the plan (SPEC.md "Katalog příkazů", docs/commands.md).</summary>
    internal enum CommandStatus
    {
        /// <summary>Runs in this build.</summary>
        Implemented,

        /// <summary>In scope (phase 1 or 2) but not implemented yet.</summary>
        Planned,

        /// <summary>Recognized tf.exe command that TfPortable refuses (TFP0003).</summary>
        OutOfScope,
    }

    /// <summary>One switch of a command: name, short form, value rule and the values it accepts.</summary>
    internal sealed class SwitchSpec
    {
        public SwitchSpec(string name, string? shortName, SwitchValue value, string summary, string[]? values = null, bool extension = false, bool secret = false)
        {
            Name = name;
            ShortName = shortName;
            Value = value;
            Summary = summary;
            Values = values ?? Array.Empty<string>();
            Extension = extension;
            Secret = secret;
        }

        /// <summary>Canonical lower-case name without the leading '/'.</summary>
        public string Name { get; }

        /// <summary>Single-letter short form (`R` for /recursive), or null.</summary>
        public string? ShortName { get; }

        public SwitchValue Value { get; }

        /// <summary>Accepted values (case-insensitive) when the value is an enumeration; empty when free text.</summary>
        public string[] Values { get; }

        public string Summary { get; }

        /// <summary>TfPortable extension, not in tf.exe.</summary>
        public bool Extension { get; }

        /// <summary>The value may be a credential (`/login`, `/pat`); error messages never echo it.</summary>
        public bool Secret { get; }

        /// <summary>`/name`, `/name:<value>` or `/name:a|b` for usage lines.</summary>
        public string Usage
        {
            get
            {
                if (Value == SwitchValue.None)
                {
                    return "/" + Name;
                }

                string value = Values.Length > 0 ? string.Join("|", Values) : "<value>";
                return Value == SwitchValue.Optional ? $"/{Name}[:{value}]" : $"/{Name}:{value}";
            }
        }

        public bool Accepts(string value) => Values.Length == 0 || Values.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>One command of the catalog with its aliases, phase, status and switches.</summary>
    internal sealed class CommandSpec
    {
        public CommandSpec(string name, string[] aliases, int? phase, CommandStatus status, string summary, string usage, params SwitchSpec[] switches)
        {
            Name = name;
            Aliases = aliases;
            Phase = phase;
            Status = status;
            Summary = summary;
            Usage = usage;
            Switches = switches;
        }

        /// <summary>Canonical lower-case name (the JSON "command" field).</summary>
        public string Name { get; }

        public string[] Aliases { get; }

        /// <summary>1 or 2 for tf.exe commands, null for the tool's own commands (login, help, ...) and for out-of-scope commands.</summary>
        public int? Phase { get; }

        public CommandStatus Status { get; }

        public string Summary { get; }

        /// <summary>Positional part of the usage line, e.g. "[itemspec ...]".</summary>
        public string Usage { get; }

        /// <summary>Command-specific switches; the global ones are added by <see cref="CommandCatalog.SwitchesOf"/>.</summary>
        public SwitchSpec[] Switches { get; }

        public bool Matches(string name) =>
            string.Equals(Name, name, StringComparison.OrdinalIgnoreCase)
            || Aliases.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Every command tf.exe 18.10 knows (docs/commands.md, from the captured help) plus the tool's own commands,
    /// with the switches TfPortable accepts. Switches that are not listed are unknown (TFP0002), as in tf.exe.
    /// </summary>
    internal static class CommandCatalog
    {
        public const string Json = "json";

        /// <summary>Switches every command accepts (SPEC.md "Globální přepínače").</summary>
        public static readonly SwitchSpec[] GlobalSwitches =
        {
            new SwitchSpec("collection", "S", SwitchValue.Required, "Collection URL; overrides the workspace detection. Alias /server."),
            new SwitchSpec("workspace", "W", SwitchValue.Required, "Workspace name[;owner]; overrides the workspace detection."),
            new SwitchSpec("login", "Y", SwitchValue.Required, "user,pat: PAT for this run only (tf.exe compatibility; prefer 'login').", secret: true),
            new SwitchSpec("noprompt", "I", SwitchValue.None, "Accepted; the tool never prompts without an interactive terminal anyway."),
            new SwitchSpec("format", "F", SwitchValue.Required, "Output format; 'json' gives one JSON document.", new[] { Json }),
            new SwitchSpec("verbose", null, SwitchValue.None, "Diagnostics on stderr (never request headers).", extension: true),
            new SwitchSpec("help", "?", SwitchValue.None, "Help of the command (also -H)."),
        };

        private static readonly SwitchSpec Recursive = new SwitchSpec("recursive", "R", SwitchValue.None, "Include subfolders.");
        private static readonly SwitchSpec Version = new SwitchSpec("version", "V", SwitchValue.Required, "Versionspec: C<n>, <n>, T, L<label>[@scope], D<date>, W[name[;owner]]; ranges a~b.");
        private static readonly SwitchSpec Preview = new SwitchSpec("preview", null, SwitchValue.None, "Show what would be done without doing it.");
        private static readonly SwitchSpec Lock = new SwitchSpec("lock", "K", SwitchValue.Required, "Lock level; only 'none' is accepted (locks write to the server).", new[] { "none", "checkin", "checkout" });
        private static readonly SwitchSpec Encoding = new SwitchSpec("encoding", null, SwitchValue.Required, "File encoding, e.g. utf-8, windows-1250, binary.");
        private static readonly SwitchSpec Owner = new SwitchSpec("owner", "O", SwitchValue.Required, "Owner filter (* for everyone).");
        private static readonly SwitchSpec Shelveset = new SwitchSpec("shelveset", null, SwitchValue.Required, "Shelveset name[;owner].");
        private static readonly SwitchSpec NoAutoResolve = new SwitchSpec("noautoresolve", null, SwitchValue.None, "Do not auto-resolve trivial conflicts.");

        private static SwitchSpec Format(params string[] values) =>
            new SwitchSpec("format", "F", SwitchValue.Required, "Output format.", values.Concat(new[] { Json }).ToArray());

        public static readonly CommandSpec[] Commands =
        {
            // Tool commands (SPEC.md "Autentizace", "Rozšíření pro AI").
            new CommandSpec("login", Array.Empty<string>(), null, CommandStatus.Implemented,
                "Ask for a Personal Access Token, verify it and store it in the Windows Credential Manager.", "",
                new SwitchSpec("pat", null, SwitchValue.Required, "'-' reads the token from standard input instead of the hidden prompt; never pass the token itself.", new[] { "-" }, extension: true, secret: true)),
            new CommandSpec("logout", Array.Empty<string>(), null, CommandStatus.Implemented,
                "Remove the stored Personal Access Token of a collection.", ""),
            new CommandSpec("whoami", Array.Empty<string>(), null, CommandStatus.Implemented,
                "Verify the connection and print identity, collection, PAT date, workspace and mappings.", ""),
            new CommandSpec("help", Array.Empty<string>(), null, CommandStatus.Implemented,
                "List the commands, or the switches of one command.", "[command]"),
            new CommandSpec("version", Array.Empty<string>(), null, CommandStatus.Implemented,
                "Print the TfPortable and client library versions.", ""),

            // Phase 1: workspace commands (milestone 3).
            new CommandSpec("status", new[] { "stat" }, 1, CommandStatus.Implemented,
                "Pending changes of the workspace and, in a local workspace, detected changes.", "[itemspec ...]",
                Recursive, Format("brief", "detailed"),
                new SwitchSpec("user", "U", SwitchValue.Required, "Owner of the pending changes (* for everyone)."),
                Shelveset,
                new SwitchSpec("nodetect", null, SwitchValue.None, "Skip the detection of local changes.")),
            new CommandSpec("add", Array.Empty<string>(), 1, CommandStatus.Implemented,
                "Pend new files and folders for addition.", "itemspec ...",
                Recursive, Lock, Encoding,
                new SwitchSpec("noignore", null, SwitchValue.None, "Ignore .tfignore files.")),
            new CommandSpec("checkout", new[] { "edit", "co" }, 1, CommandStatus.Implemented,
                "Make files writable and pend them for edit.", "itemspec ...",
                Recursive, Lock, Encoding),
            new CommandSpec("delete", new[] { "del", "rm" }, 1, CommandStatus.Implemented,
                "Pend files and folders for deletion and remove them from disk.", "itemspec ...",
                Recursive, Lock,
                new SwitchSpec("detect", null, SwitchValue.None, "Pend deletes for files already missing from disk.")),
            new CommandSpec("rename", new[] { "move", "ren" }, 1, CommandStatus.Implemented,
                "Rename or move a file or folder.", "olditem newitem",
                Lock),
            new CommandSpec("undo", Array.Empty<string>(), 1, CommandStatus.Implemented,
                "Undo pending changes.", "itemspec ...",
                Recursive),
            new CommandSpec("get", Array.Empty<string>(), 1, CommandStatus.Implemented,
                "Download the given version into the workspace.", "[itemspec ...]",
                Version,
                new SwitchSpec("all", null, SwitchValue.None, "Get every file, even those the server thinks are current."),
                new SwitchSpec("overwrite", null, SwitchValue.None, "Overwrite writable files that have no pending change."),
                new SwitchSpec("force", "P", SwitchValue.None, "/all and /overwrite together."),
                new SwitchSpec("remap", null, SwitchValue.None, "Update local versions after a mapping change without downloading."),
                Recursive, Preview, NoAutoResolve),
            new CommandSpec("resolve", Array.Empty<string>(), 1, CommandStatus.Implemented,
                "List or resolve conflicts.", "[itemspec ...]",
                new SwitchSpec("auto", null, SwitchValue.Required, "Resolution for all conflicts.",
                    new[] { "AutoMerge", "AutoMergeForced", "TakeTheirs", "KeepYours", "OverwriteLocal", "DeleteConflict", "KeepYoursRenameTheirs", "AcceptMerge", "AcceptYours", "AcceptTheirs", "AcceptYoursRenameTheirs" }),
                Preview, Recursive,
                new SwitchSpec("newname", "N", SwitchValue.Required, "New path for KeepYoursRenameTheirs.")),
            new CommandSpec("reconcile", Array.Empty<string>(), 1, CommandStatus.Implemented,
                "Compare the disk with the workspace: promote local changes or clean untracked files.", "[itemspec ...]",
                new SwitchSpec("clean", null, SwitchValue.None, "Delete files that are not under version control (needs /noprompt without /preview)."),
                new SwitchSpec("promote", null, SwitchValue.None, "Pend detected adds and deletes."),
                new SwitchSpec("adds", null, SwitchValue.None, "With /promote: adds only."),
                new SwitchSpec("deletes", null, SwitchValue.None, "With /promote: deletes only."),
                new SwitchSpec("diff", null, SwitchValue.None, "Compare file contents, not only timestamps."),
                Preview, Recursive,
                new SwitchSpec("noignore", null, SwitchValue.None, "Ignore .tfignore files."),
                new SwitchSpec("ignore", null, SwitchValue.None, "With /clean: respect .tfignore."),
                new SwitchSpec("unmapped", null, SwitchValue.None, "With /clean: include unmapped folders."),
                new SwitchSpec("exclude", null, SwitchValue.Required, "Comma-separated patterns to skip.")),
            new CommandSpec("localversions", new[] { "lv" }, 1, CommandStatus.Implemented,
                "Versions of the items in the workspace.", "itemspec ...",
                Recursive, Format("brief", "detailed")),
            new CommandSpec("workfold", Array.Empty<string>(), 1, CommandStatus.Implemented,
                "Mappings of the workspace (listing only; /map, /unmap, /cloak, /decloak are phase 2).", "[localfolder | serverfolder]",
                new SwitchSpec("map", null, SwitchValue.None, "Map serverfolder to localfolder (phase 2, TFP0003 in this build)."),
                new SwitchSpec("unmap", null, SwitchValue.None, "Remove a mapping (phase 2, TFP0003 in this build)."),
                new SwitchSpec("cloak", null, SwitchValue.None, "Cloak a server folder (phase 2, TFP0003 in this build)."),
                new SwitchSpec("decloak", null, SwitchValue.None, "Remove a cloak (phase 2, TFP0003 in this build).")),
            new CommandSpec("workspaces", Array.Empty<string>(), 1, CommandStatus.Implemented,
                "List workspaces of the collection.", "[workspacename]",
                Owner,
                new SwitchSpec("computer", "M", SwitchValue.Required, "Computer filter (* for every computer)."),
                Format("brief", "detailed"),
                new SwitchSpec("remove", null, SwitchValue.Required, "Remove workspaces from the local cache (* or a comma-separated list).")),

            // Phase 1: history and content (milestone 4).
            new CommandSpec("history", new[] { "hist" }, 1, CommandStatus.Implemented,
                "Changesets of an item; at most 256 without /stopafter.", "itemspec",
                Version,
                new SwitchSpec("stopafter", null, SwitchValue.Required, "Maximum number of changesets."),
                Recursive,
                new SwitchSpec("user", "U", SwitchValue.Required, "Only changesets of this user."),
                Format("brief", "detailed"),
                new SwitchSpec("slotmode", "X", SwitchValue.None, "Follow the path (default)."),
                new SwitchSpec("itemmode", null, SwitchValue.None, "Follow the item across renames."),
                new SwitchSpec("sort", null, SwitchValue.Required, "Order of the changesets.", new[] { "ascending", "descending" }),
                new SwitchSpec("comment", null, SwitchValue.Required, "Only changesets whose comment contains the text, or matches /regex/.", extension: true),
                new SwitchSpec("changetype", null, SwitchValue.Required, "Only changesets with these change types (comma-separated).", extension: true),
                new SwitchSpec("workitem", null, SwitchValue.Required, "Only changesets linked to the work item.", extension: true),
                new SwitchSpec("files", null, SwitchValue.None, "List the changed files in the brief output.", extension: true)),
            new CommandSpec("changeset", Array.Empty<string>(), 1, CommandStatus.Implemented,
                "Details of a changeset: owner, date, comment, items, work items.", "[changesetnumber]",
                new SwitchSpec("latest", null, SwitchValue.None, "The newest changeset."),
                new SwitchSpec("diff", null, SwitchValue.None, "Unified diff of the whole changeset.", extension: true),
                new SwitchSpec("comment", null, SwitchValue.Required, "Not supported: edits the changeset on the server (TFP0003)."),
                new SwitchSpec("notes", null, SwitchValue.Required, "Not supported: edits the changeset on the server (TFP0003).")),
            new CommandSpec("view", Array.Empty<string>(), 1, CommandStatus.Implemented,
                "Print the content of a file at a version (UTF-8); binary files need /output.", "itemspec",
                Version,
                new SwitchSpec("output", null, SwitchValue.Required, "Write the content to this local file instead of the console."),
                Shelveset,
                new SwitchSpec("console", null, SwitchValue.None, "Accepted for compatibility; the console is always used."),
                Recursive),
            new CommandSpec("difference", new[] { "diff" }, 1, CommandStatus.Implemented,
                "Unified diff between workspace and server, two versions, two paths, or a shelveset.", "itemspec [itemspec2]",
                Version, Encoding, Format("unified", "brief"),
                new SwitchSpec("ignorespace", null, SwitchValue.None, "Ignore whitespace."),
                new SwitchSpec("ignoreeol", null, SwitchValue.None, "Ignore line-ending differences."),
                new SwitchSpec("ignorecase", null, SwitchValue.None, "Ignore case."),
                Recursive, Shelveset),
            new CommandSpec("dir", Array.Empty<string>(), 1, CommandStatus.Implemented,
                "List server items.", "itemspec",
                Version, Recursive,
                new SwitchSpec("folders", null, SwitchValue.None, "Folders only."),
                new SwitchSpec("deleted", null, SwitchValue.None, "Include deleted items.")),
            new CommandSpec("properties", new[] { "info" }, 1, CommandStatus.Implemented,
                "Local and server information about items: versions, encoding, locks, pending changes.", "itemspec ...",
                Version, Recursive),
            new CommandSpec("labels", Array.Empty<string>(), 1, CommandStatus.Implemented,
                "List labels.", "[labelname]",
                Owner, Format("brief", "detailed")),
            new CommandSpec("branches", Array.Empty<string>(), 1, CommandStatus.Implemented,
                "Branch hierarchy of an item.", "itemspec",
                Version),
            new CommandSpec("merge", Array.Empty<string>(), 1, CommandStatus.Implemented,
                "With /candidate: changesets not yet merged from source to destination (read-only). Merging itself is phase 2.", "source destination",
                new SwitchSpec("candidate", null, SwitchValue.None, "List unmerged changesets instead of merging."),
                Recursive, Version, Format("brief", "detailed")),

            // Phase 2.
            new CommandSpec("merges", Array.Empty<string>(), 2, CommandStatus.Planned,
                "Merge history between two branches.", "[source] destination",
                Recursive, Format("brief", "detailed"),
                new SwitchSpec("showall", null, SwitchValue.None, "Include merges of deleted items.")),
            new CommandSpec("annotate", Array.Empty<string>(), 2, CommandStatus.Planned,
                "Who changed each line and in which changeset (TfPortable extension).", "itemspec",
                Version),
            new CommandSpec("folderdiff", Array.Empty<string>(), 2, CommandStatus.Planned,
                "Differences between two folders (text only).", "[sourcePath] targetPath",
                Recursive,
                new SwitchSpec("filter", null, SwitchValue.Required, "Filter patterns."),
                new SwitchSpec("filterlocalpathsonly", null, SwitchValue.None, "Apply the filter to local paths only."),
                new SwitchSpec("view", null, SwitchValue.Required, "Which items to show.", new[] { "same", "different", "sourceOnly", "targetOnly" })),
            new CommandSpec("shelvesets", Array.Empty<string>(), 2, CommandStatus.Planned,
                "List shelvesets.", "[shelvesetname]",
                Owner, Format("brief", "detailed")),
            new CommandSpec("unshelve", Array.Empty<string>(), 2, CommandStatus.Planned,
                "Restore a shelveset into the workspace (without /move).", "[shelvesetname[;owner]] [itemspec ...]",
                Recursive,
                new SwitchSpec("nomerge", null, SwitchValue.None, "Do not merge with local changes."),
                NoAutoResolve),
            new CommandSpec("undelete", Array.Empty<string>(), 2, CommandStatus.Planned,
                "Restore deleted items as pending changes.", "itemspec[;deletionId] ...",
                new SwitchSpec("noget", null, SwitchValue.None, "Do not download the restored items."),
                Lock, Recursive),
            new CommandSpec("rollback", Array.Empty<string>(), 2, CommandStatus.Planned,
                "Pend the reverse of changesets.", "[itemspec ...]",
                new SwitchSpec("changeset", null, SwitchValue.Required, "Changeset or range from~to to roll back."),
                new SwitchSpec("toversion", null, SwitchValue.Required, "Roll back to this version."),
                Recursive, Lock, Version,
                new SwitchSpec("keepmergehistory", null, SwitchValue.None, "Keep the merge history."),
                NoAutoResolve),
            new CommandSpec("branch", Array.Empty<string>(), 2, CommandStatus.Planned,
                "Pend a branch of an item.", "olditem newitem",
                Version,
                new SwitchSpec("noget", null, SwitchValue.None, "Do not download the new branch."),
                Lock,
                new SwitchSpec("silent", null, SwitchValue.None, "Less output.")),

            // Recognized but refused in this build (SPEC.md): phase 3 is the everyday write path (checkin, label, shelve),
            // phase 4 the rest of tf.exe including what changes the server irreversibly; msdn opens a browser and never fits.
            OutOfScope("checkin", "Checks pending changes into the server.", 3),
            OutOfScope("shelve", "Stores pending changes on the server as a shelveset.", 3),
            OutOfScope("lock", "Locks or unlocks items on the server.", 4),
            OutOfScope("label", "Creates or edits a label on the server.", 3),
            OutOfScope("unlabel", "Removes items from a label on the server.", 3),
            OutOfScope("destroy", "Permanently deletes items on the server.", 4),
            OutOfScope("permission", "Edits permissions on the server.", 4),
            OutOfScope("workspace", "Creates, edits or deletes workspaces.", 4),
            OutOfScope("configure", "Opens the source control settings dialog.", 4),
            OutOfScope("proxy", "Configures a version control proxy.", 4),
            OutOfScope("certificates", "Configures client certificates.", 4),
            OutOfScope("msdn", "Opens the documentation.", null),
            OutOfScope("property", "Edits item properties on the server.", 4),
            OutOfScope("resolvepath", "Translates a server path to a local path.", 4),
        };

        private static CommandSpec OutOfScope(string name, string summary, int? phase) =>
            new CommandSpec(name, Array.Empty<string>(), phase, CommandStatus.OutOfScope, summary, "");

        /// <summary>Command groups of tf.exe other than version control (`tf git ...`, `tf settings ...`).</summary>
        public static readonly string[] OtherGroups = { "git", "settings" };

        public static CommandSpec? Find(string name) => Commands.FirstOrDefault(c => c.Matches(name));

        /// <summary>The command's switches followed by the global ones; a command-specific /format replaces the global one.</summary>
        public static IEnumerable<SwitchSpec> SwitchesOf(CommandSpec command)
        {
            foreach (var s in command.Switches)
            {
                yield return s;
            }

            foreach (var g in GlobalSwitches)
            {
                if (!command.Switches.Any(s => s.Name == g.Name))
                {
                    yield return g;
                }
            }
        }

        /// <summary>Every switch name and short form any command knows; used by the MSYS converted-path detection.</summary>
        public static HashSet<string> AllSwitchNames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "server", "H" };
            foreach (var s in Commands.SelectMany(c => c.Switches).Concat(GlobalSwitches))
            {
                set.Add(s.Name);
                if (s.ShortName != null)
                {
                    set.Add(s.ShortName);
                }
            }

            return set;
        }
    }
}
