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

        /// <summary>
        /// Independent check of the PAT over REST (`_apis/connectionData` with Basic auth), used to explain a failed
        /// login: distinguishes a rejected token from a problem of the SOAP client connection. Never logs the token.
        /// </summary>
        public static string RestDiagnostics(string collectionUrl, string pat)
        {
            try
            {
                string url = collectionUrl.Trim().TrimEnd('/') + "/_apis/connectionData?api-version=7.1-preview.1";
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                string token = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(":" + pat));
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", token);
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                using var response = client.GetAsync(url).GetAwaiter().GetResult();
                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                string who = string.Empty;
                if (response.IsSuccessStatusCode)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(body, "\"providerDisplayName\"\\s*:\\s*\"([^\"]*)\"");
                    who = match.Success ? $" as {match.Groups[1].Value}" : string.Empty;
                }
                else if ((int)response.StatusCode == 203 || response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                         || body.IndexOf("<html", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    who = " (the token was not accepted: Azure DevOps answers an invalid or expired PAT with 203 and a sign-in page, or with 401)";
                }

                return $"REST check {url}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}{who}.";
            }
            catch (Exception ex)
            {
                return $"REST check failed: {ex.GetType().Name}: {ex.Message}";
            }
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
