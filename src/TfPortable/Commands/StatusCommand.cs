using System;
using System.Collections.Generic;
using System.IO;
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
    /// <summary>
    /// `tfp status [itemspec ...] [/recursive] [/format:brief|json]`: pending changes and, in a local workspace,
    /// detected changes (candidates) in a separate section. Shape per docs/compat.md §4; spike version (brief format only).
    /// </summary>
    internal sealed class StatusCommand : ICommand
    {
        public string Name => "status";

        public string[] Switches => new[] { "recursive", "nodetect", "user", "shelveset" };

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            string? format = commandLine.Value("format");
            if (format != null && !format.Equals("brief", StringComparison.OrdinalIgnoreCase) && !format.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"/format:{format} is not supported in this build.", "Use /format:brief or /format:json (detailed comes with milestone 3).");
            }

            if (commandLine.Has("user") || commandLine.Has("shelveset"))
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "/user and /shelveset are not supported in this build.", "They come with milestone 3.");
            }

            using var context = ContextResolver.Resolve(commandLine, toolName, requireWorkspace: true);
            var workspace = context.Workspace!;
            output.Collection = context.CollectionUrl;
            output.Workspace = context.WorkspaceSpec;

            var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.OneLevel;
            ItemSpec[] specs = commandLine.Positionals.Count == 0
                ? new[] { new ItemSpec(VersionControlPath.RootFolder, RecursionType.Full) }
                : commandLine.Positionals.Select(p => new ItemSpec(NormalizeItem(p), recursion)).ToArray();

            bool detect = !commandLine.Has("nodetect") && workspace.Location == WorkspaceLocation.Local;
            PendingChange[] candidates;
            PendingChange[] pending = workspace.GetPendingChangesWithCandidates(specs, detect, out candidates);
            candidates = detect ? candidates : Array.Empty<PendingChange>();

            if (pending.Length == 0 && candidates.Length == 0)
            {
                output.Line("There are no pending changes.");
            }
            else
            {
                if (pending.Length > 0)
                {
                    foreach (string line in RenderTable(pending))
                    {
                        output.Line(line);
                    }
                }

                if (candidates.Length > 0)
                {
                    if (pending.Length > 0)
                    {
                        output.Line();
                    }

                    string rule = new string('-', 79);
                    output.Line(rule);
                    output.Line("Detected changes:");
                    output.Line(rule);
                    foreach (string line in RenderTable(candidates))
                    {
                        output.Line(line);
                    }
                }

                output.Line();
                output.Line(candidates.Length > 0 ? $"Changes: {pending.Length}, detected changes: {candidates.Length}" : $"Changes: {pending.Length}");
            }

            output.Success(new JObject
            {
                ["changes"] = new JArray(pending.Select(p => ToJson(p, false)).Cast<object>().ToArray()),
                ["detectedChanges"] = new JArray(candidates.Select(p => ToJson(p, true)).Cast<object>().ToArray()),
                ["counts"] = new JObject { ["changes"] = pending.Length, ["detected"] = candidates.Length },
            });
            return ExitCodes.Success;
        }

        private static string NormalizeItem(string item)
        {
            if (VersionControlPath.IsServerItem(item))
            {
                return item;
            }

            return Path.GetFullPath(item.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>Table "File name / Change / Local path" grouped by server folder, as tf.exe prints it.</summary>
        private static IEnumerable<string> RenderTable(PendingChange[] changes)
        {
            var ordered = changes
                .OrderBy(c => VersionControlPath.GetFolderName(c.ServerItem), StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => VersionControlPath.GetFileName(c.ServerItem), StringComparer.OrdinalIgnoreCase)
                .ToList();

            int nameWidth = Math.Max("File name".Length, ordered.Max(c => VersionControlPath.GetFileName(c.ServerItem).Length));
            int changeWidth = Math.Max("Change".Length, ordered.Max(c => ChangeText(c).Length));

            yield return $"{"File name".PadRight(nameWidth)} {"Change".PadRight(changeWidth)} Local path";
            yield return $"{new string('-', nameWidth)} {new string('-', changeWidth)} {new string('-', Math.Max("Local path".Length, ordered.Max(c => (c.LocalItem ?? string.Empty).Length)))}";

            string? currentFolder = null;
            foreach (var change in ordered)
            {
                string folder = VersionControlPath.GetFolderName(change.ServerItem);
                if (!string.Equals(folder, currentFolder, StringComparison.OrdinalIgnoreCase))
                {
                    if (currentFolder != null)
                    {
                        yield return string.Empty;
                    }

                    yield return folder;
                    currentFolder = folder;
                }

                yield return $"{VersionControlPath.GetFileName(change.ServerItem).PadRight(nameWidth)} {ChangeText(change).PadRight(changeWidth)} {change.LocalItem}".TrimEnd();
            }
        }

        /// <summary>Change types in the English wording of the client library ("edit", "merge, edit"), lower-case like tf.exe.</summary>
        internal static string ChangeText(PendingChange change) => PendingChange.GetLocalizedStringForChangeType(change.ChangeType).ToLowerInvariant();

        private static JObject ToJson(PendingChange change, bool detected)
        {
            return new JObject
            {
                ["changeTypes"] = new JArray(ChangeText(change).Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries).Cast<object>().ToArray()),
                ["serverPath"] = change.ServerItem,
                ["localPath"] = change.LocalItem,
                ["isFolder"] = change.ItemType == ItemType.Folder,
                ["version"] = change.Version,
                ["sourceServerPath"] = change.IsRename || change.IsBranch || change.IsMerge ? change.SourceServerItem : null,
                ["lock"] = change.LockLevel.ToString().ToLowerInvariant(),
                ["encoding"] = change.ItemType == ItemType.Folder ? null : EncodingName(change.Encoding),
                ["date"] = OutputWriter.IsoDate(change.CreationDate),
                ["owner"] = change.PendingSetOwner,
                ["workspace"] = change.PendingSetName,
                ["detected"] = detected,
            };
        }

        internal static string? EncodingName(int codePage)
        {
            if (codePage == -1)
            {
                return "binary";
            }

            if (codePage <= 0)
            {
                return null;
            }

            try
            {
                return System.Text.Encoding.GetEncoding(codePage).WebName;
            }
            catch (Exception)
            {
                return codePage.ToString();
            }
        }
    }
}
