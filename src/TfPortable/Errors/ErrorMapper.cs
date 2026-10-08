using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.TeamFoundation;
using Microsoft.TeamFoundation.Framework.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace TfPortable.Errors
{
    /// <summary>Maps exceptions of the client libraries to TfPortable errors.</summary>
    internal static class ErrorMapper
    {
        private static readonly Regex TfCodePattern = new Regex(@"\b(TF|VS)\d{5,6}\b", RegexOptions.Compiled);
        private static readonly Regex ActivityIdPattern = new Regex(@"ActivityId[:=\s]+(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})", RegexOptions.Compiled);

        /// <summary>Maps an exception to a TfpException with the default exit code of its TFP code; the caller applies per-command overrides.</summary>
        public static TfpException Map(Exception exception, string toolName, string? collectionUrl)
        {
            if (exception is TfpException tfp)
            {
                return tfp;
            }

            if (exception is AggregateException aggregate && aggregate.InnerException != null)
            {
                return Map(aggregate.InnerException, toolName, collectionUrl);
            }

            string message = exception.Message;
            string? tfCode = ExtractTfCode(message);
            string? activityId = ExtractActivityId(exception);
            string loginHint = $"Create a new PAT and run '{toolName} login /collection:{collectionUrl ?? "<url>"}' in your own terminal.";

            if (exception is TeamFoundationServerUnauthorizedException || exception is VssUnauthorizedException
                || IsHttpStatus(exception, HttpStatusCode.Unauthorized) || tfCode == "TF30063" || tfCode == "TF400813")
            {
                return new TfpException(ErrorCodes.PatRejected, ExitCodes.Authentication,
                    $"The server {collectionUrl ?? ""} rejected the stored Personal Access Token (expired, revoked or wrong organization).".Replace("  ", " "),
                    loginHint, tfCode, message, activityId: activityId, inner: exception);
            }

            if (IsHttpStatus(exception, HttpStatusCode.Forbidden) || exception is AccessCheckException)
            {
                return new TfpException(ErrorCodes.PatScope, ExitCodes.Authentication,
                    "The Personal Access Token does not have the required scope for version control.",
                    $"Create a PAT with 'Scopes: Full access' and run '{toolName} login /collection:{collectionUrl ?? "<url>"}'.",
                    tfCode, message, activityId: activityId, inner: exception);
            }

            if (exception is TeamFoundationServiceUnavailableException || exception is HttpRequestException
                || exception is WebException || exception is SocketException || exception is System.Threading.Tasks.TaskCanceledException)
            {
                return new TfpException(ErrorCodes.ServerUnavailable, ExitCodes.GeneralError,
                    $"The server {collectionUrl ?? "(unknown)"} could not be reached.",
                    "Check the collection URL, the network and the proxy settings; run with /verbose for details.",
                    tfCode, message, activityId: activityId, inner: exception);
            }

            if (exception is ItemNotFoundException || exception is ChangesetNotFoundException || tfCode == "TF14019" || tfCode == "TF14022"
                || (message != null && message.IndexOf("was not found in source control", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return new TfpException(ErrorCodes.ItemNotFound, ExitCodes.GeneralError,
                    "The item or version does not exist on the server.",
                    $"Check the path with '{toolName} dir <folder>' or find a rename with '{toolName} history <item> /itemmode'.",
                    tfCode, message, activityId: activityId, inner: exception);
            }

            if (exception is Microsoft.TeamFoundation.VersionControl.Client.IdentityNotFoundException || exception is Microsoft.TeamFoundation.Framework.Client.IdentityNotFoundException || tfCode == "TF14045")
            {
                return new TfpException(ErrorCodes.ServerError, ExitCodes.GeneralError,
                    "The server does not know that identity.",
                    "Use the user's e-mail or display name as the server shows it; '*' matches everyone.",
                    tfCode, message, activityId: activityId, inner: exception);
            }

            if (exception is TeamFoundationServerException || exception is VssServiceException || exception is VssException)
            {
                return new TfpException(ErrorCodes.ServerError, ExitCodes.GeneralError,
                    "The server returned an error.",
                    "Read the server message; if it is temporary, try again.",
                    tfCode, message, activityId: activityId, inner: exception);
            }

            string? logPath = WriteLastError(exception);
            return new TfpException(ErrorCodes.Unexpected, ExitCodes.GeneralError,
                $"Unexpected error: {exception.GetType().Name}: {message}",
                logPath != null
                    ? $"The full stack trace is in {logPath}. Report it together with the output of '{toolName} version'."
                    : $"Run with /verbose for the stack trace and report it together with the output of '{toolName} version'.",
                tfCode, activityId: activityId, inner: exception);
        }

        public static string? ExtractTfCode(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            var match = TfCodePattern.Match(text);
            return match.Success ? match.Value : null;
        }

        /// <summary>
        /// The server's ActivityId when the client library kept it: in the exception data of a remote exception,
        /// or in the message text. Null otherwise.
        /// </summary>
        public static string? ExtractActivityId(Exception? exception)
        {
            for (Exception? e = exception; e != null; e = e.InnerException)
            {
                foreach (DictionaryEntry entry in e.Data)
                {
                    string key = entry.Key?.ToString() ?? string.Empty;
                    if (key.IndexOf("ActivityId", StringComparison.OrdinalIgnoreCase) >= 0 && entry.Value != null)
                    {
                        string value = entry.Value.ToString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return value;
                        }
                    }
                }

                var match = ActivityIdPattern.Match(e.Message ?? string.Empty);
                if (match.Success)
                {
                    return match.Groups["id"].Value;
                }
            }

            return null;
        }

        private static bool IsHttpStatus(Exception exception, HttpStatusCode status)
        {
            if (exception is VssServiceResponseException response && response.HttpStatusCode == status)
            {
                return true;
            }

            if (exception is WebException web && web.Response is HttpWebResponse httpResponse && httpResponse.StatusCode == status)
            {
                return true;
            }

            return false;
        }

        /// <summary>Writes the stack trace to %TEMP%\TfPortable\last-error.txt and returns the path (SPEC.md TFP0900).</summary>
        internal static string? WriteLastError(Exception exception)
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "TfPortable");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "last-error.txt");
                File.WriteAllText(path, $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}");
                return path;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
