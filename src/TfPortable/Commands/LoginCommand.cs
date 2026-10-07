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
    /// <summary>
    /// `tfp login [/collection:&lt;url&gt;] [/pat:-]`: asks for a PAT, verifies it over REST, stores it, then checks the
    /// version control (SOAP) connection and the Code scope (SPEC.md "Příkazy pro správu").
    /// </summary>
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

            // 1. Verify the token over REST; this is independent of the SOAP client.
            var rest = ConnectionFactory.CheckPatOverRest(collectionUrl, pat);
            if (!rest.Ok)
            {
                throw new TfpException(ErrorCodes.PatRejected, ExitCodes.Authentication,
                    $"The server {normalized} rejected the PAT ({pat.Length} characters); nothing was stored. {rest.Summary}",
                    $"Check that the token was copied completely, is not expired and belongs to this organization. Alternative input without the hidden prompt: 'Get-Clipboard | {toolName} login /collection:{normalized} /pat:-'.");
            }

            // 2. Store it.
            var storedAt = DateTimeOffset.Now;
            CredentialStore.Write(normalized, pat, rest.UniqueName, storedAt);

            // 3. Check the version control connection and the Code scope; problems are warnings, the token is valid.
            bool? codeScope = null;
            string? soapIdentity = null;
            using (var connection = ConnectionFactory.Open(collectionUrl, pat))
            {
                try
                {
                    var identity = ConnectionFactory.Authenticate(connection);
                    soapIdentity = identity.DisplayName;
                    codeScope = ConnectionFactory.HasVersionControlAccess(connection);
                }
                catch (Exception ex)
                {
                    var mapped = ErrorMapper.Map(ex, toolName, normalized);
                    if (mapped.Code == ErrorCodes.PatRejected)
                    {
                        // REST accepted the token a moment ago, so the SOAP refusal means a scope problem.
                        output.Warning("The PAT is stored, but the version control (TFVC) API refused it. That API works only with a PAT that has all scopes (Full access); a token with custom scopes such as 'Code: Read & write' is not enough.",
                            ErrorCodes.PatScope, $"Create a new PAT with 'Scopes: Full access' and run '{toolName} login /collection:{normalized}' again.");
                    }
                    else
                    {
                        output.Warning($"The PAT is stored, but the version control connection failed: {mapped.Code} {mapped.ServerMessage ?? mapped.Message}", mapped.Code, mapped.Hint);
                    }
                }
            }

            if (codeScope == false)
            {
                output.Warning("The PAT was stored, but the server refused a version control query. The token probably lacks the scope 'Code: Read & write'.", ErrorCodes.PatScope);
            }

            output.Line($"PAT for {normalized} stored in the Windows Credential Manager as {rest.DisplayName} ({rest.UniqueName ?? "unique name unknown"}).");
            if (soapIdentity != null)
            {
                output.Line($"Version control connection verified ({soapIdentity}).");
            }

            output.Line($"Check it with '{toolName} whoami', remove it with '{toolName} logout /collection:{normalized}'.");

            output.Success(new JObject
            {
                ["collection"] = normalized,
                ["identity"] = new JObject { ["displayName"] = rest.DisplayName, ["uniqueName"] = rest.UniqueName, ["id"] = rest.Id },
                ["stored"] = true,
                ["storedAt"] = OutputWriter.IsoDate(storedAt),
                ["codeScope"] = codeScope,
                ["versionControlConnection"] = soapIdentity != null,
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
