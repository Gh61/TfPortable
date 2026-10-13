using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.TeamFoundation.VersionControl.Client;
using Newtonsoft.Json.Linq;
using TfPortable.Cli;
using TfPortable.Context;
using TfPortable.Errors;
using TfPortable.Output;
using TfPortable.Settings;

namespace TfPortable.Commands
{
    /// <summary>
    /// `tfp destroy itemspec ... [/keephistory] [/stopat:versionspec] [/preview] [/startcleanup] [/silent]` (SPEC.md "Fáze 4",
    /// protection level 3): the real destroy needs a `/preview` of the same items and switches in the last ten minutes
    /// (<see cref="DestroyPreviewMarker"/>), otherwise TFP0410.
    /// </summary>
    internal sealed class DestroyCommand : ICommand
    {
        public string Name => "destroy";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            bool preview = commandLine.Has("preview");
            bool keepHistory = commandLine.Has("keephistory");
            string? stopAtText = commandLine.Value("stopat");
            if (stopAtText != null && !keepHistory)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "/stopat needs /keephistory.", $"Example: '{toolName} destroy $/Project/old.bin /keephistory /stopat:C1200 /preview'.");
            }

            var items = WorkspaceCommandSupport.Items(commandLine, toolName, false, "destroy $/Project/old.bin /preview");
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
            var summary = context.Workspace?.Summary;
            VersionSpec? stopAt = stopAtText == null ? null : VersionSpecParser.ParseSingle(stopAtText, summary?.Name, summary?.Owner);

            // The items as resolved (server path, deletion id, versionspec): the key covers exactly what the server is asked to destroy.
            var serverItems = items.Select(i => context.ServerPathOf(i, toolName)
                + (i.DeletionId > 0 ? ";X" + i.DeletionId : string.Empty)
                + (i.VersionText == null ? string.Empty : ";" + DestroyPreviewMarker.NormalizeVersion(i.VersionText))).ToList();
            string user = context.Client.AuthorizedUser;
            string key = DestroyPreviewMarker.Key(context.CollectionUrl, user, serverItems, keepHistory, stopAtText);
            if (!preview && !DestroyPreviewMarker.IsValid(key))
            {
                string previewCommand = $"{toolName} destroy /preview {string.Join(" ", items.Select(i => i.Original))}" + (keepHistory ? " /keephistory" : string.Empty) + (stopAtText == null ? string.Empty : " /stopat:" + stopAtText);
                throw new TfpException(ErrorCodes.DestroyNeedsPreview, ExitCodes.GeneralError,
                    "destroy needs a preview of the same items and switches first (none in the last 10 minutes).",
                    $"Run '{previewCommand}', read what it lists, then run the same command without /preview.");
            }

            var destroyed = new List<DestroyedItemInfo>();
            foreach (var item in items)
            {
                string serverPath = context.ServerPathOf(item, toolName);
                var result = context.Client.Destroy(new ItemQuery(serverPath, RecursionType.None, item.DeletionId), item.Version, preview, keepHistory, stopAt, commandLine.Has("startcleanup"), commandLine.Has("silent"));
                destroyed.AddRange(result.Items);
            }

            if (preview)
            {
                DestroyPreviewMarker.Write(key, context.CollectionUrl, user, serverItems, keepHistory, stopAtText);
            }
            else
            {
                // The marker is spent only by a destroy that reached the server; a failed call leaves it for the retry.
                DestroyPreviewMarker.Delete(key);
            }

            if (!commandLine.Has("silent"))
            {
                output.Lines(Render(destroyed, preview));
            }

            output.Success(new JObject
            {
                ["preview"] = preview,
                ["items"] = new JArray(destroyed.Select(d => d.ToJson()).Cast<object>().ToArray()),
            });
            return ExitCodes.Success;
        }

        /// <summary>`Would destroy: $/path` (preview) or `Destroyed: $/path` per item, as the original lists them.</summary>
        internal static IEnumerable<string> Render(IReadOnlyList<DestroyedItemInfo> items, bool preview)
        {
            if (items.Count == 0)
            {
                yield return "There are no items to destroy.";
                yield break;
            }

            foreach (var item in items)
            {
                yield return $"{(preview ? "Would destroy" : "Destroyed")}: {item.ServerPath}{(item.DeletionId > 0 ? ";X" + item.DeletionId : string.Empty)}";
            }
        }

    }

    /// <summary>
    /// `tfp permission itemspec ... [/recursive]` lists the ACLs (level 1); `/allow:`, `/deny:`, `/remove:` with `/user:`
    /// and/or `/group:`, and `/inherit:yes|no` change them (level 3). The identity names go to the server as given.
    /// </summary>
    internal sealed class PermissionCommand : ICommand
    {
        public string Name => "permission";

        /// <summary>The item permission names of tf.exe, in the order the server lists them.</summary>
        internal static readonly string[] PermissionNames =
        {
            "Read", "PendChange", "Checkin", "Label", "Lock", "ReviseOther", "UnlockOther", "UndoOther", "LabelOther", "AdminProjectRights", "CheckinOther", "Merge", "ManageBranch",
        };

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            var allow = Permissions(commandLine, "allow", toolName);
            var deny = Permissions(commandLine, "deny", toolName);
            var remove = Permissions(commandLine, "remove", toolName);
            bool? inherit = Inherit(commandLine, toolName);
            var identities = commandLine.Values("user").Concat(commandLine.Values("group")).ToList();
            bool changing = allow.Count > 0 || deny.Count > 0 || remove.Count > 0 || inherit != null;
            if ((allow.Count > 0 || deny.Count > 0 || remove.Count > 0) && identities.Count == 0)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "/allow, /deny and /remove need /user: or /group:.", $"Example: '{toolName} permission /allow:Lock /user:jan.novak@contoso.com $/Project/Main'.");
            }

            if (identities.Count > 0 && allow.Count == 0 && deny.Count == 0 && remove.Count == 0)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "/user and /group need /allow:, /deny: or /remove:.", $"Example: '{toolName} permission /remove:* /user:jan.novak@contoso.com $/Project/Main'.");
            }

            var items = WorkspaceCommandSupport.Items(commandLine, toolName, false, "permission $/Project/Main");
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
            var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.None;
            var serverPaths = items.Select(i => context.ServerPathOf(i, toolName)).ToList();

            if (!changing)
            {
                var permissions = context.Client.GetPermissions(serverPaths, recursion);
                if (permissions.Count == 0)
                {
                    foreach (string path in serverPaths)
                    {
                        output.ItemError(new TfpException(ErrorCodes.ItemNotFound, ExitCodes.PartialSuccess, $"{path}: no matching items.", $"Check the path with '{toolName} dir'.", item: path));
                    }
                }

                output.Lines(Render(permissions));
                output.Success(new JObject { ["items"] = new JArray(permissions.Select(p => p.ToJson()).Cast<object>().ToArray()) });
                return ExitCodes.Success;
            }

            // /recursive applies the change to every item below the folders (the server stores ACLs per item).
            var targets = new List<string>();
            foreach (string path in serverPaths)
            {
                targets.Add(path);
                if (recursion == RecursionType.Full)
                {
                    targets.AddRange(context.Client.GetItems(new ItemQuery(path, RecursionType.Full), VersionSpec.Latest, false, false)
                        .Select(i => i.ServerPath).Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)));
                }
            }

            var changes = new List<PermissionChangeRequest>();
            foreach (string target in targets.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (identities.Count == 0)
                {
                    changes.Add(new PermissionChangeRequest(target, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), inherit));
                    continue;
                }

                for (int i = 0; i < identities.Count; i++)
                {
                    changes.Add(new PermissionChangeRequest(target, identities[i], allow, deny, remove, i == 0 ? inherit : null));
                }
            }

            var result = context.Client.SetPermissions(changes);
            // The library handles the inheritance and the permission part of a request as two changes, and returns the applied ones
            // with the identity as given (`.` becomes the display name); a failure is reported without the item, in the order of
            // the changes. Without a failure everything was applied. When the counts differ (a validation failure, a rejected
            // save), every skipped part shows all the failure texts.
            var parts = new List<PermissionChangeRequest>();
            foreach (var change in changes)
            {
                if (change.Inherit != null)
                {
                    parts.Add(new PermissionChangeRequest(change.ServerItem, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), change.Inherit));
                }

                if (change.Identity != null)
                {
                    parts.Add(new PermissionChangeRequest(change.ServerItem, change.Identity, change.Allow, change.Deny, change.Remove, null));
                }
            }

            var notApplied = result.Failures.Count == 0
                ? new List<PermissionChangeRequest>()
                : parts.Where(part => !result.Applied.Any(a => string.Equals(a.ServerItem, part.ServerItem, StringComparison.OrdinalIgnoreCase)
                    && (part.Identity == null ? a.Identity == null && a.Inherit == part.Inherit : a.Identity != null && (part.Identity == "." || string.Equals(a.Identity, part.Identity, StringComparison.OrdinalIgnoreCase))))).ToList();
            string allFailures = string.Join(" ", result.Failures.Select(f => f.Message));
            var done = new JArray();
            var skipped = new JArray();
            foreach (var part in parts)
            {
                int index = notApplied.IndexOf(part);
                if (index >= 0)
                {
                    string? serverMessage = result.Failures.Count == notApplied.Count ? result.Failures[index].Message : allFailures;
                    output.ItemError(new TfpException(ErrorCodes.ServerError, ExitCodes.PartialSuccess,
                        $"{part.ServerItem}: the server did not apply the change for {part.Identity ?? "the inheritance"}.",
                        "Check the identity as the server shows it (display name, account or e-mail) and your permission to administer the item.",
                        tfCode: ErrorMapper.ExtractTfCode(serverMessage), serverMessage: string.IsNullOrEmpty(serverMessage) ? null : serverMessage, item: part.ServerItem));
                    skipped.Add(part.ToJson());
                    continue;
                }

                output.Line(part.Identity == null ? $"{part.ServerItem}: inherit {(part.Inherit == true ? "yes" : "no")}" : $"{part.ServerItem}: {part.Identity}");
                done.Add(part.ToJson());
            }

            // tf.exe exits 100 when the server applied nothing at all (an empty result); a partial result stays exit 1 through the item errors.
            int exit = done.Count == 0 && skipped.Count > 0 ? ExitCodes.GeneralError : ExitCodes.Success;
            output.Success(new JObject
            {
                ["changes"] = done,
                ["skipped"] = skipped,
            }, exit);
            return exit;
        }

        /// <summary>`*` or a comma-separated list of permission names (case-insensitive, canonical spelling in the result); an unknown name is TFP0002.</summary>
        internal static List<string> Permissions(CommandLine commandLine, string switchName, string toolName)
        {
            var result = new List<string>();
            foreach (string value in commandLine.Values(switchName))
            {
                if (value == "*")
                {
                    result.AddRange(PermissionNames);
                    continue;
                }

                string? canonical = PermissionNames.FirstOrDefault(p => string.Equals(p, value, StringComparison.OrdinalIgnoreCase));
                if (canonical == null)
                {
                    throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"/{switchName}:{value} is not a permission name.", $"Permissions: {string.Join(", ", PermissionNames)}, or * for all.");
                }

                result.Add(canonical);
            }

            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool? Inherit(CommandLine commandLine, string toolName)
        {
            string? value = commandLine.Value("inherit");
            if (value == null)
            {
                return null;
            }

            if (value.Equals("yes", StringComparison.OrdinalIgnoreCase)) { return true; }
            if (value.Equals("no", StringComparison.OrdinalIgnoreCase)) { return false; }
            throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"/inherit:{value} is not yes or no.", $"Example: '{toolName} permission /inherit:no $/Project/Main/Secret'.");
        }

        /// <summary>The listing of the original (golden `permission/folder`): one `====` block per item, an indented entry per identity.</summary>
        internal static IEnumerable<string> Render(IReadOnlyList<ItemPermissionInfo> items)
        {
            foreach (var item in items)
            {
                yield return new string('=', 79);
                yield return $"Server item: {item.ServerItem} (Inherit: {(item.Inherit ? "Yes" : "No")})";
                foreach (var entry in item.Entries.OrderBy(e => e.Identity, StringComparer.InvariantCultureIgnoreCase))
                {
                    yield return $"  Identity: {entry.Identity}";
                    yield return $"    Allow:             {string.Join(", ", entry.Allow)}";
                    yield return $"    Deny:              {string.Join(", ", entry.Deny)}";
                    yield return $"    Allow (inherited): {string.Join(", ", entry.AllowInherited)}";
                    yield return $"    Deny (inherited):  {string.Join(", ", entry.DenyInherited)}";
                    yield return string.Empty;
                }
            }
        }
    }

    /// <summary>
    /// `tfp workspace /new name [/comment:] [/computer:] [/template:ws[;owner]] [/permission:] [/location:local] [/filetime:]`,
    /// `tfp workspace /delete name[;owner]`, `tfp workspace name [/newname:] [/comment:] [/computer:] [/permission:] [/filetime:]`
    /// (level 3) and `tfp workspace name` (the detailed listing, level 1). SPEC.md "Fáze 4".
    /// </summary>
    internal sealed class WorkspaceCommand : ICommand
    {
        public string Name => "workspace";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            if (commandLine.Positionals.Count > 1)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "workspace takes at most one workspace name.", $"Example: '{toolName} workspace /new MyWs /comment:\"Build box\"'.");
            }

            string? location = commandLine.Value("location");
            if (location != null && !location.Equals("local", StringComparison.OrdinalIgnoreCase))
            {
                throw new TfpException(ErrorCodes.ServerWorkspaceNotSupported, ExitCodes.WorkspaceNotFound, $"/location:{location} is not supported; {toolName} works with local workspaces only.", "Omit /location or pass /location:local.");
            }

            if (!commandLine.Has("new") && (commandLine.Has("template") || commandLine.Has("location")))
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "/template and /location apply to workspace /new only.", $"Example: '{toolName} workspace /new MyWs /template:Other'.");
            }

            string? permission = Permission(commandLine, toolName);
            bool? fileTime = FileTime(commandLine, toolName);
            string? comment = CommentText.Optional(commandLine, toolName);
            string? computer = commandLine.Value("computer");

            if (commandLine.Has("new"))
            {
                if (commandLine.Positionals.Count == 0)
                {
                    throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "workspace /new needs the workspace name.", $"Example: '{toolName} workspace /new MyWs'. tf.exe defaults to the computer name; {toolName} wants it explicit.");
                }

                var (name, owner) = ShelvesetSpec.Parse(commandLine.Positionals[0], toolName);
                using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
                string me = context.Client.AuthorizedUser;
                if (owner != null && !string.Equals(owner, me, StringComparison.OrdinalIgnoreCase))
                {
                    throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"A workspace can be created only for the authenticated user, not for {owner}.", $"Drop the owner: '{toolName} workspace /new {name}'.");
                }

                IReadOnlyList<MappingInfo> mappings;
                string? template = commandLine.Value("template");
                if (template != null)
                {
                    var (templateName, templateOwner) = ShelvesetSpec.Parse(template, toolName);
                    var source = context.Client.GetWorkspace(templateName, templateOwner);
                    if (source == null)
                    {
                        throw new TfpException(ErrorCodes.WorkspaceNotFound, ExitCodes.WorkspaceNotFound, $"Template workspace '{template}' does not exist in {context.CollectionUrl}.", $"'{toolName} workspaces /owner:*' lists the workspaces.");
                    }

                    mappings = source.Summary.Mappings;
                }
                else
                {
                    // tf.exe maps $/ to the current folder when nothing else is said.
                    mappings = new[] { new MappingInfo(Microsoft.TeamFoundation.VersionControl.Common.VersionControlPath.RootFolder, Directory.GetCurrentDirectory(), false, false) };
                }

                var created = context.Client.CreateWorkspace(new WorkspaceDefinition(name, comment, computer, mappings, permission, fileTime));
                try
                {
                    context.Client.UpdateWorkspaceCache();
                }
                catch (Exception)
                {
                    // The cache is an optimization; the next run finds the workspace on the server.
                }

                output.Line($"Workspace {created.Spec} created.");
                output.Lines(WorkspacesCommand.RenderDetailed(new[] { created }));
                output.Success(new JObject { ["workspace"] = created.ToJson(), ["created"] = true });
                return ExitCodes.Success;
            }

            if (commandLine.Has("delete"))
            {
                if (commandLine.Positionals.Count == 0)
                {
                    throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "workspace /delete needs the workspace name.", $"Example: '{toolName} workspace /delete MyWs'.");
                }

                var (name, owner) = ShelvesetSpec.Parse(commandLine.Positionals[0], toolName);
                using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
                var existing = context.Client.GetWorkspace(name, owner);
                if (existing == null)
                {
                    throw new TfpException(ErrorCodes.WorkspaceNotFound, ExitCodes.WorkspaceNotFound, $"Workspace '{commandLine.Positionals[0]}' does not exist in {context.CollectionUrl}.", $"'{toolName} workspaces /owner:*' lists the workspaces.");
                }

                int pending = 0;
                try
                {
                    pending = existing.GetPendingChanges(new[] { new ItemQuery(Microsoft.TeamFoundation.VersionControl.Common.VersionControlPath.RootFolder, RecursionType.Full) }, false).Pending.Count;
                }
                catch (Exception)
                {
                    // A workspace of another computer may refuse the query; the delete goes on.
                }

                context.Client.DeleteWorkspace(name, owner ?? existing.Summary.OwnerUniqueName);
                if (pending > 0)
                {
                    output.Warning($"{pending} pending change(s) of workspace {existing.Summary.Spec} were dropped with it.");
                }

                output.Line($"Workspace {existing.Summary.Spec} deleted.");
                output.Success(new JObject { ["workspace"] = existing.Summary.ToJson(), ["deleted"] = true, ["droppedPendingChanges"] = pending });
                return ExitCodes.Success;
            }

            string? newName = commandLine.Value("newname");
            bool editing = newName != null || comment != null || computer != null || permission != null || fileTime != null;
            using (var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: commandLine.Positionals.Count == 0))
            {
                string name;
                string? owner;
                if (commandLine.Positionals.Count == 1)
                {
                    (name, owner) = ShelvesetSpec.Parse(commandLine.Positionals[0], toolName);
                }
                else
                {
                    var current = context.RequireWorkspace(toolName).Summary;
                    name = current.Name;
                    owner = current.OwnerUniqueName;
                }

                if (!editing)
                {
                    var workspace = context.Client.GetWorkspace(name, owner);
                    if (workspace == null)
                    {
                        throw new TfpException(ErrorCodes.WorkspaceNotFound, ExitCodes.WorkspaceNotFound, $"Workspace '{name}' does not exist in {context.CollectionUrl}.", $"'{toolName} workspaces /owner:*' lists the workspaces.");
                    }

                    output.Lines(WorkspacesCommand.RenderDetailed(new[] { workspace.Summary }));
                    output.Success(new JObject { ["workspace"] = workspace.Summary.ToJson() });
                    return ExitCodes.Success;
                }

                var updated = context.Client.UpdateWorkspace(name, owner, newName, new WorkspaceDefinition(name, comment, computer, null, permission, fileTime));
                try
                {
                    context.Client.UpdateWorkspaceCache();
                }
                catch (Exception)
                {
                    // Cache refresh is an optimization only.
                }

                output.Line($"Workspace {updated.Spec} updated.");
                output.Lines(WorkspacesCommand.RenderDetailed(new[] { updated }));
                output.Success(new JObject { ["workspace"] = updated.ToJson(), ["updated"] = true });
                return ExitCodes.Success;
            }
        }

        private static string? Permission(CommandLine commandLine, string toolName)
        {
            string? value = commandLine.Value("permission");
            if (value == null)
            {
                return null;
            }

            foreach (string profile in new[] { "Private", "PublicLimited", "Public" })
            {
                if (profile.Equals(value, StringComparison.OrdinalIgnoreCase))
                {
                    return profile;
                }
            }

            throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"/permission:{value} is not Private, PublicLimited or Public.", $"Example: '{toolName} workspace /new Build /permission:PublicLimited'.");
        }

        private static bool? FileTime(CommandLine commandLine, string toolName)
        {
            string? value = commandLine.Value("filetime");
            if (value == null)
            {
                return null;
            }

            if (value.Equals("checkin", StringComparison.OrdinalIgnoreCase)) { return true; }
            if (value.Equals("current", StringComparison.OrdinalIgnoreCase)) { return false; }
            throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"/filetime:{value} is not current or checkin.", $"Example: '{toolName} workspace /new Build /filetime:checkin'.");
        }
    }

    /// <summary>
    /// `tfp property itemspec ... [/version:] [/recursive] [/propertynames:] [/output:]` lists the versioned-item properties (level 1);
    /// `/setvalues:name=value;...`, `/deletevalues:name`, `/deleteall` pend a property change in the workspace (level 1, `/lock:checkin|checkout` level 2), as tf.exe 18.10
    /// (docs/tf-reference/property.md).
    /// </summary>
    internal sealed class PropertyCommand : ICommand
    {
        public string Name => "property";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            var lockLevel = WorkspaceCommandSupport.LockLevelOf(commandLine);
            var items = WorkspaceCommandSupport.Items(commandLine, toolName, false, "property Main\\src\\App.cs");
            bool set = commandLine.Has("setvalues");
            bool delete = commandLine.Has("deletevalues");
            bool deleteAll = commandLine.Has("deleteall");
            bool writes = set || delete || deleteAll;
            RejectCombinations(commandLine, toolName);

            if (writes)
            {
                var properties = new List<KeyValuePair<string?, object?>>();
                if (set)
                {
                    properties.AddRange(ParseSetValues(commandLine.Value("setvalues")!, toolName));
                }

                foreach (string name in commandLine.Values("deletevalues"))
                {
                    properties.Add(new KeyValuePair<string?, object?>(name, null));
                }

                if (deleteAll)
                {
                    properties.Add(new KeyValuePair<string?, object?>(null, null));
                }

                if (properties.Count == 0)
                {
                    throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "/deletevalues needs at least one property name.", $"Example: '{toolName} property /deletevalues:owner Main\\src\\App.cs'.");
                }

                using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: true);
                var workspace = context.RequireWorkspace(toolName);
                var paths = WorkspaceCommandSupport.LocalPaths(items, context, toolName);
                var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.None;
                var result = workspace.PendPropertyChange(paths, properties, recursion, commandLine.Has("silent"), lockLevel);
                output.Lines(WorkspaceCommandSupport.GroupedByFolder(result.Changes, WorkspaceCommandSupport.NameOf));
                var skipped = WorkspaceCommandSupport.ReportFailures(output, result.Failures, toolName);
                output.Success(WorkspaceCommandSupport.ChangesJson(result.Changes, skipped));
                return ExitCodes.Success;
            }

            return List(commandLine, output, toolName, items);
        }

        private static int List(CommandLine commandLine, OutputWriter output, string toolName, List<ParsedItemSpec> items)
        {
            var names = commandLine.Values("propertynames");
            string? outputPath = commandLine.Value("output");
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
            var summary = context.Workspace?.Summary;
            var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.None;
            string? versionText = commandLine.Value("version");
            var version = versionText == null ? VersionSpec.Latest : VersionSpecParser.ParseSingle(versionText, summary?.Name, summary?.Owner);

            var queries = items.Select(i => new ItemQuery(context.ServerPathOf(i, toolName), recursion, i.DeletionId)).ToList();
            var properties = context.Client.GetItemProperties(queries, version, names);
            if (properties.Count == 0)
            {
                // The server returns nothing for an item without properties: list the items asked for.
                properties = queries.Select(q => new ItemPropertiesInfo(q.Path, Array.Empty<KeyValuePair<string, string?>>())).ToList();
            }

            string? written = null;
            if (outputPath != null)
            {
                // tf.exe writes the first value of the one requested property: bytes for a binary value, text otherwise.
                string name = names[0];
                var holder = properties.FirstOrDefault(p => p.Properties.Any(v => string.Equals(v.Key, name, StringComparison.OrdinalIgnoreCase)));
                if (holder == null)
                {
                    output.Warning($"No item has the property '{name}'; nothing was written to {outputPath}.");
                }
                else
                {
                    string fullPath = Path.GetFullPath(outputPath);
                    if (holder.BinaryValues.TryGetValue(name, out var bytes))
                    {
                        File.WriteAllBytes(fullPath, bytes);
                    }
                    else
                    {
                        string? value = holder.Properties.First(v => string.Equals(v.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
                        File.WriteAllText(fullPath, value ?? string.Empty, new UTF8Encoding(false));
                    }

                    written = fullPath;
                    output.Line($"Wrote {name} of {holder.ServerPath} to {fullPath}.");
                }
            }
            else
            {
                output.Lines(Render(properties));
            }

            output.Success(new JObject
            {
                ["items"] = new JArray(properties.Select(p => p.ToJson()).Cast<object>().ToArray()),
                ["output"] = written,
            });
            return ExitCodes.Success;
        }

        /// <summary>The switch combinations tf.exe refuses (exit 100): a write form with a read filter, `/output` or `/version`, `/output` without one property name or with `/recursive`.</summary>
        internal static void RejectCombinations(CommandLine commandLine, string toolName)
        {
            bool set = commandLine.Has("setvalues");
            bool delete = commandLine.Has("deletevalues");
            bool deleteAll = commandLine.Has("deleteall");
            bool outputFile = commandLine.Has("output");
            string? conflict = null;
            if (set && delete) { conflict = "/setvalues and /deletevalues"; }
            else if (set && deleteAll) { conflict = "/setvalues and /deleteall"; }
            else if ((set || delete || deleteAll) && outputFile) { conflict = "a property change and /output"; }
            else if ((set || delete || deleteAll) && commandLine.Has("version")) { conflict = "a property change and /version"; }
            else if ((set || delete || deleteAll) && commandLine.Has("propertynames")) { conflict = "a property change and /propertynames"; }
            else if ((set || delete || deleteAll) && commandLine.Has("collection")) { conflict = "a property change and /collection"; }

            if (conflict != null)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"{conflict} cannot be combined.",
                    $"Change properties in the workspace ('{toolName} property /setvalues:name=value <item>') or list them ('{toolName} property /propertynames:name <item>'), not both.");
            }

            if (outputFile)
            {
                var names = commandLine.Values("propertynames");
                if (names.Length != 1)
                {
                    throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "/output needs exactly one property name in /propertynames.",
                        $"Example: '{toolName} property /propertynames:icon /output:icon.bin Main\\src\\App.cs'.");
                }

                if (commandLine.Has("recursive"))
                {
                    throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "/output and /recursive cannot be combined.", "Name one item.");
                }
            }
        }

        /// <summary>
        /// `name=value[;name=value...]` of tf.exe: `;;` is a literal semicolon, `==` a literal equals sign; a value `@file` reads the file
        /// as a binary value. An empty name or value is TFP0002.
        /// </summary>
        internal static List<KeyValuePair<string?, object?>> ParseSetValues(string text, string toolName)
        {
            var result = new List<KeyValuePair<string?, object?>>();
            foreach (string pair in SplitUnescaped(text, ';'))
            {
                var parts = SplitUnescaped(pair, '=');
                string name = parts.Count > 0 ? parts[0] : string.Empty;
                string value = parts.Count > 1 ? string.Join("=", parts.Skip(1)) : string.Empty;
                if (parts.Count < 2 || name.Length == 0 || value.Length == 0)
                {
                    throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"/setvalues: '{pair}' is not name=value.",
                        $"Example: '{toolName} property /setvalues:owner=team;icon=@icon.png Main\\src\\App.cs' (';;' and '==' for literal separators).");
                }

                if (value.Length > 1 && value[0] == '@')
                {
                    string file = Path.GetFullPath(value.Substring(1));
                    if (!File.Exists(file))
                    {
                        throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"/setvalues: the file {file} of the binary value '{name}' does not exist.", "A value that starts with @ names a file whose bytes become the property value.");
                    }

                    result.Add(new KeyValuePair<string?, object?>(name, File.ReadAllBytes(file)));
                    continue;
                }

                result.Add(new KeyValuePair<string?, object?>(name, value));
            }

            return result;
        }

        /// <summary>Splits on a separator unless it is doubled; a doubled separator becomes one literal character.</summary>
        internal static List<string> SplitUnescaped(string text, char separator)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == separator)
                {
                    if (i + 1 < text.Length && text[i + 1] == separator)
                    {
                        current.Append(separator);
                        i++;
                        continue;
                    }

                    result.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(text[i]);
            }

            result.Add(current.ToString());
            return result;
        }

        /// <summary>The block of the original (golden `property/read`): a dashed line, `Server item:`, then the properties or a note.</summary>
        internal static IEnumerable<string> Render(IReadOnlyList<ItemPropertiesInfo> items)
        {
            foreach (var item in items)
            {
                yield return new string('-', 79);
                yield return $"Server item: {item.ServerPath}";
                if (item.Properties.Count == 0)
                {
                    yield return "The item has no properties.";
                    continue;
                }

                foreach (var property in item.Properties)
                {
                    yield return $"{property.Key} = {property.Value}";
                }
            }
        }
    }

    /// <summary>`tfp resolvepath itemspec ...`: a local path gives its server path and a server path its local path (level 1).</summary>
    internal sealed class ResolvePathCommand : ICommand
    {
        public string Name => "resolvepath";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            var items = WorkspaceCommandSupport.Items(commandLine, toolName, false, "resolvepath Main\\src\\App.cs");
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: true);
            var workspace = context.RequireWorkspace(toolName);

            var results = new JArray();
            foreach (var item in items)
            {
                string? other = workspace.ResolvePath(item.Path, item.IsServerPath);
                if (other == null)
                {
                    output.ItemError(new TfpException(ErrorCodes.PathNotMapped, ExitCodes.WorkspaceNotFound, $"{item.Path} is not mapped in workspace {context.WorkspaceSpec}.",
                        $"'{toolName} workfold' shows the mappings.", item: item.Path));
                    continue;
                }

                output.Line($"{item.Path} -> {other}");
                results.Add(new JObject
                {
                    ["localPath"] = item.IsServerPath ? other : item.Path,
                    ["serverPath"] = item.IsServerPath ? item.Path : other,
                });
            }

            output.Success(new JObject { ["items"] = results });
            return ExitCodes.Success;
        }
    }

    /// <summary>
    /// `tfp docs [command] [/url]` (alias `msdn`): opens the TfPortable documentation on GitHub in the default browser and says
    /// which page; `/url` only prints the URL. A browser that cannot start is a warning, not an error.
    /// </summary>
    internal sealed class DocsCommand : ICommand
    {
        public const string Repository = "https://github.com/Gh61/TfPortable";

        /// <summary>Opens a URL in the default browser; replaceable for tests.</summary>
        public static Action<string> Opener { get; set; } = url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        public string Name => "docs";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            if (commandLine.Positionals.Count > 1)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "docs takes at most one command name.", $"Example: '{toolName} docs checkin'.");
            }

            string? command = commandLine.Positionals.Count == 1 ? commandLine.Positionals[0] : null;
            string url = Url(command, toolName);
            bool opened = false;
            if (commandLine.Has("url"))
            {
                output.Line(url);
            }
            else
            {
                try
                {
                    Opener(url);
                    opened = true;
                    output.Line($"Opened {url} in the default browser.");
                }
                catch (Exception ex)
                {
                    output.Line(url);
                    output.Warning($"The browser could not be started: {ex.Message}", hint: "Open the URL above by hand.");
                }
            }

            output.Success(new JObject { ["url"] = url, ["opened"] = opened });
            return ExitCodes.Success;
        }

        /// <summary>The repository page, or `docs/commands.md` at the anchor of the canonical command name (TFP0001 for an unknown one).</summary>
        internal static string Url(string? command, string toolName)
        {
            if (string.IsNullOrEmpty(command))
            {
                return Repository;
            }

            var spec = CommandCatalog.Find(command!) ?? throw HelpCommand.UnknownCommand(command!, toolName);
            return $"{Repository}/blob/main/docs/commands.md#cmd-{spec.Name}";
        }
    }
}
