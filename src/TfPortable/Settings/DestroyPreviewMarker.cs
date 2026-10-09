using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace TfPortable.Settings
{
    /// <summary>
    /// The proof that a `destroy /preview` ran (SPEC.md "Fáze 4"): a file under `%LOCALAPPDATA%\TfPortable\destroy\` named by the
    /// SHA-256 of the collection, the user, the server items and the switches. The real destroy needs a marker younger than
    /// <see cref="Validity"/> for the same key and consumes it, so that an agent cannot destroy what it did not list first.
    /// </summary>
    internal static class DestroyPreviewMarker
    {
        public static readonly TimeSpan Validity = TimeSpan.FromMinutes(10);

        /// <summary>Where the markers live; replaceable for tests.</summary>
        public static Func<string> DirectoryProvider { get; set; } = () =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TfPortable", "destroy");

        /// <summary>Clock; replaceable for tests.</summary>
        public static Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        /// <summary>
        /// The key of a destroy: the normalized collection, the user, the sorted items (`path;X<id>;<versionspec>` as the
        /// command resolved them) and the switches that change what is destroyed. `/stopat:5` and `/stopat:C5` are the same.
        /// </summary>
        public static string Key(string collectionUrl, string user, IEnumerable<string> serverItems, bool keepHistory, string? stopAt)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Canonical(collectionUrl, user, serverItems, keepHistory, stopAt)))).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string Canonical(string collectionUrl, string user, IEnumerable<string> serverItems, bool keepHistory, string? stopAt)
        {
            var canonical = new StringBuilder();
            canonical.Append(collectionUrl.ToLowerInvariant()).Append('\n');
            canonical.Append(user.ToLowerInvariant()).Append('\n');
            foreach (string item in serverItems.Select(i => i.ToLowerInvariant()).OrderBy(i => i, StringComparer.Ordinal))
            {
                canonical.Append(item).Append('\n');
            }

            canonical.Append(keepHistory ? "keephistory" : "-").Append('\n');
            canonical.Append(stopAt == null ? "-" : NormalizeVersion(stopAt)).Append('\n');
            return canonical.ToString();
        }

        /// <summary>`5` and `c5` become `C5`; everything else is upper-cased as given.</summary>
        internal static string NormalizeVersion(string text)
        {
            string upper = text.Trim().ToUpperInvariant();
            string digits = upper.StartsWith("C", StringComparison.Ordinal) ? upper.Substring(1) : upper;
            return digits.Length > 0 && digits.All(char.IsDigit) ? "C" + digits.TrimStart('0').PadLeft(1, '0') : upper;
        }

        /// <summary>Records that a preview with this key ran now, with what it covered.</summary>
        public static void Write(string key, string collectionUrl, string user, IEnumerable<string> serverItems, bool keepHistory, string? stopAt)
        {
            string directory = DirectoryProvider();
            Directory.CreateDirectory(directory);
            var json = new JObject
            {
                ["previewedAt"] = Clock().ToString("o"),
                ["collection"] = collectionUrl,
                ["user"] = user,
                ["items"] = new JArray(serverItems.Cast<object>().ToArray()),
                ["keepHistory"] = keepHistory,
                ["stopAt"] = stopAt,
            };
            File.WriteAllText(Path.Combine(directory, key + ".json"), json.ToString(Newtonsoft.Json.Formatting.Indented), Encoding.UTF8);
        }

        /// <summary>True when a marker younger than <see cref="Validity"/> exists for the key; a stale or unreadable one is deleted.</summary>
        public static bool IsValid(string key)
        {
            string path = Path.Combine(DirectoryProvider(), key + ".json");
            if (!File.Exists(path))
            {
                return false;
            }

            bool valid = false;
            try
            {
                var json = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
                string? stamp = (string?)json["previewedAt"];
                if (stamp != null && DateTime.TryParse(stamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime previewedAt))
                {
                    var age = Clock() - previewedAt.ToUniversalTime();
                    valid = age >= TimeSpan.Zero && age <= Validity;
                }
            }
            catch (Exception ex) when (ex is Newtonsoft.Json.JsonException || ex is IOException)
            {
                valid = false;
            }

            if (!valid)
            {
                Delete(key);
            }

            return valid;
        }

        /// <summary>Removes the marker: one preview covers one destroy that reached the server.</summary>
        public static void Delete(string key)
        {
            try
            {
                File.Delete(Path.Combine(DirectoryProvider(), key + ".json"));
            }
            catch (IOException)
            {
                // A marker that cannot be deleted still expires by age.
            }
        }

        /// <summary>Validity check and deletion in one step (tests and callers that do not reach a server).</summary>
        public static bool Consume(string key)
        {
            bool valid = IsValid(key);
            Delete(key);
            return valid;
        }
    }
}
