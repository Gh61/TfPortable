using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using TfPortable.Cli;
using TfPortable.Commands;
using TfPortable.Errors;
using TfPortable.Output;

namespace TfPortable
{
    /// <summary>
    /// Entry point: UTF-8 console, en-US client messages, command dispatch, global error mapping and exit codes
    /// (SPEC.md "Exit kódy", docs/compat.md §1.1).
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            var english = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentUICulture = english;
            Thread.CurrentThread.CurrentUICulture = english;

            return Run(args, Console.Out, Console.Error, GetToolName());
        }

        /// <summary>The commands this build implements; everything else in the catalog answers TFP0003.</summary>
        internal static ICommand[] Commands() => new ICommand[]
        {
            new HelpCommand(),
            new VersionCommand(),
            new LoginCommand(),
            new LogoutCommand(),
            new WhoamiCommand(),
            new StatusCommand(),
            new HistoryCommand(),
        };

        /// <summary>Parses, dispatches and maps errors; returns the exit code. Testable with any writers.</summary>
        internal static int Run(string[] args, TextWriter stdout, TextWriter stderr, string toolName)
        {
            // Decided from the raw arguments so that even a parse error produces the JSON envelope.
            bool json = args.Any(a => IsSwitch(a, "format:json") || IsSwitch(a, "F:json"));
            bool verbose = args.Any(a => IsSwitch(a, "verbose"));
            var output = new OutputWriter(json, "help", stdout, stderr);

            try
            {
                var commandLine = CommandLine.Parse(args, toolName);
                if (commandLine.RawCommand.Length > 0 && commandLine.Spec == null)
                {
                    throw HelpCommand.UnknownCommand(commandLine.RawCommand, toolName);
                }

                string commandName = commandLine.RawCommand.Length == 0 || commandLine.HelpRequested ? "help" : commandLine.Command;
                output.Command = commandName;

                var command = Commands().FirstOrDefault(c => c.Name == commandName);
                if (command == null)
                {
                    throw NotAvailable(commandLine.Spec!, toolName);
                }

                int exit = command.Run(commandLine, output, toolName);
                return exit == ExitCodes.Success && output.ItemErrorCount > 0 ? ExitCodes.PartialSuccess : exit;
            }
            catch (Exception ex)
            {
                var error = ErrorMapper.Map(ex, toolName, output.Collection);
                if (error.Code == ErrorCodes.PatRejected && output.Collection != null && !(ex is TfpException))
                {
                    // TF30063 with a token that REST accepts means the PAT lacks full access (see SPEC.md "Scope PAT").
                    var stored = Auth.CredentialStore.Read(output.Collection);
                    var scope = stored == null ? null : Auth.ConnectionFactory.ExplainSoapRejection(output.Collection, output.Collection, stored.Pat, toolName, ex);
                    if (scope != null)
                    {
                        error = scope;
                    }
                }

                error.ExitCode = ErrorCodes.ExitCodeFor(output.Command, error.Code, error.ExitCode);
                output.Error(error);
                if (verbose)
                {
                    stderr.WriteLine(ex.ToString());
                }

                return error.ExitCode;
            }
        }

        /// <summary>TFP0003 for a recognized command this build does not run: out of scope, or planned for a later milestone.</summary>
        internal static TfpException NotAvailable(CommandSpec spec, string toolName)
        {
            if (spec.Status == CommandStatus.OutOfScope)
            {
                return new TfpException(ErrorCodes.NotSupported, ExitCodes.GeneralError,
                    $"Command '{spec.Name}' is not supported by {toolName}: {spec.Summary}",
                    "Use Visual Studio or the original tf.exe for it.");
            }

            return new TfpException(ErrorCodes.NotSupported, ExitCodes.GeneralError,
                $"Command '{spec.Name}' is not available in this build of {toolName} (planned for phase {spec.Phase}).",
                $"Use the original tf.exe for now; '{toolName} help' lists the available commands.");
        }

        private static bool IsSwitch(string arg, string body) =>
            arg.Length > 1 && (arg[0] == '/' || arg[0] == '-') && arg.Substring(1).Equals(body, StringComparison.OrdinalIgnoreCase);

        /// <summary>The real file name of the running executable, so that a renamed copy reports itself correctly.</summary>
        internal static string GetToolName()
        {
            try
            {
                string? path = Assembly.GetEntryAssembly()?.Location;
                if (!string.IsNullOrEmpty(path))
                {
                    return Path.GetFileNameWithoutExtension(path);
                }
            }
            catch (Exception)
            {
                // Fall through to the default name.
            }

            return "tfp";
        }
    }
}
