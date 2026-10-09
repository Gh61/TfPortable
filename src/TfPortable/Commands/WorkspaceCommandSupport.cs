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
    /// <summary>Shared pieces of the workspace commands: itemspec resolution, grouped listings, failure mapping.</summary>
    internal static class WorkspaceCommandSupport
    {
        /// <summary>Parses the positionals as itemspecs; at least one is required unless <paramref name="allowEmpty"/>.</summary>
        public static List<ParsedItemSpec> Items(CommandLine commandLine, string toolName, bool allowEmpty, string example)
        {
            if (commandLine.Positionals.Count == 0 && !allowEmpty)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"{commandLine.Command} needs at least one itemspec.", $"Example: '{toolName} {example}'.");
            }

            return commandLine.Positionals.Select(p => ItemSpecParser.Parse(p)).ToList();
        }

        /// <summary>Queries for the items, or the whole workspace root when none were given.</summary>
        public static List<ItemQuery> Queries(IReadOnlyList<ParsedItemSpec> items, RecursionType recursion, WorkspaceContext context)
        {
            if (items.Count == 0)
            {
                return new List<ItemQuery> { new ItemQuery(Microsoft.TeamFoundation.VersionControl.Common.VersionControlPath.RootFolder, RecursionType.Full) };
            }

            return items.Select(i => new ItemQuery(i.Path, recursion, i.DeletionId)).ToList();
        }

        /// <summary>Local paths of the items: server paths are mapped through the workspace (TFP0203 when not mapped).</summary>
        public static List<string> LocalPaths(IReadOnlyList<ParsedItemSpec> items, WorkspaceContext context, string toolName)
        {
            var result = new List<string>();
            foreach (var item in items)
            {
                if (!item.IsServerPath)
                {
                    result.Add(item.Path);
                    continue;
                }

                string? local = context.Workspace?.GetLocalItemForServerItem(item.Path);
                if (local == null)
                {
                    throw new TfpException(ErrorCodes.PathNotMapped, ExitCodes.WorkspaceNotFound,
                        $"{item.Path} is not mapped in workspace {context.WorkspaceSpec}.",
                        $"Map it first, or pass a local path; '{toolName} workfold' shows the mappings.", item: item.Path);
                }

                result.Add(local);
            }

            return result;
        }

        /// <summary>
        /// Grouped listing of changes (SPEC.md "Tvar textového výstupu"): `<absolute folder>:` then one line per item,
        /// a blank line between groups; folders sorted, items sorted by name. The header is always printed.
        /// </summary>
        public static IEnumerable<string> GroupedByFolder(IEnumerable<PendingChangeInfo> changes, Func<PendingChangeInfo, string> line)
        {
            var ordered = changes
                .Select(c => new { Change = c, Folder = FolderOf(c), Name = NameOf(c) })
                .OrderBy(x => x.Folder, StringComparer.InvariantCultureIgnoreCase)
                .ThenBy(x => x.Name, StringComparer.InvariantCultureIgnoreCase)
                .Select(x => new KeyValuePair<string, string>(x.Folder, line(x.Change)));
            return TextBlocks.Grouped(ordered);
        }

        /// <summary>Folder of a change: the parent of its local path when known, otherwise of its server path.</summary>
        public static string FolderOf(PendingChangeInfo change)
        {
            if (!string.IsNullOrEmpty(change.LocalPath))
            {
                return Path.GetDirectoryName(change.LocalPath) ?? change.LocalPath!;
            }

            return Microsoft.TeamFoundation.VersionControl.Common.VersionControlPath.GetFolderName(change.ServerPath);
        }

        public static string NameOf(PendingChangeInfo change)
        {
            if (!string.IsNullOrEmpty(change.LocalPath))
            {
                return Path.GetFileName(change.LocalPath);
            }

            return Microsoft.TeamFoundation.VersionControl.Common.VersionControlPath.GetFileName(change.ServerPath);
        }

        /// <summary>Reports every failure as a per-item error (exit 1) and returns the `skipped` entries for the JSON data.</summary>
        public static JArray ReportFailures(OutputWriter output, IReadOnlyList<FailureInfo> failures, string toolName)
        {
            var skipped = new JArray();
            foreach (var failure in failures)
            {
                var error = ToError(failure, toolName);
                if (failure.IsWarning)
                {
                    output.Warning(failure.Message, error.Code, item: failure.Item);
                }
                else
                {
                    output.ItemError(error);
                }

                skipped.Add(new JObject { ["localPath"] = failure.LocalPath ?? failure.ServerPath, ["reason"] = Reason(error.Code) });
            }

            return skipped;
        }

        /// <summary>Maps a library failure to the catalog: not found → TFP0301, already pending/versioned → TFP0401, locked → TFP0404, else TFP0501.</summary>
        public static TfpException ToError(FailureInfo failure, string toolName)
        {
            string code = failure.Code ?? string.Empty;
            string message = failure.Message;
            string? tfCode = ErrorMapper.ExtractTfCode(message);
            string? item = failure.Item.Length > 0 ? failure.Item : null;
            string prefix = item == null ? string.Empty : item + ": ";

            if (code.IndexOf("NotFound", StringComparison.OrdinalIgnoreCase) >= 0 || code.IndexOf("NoMatch", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("No matching", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("could not be found", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("No pending changes", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new TfpException(ErrorCodes.ItemNotFound, ExitCodes.PartialSuccess, $"{prefix}no matching item in the workspace.",
                    $"Check the path with '{toolName} status' or '{toolName} dir'.", tfCode, message, item);
            }

            if (code.IndexOf("Exists", StringComparison.OrdinalIgnoreCase) >= 0 || code.IndexOf("Pending", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("already has pending", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new TfpException(ErrorCodes.PendingChangeConflict, ExitCodes.PartialSuccess, $"{prefix}the item is already under version control or has another pending change.",
                    $"Check it with '{toolName} status'; use '{toolName} undo' to drop the pending change.", tfCode, message, item);
            }

            if (code.IndexOf("Writable", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("writable file", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new TfpException(ErrorCodes.WritableFileSkipped, ExitCodes.PartialSuccess, $"{prefix}a writable file with the same name exists locally and was not overwritten.",
                    "Move or delete the local file, then run the command again.", tfCode, message, item);
            }

            if (code.IndexOf("Lock", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("locked", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new TfpException(ErrorCodes.ItemLocked, ExitCodes.PartialSuccess, $"{prefix}the item is locked.",
                    "Ask the lock owner to unlock it, or wait for the checkin.", tfCode, message, item);
            }

            return new TfpException(ErrorCodes.ServerError, ExitCodes.PartialSuccess, $"{prefix}{message}",
                "Read the server message; if it is temporary, try again.", tfCode, message, item);
        }

        private static string Reason(string code)
        {
            switch (code)
            {
                case ErrorCodes.ItemNotFound: return "notFound";
                case ErrorCodes.PendingChangeConflict: return "alreadyVersioned";
                case ErrorCodes.ItemLocked: return "locked";
                case ErrorCodes.WritableFileSkipped: return "writable";
                default: return "error";
            }
        }

        public static JObject ChangesJson(IReadOnlyList<PendingChangeInfo> changes, JArray skipped) => new JObject
        {
            ["changes"] = new JArray(changes.Select(c => c.ToJson()).Cast<object>().ToArray()),
            ["skipped"] = skipped,
        };

        /// <summary>`/lock:checkin|checkout` are refused: locks write to the server (docs/commands.md).</summary>
        public static void RejectServerLocks(CommandLine commandLine)
        {
            string? value = commandLine.Value("lock");
            if (value != null && !value.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"/lock:{value} is not supported; locks are written to the server.",
                    "Use /lock:none (the default), or lock the item with Visual Studio or the original tf.exe.");
            }
        }
    }
}
