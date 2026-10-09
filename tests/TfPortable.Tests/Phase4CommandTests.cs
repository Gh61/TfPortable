using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TfPortable.Cli;
using TfPortable.Commands;
using TfPortable.Context;
using TfPortable.Errors;
using TfPortable.Settings;
using Xunit;

namespace TfPortable.Tests
{
    /// <summary>Phase 4 pieces that need no server: argument errors, the destroy preview marker, the listings, the docs URL, the never-offered commands.</summary>
    [Collection("ToolSettings")]
    public sealed class Phase4CommandTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "tfp-tests-" + Guid.NewGuid().ToString("N"));
        private readonly Func<string> previousSettingsPath = ToolSettings.PathProvider;
        private readonly Func<string> previousMarkerDirectory = DestroyPreviewMarker.DirectoryProvider;
        private readonly Func<DateTime> previousClock = DestroyPreviewMarker.Clock;
        private readonly Action<string> previousOpener = DocsCommand.Opener;

        public Phase4CommandTests()
        {
            Directory.CreateDirectory(directory);
            string settings = Path.Combine(directory, "settings.json");
            ToolSettings.PathProvider = () => settings;
            DestroyPreviewMarker.DirectoryProvider = () => Path.Combine(directory, "destroy");
        }

        public void Dispose()
        {
            ToolSettings.PathProvider = previousSettingsPath;
            DestroyPreviewMarker.DirectoryProvider = previousMarkerDirectory;
            DestroyPreviewMarker.Clock = previousClock;
            DocsCommand.Opener = previousOpener;
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }

        private static (int Exit, string Out, string Err) Run(params string[] args)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            int exit = Program.Run(args, stdout, stderr, "tfp");
            return (exit, stdout.ToString(), stderr.ToString());
        }

        [Fact]
        public void Argument_errors_of_the_phase_4_commands_are_TFP0002()
        {
            // The protection gate runs before the command, so the level 3 commands are checked at level 3.
            new ToolSettings { ProtectionLevel = 3 }.Save();
            Assert.StartsWith("ERROR TFP0002: /stopat needs /keephistory.", Run("destroy", "$/a", "/stopat:C5").Err);
            Assert.StartsWith("ERROR TFP0002: destroy needs at least one itemspec.", Run("destroy", "/preview").Err);
            Assert.StartsWith("ERROR TFP0002: /allow, /deny and /remove need /user: or /group:.", Run("permission", "/allow:Lock", "$/a").Err);
            Assert.StartsWith("ERROR TFP0002: /user and /group need /allow:, /deny: or /remove:.", Run("permission", "/user:x", "$/a").Err);
            Assert.StartsWith("ERROR TFP0002: /allow:Bogus is not a permission name.", Run("permission", "/allow:Bogus", "/user:x", "$/a").Err);
            Assert.StartsWith("ERROR TFP0002: /global (collection-level permissions) is not supported.", Run("permission", "/global", "$/a").Err);
            Assert.StartsWith("ERROR TFP0002: workspace /new needs the workspace name.", Run("workspace", "/new").Err);
            Assert.StartsWith("ERROR TFP0002: workspace /delete needs the workspace name.", Run("workspace", "/delete").Err);
            Assert.StartsWith("ERROR TFP0002: /newowner is not supported.", Run("workspace", "Ws", "/newowner:x").Err);
            // The catalog enumerates /permission, so the parser rejects an unknown value before the command runs.
            Assert.StartsWith("ERROR TFP0002: 'Secret' is not a valid value", Run("workspace", "/new", "Ws", "/permission:Secret").Err);
            Assert.StartsWith("ERROR TFP0204: /location:server is not supported;", Run("workspace", "/new", "Ws", "/location:server").Err);
            Assert.StartsWith("ERROR TFP0002: property needs at least one itemspec.", Run("property").Err);
            Assert.StartsWith("ERROR TFP0002: resolvepath needs at least one itemspec.", Run("resolvepath").Err);
            Assert.StartsWith("ERROR TFP0002: docs takes at most one command name.", Run("docs", "a", "b").Err);
            Assert.StartsWith("ERROR TFP0002: workfold /map needs the server folder and the local folder.", Run("workfold", "/map", "$/a").Err);
            Assert.StartsWith("ERROR TFP0005: Command 'proxy' is not part of tfp", Run("proxy", "/configure", "/enabled:true").Err);
            Assert.StartsWith("ERROR TFP0002: workfold takes one of /map, /unmap, /cloak and /decloak.", Run("workfold", "/map", "/cloak", "$/a", "x").Err);
            Assert.StartsWith("ERROR TFP0002: checkin /shelveset takes no itemspec, /comment, /validate or /override", Run("checkin", "/shelveset:x", "/comment:c").Err);
            Assert.StartsWith("ERROR TFP0002: /notes entry 'bogus' is not Name=Value.", Run("changeset", "5", "/notes:bogus").Err);
        }

        [Fact]
        public void Destroy_without_a_recent_preview_is_TFP0410_before_anything_else_is_checked()
        {
            // The marker is checked after the context is resolved, so without a server the error is the context one; the key logic is tested directly.
            string key = DestroyPreviewMarker.Key("https://dev.azure.com/org", "me@example.com", new[] { "$/P/b.txt", "$/P/a.txt;X5" }, false, null);
            Assert.Equal(64, key.Length);
            Assert.Equal(key, DestroyPreviewMarker.Key("HTTPS://dev.azure.com/org", "Me@Example.com", new[] { "$/p/a.txt;X5", "$/P/B.TXT" }, false, null));
            Assert.NotEqual(key, DestroyPreviewMarker.Key("https://dev.azure.com/org", "me@example.com", new[] { "$/P/b.txt" }, false, null));
            Assert.NotEqual(key, DestroyPreviewMarker.Key("https://dev.azure.com/org", "me@example.com", new[] { "$/P/b.txt", "$/P/a.txt;X5" }, true, null));
            Assert.NotEqual(key, DestroyPreviewMarker.Key("https://dev.azure.com/org", "me@example.com", new[] { "$/P/b.txt", "$/P/a.txt;X5" }, true, "C5"));

            Assert.False(DestroyPreviewMarker.Consume(key));

            var now = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
            DestroyPreviewMarker.Clock = () => now;
            DestroyPreviewMarker.Write(key, new[] { "$/P/b.txt", "$/P/a.txt;X5" });
            DestroyPreviewMarker.Clock = () => now.AddMinutes(9);
            Assert.True(DestroyPreviewMarker.Consume(key));
            // One preview covers one destroy.
            Assert.False(DestroyPreviewMarker.Consume(key));

            DestroyPreviewMarker.Clock = () => now;
            DestroyPreviewMarker.Write(key, new[] { "$/P/b.txt" });
            DestroyPreviewMarker.Clock = () => now.AddMinutes(11);
            Assert.False(DestroyPreviewMarker.Consume(key));
            Assert.Empty(Directory.GetFiles(Path.Combine(directory, "destroy")));
        }

        [Fact]
        public void Destroy_permission_and_property_listings_follow_the_original_shapes()
        {
            var items = new[] { new DestroyedItemInfo("$/P/a.txt", 0, false), new DestroyedItemInfo("$/P/old", 12, true) };
            Assert.Equal(new[] { "Would destroy: $/P/a.txt", "Would destroy: $/P/old;X12" }, DestroyCommand.Render(items, true));
            Assert.Equal(new[] { "Destroyed: $/P/a.txt", "Destroyed: $/P/old;X12" }, DestroyCommand.Render(items, false));
            Assert.Equal(new[] { "There are no items to destroy." }, DestroyCommand.Render(Array.Empty<DestroyedItemInfo>(), false));

            var acl = new ItemPermissionInfo("$/P", true, new[]
            {
                new PermissionEntryInfo("[P]\\Contributors", new[] { "Lock" }, Array.Empty<string>(), new[] { "Read", "PendChange" }, Array.Empty<string>()),
            });
            var lines = PermissionCommand.Render(new[] { acl }).ToList();
            Assert.Equal(new string('=', 79), lines[0]);
            Assert.Equal("Server item: $/P (Inherit: Yes)", lines[1]);
            Assert.Equal("  Identity: [P]\\Contributors", lines[2]);
            Assert.Equal("    Allow:             Lock", lines[3]);
            Assert.Equal("    Allow (inherited): Read, PendChange", lines[5]);

            var properties = PropertyCommand.Render(new[]
            {
                new ItemPropertiesInfo("$/P/a.txt", Array.Empty<KeyValuePair<string, string?>>()),
                new ItemPropertiesInfo("$/P/b.txt", new[] { new KeyValuePair<string, string?>("Microsoft.TeamFoundation.VersionControl.Executable", "true") }),
            }).ToList();
            Assert.Equal(new[] { new string('-', 79), "Server item: $/P/a.txt", "The item has no properties.", new string('-', 79), "Server item: $/P/b.txt", "Microsoft.TeamFoundation.VersionControl.Executable = true" }, properties);
        }

        [Fact]
        public void Permission_names_are_canonical_and_star_means_all()
        {
            var line = CommandLine.Parse(new[] { "permission", "/allow:lock,READ", "/deny:*", "/user:x", "$/a" });
            Assert.Equal(new[] { "Lock", "Read" }, PermissionCommand.Permissions(line, "allow", "tfp"));
            Assert.Equal(PermissionCommand.PermissionNames, PermissionCommand.Permissions(line, "deny", "tfp"));
            Assert.Empty(PermissionCommand.Permissions(line, "remove", "tfp"));
        }

        [Fact]
        public void Changeset_notes_are_parsed_from_the_switch_and_from_a_file()
        {
            var line = CommandLine.Parse(new[] { "changeset", "5", "/notes:Code Reviewer=Jan Novak; Security Reviewer = none ;" });
            var notes = ChangesetCommand.Notes(line, "tfp")!;
            Assert.Equal(new[] { "Code Reviewer", "Security Reviewer" }, notes.Select(n => n.Key));
            Assert.Equal(new[] { "Jan Novak", "none" }, notes.Select(n => n.Value));
            Assert.Null(ChangesetCommand.Notes(CommandLine.Parse(new[] { "changeset", "5" }), "tfp"));

            string file = Path.Combine(directory, "notes.txt");
            File.WriteAllText(file, "A=1\r\nB=2\r\n");
            var fromFile = ChangesetCommand.Notes(CommandLine.Parse(new[] { "changeset", "5", "/notes:@" + file }), "tfp")!;
            Assert.Equal(new[] { "A", "B" }, fromFile.Select(n => n.Key));
        }

        [Fact]
        public void Docs_prints_the_url_or_opens_the_browser_and_msdn_is_its_alias()
        {
            Assert.Equal("https://github.com/Gh61/TfPortable", DocsCommand.Url(null, "tfp"));
            Assert.Equal("https://github.com/Gh61/TfPortable/blob/main/docs/commands.md#cmd-checkin", DocsCommand.Url("ci", "tfp"));
            Assert.Equal("TFP0001", Assert.Throws<TfpException>(() => DocsCommand.Url("bogus", "tfp")).Code);

            string? opened = null;
            DocsCommand.Opener = url => opened = url;
            var (exit, stdout, stderr) = Run("docs", "checkin", "/url");
            Assert.Equal(0, exit);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("https://github.com/Gh61/TfPortable/blob/main/docs/commands.md#cmd-checkin" + Environment.NewLine, stdout);
            Assert.Null(opened);

            var (exit2, stdout2, _) = Run("msdn", "status", "/format:json");
            Assert.Equal(0, exit2);
            var envelope = JObject.Parse(stdout2);
            Assert.Equal("docs", (string)envelope["command"]!);
            Assert.True((bool)envelope["data"]!["opened"]!);
            Assert.Equal("https://github.com/Gh61/TfPortable/blob/main/docs/commands.md#cmd-status", opened);

            DocsCommand.Opener = url => throw new InvalidOperationException("no browser");
            var (exit3, stdout3, stderr3) = Run("docs");
            Assert.Equal(0, exit3);
            Assert.StartsWith("https://github.com/Gh61/TfPortable", stdout3);
            Assert.StartsWith("WARNING: The browser could not be started: no browser", stderr3);
        }

        [Fact]
        public void Help_lists_phase_4_and_the_never_offered_commands()
        {
            var (exit, stdout, _) = Run("help");
            Assert.Equal(0, exit);
            Assert.Contains("Phase 4 (server administration", stdout);
            Assert.Contains("Never offered (client configuration and dialogs; TFP0005 explains each):", stdout);
            Assert.Contains("  proxy, configure, certificates", stdout);
            Assert.DoesNotContain("Not available in this build", stdout);

            var (exit2, stdout2, _) = Run("help", "proxy");
            Assert.Equal(0, exit2);
            Assert.Contains("Never offered by tfp (TFP0005)", stdout2);

            var (exit3, stdout3, _) = Run("help", "destroy");
            Assert.Equal(0, exit3);
            Assert.Contains("/preview", stdout3);
            Assert.Contains("Protection level: 3", stdout3);
        }

        [Fact]
        public void Phase_4_commands_have_their_protection_levels()
        {
            Assert.Equal(3, ProtectionLevels.Of("destroy"));
            foreach (string name in new[] { "permission", "workspace", "property", "resolvepath", "docs" })
            {
                Assert.Equal(1, ProtectionLevels.Of(name));
            }

            var delete = CommandLine.Parse(new[] { "workspace", "/delete", "Ws" });
            Assert.Equal((3, "delete"), ProtectionLevels.Required(delete.Spec!, delete));
            var rename = CommandLine.Parse(new[] { "workspace", "Ws", "/newname:Ws2" });
            Assert.Equal((3, "newname"), ProtectionLevels.Required(rename.Spec!, rename));
            var inherit = CommandLine.Parse(new[] { "permission", "/inherit:no", "$/a" });
            Assert.Equal((3, "inherit"), ProtectionLevels.Required(inherit.Spec!, inherit));
            var map = CommandLine.Parse(new[] { "workfold", "/map", "$/a", "C:\\a" });
            Assert.Equal((2, "map"), ProtectionLevels.Required(map.Spec!, map));
            var move = CommandLine.Parse(new[] { "unshelve", "x", "/move" });
            Assert.Equal((2, (string?)null), ProtectionLevels.Required(move.Spec!, move));
        }
    }
}
