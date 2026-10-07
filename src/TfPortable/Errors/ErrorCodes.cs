namespace TfPortable.Errors
{
    /// <summary>Exit codes (SPEC.md "Exit kódy", docs/compat.md §1.1).</summary>
    internal static class ExitCodes
    {
        public const int Success = 0;
        public const int PartialSuccess = 1;
        public const int UnknownCommand = 2;
        public const int GeneralError = 100;
        public const int Authentication = 110;
        public const int WorkspaceNotFound = 120;
    }

    /// <summary>Error catalog (SPEC.md "Katalog"). Exit codes here are the defaults; commands may override per compat.md.</summary>
    internal static class ErrorCodes
    {
        public const string UnknownCommand = "TFP0001";
        public const string InvalidSwitch = "TFP0002";
        public const string NotSupported = "TFP0003";

        public const string PatMissing = "TFP0101";
        public const string PatRejected = "TFP0102";
        public const string PatScope = "TFP0103";
        public const string CredentialManager = "TFP0104";

        public const string WorkspaceNotFound = "TFP0201";
        public const string ServerUnavailable = "TFP0202";
        public const string PathNotMapped = "TFP0203";
        public const string ServerWorkspaceNotSupported = "TFP0204";

        public const string ItemNotFound = "TFP0301";
        public const string InvalidVersionSpec = "TFP0302";
        public const string BinaryContent = "TFP0303";

        public const string ServerError = "TFP0501";
        public const string Unexpected = "TFP0900";
    }
}
