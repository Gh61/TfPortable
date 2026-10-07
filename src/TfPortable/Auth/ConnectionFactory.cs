using System;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.VisualStudio.Services.Common;
using TfPortable.Errors;

namespace TfPortable.Auth
{
    /// <summary>Opens a collection with a PAT (SPEC.md "Tok při každém příkazu").</summary>
    internal static class ConnectionFactory
    {
        /// <summary>
        /// Resolves the PAT for the collection: stored one, or an interactive prompt that verifies and stores it.
        /// Without an interactive terminal fails with TFP0101 and the exact login command.
        /// </summary>
        public static TfsTeamProjectCollection Connect(string collectionUrl, string toolName, out StoredPat stored)
        {
            string normalized = CollectionUrl.Normalize(collectionUrl);
            StoredPat? existing = CredentialStore.Read(normalized);
            if (existing == null)
            {
                if (!PatPrompt.IsInteractive)
                {
                    throw new TfpException(ErrorCodes.PatMissing, ExitCodes.Authentication,
                        $"No Personal Access Token is stored for collection {normalized}.",
                        $"Run '{toolName} login /collection:{normalized}' in your own terminal (not through an AI agent), then repeat this command.");
                }

                string pat = PatPrompt.ReadHidden(normalized);
                var connection = Open(collectionUrl, pat);
                var identity = Authenticate(connection, normalized, toolName);
                CredentialStore.Write(normalized, pat, identity.UniqueName, DateTimeOffset.Now);
                stored = CredentialStore.Read(normalized)!;
                return connection;
            }

            stored = existing;
            return Open(collectionUrl, existing.Pat);
        }

        public static TfsTeamProjectCollection Open(string collectionUrl, string pat)
        {
            var credentials = new VssCredentials(new VssBasicCredential(string.Empty, pat))
            {
                PromptType = CredentialPromptType.DoNotPrompt,
            };
            var uri = new Uri(collectionUrl.Trim().TrimEnd('/'), UriKind.Absolute);
            return new TfsTeamProjectCollection(uri, credentials);
        }

        /// <summary>Authenticates and returns the identity. Exceptions are mapped by the caller through ErrorMapper.</summary>
        public static Microsoft.TeamFoundation.Framework.Client.TeamFoundationIdentity Authenticate(TfsTeamProjectCollection connection, string normalized, string toolName)
        {
            connection.EnsureAuthenticated();
            return connection.AuthorizedIdentity;
        }

        /// <summary>Test query used by login to detect a PAT without the Code scope. Returns false on 403.</summary>
        public static bool HasVersionControlAccess(TfsTeamProjectCollection connection)
        {
            try
            {
                var vcs = connection.GetService<VersionControlServer>();
                vcs.GetItems(new ItemSpec("$/", RecursionType.OneLevel), VersionSpec.Latest, DeletedState.NonDeleted, ItemType.Folder, false);
                return true;
            }
            catch (Exception ex) when (ex is Microsoft.VisualStudio.Services.WebApi.VssServiceResponseException response
                                       && response.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return false;
            }
            catch (Microsoft.TeamFoundation.Framework.Client.AccessCheckException)
            {
                return false;
            }
        }
    }
}
