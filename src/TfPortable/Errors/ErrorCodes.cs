using System;
using System.Collections.Generic;

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
        public const string ProtectionLevel = "TFP0004";
        public const string NotOffered = "TFP0005";
        public const string SwitchNotSupported = "TFP0006";

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

        public const string PendingChangeConflict = "TFP0401";
        public const string WritableFileSkipped = "TFP0402";
        public const string GetConflicts = "TFP0403";
        public const string ItemLocked = "TFP0404";
        public const string CleanNeedsConfirmation = "TFP0405";
        public const string CheckinConflict = "TFP0406";
        public const string CheckinPolicy = "TFP0407";
        public const string ShelvesetExists = "TFP0408";
        public const string CheckinPolicyUnknown = "TFP0409";
        public const string DestroyNeedsPreview = "TFP0410";

        public const string ServerError = "TFP0501";
        public const string Unexpected = "TFP0900";

        /// <summary>Default exit code of every code in the catalog (SPEC.md "Katalog", column Exit).</summary>
        public static readonly IReadOnlyDictionary<string, int> DefaultExitCodes = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [UnknownCommand] = ExitCodes.UnknownCommand,
            [InvalidSwitch] = ExitCodes.GeneralError,
            [NotSupported] = ExitCodes.GeneralError,
            [ProtectionLevel] = ExitCodes.GeneralError,
            [NotOffered] = ExitCodes.GeneralError,
            [SwitchNotSupported] = ExitCodes.GeneralError,
            [PatMissing] = ExitCodes.Authentication,
            [PatRejected] = ExitCodes.Authentication,
            [PatScope] = ExitCodes.Authentication,
            [CredentialManager] = ExitCodes.Authentication,
            [WorkspaceNotFound] = ExitCodes.WorkspaceNotFound,
            [ServerUnavailable] = ExitCodes.GeneralError,
            [PathNotMapped] = ExitCodes.WorkspaceNotFound,
            [ServerWorkspaceNotSupported] = ExitCodes.WorkspaceNotFound,
            [ItemNotFound] = ExitCodes.GeneralError,
            [InvalidVersionSpec] = ExitCodes.GeneralError,
            [BinaryContent] = ExitCodes.GeneralError,
            [PendingChangeConflict] = ExitCodes.PartialSuccess,
            [WritableFileSkipped] = ExitCodes.PartialSuccess,
            [GetConflicts] = ExitCodes.PartialSuccess,
            [ItemLocked] = ExitCodes.PartialSuccess,
            [CheckinConflict] = ExitCodes.GeneralError,
            [CheckinPolicy] = ExitCodes.GeneralError,
            [ShelvesetExists] = ExitCodes.GeneralError,
            [CheckinPolicyUnknown] = ExitCodes.GeneralError,
            [CleanNeedsConfirmation] = ExitCodes.GeneralError,
            [DestroyNeedsPreview] = ExitCodes.GeneralError,
            [ServerError] = ExitCodes.GeneralError,
            [Unexpected] = ExitCodes.GeneralError,
        };

        /// <summary>
        /// Per-command exit codes where the original differs from the catalog default (docs/compat.md §1.1):
        /// "item not found" is 1 for add/delete/view/dir/undelete, 100 for rename/changeset/label. `history` prints an
        /// empty result (exit 0) itself, so it needs no override.
        /// </summary>
        private static readonly Dictionary<string, int> Overrides = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["add:" + ItemNotFound] = ExitCodes.PartialSuccess,
            ["delete:" + ItemNotFound] = ExitCodes.PartialSuccess,
            ["view:" + ItemNotFound] = ExitCodes.PartialSuccess,
            ["dir:" + ItemNotFound] = ExitCodes.PartialSuccess,
            ["undelete:" + ItemNotFound] = ExitCodes.PartialSuccess,
            ["annotate:" + ItemNotFound] = ExitCodes.PartialSuccess,
            ["unlabel:" + ItemNotFound] = ExitCodes.PartialSuccess,
            ["rename:" + ItemNotFound] = ExitCodes.GeneralError,
            ["changeset:" + ItemNotFound] = ExitCodes.GeneralError,
            ["label:" + ItemNotFound] = ExitCodes.GeneralError,
            ["checkin:" + ItemLocked] = ExitCodes.GeneralError,
            ["unshelve:" + PendingChangeConflict] = ExitCodes.GeneralError,
        };

        /// <summary>The exit code of a TFP code for a command: the compat.md override, else the catalog default, else the given one.</summary>
        public static int ExitCodeFor(string command, string code, int fallback)
        {
            if (Overrides.TryGetValue(command + ":" + code, out int exit))
            {
                return exit;
            }

            return DefaultExitCodes.TryGetValue(code, out exit) ? exit : fallback;
        }

        public static bool IsKnown(string code) => DefaultExitCodes.ContainsKey(code);
    }
}
