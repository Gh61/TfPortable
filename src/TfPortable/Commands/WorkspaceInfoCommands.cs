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
    /// <summary>`tfp localversions itemspec ... [/recursive] [/format:brief|detailed|json]` (alias `lv`), docs/compat.md §8.</summary>
    internal sealed class LocalVersionsCommand : ICommand
    {
        public string Name => "localversions";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            bool detailed = string.Equals(commandLine.Value("format"), "detailed", StringComparison.OrdinalIgnoreCase);
            var items = WorkspaceCommandSupport.Items(commandLine, toolName, true, "localversions Main /recursive");
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: true);
            var workspace = context.RequireWorkspace(toolName);
            var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.OneLevel;

            var versions = workspace.GetLocalVersions(WorkspaceCommandSupport.Queries(items, recursion, context));
            output.Lines(Render(versions, detailed));
            output.Success(new JObject
            {
                ["items"] = new JArray(versions.Select(v => v.ToJson()).Cast<object>().ToArray()),
                ["count"] = versions.Count,
            });
            return ExitCodes.Success;
        }

        /// <summary>Brief: `name;C75` grouped under absolute folder headers; detailed: full `path;C75` lines; trailer `Items: N`.</summary>
        internal static IEnumerable<string> Render(IReadOnlyList<LocalVersionInfo> versions, bool detailed)
        {
            var ordered = versions
                .OrderBy(v => Path.GetDirectoryName(v.LocalPath) ?? string.Empty, StringComparer.InvariantCultureIgnoreCase)
                .ThenBy(v => Path.GetFileName(v.LocalPath), StringComparer.InvariantCultureIgnoreCase)
                .ToList();

            if (ordered.Count == 0)
            {
                yield return "No items match the itemspec in the workspace.";
                yield break;
            }

            if (detailed)
            {
                foreach (var v in ordered)
                {
                    yield return $"{v.LocalPath};C{v.Version}";
                }
            }
            else
            {
                foreach (string line in TextBlocks.Grouped(ordered.Select(v => new KeyValuePair<string, string>(Path.GetDirectoryName(v.LocalPath) ?? v.LocalPath, $"{Path.GetFileName(v.LocalPath)};C{v.Version}"))))
                {
                    yield return line;
                }
            }

            yield return string.Empty;
            yield return $"Items: {ordered.Count}";
        }
    }

    /// <summary>`tfp workfold [localfolder | serverfolder] [/workspace:name]`: the mappings of the workspace (docs/compat.md §3). /map etc. are phase 2.</summary>
    internal sealed class WorkfoldCommand : ICommand
    {
        public string Name => "workfold";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            foreach (string edit in new[] { "map", "unmap", "cloak", "decloak" })
            {
                if (commandLine.Has(edit))
                {
                    throw new TfpException(ErrorCodes.NotSupported, ExitCodes.GeneralError, $"workfold /{edit} is not available in this build (phase 2).", "Change the mappings in Visual Studio or with the original tf.exe for now.");
                }
            }

            if (commandLine.Positionals.Count > 1)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "workfold takes at most one folder.", $"Example: '{toolName} workfold Main'.");
            }

            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: true);
            var summary = context.RequireWorkspace(toolName).Summary;
            IReadOnlyList<MappingInfo> mappings = summary.Mappings;
            if (commandLine.Positionals.Count == 1)
            {
                var item = ItemSpecParser.Parse(commandLine.Positionals[0]);
                mappings = summary.Mappings.Where(m => Covers(m, item)).ToList();
                if (mappings.Count == 0)
                {
                    throw new TfpException(ErrorCodes.PathNotMapped, ExitCodes.WorkspaceNotFound, $"{item.Path} is not mapped in workspace {summary.Spec}.",
                        $"'{toolName} workfold' lists the mappings.", item: item.Path);
                }
            }

            output.Lines(Render(summary, mappings));
            output.Success(new JObject { ["workspace"] = summary.ToJson() });
            return ExitCodes.Success;
        }

        private static bool Covers(MappingInfo mapping, ParsedItemSpec item)
        {
            if (item.IsServerPath)
            {
                return item.Path.StartsWith(mapping.ServerPath, StringComparison.OrdinalIgnoreCase)
                    && (item.Path.Length == mapping.ServerPath.Length || item.Path[mapping.ServerPath.Length] == '/' || mapping.ServerPath == "$/");
            }

            return mapping.LocalPath != null && item.Path.StartsWith(mapping.LocalPath, StringComparison.OrdinalIgnoreCase)
                && (item.Path.Length == mapping.LocalPath.Length || item.Path[mapping.LocalPath.Length] == Path.DirectorySeparatorChar);
        }

        internal static IEnumerable<string> Render(WorkspaceSummary summary, IReadOnlyList<MappingInfo> mappings)
        {
            yield return new string('=', 79);
            yield return $"Workspace: {summary.Name} ({summary.Owner})";
            yield return $"Collection: {summary.Collection}/";
            foreach (var mapping in mappings)
            {
                yield return mapping.ListingLine;
            }
        }
    }

    /// <summary>
    /// `tfp workspaces [workspacename] [/owner:name|*] [/computer:name|*] [/format:brief|detailed|json] [/remove:*|a,b]`:
    /// workspaces of the collection (docs/compat.md §3). No match is exit 1 with the message on stdout.
    /// </summary>
    internal sealed class WorkspacesCommand : ICommand
    {
        public string Name => "workspaces";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            bool detailed = string.Equals(commandLine.Value("format"), "detailed", StringComparison.OrdinalIgnoreCase);
            if (commandLine.Positionals.Count > 1)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "workspaces takes at most one workspace name.", $"Example: '{toolName} workspaces /owner:* /computer:*'.");
            }

            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);

            if (commandLine.Has("remove"))
            {
                string value = commandLine.Value("remove") ?? "*";
                var removed = context.Client.RemoveCachedWorkspaces(value == "*" ? null : commandLine.Values("remove"));
                output.Lines(removed.Select(n => $"Removed {n} from the local workspace cache."));
                if (removed.Count == 0)
                {
                    output.Line("No cached workspace matched.");
                }

                output.Success(new JObject { ["removed"] = new JArray(removed.Cast<object>().ToArray()), ["workspaces"] = new JArray() });
                return ExitCodes.Success;
            }

            string? name = commandLine.Positionals.Count == 1 ? commandLine.Positionals[0] : null;
            string? owner = Filter(commandLine.Value("owner"), context.Client.AuthorizedUser);
            string? computer = Filter(commandLine.Value("computer"), Environment.MachineName);
            var workspaces = context.Client.QueryWorkspaces(new WorkspaceQuery(name, owner, computer));

            if (workspaces.Count == 0)
            {
                output.Line($"No workspace matching {name ?? "*"};{owner ?? "*"} on computer {computer ?? "*"} found in {context.CollectionUrl}.");
                output.Success(new JObject { ["workspaces"] = new JArray() }, ExitCodes.PartialSuccess);
                return ExitCodes.PartialSuccess;
            }

            output.Lines(detailed ? RenderDetailed(workspaces) : RenderBrief(context.CollectionUrl, workspaces));
            output.Success(new JObject { ["workspaces"] = new JArray(workspaces.Select(w => w.ToJson()).Cast<object>().ToArray()) });
            return ExitCodes.Success;
        }

        /// <summary>`*` means any (null filter); absent means the default.</summary>
        private static string? Filter(string? value, string defaultValue) => value == null ? defaultValue : value == "*" ? null : value;

        internal static IEnumerable<string> RenderBrief(string collection, IReadOnlyList<WorkspaceSummary> workspaces)
        {
            yield return $"Collection: {collection}/";
            var table = new TextTable("Workspace", "Owner", "Computer", "Comment");
            foreach (var w in workspaces.OrderBy(w => w.Name, StringComparer.InvariantCultureIgnoreCase))
            {
                table.Add(w.Name, w.Owner, w.Computer, w.Comment ?? string.Empty);
            }

            foreach (string line in table.Render())
            {
                yield return line;
            }
        }

        /// <summary>Blocks separated by `====` lines: Workspace, Owner, Computer, Comment, Collection, Permission, Location, then `Working folders:` (docs/compat.md §3 `detailed`).</summary>
        internal static IEnumerable<string> RenderDetailed(IReadOnlyList<WorkspaceSummary> workspaces)
        {
            foreach (var w in workspaces.OrderBy(w => w.Name, StringComparer.InvariantCultureIgnoreCase))
            {
                yield return new string('=', 79);
                foreach (string line in TextBlocks.Fields(new[]
                {
                    new KeyValuePair<string, string?>("Workspace", w.Name),
                    new KeyValuePair<string, string?>("Owner", w.Owner),
                    new KeyValuePair<string, string?>("Computer", w.Computer),
                    new KeyValuePair<string, string?>("Comment", w.Comment),
                    new KeyValuePair<string, string?>("Collection", w.Collection),
                    new KeyValuePair<string, string?>("Permission", w.Permission == null ? null : char.ToUpperInvariant(w.Permission[0]) + w.Permission.Substring(1)),
                    new KeyValuePair<string, string?>("Location", w.IsLocal ? "Local" : "Server"),
                    new KeyValuePair<string, string?>("Last access", OutputWriter.IsoDate(w.LastAccess)),
                }))
                {
                    yield return line;
                }

                yield return string.Empty;
                yield return "Working folders:";
                foreach (var mapping in w.Mappings)
                {
                    yield return mapping.ListingLine;
                }

                yield return string.Empty;
            }
        }
    }
}
