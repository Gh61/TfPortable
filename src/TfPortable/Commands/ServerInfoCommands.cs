using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.TeamFoundation.VersionControl.Common;
using Newtonsoft.Json.Linq;
using TfPortable.Cli;
using TfPortable.Context;
using TfPortable.Errors;
using TfPortable.Output;

namespace TfPortable.Commands
{
    /// <summary>`tfp dir itemspec [/version:spec] [/recursive] [/folders] [/deleted]` (docs/compat.md §13): `$/path:` per folder, `$folder` entries first, trailer `Items: N`.</summary>
    internal sealed class DirCommand : ICommand
    {
        public string Name => "dir";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            if (commandLine.Positionals.Count != 1)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "dir needs exactly one itemspec.", $"Example: '{toolName} dir $/Project/Main /recursive'.");
            }

            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
            var workspace = context.Workspace?.Summary;
            var item = ItemSpecParser.Parse(commandLine.Positionals[0], workspace?.Name, workspace?.Owner);
            string path = context.ServerPathOf(item, toolName);
            VersionSpec version = item.Version ?? VersionSpec.Latest;
            string? versionText = commandLine.Value("version");
            if (!string.IsNullOrEmpty(versionText))
            {
                version = VersionSpecParser.ParseRange(versionText!, workspace?.Name, workspace?.Owner).To;
            }

            bool recursive = commandLine.Has("recursive");
            bool deleted = commandLine.Has("deleted");
            bool foldersOnly = commandLine.Has("folders");
            IReadOnlyList<ItemInfo> items;
            try
            {
                items = context.Client.GetItems(new ItemQuery(path, recursive ? RecursionType.Full : RecursionType.OneLevel, item.DeletionId), version, deleted, foldersOnly);
            }
            catch (Exception ex) when (ErrorMapper.Map(ex, toolName, context.CollectionUrl).Code == ErrorCodes.ItemNotFound)
            {
                items = Array.Empty<ItemInfo>();
            }

            // The queried folder itself heads the listing but is not counted (compat.md §13).
            var root = items.FirstOrDefault(i => string.Equals(i.ServerPath, path, StringComparison.OrdinalIgnoreCase));
            if (items.Count == 0 || (root == null && !item.HasWildcard))
            {
                output.Line($"No items match {path}.");
                output.Success(new JObject { ["folders"] = new JArray(), ["count"] = 0 }, ExitCodes.PartialSuccess);
                return ExitCodes.PartialSuccess;
            }

            // A file itemspec lists the file itself; only a queried folder heads the listing without being counted.
            var listed = items.Where(i => !(root != null && root.IsFolder && ReferenceEquals(i, root))).ToList();
            string rootFolder = root != null && root.IsFolder ? root.ServerPath : VersionControlPath.GetFolderName(path);
            output.Lines(Render(rootFolder, listed));
            output.Success(new JObject
            {
                ["folders"] = new JArray(Group(rootFolder, listed).Select(g => new JObject
                {
                    ["serverPath"] = g.Key,
                    ["items"] = new JArray(g.Select(i => i.ToJson(context.Workspace?.GetLocalItemForServerItem(i.ServerPath))).Cast<object>().ToArray()),
                }).Cast<object>().ToArray()),
                ["count"] = listed.Count,
            });
            return ExitCodes.Success;
        }

        internal static IEnumerable<string> Render(string rootFolder, IReadOnlyList<ItemInfo> items)
        {
            bool first = true;
            foreach (var group in Group(rootFolder, items))
            {
                if (!first)
                {
                    yield return string.Empty;
                }

                first = false;
                yield return group.Key + ":";
                foreach (var entry in group)
                {
                    string name = VersionControlPath.GetFileName(entry.ServerPath);
                    yield return (entry.IsFolder ? "$" : string.Empty) + name + (entry.DeletionId > 0 ? $";X{entry.DeletionId}" : string.Empty);
                }
            }

            yield return string.Empty;
            yield return $"Items: {items.Count}";
        }

        /// <summary>Items grouped by their parent folder (the root first), folders before files, names in ordinal-ignore-case order.</summary>
        internal static IEnumerable<IGrouping<string, ItemInfo>> Group(string rootFolder, IReadOnlyList<ItemInfo> items)
        {
            return items
                .GroupBy(i => VersionControlPath.GetFolderName(i.ServerPath), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => string.Equals(g.Key, rootFolder, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => (IGrouping<string, ItemInfo>)new Grouping(g.Key, g.OrderBy(i => i.IsFolder ? 0 : 1).ThenBy(i => VersionControlPath.GetFileName(i.ServerPath), StringComparer.OrdinalIgnoreCase).ToList()));
        }

        private sealed class Grouping : IGrouping<string, ItemInfo>
        {
            private readonly List<ItemInfo> items;

            public Grouping(string key, List<ItemInfo> items)
            {
                Key = key;
                this.items = items;
            }

            public string Key { get; }

            public IEnumerator<ItemInfo> GetEnumerator() => items.GetEnumerator();

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => items.GetEnumerator();
        }
    }

    /// <summary>`tfp properties itemspec ... [/version:spec] [/recursive]` (alias `info`, docs/compat.md §8): a local and a server section per item.</summary>
    internal sealed class PropertiesCommand : ICommand
    {
        public string Name => "properties";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            var items = WorkspaceCommandSupport.Items(commandLine, toolName, false, "properties Main\\src\\App.cs");
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
            var summary = context.Workspace?.Summary;
            var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.None;
            VersionSpec? version = null;
            string? versionText = commandLine.Value("version");
            if (!string.IsNullOrEmpty(versionText))
            {
                version = VersionSpecParser.ParseRange(versionText!, summary?.Name, summary?.Owner).To;
            }

            var results = new JArray();
            bool first = true;
            foreach (var item in items)
            {
                string serverPath = context.ServerPathOf(item, toolName);
                var local = context.Workspace?.GetExtendedItems(new[] { new ItemQuery(item.Path, recursion, item.DeletionId) }) ?? Array.Empty<ExtendedItemInfo>();
                IReadOnlyList<ItemInfo> server;
                try
                {
                    server = context.Client.GetItems(new ItemQuery(serverPath, recursion, item.DeletionId), version ?? (item.Version ?? VersionSpec.Latest), item.DeletionId > 0, false);
                }
                catch (Exception ex) when (ErrorMapper.Map(ex, toolName, context.CollectionUrl).Code == ErrorCodes.ItemNotFound)
                {
                    server = Array.Empty<ItemInfo>();
                }

                if (local.Count == 0 && server.Count == 0)
                {
                    output.ItemError(new TfpException(ErrorCodes.ItemNotFound, ExitCodes.PartialSuccess, $"{item.Path}: no matching items.", $"Check the path with '{toolName} dir'.", item: item.Path));
                    continue;
                }

                var paths = local.Select(l => l.ServerPath).Concat(server.Select(s => s.ServerPath)).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => VersionControlPath.GetFolderName(p), StringComparer.OrdinalIgnoreCase).ThenBy(p => p, StringComparer.OrdinalIgnoreCase);
                foreach (string path in paths)
                {
                    var l = local.FirstOrDefault(x => string.Equals(x.ServerPath, path, StringComparison.OrdinalIgnoreCase));
                    var s = server.FirstOrDefault(x => string.Equals(x.ServerPath, path, StringComparison.OrdinalIgnoreCase));
                    if (!first)
                    {
                        output.Line(string.Empty);
                    }

                    first = false;
                    output.Lines(Render(l, s));
                    results.Add(new JObject
                    {
                        ["local"] = l == null || !l.IsInWorkspace ? null : new JObject
                        {
                            ["localPath"] = l.LocalPath,
                            ["serverPath"] = l.ServerPath,
                            ["changesetId"] = l.VersionLocal,
                            ["changeTypes"] = new JArray(l.ChangeText == "none" ? Array.Empty<object>() : l.ChangeText.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries).Cast<object>().ToArray()),
                            ["isFolder"] = l.IsFolder,
                        },
                        ["server"] = s == null ? null : ServerJson(s, l),
                    });
                }
            }

            output.Success(new JObject { ["items"] = results });
            return ExitCodes.Success;
        }

        private static JObject ServerJson(ItemInfo s, ExtendedItemInfo? l)
        {
            var json = s.ToJson(l?.LocalPath);
            json["lock"] = l?.Lock;
            json["lockOwner"] = l?.LockOwner;
            return json;
        }

        internal static IEnumerable<string> Render(ExtendedItemInfo? local, ItemInfo? server)
        {
            if (local != null && local.IsInWorkspace && local.LocalPath != null)
            {
                yield return "Local information:";
                foreach (string line in TextBlocks.Fields(new[]
                {
                    new KeyValuePair<string, string?>("Local path", local.LocalPath),
                    new KeyValuePair<string, string?>("Server path", local.ServerPath),
                    new KeyValuePair<string, string?>("Changeset", local.VersionLocal.ToString()),
                    new KeyValuePair<string, string?>("Change", local.ChangeText),
                    new KeyValuePair<string, string?>("Type", local.IsFolder ? "folder" : "file"),
                }))
                {
                    yield return "  " + line;
                }
            }

            if (server != null)
            {
                yield return "Server information:";
                var fields = new List<KeyValuePair<string, string?>>
                {
                    new KeyValuePair<string, string?>("Server path", server.ServerPath),
                    new KeyValuePair<string, string?>("Changeset", server.ChangesetId.ToString()),
                    new KeyValuePair<string, string?>("Deletion ID", server.DeletionId.ToString()),
                    new KeyValuePair<string, string?>("Lock", local?.Lock),
                    new KeyValuePair<string, string?>("Lock owner", local?.LockOwner),
                    new KeyValuePair<string, string?>("Last modified", OutputWriter.IsoDate(server.LastModified)),
                    new KeyValuePair<string, string?>("Type", server.IsFolder ? "folder" : "file"),
                };
                if (!server.IsFolder)
                {
                    fields.Add(new KeyValuePair<string, string?>("File type", server.Encoding == "binary" ? "Binary" : server.Encoding));
                    fields.Add(new KeyValuePair<string, string?>("Size", server.Size?.ToString()));
                }

                foreach (string line in TextBlocks.Fields(fields))
                {
                    yield return "  " + line;
                }
            }
        }
    }

    /// <summary>`tfp labels [labelname[@scope]] [/owner:name|*] [/format:brief|detailed]` (docs/compat.md §14).</summary>
    internal sealed class LabelsCommand : ICommand
    {
        public string Name => "labels";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            bool detailed = string.Equals(commandLine.Value("format"), "detailed", StringComparison.OrdinalIgnoreCase);
            if (commandLine.Positionals.Count > 1)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "labels takes at most one label name.", $"Example: '{toolName} labels v1.0@$/Project /format:detailed'.");
            }

            string? name = null;
            string? scope = null;
            if (commandLine.Positionals.Count == 1)
            {
                string[] parts = commandLine.Positionals[0].Split(new[] { '@' }, 2);
                name = parts[0];
                scope = parts.Length > 1 ? parts[1] : null;
            }

            string? owner = commandLine.Value("owner");
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
            var labels = context.Client.QueryLabels(name, scope, owner == "*" ? null : owner, detailed);
            if (labels.Count == 0 && !string.IsNullOrEmpty(name))
            {
                // tf.exe exits 100 when a named label does not exist (golden labels/after-delete).
                throw new TfpException(ErrorCodes.ItemNotFound, ExitCodes.GeneralError, $"No label matches {name}.", $"'{toolName} labels' lists every label; '{toolName} labels <name>@<scope>' narrows by scope.", item: name);
            }

            output.Lines(detailed ? RenderDetailed(labels) : RenderBrief(labels));
            output.Success(new JObject { ["labels"] = new JArray(labels.Select(l => l.ToJson(detailed)).Cast<object>().ToArray()) });
            return ExitCodes.Success;
        }

        internal static IEnumerable<string> RenderBrief(IReadOnlyList<LabelInfo> labels)
        {
            if (labels.Count == 0)
            {
                yield return "No labels found.";
                yield break;
            }

            var table = new TextTable("Label", "Owner", "Date");
            foreach (var label in labels.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            {
                table.Add(label.Name, label.Owner, OutputWriter.IsoDate(label.Date) ?? string.Empty);
            }

            foreach (string line in table.Render())
            {
                yield return line;
            }
        }

        internal static IEnumerable<string> RenderDetailed(IReadOnlyList<LabelInfo> labels)
        {
            if (labels.Count == 0)
            {
                yield return "No labels found.";
                yield break;
            }

            bool first = true;
            foreach (var label in labels.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!first)
                {
                    yield return string.Empty;
                }

                first = false;
                foreach (string line in TextBlocks.Fields(new[]
                {
                    new KeyValuePair<string, string?>("Label", label.Name),
                    new KeyValuePair<string, string?>("Scope", label.Scope),
                    new KeyValuePair<string, string?>("Owner", label.Owner),
                    new KeyValuePair<string, string?>("Date", OutputWriter.IsoDate(label.Date)),
                    new KeyValuePair<string, string?>("Comment", label.Comment),
                }))
                {
                    yield return line;
                }

                yield return string.Empty;
                var table = new TextTable("Changeset", "Item");
                foreach (var item in (label.Items ?? Array.Empty<LabelItemInfo>()).OrderBy(i => VersionControlPath.GetFolderName(i.ServerPath), StringComparer.OrdinalIgnoreCase).ThenBy(i => i.ServerPath, StringComparer.OrdinalIgnoreCase))
                {
                    table.Add(item.ChangesetId.ToString(), item.DisplayPath);
                }

                foreach (string line in table.Render())
                {
                    yield return line;
                }
            }
        }
    }

    /// <summary>`tfp branches itemspec [/version:spec]`: `>> $/path <<` then one tab-indented line per branch (docs/compat.md §14).</summary>
    internal sealed class BranchesCommand : ICommand
    {
        public string Name => "branches";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            if (commandLine.Positionals.Count != 1)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "branches needs exactly one itemspec.", $"Example: '{toolName} branches $/Project/Main'.");
            }

            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
            var workspace = context.Workspace?.Summary;
            var item = ItemSpecParser.Parse(commandLine.Positionals[0], workspace?.Name, workspace?.Owner);
            string path = context.ServerPathOf(item, toolName);
            VersionSpec version = item.Version ?? VersionSpec.Latest;
            string? versionText = commandLine.Value("version");
            if (!string.IsNullOrEmpty(versionText))
            {
                version = VersionSpecParser.ParseRange(versionText!, workspace?.Name, workspace?.Owner).To;
            }

            var roots = context.Client.GetBranchHistory(path, version);
            if (roots.Count == 0)
            {
                // Not an error in tf.exe (golden branches/not-branched): the message and exit 0.
                output.Line($"{path} has no branch relationships.");
                output.Success(new JObject { ["root"] = path, ["branches"] = new JArray() });
                return ExitCodes.Success;
            }

            output.Lines(Render(path, roots));
            output.Success(new JObject { ["root"] = path, ["branches"] = new JArray(roots.Select(r => r.ToJson()).Cast<object>().ToArray()) });
            return ExitCodes.Success;
        }

        /// <summary>The whole branch tree; the requested item is marked `>> path <<` wherever it sits, as tf.exe does.</summary>
        internal static IEnumerable<string> Render(string requested, IReadOnlyList<BranchInfo> roots)
        {
            foreach (var root in roots)
            {
                foreach (string line in Lines(root, 0, requested))
                {
                    yield return line;
                }
            }
        }

        private static IEnumerable<string> Lines(BranchInfo node, int depth, string requested)
        {
            bool marked = node.IsRequested || string.Equals(node.ServerPath, requested, StringComparison.OrdinalIgnoreCase);
            string name = marked ? $">> {node.ServerPath} <<" : node.ServerPath;
            yield return depth == 0 ? name : $"{new string('\t', depth)}{name}\tBranched from version {node.BranchedFromVersion}";
            foreach (var child in node.Children)
            {
                foreach (string line in Lines(child, depth + 1, requested))
                {
                    yield return line;
                }
            }
        }
    }

    /// <summary>`tfp merge /candidate source destination [/recursive] [/version:spec] [/format:brief|detailed]`: unmerged changesets, `*` marks partial ones (docs/compat.md §14). Merging itself is phase 2.</summary>
    internal sealed class MergeCommand : ICommand
    {
        public string Name => "merge";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            if (commandLine.Positionals.Count != 2)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "merge needs a source and a destination.", $"Example: '{toolName} merge $/Project/Main $/Project/Release /recursive'.");
            }

            if (!commandLine.Has("candidate"))
            {
                return BranchingCommands.Merge(commandLine, output, toolName);
            }

            bool detailed = string.Equals(commandLine.Value("format"), "detailed", StringComparison.OrdinalIgnoreCase);
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
            var workspace = context.Workspace?.Summary;
            var items = commandLine.Positionals.Select(p => ItemSpecParser.Parse(p, workspace?.Name, workspace?.Owner)).ToList();
            string source = context.ServerPathOf(items[0], toolName);
            string target = context.ServerPathOf(items[1], toolName);
            var candidates = context.Client.GetMergeCandidates(source, target, commandLine.Has("recursive") ? RecursionType.Full : RecursionType.None);

            // /version limits the candidates by changeset number (C<a>~C<b>, or up to C<n>); the library has no such filter.
            string? versionText = commandLine.Value("version");
            if (!string.IsNullOrEmpty(versionText))
            {
                var range = VersionSpecParser.ParseRange(versionText!, workspace?.Name, workspace?.Owner);
                int from = range.IsRange ? ChangesetNumber(range.From!, toolName) : 1;
                int to = ChangesetNumber(range.To, toolName);
                candidates = candidates.Where(c => c.Changeset.Id >= from && c.Changeset.Id <= to).ToList();
            }

            if (detailed)
            {
                // The candidate query returns the changeset header only; the detailed block needs the items (json-schema.md).
                candidates = candidates.Select(c => new MergeCandidateInfo(context.Client.GetChangeset(c.Changeset.Id), c.Partial)).ToList();
            }

            output.Lines(Render(candidates, detailed));
            output.Success(new JObject
            {
                ["source"] = source,
                ["target"] = target,
                ["candidates"] = new JArray(candidates.Select(c =>
                {
                    var json = c.Changeset.ToJson(detailed);
                    json["partial"] = c.Partial;
                    return json;
                }).Cast<object>().ToArray()),
            });
            return ExitCodes.Success;
        }

        private static int ChangesetNumber(VersionSpec spec, string toolName)
        {
            switch (spec)
            {
                case ChangesetVersionSpec changeset: return changeset.ChangesetId;
                case LatestVersionSpec _: return int.MaxValue;
                default:
                    throw new TfpException(ErrorCodes.InvalidVersionSpec, ExitCodes.GeneralError, "merge /candidate takes changeset numbers in /version (C<n>, C<a>~C<b> or T).",
                        $"Example: '{toolName} merge /candidate $/Project/Main $/Project/Release /recursive /version:C100~C120'.");
            }
        }

        internal static IEnumerable<string> Render(IReadOnlyList<MergeCandidateInfo> candidates, bool detailed)
        {
            if (candidates.Count == 0)
            {
                yield return "There are no changes to merge.";
                yield break;
            }

            if (detailed)
            {
                foreach (var c in candidates)
                {
                    yield return TextBlocks.Separator;
                    foreach (string line in ChangesetCommand.RenderBlock(c.Changeset))
                    {
                        yield return c.Partial && line.StartsWith("Changeset: ", StringComparison.Ordinal) ? line + "*" : line;
                    }

                    yield return string.Empty;
                }

                yield break;
            }

            var table = new TextTable("Changeset", "User", "Date", "Comment");
            foreach (var c in candidates)
            {
                table.Add(c.Changeset.Id + (c.Partial ? "*" : string.Empty), c.Changeset.Owner, OutputWriter.IsoDate(c.Changeset.Date) ?? string.Empty, HistoryCommand.FirstLine(c.Changeset.Comment));
            }

            foreach (string line in table.Render())
            {
                yield return line;
            }
        }
    }
}
