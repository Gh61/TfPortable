using System;

namespace TfPortable.Errors
{
    /// <summary>
    /// An error with a stable TfPortable code (TFPxxxx), the original TFS code when there is one,
    /// the server message verbatim and a one-sentence hint. See SPEC.md, "Chybové hlášky".
    /// </summary>
    internal sealed class TfpException : Exception
    {
        public TfpException(string code, int exitCode, string message, string? hint = null, string? tfCode = null,
            string? serverMessage = null, string? item = null, string? activityId = null, Exception? inner = null)
            : base(message, inner)
        {
            Code = code;
            ExitCode = exitCode;
            Hint = hint;
            TfCode = tfCode;
            ServerMessage = serverMessage;
            Item = item;
            ActivityId = activityId;
        }

        public string Code { get; }

        public int ExitCode { get; }

        public string? Hint { get; }

        public string? TfCode { get; }

        public string? ServerMessage { get; }

        public string? Item { get; }

        public string? ActivityId { get; }
    }
}
