using System;
using TfPortable.Errors;

namespace TfPortable.Auth
{
    /// <summary>Normalization of collection URLs for the Credential Manager key (SPEC.md "Uložení").</summary>
    internal static class CollectionUrl
    {
        public const string CredentialPrefix = "TfPortable:";

        /// <summary>
        /// Lower-case, no trailing slash, `org.visualstudio.com[/DefaultCollection]` becomes `dev.azure.com/org`,
        /// `dev.azure.com/org/DefaultCollection` loses the DefaultCollection segment. Server URLs keep their collection name.
        /// </summary>
        public static string Normalize(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "The collection URL is empty.",
                    "Pass /collection:https://dev.azure.com/<organization> or the URL of your Azure DevOps Server collection.");
            }

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"'{url}' is not a valid collection URL.",
                    "Use an absolute http(s) URL, e.g. https://dev.azure.com/contoso or https://tfs.contoso.local/tfs/DefaultCollection.");
            }

            string host = uri.Host.ToLowerInvariant();
            string path = uri.AbsolutePath.TrimEnd('/').ToLowerInvariant();

            if (host.EndsWith(".visualstudio.com", StringComparison.Ordinal))
            {
                string org = host.Substring(0, host.Length - ".visualstudio.com".Length);
                return $"https://dev.azure.com/{org}";
            }

            if (host == "dev.azure.com")
            {
                string[] segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 1)
                {
                    return $"https://dev.azure.com/{segments[0]}";
                }
            }

            string port = uri.IsDefaultPort ? string.Empty : ":" + uri.Port;
            return $"{uri.Scheme}://{host}{port}{path}";
        }

        public static string ToCredentialTarget(string url) => CredentialPrefix + Normalize(url);

        /// <summary>Where to create a PAT: Services use dev.azure.com/org/_usersSettings/tokens, Server uses the collection URL.</summary>
        public static string TokenPageUrl(string normalizedUrl) => normalizedUrl.TrimEnd('/') + "/_usersSettings/tokens";
    }
}
