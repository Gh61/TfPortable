using System;
using System.Globalization;
using Microsoft.TeamFoundation.VersionControl.Client;
using TfPortable.Errors;

namespace TfPortable.Cli
{
    /// <summary>A parsed `/version:` value: a single version, or a range `from~to`.</summary>
    internal sealed class VersionRange
    {
        public VersionRange(VersionSpec? from, VersionSpec to, bool isRange)
        {
            From = from;
            To = to;
            IsRange = isRange;
        }

        /// <summary>Lower bound of a range; null for a single version.</summary>
        public VersionSpec? From { get; }

        /// <summary>The single version, or the upper bound of a range.</summary>
        public VersionSpec To { get; }

        public bool IsRange { get; }
    }

    /// <summary>
    /// Versionspec syntax of SPEC.md: `C<n>`, a bare number, `T`, `L<label>[@scope]`, `D<date>` (ISO 8601, then the
    /// current culture, then the invariant culture; a date without time is 00:00 local), `W[<name>[;<owner>]]`, and
    /// ranges `<from>~<to>`. Errors are TFP0302 (exit 100).
    /// </summary>
    internal static class VersionSpecParser
    {
        public const string Syntax = "C<changeset>, a bare changeset number, T (latest), L<label>[@scope], D<date> (ISO 8601), W[<workspace>[;<owner>]], or a range <from>~<to>";

        private static readonly string[] IsoFormats =
        {
            "yyyy-MM-dd",
            "yyyy-MM-dd'T'HH:mm",
            "yyyy-MM-dd'T'HH:mm:ss",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
            "yyyy-MM-dd'T'HH:mmK",
            "yyyy-MM-dd'T'HH:mm:ssK",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd HH:mm:ss",
        };

        /// <summary>Parses a single version or a range. `workspaceName`/`workspaceOwner` are used for a bare `W`.</summary>
        public static VersionRange ParseRange(string text, string? workspaceName, string? workspaceOwner)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw Invalid(text, "the versionspec is empty");
            }

            string[] parts = text.Trim().Split(new[] { '~' }, 2);
            if (parts.Length == 2)
            {
                return new VersionRange(ParseSingle(parts[0], workspaceName, workspaceOwner), ParseSingle(parts[1], workspaceName, workspaceOwner), true);
            }

            return new VersionRange(null, ParseSingle(parts[0], workspaceName, workspaceOwner), false);
        }

        public static VersionSpec ParseSingle(string text, string? workspaceName, string? workspaceOwner)
        {
            string t = (text ?? string.Empty).Trim();
            if (t.Length == 0)
            {
                throw Invalid(text, "the versionspec is empty");
            }

            if (char.IsDigit(t[0]))
            {
                return Changeset(t, t);
            }

            string rest = t.Substring(1);
            switch (char.ToUpperInvariant(t[0]))
            {
                case 'C':
                    return Changeset(rest, t);

                case 'T':
                    if (rest.Length != 0)
                    {
                        throw Invalid(t, "T takes no value");
                    }

                    return VersionSpec.Latest;

                case 'L':
                    {
                        if (rest.Length == 0)
                        {
                            throw Invalid(t, "L needs a label name");
                        }

                        int at = rest.IndexOf('@');
                        return at < 0 ? new LabelVersionSpec(rest) : new LabelVersionSpec(rest.Substring(0, at), rest.Substring(at + 1));
                    }

                case 'D':
                    return new DateVersionSpec(ParseDate(rest, t), t);

                case 'W':
                    {
                        if (rest.Length == 0)
                        {
                            if (workspaceName == null)
                            {
                                throw Invalid(t, "W without a name needs a workspace; run inside one or pass /workspace");
                            }

                            return new WorkspaceVersionSpec(workspaceName, workspaceOwner ?? string.Empty);
                        }

                        string[] parts = rest.Split(new[] { ';' }, 2);
                        return new WorkspaceVersionSpec(parts[0], parts.Length > 1 ? parts[1] : workspaceOwner ?? string.Empty);
                    }

                default:
                    throw Invalid(t, $"'{t[0]}' is not a version prefix");
            }
        }

        /// <summary>ISO 8601 first, then the current culture (as tf.exe), then the invariant culture.</summary>
        internal static DateTime ParseDate(string text, string? whole = null)
        {
            string t = text.Trim().Trim('"');
            if (t.Length == 0)
            {
                throw Invalid(whole ?? text, "D needs a date");
            }

            if (DateTime.TryParseExact(t, IsoFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var iso))
            {
                return iso;
            }

            if (DateTime.TryParse(t, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var current))
            {
                return current;
            }

            if (DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var invariant))
            {
                return invariant;
            }

            throw Invalid(whole ?? text, $"'{t}' is not a date");
        }

        private static VersionSpec Changeset(string number, string whole)
        {
            if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id <= 0)
            {
                throw Invalid(whole, "a changeset number must be a positive integer");
            }

            return new ChangesetVersionSpec(id);
        }

        private static TfpException Invalid(string? text, string why) =>
            new TfpException(ErrorCodes.InvalidVersionSpec, ExitCodes.GeneralError,
                $"'{text}' is not a valid version specification: {why}.",
                $"Use {Syntax}.");
    }
}
