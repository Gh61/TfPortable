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
    /// (SPEC.md "Exit kódy", docs/compat.md §1.1). Spike build: login, logout, whoami, status, help, version.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            var english = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentUICulture = english;
            Thread.CurrentThread.CurrentUICulture = english;

            string toolName = GetToolName();
            ICommand[] commands = Array.Empty<ICommand>();
            commands = new ICommand[]
            {
                new HelpCommand(() => commands),
                new VersionCommand(),
                new LoginCommand(),
                new LogoutCommand(),
                new WhoamiCommand(),
                new StatusCommand(),
                new HistoryCommand(),
            };

            bool json = args.Any(a => a.Length > 1 && (a[0] == '/' || a[0] == '-') && a.Substring(1).Equals("format:json", StringComparison.OrdinalIgnoreCase));
            bool verbose = args.Any(a => a.Length > 1 && (a[0] == '/' || a[0] == '-') && a.Substring(1).Equals("verbose", StringComparison.OrdinalIgnoreCase));
            string commandName = "help";
            var output = new OutputWriter(json, commandName, Console.Out, Console.Error);

            try
            {
                var commandLine = CommandLine.Parse(args, commands.SelectMany(c => c.Switches));
                commandName = commandLine.Command.Length == 0 || commandLine.Has("help") ? "help" : commandLine.Command;
                if (commandName == "stat")
                {
                    commandName = "status";
                }
                else if (commandName == "hist")
                {
                    commandName = "history";
                }

                var command = commands.FirstOrDefault(c => c.Name == commandName);
                output = new OutputWriter(json, command?.Name ?? commandName, Console.Out, Console.Error);
                if (command == null)
                {
                    throw new TfpException(ErrorCodes.UnknownCommand, ExitCodes.UnknownCommand,
                        $"Unknown command '{commandName}'.",
                        $"Commands in this build: {string.Join(", ", commands.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal))}. Run '{toolName} help'.");
                }

                if (command.Name != "help")
                {
                    commandLine.Validate(toolName, command.Switches);
                }

                return command.Run(commandLine, output, toolName);
            }
            catch (Exception ex)
            {
                var error = ErrorMapper.Map(ex, toolName, output.Collection);
                if (error.Code == ErrorCodes.PatRejected && output.Collection != null)
                {
                    // TF30063 with a token that REST accepts means the PAT lacks full access (see SPEC.md "Scope PAT").
                    var stored = Auth.CredentialStore.Read(output.Collection);
                    var scope = stored == null ? null : Auth.ConnectionFactory.ExplainSoapRejection(output.Collection, output.Collection, stored.Pat, toolName, ex);
                    if (scope != null)
                    {
                        error = scope;
                    }
                }

                output.Error(error);
                if (verbose)
                {
                    Console.Error.WriteLine(ex.ToString());
                }

                return error.ExitCode;
            }
        }

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
