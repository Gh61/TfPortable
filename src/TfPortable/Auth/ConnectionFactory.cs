using System;
using System.Text.RegularExpressions;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.VisualStudio.Services.Common;
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
        /// Basic authentication with the PAT, sent preemptively by the client library; no default Windows credentials,
        /// no prompting. The SOAP endpoints of Azure DevOps challenge with "WWW-Authenticate: Basic" and accept a PAT only
        /// when it has all scopes (full access); a PAT with custom scopes passes the REST connectionData check but gets 401
        /// here, which the client library reports as TF30063 (see SPEC.md "Autentizace" and docs/PROGRESS.md decisions).
        /// </summary>
        public static TfsTeamProjectCollection Open(string collectionUrl, string pat)
        {
            var credentials = new VssCredentials(new Microsoft.VisualStudio.Services.Common.WindowsCredential(false), new VssBasicCredential(string.Empty, pat), CredentialPromptType.DoNotPrompt);
            var uri = new Uri(collectionUrl.Trim().TrimEnd('/'), UriKind.Absolute);
            return new TfsTeamProjectCollection(uri, credentials);
        }

        /// <summary>
        /// Explains a TF30063 of the SOAP connection when the token itself is valid: the PAT lacks full access.
        /// Returns null when the token is rejected by REST too (then the original TFP0102 stands).
        /// </summary>
        public static TfpException? ExplainSoapRejection(string collectionUrl, string normalized, string pat, string toolName, Exception inner)
        {
            var rest = CheckPatOverRest(collectionUrl, pat);
            if (!rest.Ok)
            {
                return null;
            }

            return new TfpException(ErrorCodes.PatScope, ExitCodes.Authentication,
                $"The PAT for {normalized} is valid ({rest.DisplayName}) but the version control API refused it: the TFVC (SOAP) API of Azure DevOps works only with a PAT that has all scopes (Full access).",
                $"Create a new PAT with 'Scopes: Full access' and run '{toolName} login /collection:{normalized}' in your own terminal.",
                "TF30063", inner.Message, inner: inner);
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
