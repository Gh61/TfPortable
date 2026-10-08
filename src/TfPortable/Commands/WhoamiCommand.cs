using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using TfPortable.Auth;
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

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
            var identity = context.Client.Authenticate();
            string cwd = Directory.GetCurrentDirectory();
            var workspace = context.Workspace?.Summary;

            output.Lines(Render(identity, context.CollectionUrl, context.StoredPat, workspace, cwd));
            output.Success(ToJson(identity, context.CollectionUrl, context.StoredPat, workspace, cwd));
            return ExitCodes.Success;
        }

        internal static IEnumerable<string> Render(IdentityInfo identity, string collection, StoredPat? pat, WorkspaceSummary? workspace, string cwd)
        {
            yield return $"Identity   : {identity.DisplayName} ({identity.UniqueName})";
            yield return $"Collection : {collection}";
            yield return pat == null
                ? "PAT stored : no (the PAT of this run came from /login)"
                : $"PAT stored : {pat.StoredAt ?? "unknown"} (Windows Credential Manager, {pat.Target})";
            if (workspace == null)
            {
                yield return $"Workspace  : none maps {cwd}";
                yield break;
            }

            yield return $"Workspace  : {workspace.Spec} ({workspace.Location}, computer {workspace.Computer})";
            foreach (var mapping in workspace.Mappings)
            {
                yield return " " + mapping.ListingLine;
            }
        }

        internal static JObject ToJson(IdentityInfo identity, string collection, StoredPat? pat, WorkspaceSummary? workspace, string cwd)
        {
            return new JObject
            {
                ["collection"] = collection,
                ["identity"] = identity.ToJson(),
                ["patStoredAt"] = pat?.StoredAt,
                ["workspace"] = workspace?.ToJson(),
                ["cwd"] = cwd,
            };
        }
    }
}
