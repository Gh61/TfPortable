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

        /// <summary>The key of a destroy: the normalized collection, the user, the sorted `path;X<id>` items and the switches that change what is destroyed.</summary>
        public static string Key(string collectionUrl, string user, IEnumerable<string> serverItems, bool keepHistory, string? stopAt)
        {
            var canonical = new StringBuilder();
            canonical.Append(collectionUrl.ToLowerInvariant()).Append('\n');
            canonical.Append(user.ToLowerInvariant()).Append('\n');
            foreach (string item in serverItems.Select(i => i.ToLowerInvariant()).OrderBy(i => i, StringComparer.Ordinal))
            {
                canonical.Append(item).Append('\n');
            }

            canonical.Append(keepHistory ? "keephistory" : "-").Append('\n');
            canonical.Append(stopAt == null ? "-" : stopAt.ToUpperInvariant()).Append('\n');

            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()))).Replace("-", string.Empty).ToLowerInvariant();
        }

        /// <summary>Records that a preview with this key ran now.</summary>
        public static void Write(string key, IEnumerable<string> serverItems)
        {
            string directory = DirectoryProvider();
            Directory.CreateDirectory(directory);
            var json = new JObject
            {
                ["previewedAt"] = Clock().ToString("o"),
                ["items"] = new JArray(serverItems.Cast<object>().ToArray()),
            };
            File.WriteAllText(Path.Combine(directory, key + ".json"), json.ToString(Newtonsoft.Json.Formatting.Indented), Encoding.UTF8);
        }

        /// <summary>True when a valid marker exists for the key; the marker is deleted either way (one preview covers one destroy).</summary>
        public static bool Consume(string key)
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

            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A marker that cannot be deleted still expires by age.
            }

            return valid;
        }
    }
}
