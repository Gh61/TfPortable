using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.TeamFoundation.VersionControl.Client;
using Newtonsoft.Json.Linq;
using TfPortable.Cli;
using TfPortable.Context;
using TfPortable.Errors;
using TfPortable.Output;

namespace TfPortable.Commands
{
    /// <summary>
    /// `tfp difference itemspec [itemspec2] [/version:spec[~spec]] [/format:unified|brief] [/ignorespace] [/ignoreeol]
    /// [/ignorecase] [/recursive] [/shelveset:name[;owner]] [/encoding:x]` (alias `diff`). Compares the workspace with
    /// the server, two versions of a server item, two items, or a shelveset with the server (docs/compat.md §12).
    /// Exit 0 even when the files differ, as the original.
    /// </summary>
    internal sealed class DifferenceCommand : ICommand
    {
        public const string Rule = "===================================================================";

        public string Name => "difference";

        private sealed class Side
        {
            public Side(string kind, string path, int? version, string? text, bool binary, string label, string? encoding = null)
            {
                Kind = kind;
                Path = path;
                Version = version;
                Text = text;
                Binary = binary;
                Label = label;
                Encoding = encoding;
            }

            /// <summary>The server encoding name of the item, so that the local side is decoded the same way.</summary>
            public string? Encoding { get; }

            public string Kind { get; }

            public string Path { get; }

            public int? Version { get; }

            public string? Text { get; }

            public bool Binary { get; }

            public string Label { get; }

            public JObject ToJson() => new JObject { ["kind"] = Kind, ["path"] = Path, ["version"] = Version };
        }

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            bool brief = string.Equals(commandLine.Value("format"), "brief", StringComparison.OrdinalIgnoreCase);
            var options = DiffOptions.None;
            if (commandLine.Has("ignorespace")) { options |= DiffOptions.IgnoreSpace; }
            if (commandLine.Has("ignoreeol")) { options |= DiffOptions.IgnoreEol; }
            if (commandLine.Has("ignorecase")) { options |= DiffOptions.IgnoreCase; }
            string? shelveset = commandLine.Value("shelveset");

            if (commandLine.Positionals.Count > 2 || (commandLine.Positionals.Count == 0 && shelveset == null))
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "difference needs one itemspec (local against server, or /version:a~b), two itemspecs, or /shelveset.", $"Example: '{toolName} diff Main\\src\\App.cs'.");
            }

            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
            var workspace = context.Workspace?.Summary;
            var items = commandLine.Positionals.Select(p => ItemSpecParser.Parse(p, workspace?.Name, workspace?.Owner)).ToList();
            VersionRange? range = null;
            string? versionText = commandLine.Value("version");
            if (!string.IsNullOrEmpty(versionText))
            {
                range = VersionSpecParser.ParseRange(versionText!, workspace?.Name, workspace?.Owner);
            }

            var pairs = new List<(string Path, string JsonPath, Side Left, Side Right, string? Change)>();
            if (shelveset != null)
            {
                pairs.AddRange(ShelvesetPairs(context, shelveset, items, toolName));
            }
            else if (items.Count == 2)
            {
                // A single /version applies to both sides; a range gives each side its own version.
                var left = Load(context, items[0], (range != null && !range.IsRange ? range.To : range?.From) ?? items[0].Version, "left", toolName);
                var right = Load(context, items[1], range?.To ?? items[1].Version, "right", toolName);
                string shown = items[1].IsServerPath ? context.Workspace?.GetLocalItemForServerItem(items[1].Path) ?? items[1].Path : items[1].Path;
                pairs.Add((shown, shown, left, right, null));
            }
            else if (range != null && range.IsRange)
            {
                string path = context.ServerPathOf(items[0], toolName);
                foreach (string file in Expand(context, path, range.To, commandLine.Has("recursive")))
                {
                    // The original names the file by its mapped local path when the workspace knows it.
                    string shown = context.Workspace?.GetLocalItemForServerItem(file) ?? file;
                    pairs.Add((shown, shown, Server(context, file, range.From!, 0, true), Server(context, file, range.To, 0, true), null));
                }
            }
            else
            {
                // Local against the server: every pending edit under the itemspec, or the item itself.
                pairs.AddRange(LocalPairs(context, items[0], range?.To, commandLine.Has("recursive"), toolName));
            }

            var files = new JArray();
            foreach (var pair in pairs)
            {
                string status;
                string? diff = null;
                if (pair.Left.Binary || pair.Right.Binary)
                {
                    status = "binary";
                    if (pair.Change != null)
                    {
                        output.Line(pair.Change);
                    }

                    output.Line($"{pair.Path}: binary files differ");
                }
                else if (UnifiedDiff.AreEqual(pair.Left.Text ?? string.Empty, pair.Right.Text ?? string.Empty, options))
                {
                    status = "identical";
                    if (pair.Change == null && (pair.Left.Kind == "local" || pair.Right.Kind == "local"))
                    {
                        output.Line($"Comparing local with latest: {pair.Path}");
                    }
                    else if (brief)
                    {
                        output.Line($"{pair.Path}: files are identical");
                    }
                }
                else
                {
                    status = "different";
                    if (brief)
                    {
                        output.Line($"{pair.Path}: files differ");
                    }
                    else
                    {
                        var lines = UnifiedDiff.Render(pair.Left.Text ?? string.Empty, pair.Right.Text ?? string.Empty, pair.Left.Label, pair.Right.Label, options);
                        diff = string.Join("\n", lines);
                        if (pair.Change != null)
                        {
                            output.Line(pair.Change);
                        }

                        output.Line($"File: {pair.Path}");
                        output.Line(Rule);
                        output.Lines(lines);
                        output.Line(Rule);
                    }
                }

                files.Add(new JObject
                {
                    ["path"] = pair.JsonPath,
                    ["source"] = pair.Left.ToJson(),
                    ["target"] = pair.Right.ToJson(),
                    ["status"] = status,
                    ["diff"] = diff,
                });
            }

            output.Success(new JObject { ["files"] = files, ["format"] = brief ? "brief" : "unified" });
            return ExitCodes.Success;
        }

        private static IEnumerable<string> Expand(WorkspaceContext context, string serverPath, VersionSpec version, bool recursive)
        {
            var items = context.Client.GetItems(new ItemQuery(serverPath, recursive ? RecursionType.Full : RecursionType.None), version, false, false);
            return items.Where(i => !i.IsFolder).Select(i => i.ServerPath);
        }

        private static Side Server(WorkspaceContext context, string serverPath, VersionSpec version, int deletionId, bool shortLabel)
        {
            var content = context.Client.Download(serverPath, version, deletionId);
            string? text = TextContent.Decode(content.Bytes, content.Item.Encoding);
            string name = shortLabel ? Microsoft.TeamFoundation.VersionControl.Common.VersionControlPath.GetFileName(serverPath) : serverPath;
            return new Side("server", serverPath, content.Item.ChangesetId, text, text == null, $"Server: {name};{(shortLabel ? string.Empty : "C")}{content.Item.ChangesetId}", content.Item.Encoding);
        }

        /// <summary>The local file decoded with the item's server encoding (a BOM wins), so that both sides compare alike.</summary>
        private static Side Local(string localPath, string? encoding)
        {
            string? text = TextContent.ReadLocal(localPath, encoding);
            return new Side("local", localPath, null, text, text == null, $"Local: {localPath}", encoding);
        }

        private static Side Load(WorkspaceContext context, ParsedItemSpec item, VersionSpec? version, string what, string toolName)
        {
            if (item.IsServerPath || version != null)
            {
                string path = context.ServerPathOf(item, toolName);
                return Server(context, path, version ?? VersionSpec.Latest, item.DeletionId, false);
            }

            if (!File.Exists(item.Path))
            {
                throw new TfpException(ErrorCodes.ItemNotFound, ExitCodes.PartialSuccess, $"{item.Path}: no such local file ({what} side).", "Pass an existing local file or a server path.", item: item.Path);
            }

            return Local(item.Path, null);
        }

        private IEnumerable<(string Path, string JsonPath, Side Left, Side Right, string? Change)> LocalPairs(WorkspaceContext context, ParsedItemSpec item, VersionSpec? version, bool recursive, string toolName)
        {
            var workspace = context.RequireWorkspace(toolName);
            string serverPath = context.ServerPathOf(item, toolName);
            string? localPath = item.IsServerPath ? workspace.GetLocalItemForServerItem(serverPath) : item.Path;
            if (localPath == null)
            {
                throw new TfpException(ErrorCodes.PathNotMapped, ExitCodes.WorkspaceNotFound, $"{serverPath} is not mapped in workspace {workspace.Summary.Spec}.", $"'{toolName} workfold' shows the mappings.", item: serverPath);
            }

            var recursion = recursive ? RecursionType.Full : (Directory.Exists(localPath) ? RecursionType.OneLevel : RecursionType.None);
            var pending = workspace.GetPendingChanges(new[] { new ItemQuery(localPath, recursion) }, true);
            var edits = pending.Pending.Concat(pending.Candidates).Where(c => !c.IsFolder && c.LocalPath != null && (c.ChangeTypes.Contains("edit") || c.ChangeTypes.Contains("add") || c.ChangeTypes.Contains("delete"))).ToList();

            if (edits.Count == 0 && File.Exists(localPath))
            {
                // No pending change: compare the file with the latest (or the given) server version anyway.
                var server = Server(context, serverPath, version ?? VersionSpec.Latest, 0, false);
                var local = Local(localPath, server.Encoding);
                yield return (localPath, localPath, server, local, null);
                yield break;
            }

            foreach (var edit in edits)
            {
                Side left;
                if (edit.ChangeTypes.Contains("add"))
                {
                    left = new Side("server", edit.ServerPath, null, string.Empty, false, "[no source file]");
                }
                else
                {
                    var content = context.Client.Download(edit.SourceServerPath ?? edit.ServerPath, version ?? new ChangesetVersionSpec(edit.Version), 0);
                    string? text = TextContent.Decode(content.Bytes, content.Item.Encoding);
                    left = new Side("server", edit.ServerPath, content.Item.ChangesetId, text, text == null, $"Server: {edit.ServerPath};C{content.Item.ChangesetId}", content.Item.Encoding);
                }

                Side right = File.Exists(edit.LocalPath)
                    ? Local(edit.LocalPath!, left.Encoding ?? edit.Encoding)
                    : new Side("local", edit.LocalPath!, null, string.Empty, false, "[no target file]");
                yield return (edit.LocalPath!, edit.LocalPath!, left, right, $"{edit.ChangeText}: {edit.LocalPath}");
            }
        }

        private IEnumerable<(string Path, string JsonPath, Side Left, Side Right, string? Change)> ShelvesetPairs(WorkspaceContext context, string shelveset, List<ParsedItemSpec> items, string toolName)
        {
            string[] parts = shelveset.Split(new[] { ';' }, 2);
            var queries = items.Select(i => new ItemQuery(context.ServerPathOf(i, toolName), RecursionType.Full)).ToList();
            var sets = context.Client.QueryShelvedChanges(parts[0], parts.Length > 1 ? parts[1] : null, queries);
            foreach (var change in sets.SelectMany(s => s.Changes).Where(c => !c.IsFolder))
            {
                var shelved = context.Client.DownloadShelved(parts[0], parts.Length > 1 ? parts[1] : null, change.ServerPath);
                string? shelvedText = shelved == null ? null : TextContent.Decode(shelved.Bytes, shelved.Item.Encoding);
                var right = new Side("shelveset", change.ServerPath, change.Version, shelvedText ?? string.Empty, shelved != null && shelvedText == null, $"Shelved change: {change.ServerPath};{parts[0]}");
                Side left;
                if (change.ChangeTypes.Contains("add"))
                {
                    left = new Side("server", change.ServerPath, null, string.Empty, false, "[no source file]");
                }
                else
                {
                    var content = context.Client.Download(change.SourceServerPath ?? change.ServerPath, new ChangesetVersionSpec(change.Version), 0);
                    string? text = TextContent.Decode(content.Bytes, content.Item.Encoding);
                    left = new Side("server", change.ServerPath, content.Item.ChangesetId, text, text == null, $"Server: {change.ServerPath};C{content.Item.ChangesetId}");
                }

                // The original prints `<change>: $/path;C<n>` and then `File: <name>` (golden difference/shelveset).
                string label = change.Version > 0 ? $"{change.ServerPath};C{change.Version}" : change.ServerPath;
                yield return (Microsoft.TeamFoundation.VersionControl.Common.VersionControlPath.GetFileName(change.ServerPath), change.ServerPath, left, right, $"{change.ChangeText}: {label}");
            }
        }
    }
}
