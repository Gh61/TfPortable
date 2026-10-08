using System;
using System.IO;
using Xunit;

namespace TfPortable.IntegrationTests
{
    /// <summary>
    /// Environment of the integration and golden tests. Everything that talks to a server reads
    /// TFP_TEST_COLLECTION and TFP_TEST_PAT; when they are missing the tests are skipped, never failed.
    /// The PAT is never written to output, logs or exceptions.
    /// </summary>
    internal static class TestEnvironment
    {
        public static string? Collection => NullIfEmpty(Environment.GetEnvironmentVariable("TFP_TEST_COLLECTION"));

        public static bool HasPat => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TFP_TEST_PAT"));

        public static bool IsConfigured => Collection != null && HasPat;

        public const string SkipReason = "TFP_TEST_COLLECTION and TFP_TEST_PAT are not set; server tests are skipped.";

        /// <summary>Repository root, found by walking up from the test assembly until tests/golden exists.</summary>
        public static string RepositoryRoot
        {
            get
            {
                string? dir = AppContext.BaseDirectory;
                while (dir != null)
                {
                    if (Directory.Exists(Path.Combine(dir, "tests", "golden")))
                    {
                        return dir;
                    }

                    dir = Path.GetDirectoryName(dir);
                }

                throw new InvalidOperationException("Repository root with tests/golden not found above " + AppContext.BaseDirectory);
            }
        }

        public static string GoldenRoot => Path.Combine(RepositoryRoot, "tests", "golden");

        private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }

    /// <summary>A fact that is skipped when the test collection and PAT are not configured.</summary>
    public sealed class IntegrationFactAttribute : FactAttribute
    {
        public IntegrationFactAttribute()
        {
            if (!TestEnvironment.IsConfigured)
            {
                Skip = TestEnvironment.SkipReason;
            }
        }
    }

    /// <summary>A theory that is skipped when the test collection and PAT are not configured.</summary>
    public sealed class IntegrationTheoryAttribute : TheoryAttribute
    {
        public IntegrationTheoryAttribute()
        {
            if (!TestEnvironment.IsConfigured)
            {
                Skip = TestEnvironment.SkipReason;
            }
        }
    }
}
