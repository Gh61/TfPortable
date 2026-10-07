using System;
using System.Text.RegularExpressions;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.OAuth;
using TfPortable.Errors;

namespace TfPortable.Auth
{
    /// <summary>Result of the REST `_apis/connectionData` check of a PAT.</summary>
    internal sealed class RestIdentity
    {
        public RestIdentity(int status, string? displayName, string? uniqueName, string? id, string summary)
        {
            Status = status;
            DisplayName = displayName;
            UniqueName = uniqueName;
            Id = id;
            Summary = summary;
        }

        public int Status { get; }

        public bool Ok => Status == 200 && !string.IsNullOrEmpty(DisplayName);

        public string? DisplayName { get; }

        public string? UniqueName { get; }

        public string? Id { get; }

        /// <summary>One line for messages, never contains the token.</summary>
        public string Summary { get; }
    }

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
                var rest = CheckPatOverRest(collectionUrl, pat);
                if (!rest.Ok)
                {
                    throw new TfpException(ErrorCodes.PatRejected, ExitCodes.Authentication,
                        $"The server {normalized} rejected the PAT; nothing was stored. {rest.Summary}",
                        "Check that the token was copied completely, is not expired and belongs to this organization.");
                }

                CredentialStore.Write(normalized, pat, rest.UniqueName, DateTimeOffset.Now);
                stored = CredentialStore.Read(normalized)!;
                return Open(collectionUrl, pat);
            }

            stored = existing;
            return Open(collectionUrl, existing.Pat);
        }

        /// <summary>
        /// The SOAP channel of the client library (TfsTeamProjectCollection) answers the server's 401 with a bearer token;
        /// Azure DevOps accepts a PAT as a bearer token (tf.exe does the same with /loginType:OAuth). A VssBasicCredential
        /// produced no token provider for that challenge (TF30063 in TfsHttpWebRequest.EnsureTokenProvider).
        /// </summary>
        public static TfsTeamProjectCollection Open(string collectionUrl, string pat)
        {
            var credentials = new VssCredentials(new VssOAuthAccessTokenCredential(pat))
            {
                PromptType = CredentialPromptType.DoNotPrompt,
            };
            var uri = new Uri(collectionUrl.Trim().TrimEnd('/'), UriKind.Absolute);
            return new TfsTeamProjectCollection(uri, credentials);
        }

        /// <summary>Authenticates and returns the identity. Exceptions are mapped by the caller through ErrorMapper.</summary>
        public static Microsoft.TeamFoundation.Framework.Client.TeamFoundationIdentity Authenticate(TfsTeamProjectCollection connection)
        {
            connection.EnsureAuthenticated();
            return connection.AuthorizedIdentity;
        }

        /// <summary>
        /// Independent check of the PAT over REST (`_apis/connectionData` with Basic auth). Used by login to verify the
        /// token before storing it and to explain failures of the SOAP connection. Never logs the token.
        /// </summary>
        public static RestIdentity CheckPatOverRest(string collectionUrl, string pat)
        {
            string url = collectionUrl.Trim().TrimEnd('/') + "/_apis/connectionData?api-version=7.1-preview.1";
            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                string token = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(":" + pat));
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", token);
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                using var response = client.GetAsync(url).GetAwaiter().GetResult();
                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                int status = (int)response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    string? display = Capture(body, "\"providerDisplayName\"\\s*:\\s*\"([^\"]*)\"");
                    string? id = Capture(body, "\"authenticatedUser\"\\s*:\\s*\\{\\s*\"id\"\\s*:\\s*\"([^\"]*)\"");
                    string? account = Capture(body, "\"Account\"\\s*:\\s*\\{[^}]*\"\\$value\"\\s*:\\s*\"([^\"]*)\"");
                    return new RestIdentity(status, display, account, id, $"REST check {url}: HTTP {status} as {display}.");
                }

                string why = status == 203 || status == 401
                    ? " (the token was not accepted: Azure DevOps answers an invalid or expired PAT with 203 and a sign-in page, or with 401)"
                    : string.Empty;
                return new RestIdentity(status, null, null, null, $"REST check {url}: HTTP {status} {response.ReasonPhrase}{why}.");
            }
            catch (Exception ex)
            {
                return new RestIdentity(0, null, null, null, $"REST check {url} failed: {ex.GetType().Name}: {ex.Message}");
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

        private static string? Capture(string text, string pattern)
        {
            var match = Regex.Match(text, pattern, RegexOptions.Singleline);
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}
