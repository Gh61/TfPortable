using System;
using System.IO;
using System.Text;
using TfPortable.Errors;

namespace TfPortable.Auth
{
    /// <summary>Text and input of the PAT prompt (SPEC.md "Text výzvy při chybějícím PAT").</summary>
    internal static class PatPrompt
    {
        /// <summary>SPEC.md "Tok při každém příkazu" step 3: the prompt needs an interactive stdin; stdout may be redirected (the prompt goes to stderr).</summary>
        public static bool IsInteractive => !Console.IsInputRedirected && Environment.UserInteractive;

        public static string Explanation(string normalizedCollection)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"No Personal Access Token (PAT) is stored for collection {normalizedCollection}.");
            sb.AppendLine();
            sb.AppendLine("Create one here:");
            sb.AppendLine($"  {CollectionUrl.TokenPageUrl(normalizedCollection)}");
            sb.AppendLine();
            sb.AppendLine("If the link does not open the token page, use the web UI menu:");
            sb.AppendLine("  Azure DevOps Services (dev.azure.com, *.visualstudio.com):");
            sb.AppendLine("    user settings icon (top right) > Personal access tokens");
            sb.AppendLine("  Azure DevOps Server / TFS 2017+ (your own domain, e.g. https://tfs.contoso.local/tfs):");
            sb.AppendLine("    profile picture (top right) > Security > Personal access tokens");
            sb.AppendLine();
            sb.AppendLine("Token settings:");
            sb.AppendLine("  1. New Token, Expiration: as needed (Services allow max. 1 year)");
            sb.AppendLine("  2. Scopes: Full access");
            sb.AppendLine("     (the TFVC API does not accept tokens with custom scopes, not even Code: Read & write)");
            sb.AppendLine("  3. Create, then copy the token. It will not be shown again.");
            return sb.ToString();
        }

        /// <summary>Reads the PAT from stdin (one line) for `/pat:-`.</summary>
        public static string ReadFromStdin()
        {
            using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            string? line = reader.ReadLine();
            string pat = (line ?? string.Empty).Trim();
            if (pat.Length == 0)
            {
                throw new TfpException(ErrorCodes.PatMissing, ExitCodes.Authentication, "No PAT was read from standard input.",
                    "Pipe the token into the command, e.g. 'echo <pat> | tfp login /collection:<url> /pat:-'.");
            }

            return pat;
        }

        /// <summary>Hidden interactive input. Never called when the terminal is not interactive.</summary>
        public static string ReadHidden(string normalizedCollection)
        {
            Console.Error.Write(Explanation(normalizedCollection));
            Console.Error.WriteLine();
            Console.Error.Write("Paste the PAT (shown as *, Enter to confirm, Esc to clear): ");

            var sb = new StringBuilder();
            bool treatControlC = Console.TreatControlCAsInput;
            try
            {
                // Ctrl+C must end with a message and exit 110, not silently (milestone 2 note in docs/KICKOFF-CLOUD.md).
                Console.TreatControlCAsInput = true;
            }
            catch (Exception)
            {
                // Not a real console; Ctrl+C then terminates the process as before.
            }

            try
            {
                return ReadHiddenLoop(sb);
            }
            finally
            {
                try
                {
                    Console.TreatControlCAsInput = treatControlC;
                }
                catch (Exception)
                {
                    // Ignore: nothing to restore.
                }
            }
        }

        private static string ReadHiddenLoop(StringBuilder sb)
        {
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    break;
                }

                if (key.Key == ConsoleKey.C && (key.Modifiers & ConsoleModifiers.Control) != 0)
                {
                    Console.Error.WriteLine();
                    throw new TfpException(ErrorCodes.PatMissing, ExitCodes.Authentication, "The PAT prompt was cancelled (Ctrl+C); nothing was stored.",
                        "Run the command again and paste the token, or use '/pat:-' to read it from standard input.");
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (sb.Length > 0)
                    {
                        sb.Length--;
                        Console.Error.Write("\b \b");
                    }

                    continue;
                }

                if (key.Key == ConsoleKey.Escape)
                {
                    Console.Error.Write(new string('\b', sb.Length) + new string(' ', sb.Length) + new string('\b', sb.Length));
                    sb.Clear();
                    continue;
                }

                if (!char.IsControl(key.KeyChar) && !char.IsWhiteSpace(key.KeyChar))
                {
                    sb.Append(key.KeyChar);
                    Console.Error.Write('*');
                }
            }

            Console.Error.WriteLine();
            string pat = sb.ToString().Trim();
            Console.Error.WriteLine($"Read {pat.Length} characters.");
            if (pat.Length == 0)
            {
                throw new TfpException(ErrorCodes.PatMissing, ExitCodes.Authentication, "No PAT was entered.", "Run the command again and paste the token.");
            }

            return pat;
        }
    }
}
