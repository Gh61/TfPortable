using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.TeamFoundation;
using Microsoft.TeamFoundation.Framework.Client;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace TfPortable.Errors
{
    /// <summary>Maps exceptions of the client libraries to TfPortable errors.</summary>
    internal static class ErrorMapper
    {
        private static readonly Regex TfCodePattern = new Regex(@"\b(TF|VS)\d{5,6}\b", RegexOptions.Compiled);

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
            string loginHint = $"Create a new PAT and run '{toolName} login /collection:{collectionUrl ?? "<url>"}' in your own terminal.";

            if (exception is TeamFoundationServerUnauthorizedException || exception is VssUnauthorizedException
                || IsHttpStatus(exception, HttpStatusCode.Unauthorized) || tfCode == "TF30063" || tfCode == "TF400813")
            {
                return new TfpException(ErrorCodes.PatRejected, ExitCodes.Authentication,
                    $"The server {collectionUrl ?? ""} rejected the stored Personal Access Token (expired, revoked or wrong organization).".Replace("  ", " "),
                    loginHint, tfCode, message, inner: exception);
            }

            if (IsHttpStatus(exception, HttpStatusCode.Forbidden) || exception is AccessCheckException)
            {
                return new TfpException(ErrorCodes.PatScope, ExitCodes.Authentication,
                    "The Personal Access Token does not have the required scope for version control.",
                    $"Create a PAT with scope 'Code: Read & write' and run '{toolName} login /collection:{collectionUrl ?? "<url>"}'.",
                    tfCode, message, inner: exception);
            }

            if (exception is TeamFoundationServiceUnavailableException || exception is HttpRequestException
                || exception is WebException || exception is SocketException || exception is System.Threading.Tasks.TaskCanceledException)
            {
                return new TfpException(ErrorCodes.ServerUnavailable, ExitCodes.GeneralError,
                    $"The server {collectionUrl ?? "(unknown)"} could not be reached.",
                    "Check the collection URL, the network and the proxy settings; run with /verbose for details.",
                    tfCode, message, inner: exception);
            }

            if (exception is TeamFoundationServerException || exception is VssServiceException || exception is VssException)
            {
                return new TfpException(ErrorCodes.ServerError, ExitCodes.GeneralError,
                    "The server returned an error.",
                    "Read the server message; if it is temporary, try again.",
                    tfCode, message, inner: exception);
            }

            string? logPath = WriteLastError(exception);
            return new TfpException(ErrorCodes.Unexpected, ExitCodes.GeneralError,
                $"Unexpected error: {exception.GetType().Name}: {message}",
                logPath != null
                    ? $"The full stack trace is in {logPath}. Report it together with the output of '{toolName} version'."
                    : $"Run with /verbose for the stack trace and report it together with the output of '{toolName} version'.",
                tfCode, inner: exception);
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

        private static string? WriteLastError(Exception exception)
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
