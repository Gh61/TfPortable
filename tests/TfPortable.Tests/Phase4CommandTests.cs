using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.TeamFoundation.VersionControl.Client;
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
            Assert.StartsWith("ERROR TFP0002: /user and /group need /allow:, /deny: or /remove:.", Run("permission", "/inherit:no", "/user:x", "$/a").Err);
            Assert.StartsWith("ERROR TFP0002: /template and /location apply to workspace /new only.", Run("workspace", "Ws", "/template:Other").Err);
            Assert.StartsWith("ERROR TFP0002: /allow:Bogus is not a permission name.", Run("permission", "/allow:Bogus", "/user:x", "$/a").Err);
            Assert.StartsWith("ERROR TFP0006: Switch '/global' of command 'permission' is not supported by tfp yet: it collection-level permissions", Run("permission", "/global", "$/a").Err);
            Assert.StartsWith("ERROR TFP0002: workspace /new needs the workspace name.", Run("workspace", "/new").Err);
            Assert.StartsWith("ERROR TFP0002: workspace /delete needs the workspace name.", Run("workspace", "/delete").Err);
            Assert.StartsWith("ERROR TFP0006: Switch '/newowner' of command 'workspace' is not supported by tfp yet: it hands a workspace to another user", Run("workspace", "Ws", "/newowner:x").Err);
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
            DestroyPreviewMarker.Write(key, "https://dev.azure.com/org", "me@example.com", new[] { "$/P/b.txt", "$/P/a.txt;X5" }, false, null);
            DestroyPreviewMarker.Clock = () => now.AddMinutes(9);
            // Checking does not spend the marker; the destroy that reached the server deletes it.
            Assert.True(DestroyPreviewMarker.IsValid(key));
            Assert.True(DestroyPreviewMarker.Consume(key));
            Assert.False(DestroyPreviewMarker.Consume(key));

            DestroyPreviewMarker.Clock = () => now;
            DestroyPreviewMarker.Write(key, "https://dev.azure.com/org", "me@example.com", new[] { "$/P/b.txt" }, false, null);
            DestroyPreviewMarker.Clock = () => now.AddMinutes(11);
            Assert.False(DestroyPreviewMarker.IsValid(key));
            Assert.Empty(Directory.GetFiles(Path.Combine(directory, "destroy")));

            // The stamp is read as text and as UTC whatever the culture and the time zone (the developer's cs-CZ, UTC+2 machine saw every marker as stale:
            // Json.NET had turned the stamp into a DateTime whose string form lost the offset).
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("cs-CZ");
                var fromZulu = DestroyPreviewMarker.PreviewedAt("{ \"previewedAt\": \"2026-10-09T12:00:00.0000000Z\" }");
                Assert.Equal(DateTimeKind.Utc, fromZulu!.Value.Kind);
                Assert.Equal(now, fromZulu.Value);
                var fromOffset = DestroyPreviewMarker.PreviewedAt("{ \"previewedAt\": \"2026-10-09T14:00:00.0000000+02:00\" }");
                Assert.Equal(DateTimeKind.Utc, fromOffset!.Value.Kind);
                Assert.Equal(now, fromOffset.Value);
                Assert.Null(DestroyPreviewMarker.PreviewedAt("{ \"previewedAt\": \"yesterday\" }"));
                Assert.Null(DestroyPreviewMarker.PreviewedAt("{ }"));
                DestroyPreviewMarker.Clock = () => now;
                DestroyPreviewMarker.Write(key, "https://dev.azure.com/org", "me@example.com", new[] { "$/P/b.txt" }, false, null);
                DestroyPreviewMarker.Clock = () => now.AddMinutes(9);
                Assert.True(DestroyPreviewMarker.Consume(key));
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = culture;
            }

            // The version of the item and the spelling of /stopat are part of the key.
            Assert.Equal(DestroyPreviewMarker.Key("u", "me", new[] { "$/a;C5" }, true, "C5"), DestroyPreviewMarker.Key("u", "me", new[] { "$/a;C5" }, true, "5"));
            Assert.NotEqual(DestroyPreviewMarker.Key("u", "me", new[] { "$/a;C5" }, true, "C5"), DestroyPreviewMarker.Key("u", "me", new[] { "$/a;C6" }, true, "C5"));
            Assert.Equal("C5", DestroyPreviewMarker.NormalizeVersion("c05"));
            Assert.Equal("T", DestroyPreviewMarker.NormalizeVersion("t"));
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
        public void Property_write_form_parses_values_and_refuses_the_combinations_of_the_original()
        {
            var values = PropertyCommand.ParseSetValues("owner=team;note=a==b;;c;empty-ok=x", "tfp");
            Assert.Equal(new[] { "owner", "note", "empty-ok" }, values.Select(v => v.Key));
            Assert.Equal("team", values[0].Value);
            Assert.Equal("a=b;c", values[1].Value);
            Assert.Equal(new[] { "a", "b;c", "" }, PropertyCommand.SplitUnescaped("a;b;;c;", ';'));

            string file = Path.Combine(directory, "icon.bin");
            File.WriteAllBytes(file, new byte[] { 1, 2, 3 });
            var binary = PropertyCommand.ParseSetValues("icon=@" + file, "tfp").Single();
            Assert.Equal(new byte[] { 1, 2, 3 }, (byte[])binary.Value!);
            Assert.Contains("does not exist", Assert.Throws<TfpException>(() => PropertyCommand.ParseSetValues("icon=@" + Path.Combine(directory, "missing.bin"), "tfp")).Message);
            foreach (string bad in new[] { "owner", "=x", "owner=", "owner=team;", "" })
            {
                var error = Assert.Throws<TfpException>(() => PropertyCommand.ParseSetValues(bad, "tfp"));
                Assert.Equal(ErrorCodes.InvalidSwitch, error.Code);
            }

            // Repeated switches accumulate, as tf.exe accepts /deletevalues and /propertynames more than once.
            var repeated = CommandLine.Parse(new[] { "property", "/deletevalues:a", "/deletevalues:b,c", "x.txt" });
            Assert.Equal(new[] { "a", "b", "c" }, repeated.Values("deletevalues"));

            Assert.StartsWith("ERROR TFP0002: /setvalues and /deletevalues cannot be combined.", Run("property", "/setvalues:a=1", "/deletevalues:b", "x.txt").Err);
            Assert.StartsWith("ERROR TFP0002: /setvalues and /deleteall cannot be combined.", Run("property", "/setvalues:a=1", "/deleteall", "x.txt").Err);
            Assert.StartsWith("ERROR TFP0002: a property change and /output cannot be combined.", Run("property", "/deleteall", "/output:v.txt", "x.txt").Err);
            Assert.StartsWith("ERROR TFP0002: a property change and /version cannot be combined.", Run("property", "/deletevalues:a", "/version:C5", "x.txt").Err);
            Assert.StartsWith("ERROR TFP0002: a property change and /propertynames cannot be combined.", Run("property", "/setvalues:a=1", "/propertynames:a", "x.txt").Err);
            Assert.StartsWith("ERROR TFP0002: a property change and /collection cannot be combined.", Run("property", "/setvalues:a=1", "/collection:https://dev.azure.com/org", "x.txt").Err);
            Assert.StartsWith("ERROR TFP0002: /output needs exactly one property name in /propertynames.", Run("property", "/output:v.txt", "x.txt").Err);
            Assert.StartsWith("ERROR TFP0002: /output needs exactly one property name in /propertynames.", Run("property", "/output:v.txt", "/propertynames:a,b", "x.txt").Err);
            Assert.StartsWith("ERROR TFP0002: /output and /recursive cannot be combined.", Run("property", "/output:v.txt", "/propertynames:a", "/recursive", "x.txt").Err);
            Assert.StartsWith("ERROR TFP0002: /setvalues: 'owner' is not name=value.", Run("property", "/setvalues:owner", "x.txt").Err);
            Assert.Equal(LockLevel.Checkin, WorkspaceCommandSupport.LockLevelOf(CommandLine.Parse(new[] { "property", "/setvalues:a=1", "/lock:checkin", "x.txt" })));

            var set = CommandLine.Parse(new[] { "property", "/setvalues:a=1", "x.txt" });
            Assert.Equal((2, "setvalues"), ProtectionLevels.Required(set.Spec!, set));
            var deleteAll = CommandLine.Parse(new[] { "property", "/deleteall", "x.txt" });
            Assert.Equal((2, "deleteall"), ProtectionLevels.Required(deleteAll.Spec!, deleteAll));
            var read = CommandLine.Parse(new[] { "property", "/propertynames:a", "/output:v.txt", "x.txt" });
            Assert.Equal((1, (string?)null), ProtectionLevels.Required(read.Spec!, read));
            new ToolSettings { ProtectionLevel = 1 }.Save();
            Assert.StartsWith("ERROR TFP0004: Switch '/setvalues' of 'property' needs protection level 2.", Run("property", "/setvalues:a=1", "x.txt").Err);
        }

        [Fact]
        public void Unsupported_switches_of_tf_exe_are_TFP0006_and_server_locks_and_checkin_notes_are_accepted()
        {
            // Switches tf.exe has and TfPortable leaves out for now: a distinct code with the reason, before anything connects.
            Assert.StartsWith("ERROR TFP0006: Switch '/bypass' of command 'checkin' is not supported by tfp yet: it bypasses a gated check-in build", Run("checkin", "/bypass", "/comment:x", "a.txt").Err);
            Assert.StartsWith("ERROR TFP0006: Switch '/new' of command 'shelve' is not supported by tfp yet", Run("shelve", "/new", "s1").Err);
            Assert.StartsWith("ERROR TFP0006: Switch '/options' of command 'difference' is not supported by tfp yet", Run("difference", "/options:x", "a.txt").Err);
            Assert.StartsWith("ERROR TFP0006: Value 'context' of '/format' for command 'difference' is not supported by tfp yet.", Run("difference", "/format:context", "a.txt").Err);
            Assert.StartsWith("ERROR TFP0006: Switch '/updateusername' of command 'workspaces' is not supported by tfp yet", Run("workspaces", "/updateUserName:x").Err);
            Assert.StartsWith("ERROR TFP0006: Switch '/owner' of command 'label' is not supported by tfp yet", Run("label", "L1", "a.txt", "/owner:x").Err);
            Assert.StartsWith("ERROR TFP0006: Switch '/extended' of command 'merges' is not supported by tfp yet", Run("merges", "$/a", "/extended").Err);
            Assert.StartsWith("ERROR TFP0006: Switch '/filterlocalpathsonly' of command 'folderdiff' is not supported by tfp yet", Run("folderdiff", "$/a", "$/b", "/filterLocalPathsOnly").Err);
            Assert.StartsWith("ERROR TFP0006: Switch '/checkin' of command 'branch' is not supported by tfp yet", Run("branch", "$/a", "$/b", "/checkin").Err);
            Assert.StartsWith("ERROR TFP0006: Switch '/overridetype' of command 'resolve' is not supported by tfp yet", Run("resolve", "/overridetype:text").Err);
            Assert.Equal(100, Run("shelvesets", "/format:xml").Exit);
            Assert.StartsWith("ERROR TFP0002:", Run("status", "/format:bogus").Err);
            Assert.StartsWith("ERROR TFP0002:", Run("checkin", "/nosuchswitch", "/comment:x").Err);

            // Help marks them and the catalog keeps them; the JSON help carries the reason.
            var help = Run("help", "checkin");
            Assert.Contains("/bypass", help.Out);
            Assert.Contains("(TFP0006: it bypasses a gated check-in build", help.Out);
            var json = JObject.Parse(Run("help", "checkin", "/format:json").Out);
            var bypass = ((JArray)json["data"]!["commands"]!.First(c => (string)c["name"]! == "checkin")["switches"]!).First(s => (string)s["name"]! == "bypass");
            Assert.StartsWith("bypasses a gated check-in build", (string)bypass["unsupported"]!);
            var comment = ((JArray)json["data"]!["commands"]!.First(c => (string)c["name"]! == "checkin")["switches"]!).First(s => (string)s["name"]! == "comment");
            Assert.Null((string?)comment["unsupported"]);

            // Server locks on the pend commands and the checkin notes are accepted by the parser and levels (the server call follows).
            foreach (string[] line in new[] { new[] { "add", "/lock:checkin", "a.txt" }, new[] { "checkout", "/lock:checkout", "a.txt" }, new[] { "delete", "/lock:checkin", "a.txt" },
                new[] { "rename", "/lock:checkin", "a.txt", "b.txt" }, new[] { "merge", "$/a", "$/b", "/lock:checkin" }, new[] { "branch", "$/a", "$/b", "/lock:checkout" },
                new[] { "rollback", "/changeset:5", "/lock:checkin" }, new[] { "undelete", "a.txt", "/lock:checkin" } })
            {
                var parsed = CommandLine.Parse(line);
                Assert.Equal(line[0], parsed.Spec!.Name);
                Assert.Equal(line.Any(a => a == "/lock:checkout") ? LockLevel.CheckOut : LockLevel.Checkin, WorkspaceCommandSupport.LockLevelOf(parsed));
                Assert.Equal(2, ProtectionLevels.Required(parsed.Spec!, parsed).Level);
            }

            Assert.Equal(LockLevel.None, WorkspaceCommandSupport.LockLevelOf(CommandLine.Parse(new[] { "add", "a.txt" })));
            Assert.Equal(LockLevel.None, WorkspaceCommandSupport.LockLevelOf(CommandLine.Parse(new[] { "add", "/lock:none", "a.txt" })));
            Assert.StartsWith("ERROR TFP0002: 'both' is not a valid value of '/lock'", Run("add", "/lock:both", "a.txt").Err);

            var notes = ChangesetCommand.Notes(CommandLine.Parse(new[] { "checkin", "/comment:x", "/notes:Code Reviewer=Jan Novak;Security Reviewer=none", "a.txt" }), "tfp")!;
            Assert.Equal(new[] { "Code Reviewer", "Security Reviewer" }, notes.Select(n => n.Key));
            Assert.Equal("Jan Novak", notes[0].Value);
            Assert.StartsWith("ERROR TFP0002: /notes entry 'nope' is not Name=Value.", Run("checkin", "/comment:x", "/notes:nope", "a.txt").Err);
            Assert.StartsWith("ERROR TFP0002: checkin /shelveset takes no itemspec, /comment, /notes, /validate or /override", Run("checkin", "/shelveset:s1", "/notes:a=b").Err);

            // The changeset block prints the notes as tf.exe does.
            var changeset = new ChangesetInfo(5, "me", "me@x", new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc), "c", Array.Empty<ChangeInfo>(), Array.Empty<WorkItemInfo>(),
                new[] { new KeyValuePair<string, string>("Code Reviewer", "Jan Novak") });
            var block = ChangesetCommand.RenderBlock(changeset).ToList();
            Assert.Contains("Check-in Notes:", block);
            Assert.Contains("  Code Reviewer: Jan Novak", block);
            Assert.Equal("Jan Novak", (string)changeset.ToJson(true)["notes"]!["Code Reviewer"]!);
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
