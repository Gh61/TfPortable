using System;
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
    /// <summary>`tfp whoami [/collection:&lt;url&gt;]`: identity, collection, PAT date, workspace and mappings in one call.</summary>
    internal sealed class WhoamiCommand : ICommand
    {
        public string Name => "whoami";

        public string[] Switches => Array.Empty<string>();

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            using var context = ContextResolver.Resolve(commandLine, toolName, requireWorkspace: false);
            output.Collection = context.CollectionUrl;
            output.Workspace = context.Workspace == null ? null : context.WorkspaceSpec;

            var identity = context.Connection.AuthorizedIdentity;
            string cwd = Directory.GetCurrentDirectory();
            var workspace = context.Workspace;

            output.Line($"Identity   : {identity.DisplayName} ({identity.UniqueName})");
            output.Line($"Collection : {context.CollectionUrl}");
            output.Line($"PAT stored : {context.StoredPat?.StoredAt ?? "unknown"} (Windows Credential Manager, {context.StoredPat?.Target ?? "-"})");
            if (workspace == null)
            {
                output.Line($"Workspace  : none maps {cwd}");
            }
            else
            {
                output.Line($"Workspace  : {workspace.Name};{workspace.OwnerDisplayName} ({workspace.Location.ToString().ToLowerInvariant()}, computer {workspace.Computer})");
                foreach (var folder in workspace.Folders)
                {
                    output.Line(folder.IsCloaked ? $"  (cloaked) {folder.ServerItem}:" : $"  {folder.ServerItem}: {folder.LocalItem}");
                }
            }

            output.Success(new JObject
            {
                ["collection"] = context.CollectionUrl,
                ["identity"] = new JObject { ["displayName"] = identity.DisplayName, ["uniqueName"] = identity.UniqueName, ["id"] = identity.TeamFoundationId.ToString() },
                ["patStoredAt"] = context.StoredPat?.StoredAt,
                ["workspace"] = workspace == null ? null : WorkspaceJson(workspace),
                ["cwd"] = cwd,
            });
            return ExitCodes.Success;
        }

        internal static JObject WorkspaceJson(Workspace workspace)
        {
            return new JObject
            {
                ["name"] = workspace.Name,
                ["owner"] = workspace.OwnerDisplayName,
                ["ownerUniqueName"] = workspace.OwnerName,
                ["computer"] = workspace.Computer,
                ["comment"] = workspace.Comment,
                ["collection"] = workspace.VersionControlServer.TeamProjectCollection.Uri.AbsoluteUri,
                ["location"] = workspace.Location.ToString().ToLowerInvariant(),
                ["permission"] = workspace.PermissionsProfile?.Name?.ToLowerInvariant(),
                ["lastAccess"] = OutputWriter.IsoDate(workspace.LastAccessDate),
                ["mappings"] = new JArray(workspace.Folders.Select(f => new JObject
                {
                    ["serverPath"] = f.ServerItem,
                    ["localPath"] = f.IsCloaked ? null : f.LocalItem,
                    ["cloaked"] = f.IsCloaked,
                    ["recursionType"] = f.Depth == RecursionType.Full ? "full" : "onelevel",
                }).Cast<object>().ToArray()),
            };
        }
    }
}
