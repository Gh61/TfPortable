using System;
using System.Collections.Generic;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace TfPortable.Auth
{
    /// <summary>
    /// Spike diagnostics: tries several ways of building a PAT connection for the SOAP client and reports
    /// which one authenticates. Used by `login /verbose` when the default connection fails. Never prints the token.
    /// </summary>
    internal static class ConnectionDiagnostics
    {
        public static IEnumerable<string> TryVariants(string collectionUrl, string normalizedUrl, string pat)
        {
            var uri = new Uri(collectionUrl.Trim().TrimEnd('/'));
            var normalized = new Uri(normalizedUrl);

            yield return Try("V1 VssCredentials(basic), original URL", () => new TfsTeamProjectCollection(uri, new VssCredentials(new VssBasicCredential(string.Empty, pat))));
            yield return Try("V2 VssCredentials(basic), dev.azure.com URL", () => new TfsTeamProjectCollection(normalized, new VssCredentials(new VssBasicCredential(string.Empty, pat))));
            yield return Try("V3 VssCredentials(WindowsCredential(false), basic, DoNotPrompt)", () => new TfsTeamProjectCollection(uri, new VssCredentials(new Microsoft.VisualStudio.Services.Common.WindowsCredential(false), new VssBasicCredential(string.Empty, pat), CredentialPromptType.DoNotPrompt)));
            yield return Try("V4 VssClientCredentials(WindowsCredential(false), basic, DoNotPrompt)", () => new TfsTeamProjectCollection(uri, new VssClientCredentials(new Microsoft.VisualStudio.Services.Common.WindowsCredential(false), new VssBasicCredential(string.Empty, pat), CredentialPromptType.DoNotPrompt)));
            yield return Try("V5 VssConnection pre-warm, then TfsTeamProjectCollection with the same credentials", () =>
            {
                var creds = new VssCredentials(new VssBasicCredential(string.Empty, pat));
                using (var connection = new VssConnection(uri, creds))
                {
                    connection.ConnectAsync().GetAwaiter().GetResult();
                }

                return new TfsTeamProjectCollection(uri, creds);
            });
            yield return Try("V6 TfsTeamProjectCollection(uri, basic) implicit conversion", () => new TfsTeamProjectCollection(uri, new VssBasicCredential(string.Empty, pat)));
            yield return Try("V7 VssOAuthAccessTokenCredential(pat) as bearer token", () => new TfsTeamProjectCollection(uri, new VssCredentials(new Microsoft.VisualStudio.Services.OAuth.VssOAuthAccessTokenCredential(pat))));
            yield return Try("V8 VssBasicCredential with initial token (preemptive Basic header)", () => new TfsTeamProjectCollection(uri, new VssCredentials(new VssBasicCredential(new VssBasicToken(new System.Net.NetworkCredential(string.Empty, pat))))));
        }

        private static string Try(string label, Func<TfsTeamProjectCollection> factory)
        {
            try
            {
                using (var tpc = factory())
                {
                    tpc.EnsureAuthenticated();
                    var vcs = tpc.GetService<VersionControlServer>();
                    string user = vcs.AuthorizedUser;
                    return $"{label}: OK, AuthorizedUser={user}, identity={tpc.AuthorizedIdentity?.DisplayName}";
                }
            }
            catch (Exception ex)
            {
                var inner = ex;
                while (inner.InnerException != null && (inner is AggregateException || inner is System.Reflection.TargetInvocationException))
                {
                    inner = inner.InnerException;
                }

                return $"{label}: FAILED {inner.GetType().Name}: {FirstLine(inner.Message)}";
            }
        }

        private static string FirstLine(string text)
        {
            int i = text.IndexOfAny(new[] { '\r', '\n' });
            return i < 0 ? text : text.Substring(0, i);
        }
    }
}
