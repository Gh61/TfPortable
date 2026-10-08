using System;
using System.Collections.Generic;
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
    /// `tfp changeset [changesetnumber] [/latest] [/diff]`: the block of docs/compat.md §10 (Changeset, User, Date,
    /// Comment, Items, Work items); `/diff` adds the unified diff of every file against its previous version
    /// (SPEC.md "Další rozšíření"); `/comment:` and `/notes:` are TFP0003.
    /// </summary>
    internal sealed class ChangesetCommand : ICommand
    {
        public string Name => "changeset";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            if (commandLine.Has("comment") || commandLine.Has("notes"))
            {
                throw new TfpException(ErrorCodes.NotSupported, ExitCodes.GeneralError, "/comment and /notes edit the changeset on the server; TfPortable does not write to the server.",
                    "Edit the changeset in Visual Studio or with the original tf.exe.");
            }

            bool latest = commandLine.Has("latest");
            if (commandLine.Positionals.Count != (latest ? 0 : 1))
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "changeset needs a changeset number or /latest.", $"Example: '{toolName} changeset 1234' or '{toolName} changeset /latest'.");
            }

            int id = 0;
            if (!latest)
            {
                string text = commandLine.Positionals[0].TrimStart('C', 'c');
                if (!int.TryParse(text, out id) || id <= 0)
                {
                    throw new TfpException(ErrorCodes.InvalidVersionSpec, ExitCodes.GeneralError, $"'{commandLine.Positionals[0]}' is not a changeset number.", "Pass a positive number, optionally prefixed with C.");
                }
            }

            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
            if (latest)
            {
                id = context.Client.GetLatestChangesetId();
            }

            var changeset = context.Client.GetChangeset(id);
            output.Lines(RenderBlock(changeset));

            string? diff = null;
            if (commandLine.Has("diff"))
            {
                diff = string.Join("\n", Diff(changeset, context.Client));
                output.Line(string.Empty);
                output.Line(diff);
            }

            output.Success(new JObject { ["changeset"] = changeset.ToJson(true), ["diff"] = diff });
            return ExitCodes.Success;
        }

        /// <summary>`Changeset: N`, `User:`, `Date:`, blank, `Comment:` + indented text, blank, `Items:` + aligned change types, `Work items:` when present.</summary>
        internal static IEnumerable<string> RenderBlock(ChangesetInfo cs)
        {
            yield return $"Changeset: {cs.Id}";
            yield return $"User: {cs.Owner}";
            yield return $"Date: {OutputWriter.IsoDate(cs.Date)}";
            yield return string.Empty;
            yield return "Comment:";
            foreach (string line in TextBlocks.Indented(cs.Comment))
            {
                yield return line;
            }

            if (string.IsNullOrEmpty(cs.Comment))
            {
                yield return "  ";
            }

            yield return string.Empty;
            yield return "Items:";
            var changes = cs.Changes ?? Array.Empty<ChangeInfo>();
            int width = changes.Count == 0 ? 0 : changes.Max(c => c.ChangeText.Length);
            foreach (var change in changes.OrderBy(c => c.ServerPath, StringComparer.InvariantCultureIgnoreCase))
            {
                yield return $"  {change.ChangeText.PadRight(width)} {change.DisplayPath}";
            }

            if (cs.WorkItems != null && cs.WorkItems.Count > 0)
            {
                yield return string.Empty;
                yield return "Work items:";
                foreach (var workItem in cs.WorkItems)
                {
                    yield return $"  {workItem.Id} {workItem.Title}";
                }
            }
        }

        /// <summary>Unified diff of every file of the changeset against its previous version; binary and folder items are summarized.</summary>
        internal static IEnumerable<string> Diff(ChangesetInfo cs, ITfvcClient client)
        {
            foreach (var change in (cs.Changes ?? Array.Empty<ChangeInfo>()).OrderBy(c => c.ServerPath, StringComparer.InvariantCultureIgnoreCase))
            {
                if (change.IsFolder)
                {
                    continue;
                }

                string label = $"{change.ChangeText}: {change.DisplayPath}";
                bool isAdd = change.ChangeTypes.Contains("add") || change.ChangeTypes.Contains("branch") || change.ChangeTypes.Contains("undelete");
                bool isDelete = change.ChangeTypes.Contains("delete");

                string? before = null;
                string? after = null;
                bool binary = change.Encoding == "binary";
                if (!binary)
                {
                    if (!isDelete)
                    {
                        var content = client.Download(change.ServerPath, new ChangesetVersionSpec(cs.Id), change.DeletionId);
                        after = TextContent.Decode(content.Bytes, content.Item.Encoding);
                        binary = after == null;
                    }

                    if (!isAdd && !binary)
                    {
                        try
                        {
                            var previous = client.Download(change.SourceServerPath ?? change.ServerPath, new ChangesetVersionSpec(cs.Id - 1), 0);
                            before = TextContent.Decode(previous.Bytes, previous.Item.Encoding);
                            binary = before == null;
                        }
                        catch (Exception)
                        {
                            before = null;
                        }
                    }
                }

                yield return label;
                yield return DifferenceCommand.Rule;
                if (binary)
                {
                    yield return "Binary files differ.";
                }
                else
                {
                    string leftLabel = before == null ? "[no source file]" : $"Server: {change.SourceServerPath ?? change.ServerPath};C{cs.Id - 1}";
                    string rightLabel = after == null ? "[no target file]" : $"Server: {change.ServerPath};C{cs.Id}";
                    var lines = UnifiedDiff.Render(before ?? string.Empty, after ?? string.Empty, leftLabel, rightLabel);
                    if (lines.Count == 0)
                    {
                        yield return "No content change.";
                    }

                    foreach (string line in lines)
                    {
                        yield return line;
                    }
                }

                yield return DifferenceCommand.Rule;
            }
        }
    }
}
