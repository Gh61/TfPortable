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
    /// <summary>
    /// `tfp status [itemspec ...] [/recursive] [/format:brief|json]`: pending changes and, in a local workspace,
    /// detected changes (candidates) in a separate section. Shape per docs/compat.md §4; brief format only until
    /// milestone 3 adds /format:detailed, /user and /shelveset.
    /// </summary>
    internal sealed class StatusCommand : ICommand
    {
        public string Name => "status";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            string? format = commandLine.Value("format");
            if (format != null && format.Equals("detailed", StringComparison.OrdinalIgnoreCase))
            {
                throw new TfpException(ErrorCodes.NotSupported, ExitCodes.GeneralError, "/format:detailed is not available in this build.", "Use /format:brief or /format:json; detailed comes with milestone 3.");
            }

            if (commandLine.Has("user") || commandLine.Has("shelveset"))
            {
                throw new TfpException(ErrorCodes.NotSupported, ExitCodes.GeneralError, "/user and /shelveset are not available in this build.", "They come with milestone 3.");
            }

            var items = commandLine.Positionals.Select(p => ItemSpecParser.Parse(p)).ToList();
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: true);
            var workspace = context.RequireWorkspace(toolName);

            var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.OneLevel;
            var queries = items.Count == 0
                ? new List<ItemQuery> { new ItemQuery(VersionControlPath.RootFolder, RecursionType.Full) }
                : items.Select(i => new ItemQuery(i.Path, recursion, i.DeletionId)).ToList();

            var changes = workspace.GetPendingChanges(queries, detectCandidates: !commandLine.Has("nodetect"));
            output.Lines(Render(changes));
            output.Success(ToJson(changes));
            return ExitCodes.Success;
        }

        internal static IEnumerable<string> Render(PendingChangeSet changes)
        {
            var pending = changes.Pending;
            var candidates = changes.Candidates;
            if (pending.Count == 0 && candidates.Count == 0)
            {
                yield return "There are no pending changes.";
                yield break;
            }

            if (pending.Count > 0)
            {
                foreach (string line in RenderTable(pending))
                {
                    yield return line;
                }
            }

            if (candidates.Count > 0)
            {
                if (pending.Count > 0)
                {
                    yield return string.Empty;
                }

                yield return TextBlocks.Separator;
                yield return "Detected changes:";
                yield return TextBlocks.Separator;
                foreach (string line in RenderTable(candidates))
                {
                    yield return line;
                }
            }

            yield return string.Empty;
            yield return candidates.Count > 0 ? $"Changes: {pending.Count}, detected changes: {candidates.Count}" : $"Changes: {pending.Count}";
        }

        internal static JObject ToJson(PendingChangeSet changes)
        {
            return new JObject
            {
                ["changes"] = new JArray(changes.Pending.Select(p => p.ToJson()).Cast<object>().ToArray()),
                ["detectedChanges"] = new JArray(changes.Candidates.Select(p => p.ToJson()).Cast<object>().ToArray()),
                ["counts"] = new JObject { ["changes"] = changes.Pending.Count, ["detected"] = changes.Candidates.Count },
            };
        }

        /// <summary>Table "File name / Change / Local path" grouped by server folder, as tf.exe prints it.</summary>
        internal static IEnumerable<string> RenderTable(IReadOnlyList<PendingChangeInfo> changes)
        {
            var ordered = changes
                .OrderBy(c => VersionControlPath.GetFolderName(c.ServerPath), StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => VersionControlPath.GetFileName(c.ServerPath), StringComparer.OrdinalIgnoreCase)
                .ToList();

            int nameWidth = Math.Max("File name".Length, ordered.Max(c => VersionControlPath.GetFileName(c.ServerPath).Length));
            int changeWidth = Math.Max("Change".Length, ordered.Max(c => c.ChangeText.Length));

            yield return $"{"File name".PadRight(nameWidth)} {"Change".PadRight(changeWidth)} Local path";
            yield return $"{new string('-', nameWidth)} {new string('-', changeWidth)} {new string('-', Math.Max("Local path".Length, ordered.Max(c => (c.LocalPath ?? string.Empty).Length)))}";

            string? currentFolder = null;
            foreach (var change in ordered)
            {
                string folder = VersionControlPath.GetFolderName(change.ServerPath);
                if (!string.Equals(folder, currentFolder, StringComparison.OrdinalIgnoreCase))
                {
                    if (currentFolder != null)
                    {
                        yield return string.Empty;
                    }

                    yield return folder;
                    currentFolder = folder;
                }

                yield return $"{VersionControlPath.GetFileName(change.ServerPath).PadRight(nameWidth)} {change.ChangeText.PadRight(changeWidth)} {change.LocalPath}".TrimEnd();
            }
        }
    }
}
