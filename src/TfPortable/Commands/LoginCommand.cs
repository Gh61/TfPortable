using System;
using System.IO;
using Microsoft.TeamFoundation.VersionControl.Client;
using Newtonsoft.Json.Linq;
using TfPortable.Auth;
using TfPortable.Cli;
using TfPortable.Errors;
using TfPortable.Output;

namespace TfPortable.Commands
{
    /// <summary>`tfp login [/collection:&lt;url&gt;] [/pat:-]`: asks for a PAT, verifies it and stores it (SPEC.md "Příkazy pro správu").</summary>
    internal sealed class LoginCommand : ICommand
    {
        public string Name => "login";

        public string[] Switches => new[] { "pat" };

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            string collectionUrl = ResolveCollection(commandLine, toolName);
            string normalized = CollectionUrl.Normalize(collectionUrl);
            output.Collection = normalized;

            string pat;
            string? patSwitch = commandLine.Value("pat");
            if (patSwitch == "-")
            {
                pat = PatPrompt.ReadFromStdin();
            }
            else if (patSwitch != null)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "/pat accepts only '-' (read the token from standard input).",
                    "Never pass the token on the command line; use '/pat:-' and pipe it, or run without /pat for a hidden prompt.");
            }
            else if (PatPrompt.IsInteractive)
            {
                pat = PatPrompt.ReadHidden(normalized);
            }
            else
            {
                throw new TfpException(ErrorCodes.PatMissing, ExitCodes.Authentication,
                    "login needs an interactive terminal to ask for the PAT, or '/pat:-' to read it from standard input.",
                    $"Run '{toolName} login /collection:{normalized}' in your own terminal.");
            }

            bool codeScope;
            Microsoft.TeamFoundation.Framework.Client.TeamFoundationIdentity identity;
            using (var connection = ConnectionFactory.Open(collectionUrl, pat))
            {
                try
                {
                    identity = ConnectionFactory.Authenticate(connection, normalized, toolName);
                }
                catch (Exception ex)
                {
                    var mapped = ErrorMapper.Map(ex, toolName, normalized);
                    string diagnostics = ConnectionFactory.RestDiagnostics(collectionUrl, pat);
                    throw new TfpException(mapped.Code == ErrorCodes.PatRejected ? ErrorCodes.PatRejected : mapped.Code, mapped.ExitCode,
                        mapped.Code == ErrorCodes.PatRejected ? $"The server {normalized} rejected the PAT ({pat.Length} characters); nothing was stored. {diagnostics}" : $"{mapped.Message} {diagnostics}",
                        mapped.Code == ErrorCodes.PatRejected
                            ? $"Check that the token was copied completely, is not expired and belongs to this organization. Alternative input without the hidden prompt: 'Get-Clipboard | {toolName} login /collection:{normalized} /pat:-'. Add /verbose for the full exception."
                            : mapped.Hint,
                        mapped.TfCode, mapped.ServerMessage, inner: ex);
                }

                codeScope = ConnectionFactory.HasVersionControlAccess(connection);
            }

            var storedAt = DateTimeOffset.Now;
            CredentialStore.Write(normalized, pat, identity.UniqueName, storedAt);

            if (!codeScope)
            {
                output.Warning($"The PAT was stored, but the server refused a version control query. The token probably lacks the scope 'Code: Read & write'.", ErrorCodes.PatScope);
            }

            output.Line($"PAT for {normalized} stored in the Windows Credential Manager as {identity.DisplayName} ({identity.UniqueName}).");
            output.Line($"Check it with '{toolName} whoami', remove it with '{toolName} logout /collection:{normalized}'.");

            output.Success(new JObject
            {
                ["collection"] = normalized,
                ["identity"] = new JObject { ["displayName"] = identity.DisplayName, ["uniqueName"] = identity.UniqueName, ["id"] = identity.TeamFoundationId.ToString() },
                ["stored"] = true,
                ["storedAt"] = OutputWriter.IsoDate(storedAt),
                ["codeScope"] = codeScope,
            });
            return ExitCodes.Success;
        }

        /// <summary>/collection, else the workspace cache of the current directory, else TFP_COLLECTION.</summary>
        internal static string ResolveCollection(CommandLine commandLine, string toolName)
        {
            string? url = commandLine.Value("collection");
            if (!string.IsNullOrWhiteSpace(url))
            {
                return url!;
            }

            string? dir = Directory.GetCurrentDirectory();
            while (dir != null)
            {
                var info = Workstation.Current.GetLocalWorkspaceInfo(dir);
                if (info != null)
                {
                    return info.ServerUri.AbsoluteUri;
                }

                dir = Path.GetDirectoryName(dir);
            }

            url = Environment.GetEnvironmentVariable("TFP_COLLECTION");
            if (!string.IsNullOrWhiteSpace(url))
            {
                return url!;
            }

            throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError,
                "No collection given and the current directory is not inside a known workspace.",
                $"Pass /collection:<url>, e.g. '{toolName} {commandLine.Command} /collection:https://dev.azure.com/<organization>'.");
        }
    }

    /// <summary>`tfp logout [/collection:&lt;url&gt;]`: removes the stored PAT.</summary>
    internal sealed class LogoutCommand : ICommand
    {
        public string Name => "logout";

        public string[] Switches => Array.Empty<string>();

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            string normalized = CollectionUrl.Normalize(LoginCommand.ResolveCollection(commandLine, toolName));
            output.Collection = normalized;
            bool removed = CredentialStore.Delete(normalized);
            output.Line(removed
                ? $"PAT for {normalized} removed from the Windows Credential Manager."
                : $"No PAT was stored for {normalized}.");
            output.Success(new JObject { ["collection"] = normalized, ["removed"] = removed });
            return ExitCodes.Success;
        }
    }
}
