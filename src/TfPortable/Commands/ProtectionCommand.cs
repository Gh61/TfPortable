using System;
using Newtonsoft.Json.Linq;
using TfPortable.Auth;
using TfPortable.Cli;
using TfPortable.Errors;
using TfPortable.Output;
using TfPortable.Settings;

namespace TfPortable.Commands
{
    /// <summary>
    /// `tfp protection` shows the protection level, `tfp protection set &lt;0|1|2|3&gt;` changes it (SPEC.md "Úrovně ochrany").
    /// Setting needs an interactive terminal: a script or an agent cannot do it, and raising asks for a typed confirmation.
    /// </summary>
    internal sealed class ProtectionCommand : ICommand
    {
        /// <summary>Whether stdin is a terminal; replaceable for tests.</summary>
        public static Func<bool> InteractiveProvider { get; set; } = () => PatPrompt.IsInteractive;

        /// <summary>Reads the confirmation line; replaceable for tests.</summary>
        public static Func<string?> ReadLineProvider { get; set; } = () => Console.In.ReadLine();

        public string Name => "protection";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            var settings = ToolSettings.Load();
            if (commandLine.Positionals.Count == 0)
            {
                output.Line($"Protection level: {settings.ProtectionLevel} ({ProtectionLevels.Name(settings.ProtectionLevel)})");
                output.Line($"Settings file   : {ToolSettings.FilePath}");
                output.Success(new JObject { ["protectionLevel"] = settings.ProtectionLevel, ["settingsPath"] = ToolSettings.FilePath });
                return ExitCodes.Success;
            }

            if (commandLine.Positionals.Count != 2 || !string.Equals(commandLine.Positionals[0], "set", StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(commandLine.Positionals[1], out int level) || level < ProtectionLevels.ReadOnly || level > ProtectionLevels.Everything)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "protection takes no argument, or 'set' followed by 0, 1, 2 or 3.",
                    $"Example: '{toolName} protection set 2'. Levels: 0 read-only, 1 no server writes, 2 everyday work, 3 everything.");
            }

            if (!InteractiveProvider())
            {
                throw new TfpException(ErrorCodes.ProtectionLevel, ExitCodes.GeneralError,
                    "The protection level is changed in an interactive terminal, not from a script or an agent.",
                    $"Open a terminal and run '{toolName} protection set {level}' there.");
            }

            int previous = settings.ProtectionLevel;
            if (level > previous)
            {
                output.Line($"Level {level} ({ProtectionLevels.Name(level)}) allows more than level {previous} ({ProtectionLevels.Name(previous)}).");
                output.Line($"Type {level} to confirm, anything else to keep the current level:");
                string? answer = ReadLineProvider();
                if (answer?.Trim() != level.ToString())
                {
                    throw new TfpException(ErrorCodes.ProtectionLevel, ExitCodes.GeneralError,
                        $"The confirmation did not match. The protection level stays at {previous} ({ProtectionLevels.Name(previous)}).",
                        $"Run '{toolName} protection set {level}' again and type {level}.");
                }
            }

            settings.ProtectionLevel = level;
            settings.Save();
            output.Line($"Protection level set to {level} ({ProtectionLevels.Name(level)}).");
            output.Success(new JObject { ["protectionLevel"] = level, ["previous"] = previous, ["settingsPath"] = ToolSettings.FilePath });
            return ExitCodes.Success;
        }
    }
}
