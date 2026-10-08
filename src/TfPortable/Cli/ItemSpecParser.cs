using System;
using System.Globalization;
using System.IO;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.TeamFoundation.VersionControl.Common;
using TfPortable.Errors;

namespace TfPortable.Cli
{
    /// <summary>
    /// A parsed itemspec (SPEC.md "Itemspec"): a server path (`$/Project/...`) or an absolute local path, optionally
    /// followed by `;<versionspec>` and `;X<deletionId>`. Local paths accept '/' and '\' and are made absolute
    /// against the current directory; wildcards (`*`, `?`) are allowed in the last segment only.
    /// </summary>
    internal sealed class ParsedItemSpec
    {
        public ParsedItemSpec(string original, string path, bool isServerPath, VersionSpec? version, string? versionText, int deletionId)
        {
            Original = original;
            Path = path;
            IsServerPath = isServerPath;
            Version = version;
            VersionText = versionText;
            DeletionId = deletionId;
        }

        /// <summary>The argument as given.</summary>
        public string Original { get; }

        /// <summary>Normalized path without the `;` suffixes: `$/...` or an absolute local path.</summary>
        public string Path { get; }

        public bool IsServerPath { get; }

        /// <summary>Version from a `;C123` style suffix, or null.</summary>
        public VersionSpec? Version { get; }

        public string? VersionText { get; }

        /// <summary>Deletion id from a `;X18` suffix, 0 when none.</summary>
        public int DeletionId { get; }

        public bool HasWildcard => ItemSpecParser.HasWildcard(Path);

        /// <summary>The library's ItemSpec for the path (server or local) with the given recursion.</summary>
        public ItemSpec ToItemSpec(RecursionType recursion) => new ItemSpec(Path, recursion, DeletionId);
    }

    internal static class ItemSpecParser
    {
        private static readonly char[] Wildcards = { '*', '?' };

        public static ParsedItemSpec Parse(string text, string? workspaceName = null, string? workspaceOwner = null)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "An itemspec is empty.",
                    "Pass a local path or a server path ($/Project/...).");
            }

            string[] parts = text.Trim().Split(';');
            string path = parts[0];
            VersionSpec? version = null;
            string? versionText = null;
            int deletionId = 0;

            for (int i = 1; i < parts.Length; i++)
            {
                string suffix = parts[i].Trim();
                if (suffix.Length == 0)
                {
                    continue;
                }

                if ((suffix[0] == 'X' || suffix[0] == 'x') && suffix.Length > 1 && int.TryParse(suffix.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int id))
                {
                    deletionId = id;
                    continue;
                }

                if (version != null)
                {
                    throw new TfpException(ErrorCodes.InvalidVersionSpec, ExitCodes.GeneralError,
                        $"'{text}' has more than one version suffix.",
                        "Use one ';<versionspec>' suffix, e.g. '$/Project/a.cs;C1234'.", item: text);
                }

                version = VersionSpecParser.ParseSingle(suffix, workspaceName, workspaceOwner);
                versionText = suffix;
            }

            bool server = IsServerPath(path);
            string normalized = server ? NormalizeServerPath(path) : NormalizeLocalPath(path, text);
            return new ParsedItemSpec(text, normalized, server, version, versionText, deletionId);
        }

        public static bool IsServerPath(string path) =>
            path.Length >= 2 && path[0] == '$' && (path[1] == '/' || path[1] == '\\');

        public static bool HasWildcard(string path) => path.IndexOfAny(Wildcards) >= 0;

        /// <summary>`*` and `?` match a file name (case-insensitive), as the filters of `folderdiff` need.</summary>
        public static bool WildcardMatches(string pattern, string name)
        {
            string regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(name, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        /// <summary>`$\a\b` becomes `$/a/b`; a trailing separator is removed except on `$/`.</summary>
        public static string NormalizeServerPath(string path)
        {
            string p = path.Replace('\\', '/');
            while (p.Length > 2 && p.EndsWith("/", StringComparison.Ordinal))
            {
                p = p.Substring(0, p.Length - 1);
            }

            return p;
        }

        /// <summary>Absolute path with the platform separator; a wildcard is kept in the last segment.</summary>
        public static string NormalizeLocalPath(string path, string? original = null)
        {
            string p = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            try
            {
                if (!HasWildcard(p))
                {
                    return TrimSeparator(Path.GetFullPath(p));
                }

                int separator = p.LastIndexOf(Path.DirectorySeparatorChar);
                string directory = separator < 0 ? "." : (separator == 0 ? p.Substring(0, 1) : p.Substring(0, separator));
                string name = p.Substring(separator + 1);
                if (HasWildcard(directory))
                {
                    throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError,
                        $"'{original ?? path}' has a wildcard outside the last path segment.",
                        "Wildcards (* and ?) are allowed in the file name only; use /recursive for subfolders.", item: original ?? path);
                }

                return Path.Combine(TrimSeparator(Path.GetFullPath(directory)), name);
            }
            catch (ArgumentException ex)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError,
                    $"'{original ?? path}' is not a valid local path: {ex.Message}",
                    "Pass a local path (absolute or relative to the current directory) or a server path ($/Project/...).", item: original ?? path, inner: ex);
            }
            catch (NotSupportedException ex)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError,
                    $"'{original ?? path}' is not a valid local path: {ex.Message}",
                    "Pass a local path (absolute or relative to the current directory) or a server path ($/Project/...).", item: original ?? path, inner: ex);
            }
        }

        private static string TrimSeparator(string path)
        {
            string root = Path.GetPathRoot(path) ?? string.Empty;
            while (path.Length > root.Length && path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                path = path.Substring(0, path.Length - 1);
            }

            return path;
        }
    }
}
