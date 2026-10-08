using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace TfPortable.IntegrationTests.Golden
{
    /// <summary>Result of one tfp.exe run.</summary>
    public sealed class TfpResult
    {
        public TfpResult(int exitCode, string stdout, string stderr, bool timedOut)
        {
            ExitCode = exitCode;
            Stdout = stdout;
            Stderr = stderr;
            TimedOut = timedOut;
        }

        public int ExitCode { get; }

        public string Stdout { get; }

        public string Stderr { get; }

        public bool TimedOut { get; }
    }

    /// <summary>
    /// Runs the built tfp.exe as a separate process, as the golden capture ran tf.exe: given arguments, given
    /// working directory, stdin redirected and closed (or fed with a text), UTF-8 output captured.
    /// </summary>
    public static class TfpRunner
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

        /// <summary>
        /// `TFP_EXE` when set, otherwise `src/TfPortable/bin/&lt;configuration of the test build&gt;/net48/tfp.exe`.
        /// </summary>
        public static string ExePath
        {
            get
            {
                string? fromEnvironment = Environment.GetEnvironmentVariable("TFP_EXE");
                if (!string.IsNullOrWhiteSpace(fromEnvironment))
                {
                    return fromEnvironment!;
                }

                string configuration = "Release";
                string baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                string? framework = Path.GetFileName(baseDirectory);
                string? configDirectory = Path.GetDirectoryName(baseDirectory);
                if (framework != null && configDirectory != null && framework.StartsWith("net", StringComparison.OrdinalIgnoreCase))
                {
                    configuration = Path.GetFileName(configDirectory) ?? configuration;
                }

                return Path.Combine(TestEnvironment.RepositoryRoot, "src", "TfPortable", "bin", configuration, "net48", "tfp.exe");
            }
        }

        public static bool ExeExists => File.Exists(ExePath);

        public static TfpResult Run(string arguments, string workingDirectory, string? stdin = null, IDictionary<string, string?>? environment = null)
        {
            if (!ExeExists)
            {
                throw new FileNotFoundException("tfp.exe not found; build the solution in Release first or set TFP_EXE.", ExePath);
            }

            var start = new ProcessStartInfo(ExePath, arguments)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                CreateNoWindow = true,
            };

            // The golden tests must never pick up the developer's environment or an interactive terminal.
            start.EnvironmentVariables.Remove("TFP_COLLECTION");
            start.EnvironmentVariables.Remove("MSYSTEM");
            start.EnvironmentVariables.Remove("EXEPATH");
            if (environment != null)
            {
                foreach (var pair in environment)
                {
                    if (pair.Value == null)
                    {
                        start.EnvironmentVariables.Remove(pair.Key);
                    }
                    else
                    {
                        start.EnvironmentVariables[pair.Key] = pair.Value;
                    }
                }
            }

            using var process = new Process { StartInfo = start };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) { stdout.AppendLine(e.Data); } };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) { stderr.AppendLine(e.Data); } };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (stdin != null)
            {
                process.StandardInput.Write(stdin);
            }

            process.StandardInput.Close();

            bool exited = process.WaitForExit((int)Timeout.TotalMilliseconds);
            if (!exited)
            {
                try
                {
                    process.Kill();
                }
                catch (Exception)
                {
                    // Already gone.
                }

                process.WaitForExit();
                return new TfpResult(-1, stdout.ToString(), stderr.ToString(), true);
            }

            process.WaitForExit();
            return new TfpResult(process.ExitCode, stdout.ToString(), stderr.ToString(), false);
        }
    }
}
