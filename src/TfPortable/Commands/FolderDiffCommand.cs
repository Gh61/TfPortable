using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.TeamFoundation.VersionControl.Common;
using Newtonsoft.Json.Linq;
using TfPortable.Cli;
using TfPortable.Context;
using TfPortable.Errors;
using TfPortable.Output;

namespace TfPortable.Commands
{
    /// <summary>
    /// `tfp folderdiff [sourcePath] targetPath [/recursive] [/filter:patterns] [/view:same,different,sourceOnly,targetOnly]`:
    /// compares two server folders, a local folder with a server folder, or two local folders by the content hash of the
    /// files (docs/compat.md §15). With one path the folder is compared with its mapping in the workspace.
    /// </summary>
    internal sealed class FolderDiffCommand : ICommand
    {
        public string Name => "folderdiff";

        private sealed class Side
        {
            public Side(string root, string display, bool isServer)
            {
                Root = root;
                Display = display;
                IsServer = isServer;
            }

            public string Root { get; }

            public string Display { get; }

            public bool IsServer { get; }

            /// <summary>Relative path (forward slashes, lower-case key) → entry.</summary>
            public Dictionary<string, Entry> Entries { get; } = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class Entry
        {
            public Entry(string path, string relative, bool isFolder, string? hash)
            {
                Path = path;
                Relative = relative;
                IsFolder = isFolder;
                Hash = hash;
            }

            public string Path { get; }

            public string Relative { get; }

            public bool IsFolder { get; }

            public string? Hash { get; }
        }

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            if (commandLine.Positionals.Count == 0 || commandLine.Positionals.Count > 2)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "folderdiff needs a target path and optionally a source path.", $"Example: '{toolName} folderdiff $/Project/Main $/Project/Release /recursive'.");
            }

            var views = Views(commandLine, toolName);
            var filters = Filters(commandLine.Value("filter"));
            bool recursive = commandLine.Has("recursive");
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
            var summary = context.Workspace?.Summary;
            var specs = commandLine.Positionals.Select(p => ItemSpecParser.Parse(p, summary?.Name, summary?.Owner)).ToList();

            Side source;
            Side target;
            if (specs.Count == 2)
            {
                source = Load(specs[0], context, recursive, filters, toolName);
                target = Load(specs[1], context, recursive, filters, toolName);
            }
            else
            {
                // One path: the folder against its counterpart in the workspace (local ↔ server).
                var given = specs[0];
                if (context.Workspace == null)
                {
                    throw new TfpException(ErrorCodes.WorkspaceNotFound, ExitCodes.WorkspaceNotFound, "folderdiff with one path compares it with the workspace, and no workspace is known here.",
                        $"Give both paths, or run the command inside a mapped folder.");
                }

                var counterpart = given.IsServerPath
                    ? new ParsedItemSpec(given.Original, context.Workspace.GetLocalItemForServerItem(given.Path) ?? throw NotMapped(given.Path, context, toolName), false, null, null, 0)
                    : new ParsedItemSpec(given.Original, context.ServerPathOf(given, toolName), true, null, null, 0);
                source = Load(given.IsServerPath ? counterpart : given, context, recursive, filters, toolName);
                target = Load(given.IsServerPath ? given : counterpart, context, recursive, filters, toolName);
            }

            var sourceOnly = new List<Entry>();
            var targetOnly = new List<Entry>();
            var different = new List<KeyValuePair<Entry, Entry>>();
            var same = new List<KeyValuePair<Entry, Entry>>();
            foreach (var entry in source.Entries.Values.OrderBy(e => e.Relative, StringComparer.OrdinalIgnoreCase))
            {
                if (!target.Entries.TryGetValue(entry.Relative, out var other))
                {
                    sourceOnly.Add(entry);
                }
                else if (entry.IsFolder || other.IsFolder)
                {
                    if (entry.IsFolder != other.IsFolder) { different.Add(new KeyValuePair<Entry, Entry>(entry, other)); }
                }
                else if (string.Equals(entry.Hash, other.Hash, StringComparison.OrdinalIgnoreCase) && entry.Hash != null)
                {
                    same.Add(new KeyValuePair<Entry, Entry>(entry, other));
                }
                else
                {
                    different.Add(new KeyValuePair<Entry, Entry>(entry, other));
                }
            }

            foreach (var entry in target.Entries.Values.OrderBy(e => e.Relative, StringComparer.OrdinalIgnoreCase))
            {
                if (!source.Entries.ContainsKey(entry.Relative))
                {
                    targetOnly.Add(entry);
                }
            }

            int folders = source.Entries.Values.Concat(target.Entries.Values).Where(e => e.IsFolder).Select(e => e.Relative).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            int files = source.Entries.Values.Concat(target.Entries.Values).Where(e => !e.IsFolder).Select(e => e.Relative).Distinct(StringComparer.OrdinalIgnoreCase).Count();

            output.Lines(Render(source, target, sourceOnly, targetOnly, different, same, views, folders, files));
            output.Success(new JObject
            {
                ["source"] = source.Display,
                ["target"] = target.Display,
                ["sourceOnly"] = new JArray(sourceOnly.Select(e => e.Path).Cast<object>().ToArray()),
                ["targetOnly"] = new JArray(targetOnly.Select(e => e.Path).Cast<object>().ToArray()),
                ["different"] = new JArray(different.Select(d => new JObject { ["source"] = d.Key.Path, ["target"] = d.Value.Path }).Cast<object>().ToArray()),
                ["same"] = views.Contains("same") ? new JArray(same.Select(d => new JObject { ["source"] = d.Key.Path, ["target"] = d.Value.Path }).Cast<object>().ToArray()) : null,
                ["counts"] = new JObject
                {
                    ["folders"] = folders,
                    ["files"] = files,
                    ["sourceOnly"] = sourceOnly.Count,
                    ["targetOnly"] = targetOnly.Count,
                    ["different"] = different.Count,
                    ["same"] = same.Count,
                },
            });
            return ExitCodes.Success;
        }

        private static TfpException NotMapped(string path, WorkspaceContext context, string toolName) =>
            new TfpException(ErrorCodes.PathNotMapped, ExitCodes.WorkspaceNotFound, $"{path} is not mapped in workspace {context.WorkspaceSpec}.", $"'{toolName} workfold' shows the mappings.", item: path);

        private static HashSet<string> Views(CommandLine commandLine, string toolName)
        {
            var views = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string value in commandLine.Values("view"))
            {
                if (value != "same" && value != "different" && value != "sourceOnly" && value != "targetOnly")
                {
                    if (!new[] { "same", "different", "sourceonly", "targetonly" }.Contains(value.ToLowerInvariant()))
                    {
                        throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"/view:{value} is not one of same, different, sourceOnly, targetOnly.", $"Example: '{toolName} folderdiff $/a $/b /view:different,sourceOnly'.");
                    }
                }

                views.Add(value.ToLowerInvariant() == "sourceonly" ? "sourceOnly" : value.ToLowerInvariant() == "targetonly" ? "targetOnly" : value.ToLowerInvariant());
            }

            if (views.Count == 0)
            {
                views.Add("different");
                views.Add("sourceOnly");
                views.Add("targetOnly");
            }

            return views;
        }

        /// <summary>`/filter:*.cs;!*.designer.cs`: semicolon-separated name patterns, `!` excludes; an empty list matches everything.</summary>
        internal static List<KeyValuePair<string, bool>> Filters(string? text)
        {
            var filters = new List<KeyValuePair<string, bool>>();
            foreach (string part in (text ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string pattern = part.Trim();
                bool exclude = pattern.StartsWith("!", StringComparison.Ordinal);
                filters.Add(new KeyValuePair<string, bool>(exclude ? pattern.Substring(1) : pattern, exclude));
            }

            return filters;
        }

        internal static bool Matches(string name, List<KeyValuePair<string, bool>> filters)
        {
            if (filters.Count == 0)
            {
                return true;
            }

            bool included = !filters.Any(f => !f.Value);
            foreach (var filter in filters)
            {
                if (ItemSpecParser.WildcardMatches(filter.Key, name))
                {
                    included = !filter.Value;
                }
            }

            return included;
        }

        private static Side Load(ParsedItemSpec spec, WorkspaceContext context, bool recursive, List<KeyValuePair<string, bool>> filters, string toolName)
        {
            if (spec.IsServerPath)
            {
                var version = spec.Version ?? VersionSpec.Latest;
                var side = new Side(spec.Path, $"{spec.Path};{spec.VersionText ?? "T"}", true);
                if (context.Client.GetItem(spec.Path, version, 0) == null)
                {
                    throw new TfpException(ErrorCodes.ItemNotFound, ExitCodes.GeneralError, $"The server folder {spec.Path} does not exist at version {spec.VersionText ?? "T"}.", $"Check the path with '{toolName} dir'.", item: spec.Path);
                }

                foreach (var item in context.Client.GetItems(new ItemQuery(spec.Path, recursive ? RecursionType.Full : RecursionType.OneLevel), version, false, false))
                {
                    if (string.Equals(item.ServerPath, spec.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string relative = item.ServerPath.Substring(spec.Path.TrimEnd('/').Length + 1);
                    if (!item.IsFolder && !Matches(VersionControlPath.GetFileName(item.ServerPath), filters))
                    {
                        continue;
                    }

                    side.Entries[relative] = new Entry(item.ServerPath, relative, item.IsFolder, item.Hash);
                }

                return side;
            }

            string root = Path.GetFullPath(spec.Path);
            if (!Directory.Exists(root))
            {
                throw new TfpException(ErrorCodes.ItemNotFound, ExitCodes.GeneralError, $"The local folder {root} does not exist.", "Check the path.", item: root);
            }

            var local = new Side(root, root, false);
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (string folder in Directory.GetDirectories(root, "*", option))
            {
                string relative = folder.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                if (relative.Split('/')[0].Equals("$tf", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                local.Entries[relative] = new Entry(folder, relative, true, null);
            }

            foreach (string file in Directory.GetFiles(root, "*", option))
            {
                string relative = file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                if (relative.Split('/')[0].Equals("$tf", StringComparison.OrdinalIgnoreCase) || !Matches(Path.GetFileName(file), filters))
                {
                    continue;
                }

                local.Entries[relative] = new Entry(file, relative, false, Md5(file));
            }

            return local;
        }

        private static string Md5(string file)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(file);
            return BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static IEnumerable<string> Render(Side source, Side target, List<Entry> sourceOnly, List<Entry> targetOnly, List<KeyValuePair<Entry, Entry>> different, List<KeyValuePair<Entry, Entry>> same, HashSet<string> views, int folders, int files)
        {
            string rule = new string('=', 79);
            if (views.Contains("sourceOnly"))
            {
                yield return rule;
                yield return $"Items that exist only in {source.Display}";
                yield return rule;
                yield return string.Empty;
                foreach (var entry in sourceOnly) { yield return entry.Path; }
                yield return string.Empty;
            }

            if (views.Contains("targetOnly"))
            {
                yield return rule;
                yield return $"Items that exist only in {target.Display}";
                yield return rule;
                yield return string.Empty;
                foreach (var entry in targetOnly) { yield return entry.Path; }
                yield return string.Empty;
            }

            if (views.Contains("different"))
            {
                yield return rule;
                yield return "Items with different content";
                yield return rule;
                yield return string.Empty;
                foreach (var pair in different)
                {
                    yield return pair.Key.Path + " - ";
                    yield return "\t" + pair.Value.Path;
                }

                yield return string.Empty;
            }

            if (views.Contains("same"))
            {
                yield return rule;
                yield return "Items with the same content";
                yield return rule;
                yield return string.Empty;
                foreach (var pair in same)
                {
                    yield return pair.Key.Path + " - ";
                    yield return "\t" + pair.Value.Path;
                }

                yield return string.Empty;
            }

            yield return rule;
            yield return $"Summary: folders: {folders}, files: {files}, source only: {sourceOnly.Count}, target only: {targetOnly.Count}, different: {different.Count}, errors: 0";
            yield return rule;
        }
    }
}
