using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace TfPortable
{
    /// <summary>
    /// Entry point. Scaffold only: milestone 1 (spike) adds the Credential Manager store and the
    /// login, whoami and status commands; milestone 2 adds the parser, context, errors and JSON.
    /// Exit codes follow SPEC.md ("Exit kódy") and docs/compat.md §1.1.
    /// </summary>
    internal static class Program
    {
        internal const int ExitSuccess = 0;
        internal const int ExitUnknownCommand = 2;
        internal const int ExitGeneralError = 100;

        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Console.InputEncoding = Console.OutputEncoding;

            string toolName = GetToolName();
            string command = args.Length > 0 ? args[0] : "help";

            switch (command.ToLowerInvariant())
            {
                case "help":
                case "/?":
                case "-?":
                case "-h":
                case "/help":
                case "-help":
                    Console.WriteLine($"{toolName} {GetVersion()} - portable TFVC client (scaffold build).");
                    Console.WriteLine("Commands are not implemented yet. See docs/SPEC.md and docs/PROGRESS.md.");
                    return ExitSuccess;

                case "version":
                    Console.WriteLine($"{toolName} {GetVersion()}");
                    Console.WriteLine($"Microsoft.TeamFoundationServer.ExtendedClient {GetClientLibraryVersion()}");
                    return ExitSuccess;

                default:
                    Console.Error.WriteLine($"ERROR TFP0001: Unknown command '{command}'.");
                    Console.Error.WriteLine($"  Next step: run '{toolName} help' for the list of commands.");
                    return ExitUnknownCommand;
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

        internal static string GetVersion()
        {
            var assembly = typeof(Program).Assembly;
            string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return string.IsNullOrEmpty(informational) ? assembly.GetName().Version.ToString() : informational!;
        }

        private static string GetClientLibraryVersion()
        {
            var clientAssembly = typeof(Microsoft.TeamFoundation.VersionControl.Client.VersionControlServer).Assembly;
            return clientAssembly.GetName().Version.ToString();
        }
    }
}
