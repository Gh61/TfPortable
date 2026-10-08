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
    /// <summary>
    /// `tfp view itemspec [/version:spec] [/output:file] [/shelveset:name[;owner]] [/recursive] [/console]`: the content of
    /// a file, decoded by its server encoding and written as UTF-8 (docs/compat.md §11); binary content needs /output
    /// (TFP0303); not found is TFP0301 with exit 1. /recursive prints several files, each preceded by its path.
    /// </summary>
    internal sealed class ViewCommand : ICommand
    {
        public string Name => "view";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            if (commandLine.Positionals.Count != 1)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "view needs exactly one itemspec.", $"Example: '{toolName} view $/Project/Main/src/App.cs /version:C1234'.");
            }

            string? outputFile = commandLine.Value("output");
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
            var workspace = context.Workspace?.Summary;
            var item = ItemSpecParser.Parse(commandLine.Positionals[0], workspace?.Name, workspace?.Owner);
            string path = context.ServerPathOf(item, toolName);

            VersionSpec version = item.Version ?? VersionSpec.Latest;
            string? versionText = commandLine.Value("version");
            if (!string.IsNullOrEmpty(versionText))
            {
                var range = VersionSpecParser.ParseRange(versionText!, workspace?.Name, workspace?.Owner);
                if (range.IsRange)
                {
                    throw new TfpException(ErrorCodes.InvalidVersionSpec, ExitCodes.GeneralError, "view takes a single version, not a range.", "Example: /version:C1234.");
                }

                version = range.To;
            }

            string? shelveset = commandLine.Value("shelveset");
            // /recursive and wildcards may match several files; /output is then a directory, even for a single match.
            bool many = shelveset == null && (commandLine.Has("recursive") || item.HasWildcard);
            var contents = new List<ItemContent>();
            if (shelveset != null)
            {
                string[] parts = shelveset.Split(new[] { ';' }, 2);
                var shelved = context.Client.DownloadShelved(parts[0], parts.Length > 1 ? parts[1] : null, path);
                if (shelved == null)
                {
                    throw new TfpException(ErrorCodes.ItemNotFound, ExitCodes.PartialSuccess, $"{path}: no matching file in shelveset {shelveset}.",
                        $"'{toolName} status /shelveset:{shelveset}' lists the shelved changes.", item: path);
                }

                contents.Add(shelved);
            }
            else if (many)
            {
                // A wildcard itemspec goes through the listing too: GetItem rejects wildcards before any server call.
                IReadOnlyList<ItemContent> found;
                try
                {
                    found = context.Client.DownloadItems(new ItemQuery(path, commandLine.Has("recursive") ? RecursionType.Full : RecursionType.None, item.DeletionId), version, item.DeletionId > 0);
                }
                catch (Exception ex) when (ErrorMapper.Map(ex, toolName, context.CollectionUrl).Code == ErrorCodes.ItemNotFound)
                {
                    throw NotFound(path, toolName, ex);
                }

                if (found.Count == 0)
                {
                    throw NotFound(path, toolName);
                }

                contents.AddRange(found);
            }
            else
            {
                ItemContent content;
                try
                {
                    content = context.Client.Download(path, version, item.DeletionId);
                }
                catch (Exception ex) when (ErrorMapper.Map(ex, toolName, context.CollectionUrl).Code == ErrorCodes.ItemNotFound)
                {
                    throw NotFound(path, toolName, ex);
                }

                if (content.Item.IsFolder)
                {
                    throw new TfpException(ErrorCodes.ItemNotFound, ExitCodes.PartialSuccess, $"{content.Item.ServerPath} is a folder; view shows files.",
                        $"Use '{toolName} dir {content.Item.ServerPath}' to list it, or /recursive to view the files under it.", item: content.Item.ServerPath);
                }

                contents.Add(content);
            }

            var data = new JArray();
            foreach (var content in contents)
            {
                string? text = TextContent.Decode(content.Bytes, content.Item.Encoding);
                string? written = null;
                if (outputFile != null)
                {
                    written = many ? Path.Combine(Path.GetFullPath(outputFile), Microsoft.TeamFoundation.VersionControl.Common.VersionControlPath.GetFileName(content.Item.ServerPath)) : Path.GetFullPath(outputFile);
                    Directory.CreateDirectory(Path.GetDirectoryName(written)!);
                    File.WriteAllBytes(written, content.Bytes);
                    output.Line($"{content.Item.DisplayPath};C{content.Item.ChangesetId} written to {written} ({content.Bytes.Length} bytes).");
                }
                else if (text == null)
                {
                    throw new TfpException(ErrorCodes.BinaryContent, ExitCodes.GeneralError, $"{content.Item.ServerPath} is binary ({content.Bytes.Length} bytes); it cannot be written to the console.",
                        $"Use '{toolName} view {content.Item.ServerPath} /output:<file>' to save it.", item: content.Item.ServerPath);
                }
                else
                {
                    if (contents.Count > 1)
                    {
                        output.Line($"{content.Item.ServerPath};C{content.Item.ChangesetId}:");
                    }

                    // The final line ending is supplied by the console; a CRLF file must not end in a stray CR.
                    output.Line(text.EndsWith("\r\n", StringComparison.Ordinal) ? text.Substring(0, text.Length - 2)
                        : text.EndsWith("\n", StringComparison.Ordinal) ? text.Substring(0, text.Length - 1) : text);
                }

                data.Add(new JObject
                {
                    ["item"] = content.Item.ToJson(context.Workspace?.GetLocalItemForServerItem(content.Item.ServerPath)),
                    ["version"] = content.Item.ChangesetId,
                    ["content"] = written == null ? text : null,
                    ["output"] = written,
                });
            }

            var first = (JObject)data[0];
            var result = new JObject { ["item"] = first["item"], ["version"] = first["version"], ["content"] = first["content"], ["output"] = first["output"] };
            if (data.Count > 1)
            {
                result["items"] = data;
            }

            output.Success(result);
            return ExitCodes.Success;
        }

        private static TfpException NotFound(string path, string toolName, Exception? inner = null) =>
            new TfpException(ErrorCodes.ItemNotFound, ExitCodes.PartialSuccess, $"{path}: no matching files.",
                $"Check the path and version with '{toolName} dir' or '{toolName} history'.", ErrorMapper.ExtractTfCode(inner?.Message), inner?.Message, path, inner: inner);
    }
}
