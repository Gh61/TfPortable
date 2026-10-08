using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using TfPortable.Errors;

namespace TfPortable.Settings
{
    /// <summary>
    /// `%APPDATA%\TfPortable\settings.json` (SPEC.md "Úrovně ochrany"): the protection level of this user. A missing file
    /// means the defaults; the file is written only by `tfp protection set`. No environment variable takes part.
    /// </summary>
    internal sealed class ToolSettings
    {
        public const int DefaultProtectionLevel = 2;

        /// <summary>Where the file lives; replaceable for tests.</summary>
        public static Func<string> PathProvider { get; set; } = () =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TfPortable", "settings.json");

        public static string FilePath => PathProvider();

        public int ProtectionLevel { get; set; } = DefaultProtectionLevel;

        /// <summary>Reads the file; defaults when it does not exist. An unreadable file is TFP0004 (the level cannot be trusted).</summary>
        public static ToolSettings Load()
        {
            string path = FilePath;
            var settings = new ToolSettings();
            if (!File.Exists(path))
            {
                return settings;
            }

            try
            {
                var json = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
                var token = json["protectionLevel"];
                if (token != null && token.Type != JTokenType.Null)
                {
                    long? level = token.Type == JTokenType.Integer ? (long?)token : null;
                    if (level == null || level < 1 || level > 3)
                    {
                        throw new TfpException(ErrorCodes.ProtectionLevel, ExitCodes.GeneralError,
                            $"The settings file {path} has protectionLevel {token.ToString(Newtonsoft.Json.Formatting.None)}. Only 1, 2 and 3 are valid.",
                            "Fix the value or delete the file to return to level 2.");
                    }

                    settings.ProtectionLevel = (int)level.Value;
                }

                return settings;
            }
            catch (Exception ex) when (ex is Newtonsoft.Json.JsonException || ex is IOException || ex is UnauthorizedAccessException || ex is InvalidCastException)
            {
                throw new TfpException(ErrorCodes.ProtectionLevel, ExitCodes.GeneralError,
                    $"The settings file {path} cannot be read: {ex.Message}",
                    "Fix or delete the file. Without it the protection level is 2.", inner: ex);
            }
        }

        public void Save()
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = new JObject { ["protectionLevel"] = ProtectionLevel };
            File.WriteAllText(path, json.ToString(Newtonsoft.Json.Formatting.Indented) + Environment.NewLine, new UTF8Encoding(false));
        }
    }
}
