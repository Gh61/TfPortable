using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TfPortable.IntegrationTests.Golden;
using Xunit;

namespace TfPortable.IntegrationTests
{
    /// <summary>
    /// Live tests of phase 4 against the test collection: a second workspace of the CI user (created, edited, used for a
    /// foreign lock and undo, deleted), permissions, properties, path resolution, mapping edits, changeset edits, shelveset
    /// checkin, unshelve /move and a destroy after its preview. Everything is confined to `$/TfsPortableTestRepo/CI/&lt;run&gt;-*`
    /// and the temporary workspace, and cleaned up at the end.
    /// </summary>
    [Collection(GoldenCollection.Name)]
    public class Phase4Tests
    {
        private static readonly string RunId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID") ?? "local-" + Guid.NewGuid().ToString("N").Substring(0, 8);

        private readonly GoldenWorkspaceFixture fixture;

        public Phase4Tests(GoldenWorkspaceFixture fixture)
        {
            this.fixture = fixture;
        }

        private TfpResult Run(string arguments) => TfpRunner.Run(arguments, fixture.WorkspaceRoot);

        private static JToken Data(TfpResult result) => JObject.Parse(result.Stdout)["data"]!;

        private string CiFolder(string test) => Path.Combine(fixture.WorkspaceRoot, "CI", RunId + "-" + test);

        private static string CiArgument(string test) => "CI\\" + RunId + "-" + test;

        private static string CiServerPath(string test) => GoldenWorkspaceFixture.ServerRoot + "/CI/" + RunId + "-" + test;

        [IntegrationFact]
        public void Docs_resolvepath_property_and_permission_listing_read_without_level_3()
        {
            fixture.EnsureReady();

            var docs = Run("docs checkin /url /format:json");
            Assert.True(docs.ExitCode == 0, docs.Stderr + docs.Stdout);
            Assert.Equal("https://github.com/Gh61/TfPortable/blob/main/docs/commands.md#cmd-checkin", (string)Data(docs)["url"]!);
            Assert.False((bool)Data(docs)["opened"]!);

            var resolve = Run($"resolvepath Main {GoldenWorkspaceFixture.ServerRoot}/Main/README.md /format:json");
            Assert.True(resolve.ExitCode == 0, resolve.Stderr + resolve.Stdout);
            var items = (JArray)Data(resolve)["items"]!;
            Assert.Equal(2, items.Count);
            Assert.Equal(GoldenWorkspaceFixture.ServerRoot + "/Main", (string)items[0]["serverPath"]!);
            Assert.Equal(Path.Combine(fixture.WorkspaceRoot, "Main", "README.md"), (string)items[1]["localPath"]!, StringComparer.OrdinalIgnoreCase);

            var unmapped = Run("resolvepath $/NoSuchProject/x.txt");
            Assert.Equal(1, unmapped.ExitCode);
            Assert.Contains("TFP0203", unmapped.Stderr);

            var property = Run("property Main\\README.md /format:json");
            Assert.True(property.ExitCode == 0, property.Stderr + property.Stdout);
            var propertyItems = (JArray)Data(property)["items"]!;
            Assert.Single(propertyItems);
            Assert.Equal(GoldenWorkspaceFixture.ServerRoot + "/Main/README.md", (string)propertyItems[0]["serverPath"]!);
            var text = Run("property Main\\README.md");
            Assert.Contains("Server item: " + GoldenWorkspaceFixture.ServerRoot + "/Main/README.md", text.Stdout);

            var permission = Run($"permission {GoldenWorkspaceFixture.ServerRoot}/Main /format:json");
            Assert.True(permission.ExitCode == 0, permission.Stderr + permission.Stdout);
            var acl = ((JArray)Data(permission)["items"]!).Single();
            Assert.Equal(GoldenWorkspaceFixture.ServerRoot + "/Main", (string)acl["serverItem"]!);
            Assert.NotEmpty((JArray)acl["entries"]!);
            var listing = Run($"permission {GoldenWorkspaceFixture.ServerRoot}/Main");
            Assert.Contains("Server item: " + GoldenWorkspaceFixture.ServerRoot + "/Main (Inherit:", listing.Stdout);
            Assert.Contains("    Allow (inherited):", listing.Stdout);

            var blocked = Run($"permission /allow:Lock /user:nobody@example.com {GoldenWorkspaceFixture.ServerRoot}/Main");
            Assert.Equal(100, blocked.ExitCode);
            Assert.StartsWith("ERROR TFP0004: Switch '/allow' of 'permission' needs protection level 3.", blocked.Stderr);
        }

        [IntegrationFact]
        public void Workspace_new_edit_foreign_lock_undo_and_delete()
        {
            fixture.EnsureReady();
            string name = "tfp-ci-ws-" + RunId;
            string root = Path.Combine(Path.GetTempPath(), "tfp-ci-ws-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(root);
            string owner = (string)Data(Run("whoami /format:json"))["identity"]!["uniqueName"]!;
            bool created = false;
            try
            {
                var blocked = TfpRunner.Run($"workspace /new {name} /collection:{fixture.Collection} /noprompt", root);
                Assert.Equal(100, blocked.ExitCode);
                Assert.StartsWith("ERROR TFP0004: Switch '/new' of 'workspace' needs protection level 3.", blocked.Stderr);

                using (ProtectionLevelScope.Set(3))
                {
                    var create = TfpRunner.Run($"workspace /new {name} /comment:\"CI workspace ({RunId})\" /permission:PublicLimited /filetime:checkin /collection:{fixture.Collection} /noprompt /format:json", root);
                    created = create.ExitCode == 0;
                    Assert.True(created, create.Stderr + create.Stdout);
                    var workspace = Data(create)["workspace"]!;
                    Assert.Equal(name, (string)workspace["name"]!);
                    Assert.Equal("local", (string)workspace["location"]!);
                    // The profile is applied through the workspace security namespace; a server without it reports Private.
                    Assert.Contains((string)workspace["permission"]!, new[] { "publiclimited", "private" });
                    var mapping = ((JArray)workspace["mappings"]!).Single();
                    Assert.Equal("$/", (string)mapping["serverPath"]!);
                    Assert.Equal(root.TrimEnd(Path.DirectorySeparatorChar), ((string)mapping["localPath"]!).TrimEnd(Path.DirectorySeparatorChar), StringComparer.OrdinalIgnoreCase);

                    var show = Run($"workspace {name} /format:json");
                    Assert.True(show.ExitCode == 0, show.Stderr + show.Stdout);
                    Assert.Equal($"CI workspace ({RunId})", (string)Data(show)["workspace"]!["comment"]!);
                    var listed = Run($"workspaces {name} /format:json");
                    Assert.True(listed.ExitCode == 0, listed.Stderr + listed.Stdout);

                    var edit = Run($"workspace {name} /comment:\"CI workspace edited ({RunId})\" /permission:Private /format:json");
                    Assert.True(edit.ExitCode == 0, edit.Stderr + edit.Stdout);
                    Assert.Equal($"CI workspace edited ({RunId})", (string)Data(edit)["workspace"]!["comment"]!);
                    if ((string)workspace["permission"]! == "publiclimited")
                    {
                        // The server applies profiles, but its security service reports the new ACL with a delay (CI run 158 read the
                        // old profile right after the edit, and the creation sometimes reports Private before the profile is visible).
                        string? permission = (string?)Data(edit)["workspace"]!["permission"];
                        for (int attempt = 0; attempt < 10 && permission != "private"; attempt++)
                        {
                            System.Threading.Thread.Sleep(1500);
                            permission = (string?)Data(Run($"workspace {name} /format:json"))["workspace"]!["permission"];
                        }

                        Assert.Equal("private", permission);
                    }

                    // A lock and its undo in the other workspace of the same owner (phase 4 captures lock/other-workspace, undo/other-workspace).
                    string item = GoldenWorkspaceFixture.ServerRoot + "/Main/README.md";
                    var lockOther = Run($"lock /lock:checkin /workspace:{name} {item} /noprompt /format:json");
                    Assert.True(lockOther.ExitCode == 0, lockOther.Stderr + lockOther.Stdout);
                    var statusOther = Run($"status /workspace:{name} {item} /nodetect /format:json");
                    Assert.True(statusOther.ExitCode == 0, statusOther.Stderr + statusOther.Stdout);
                    Assert.Contains((JArray)Data(statusOther)["changes"]!, c => c["changeTypes"]!.Any(t => (string?)t == "lock"));
                    var undoOther = Run($"undo \"/workspace:{name};{owner}\" {item} /noprompt /format:json");
                    Assert.True(undoOther.ExitCode == 0, undoOther.Stderr + undoOther.Stdout);
                    Assert.NotEmpty((JArray)Data(undoOther)["undone"]!);
                    Assert.Empty((JArray)Data(Run($"status /workspace:{name} {item} /nodetect /format:json"))["changes"]!);

                    var delete = Run($"workspace /delete {name} /format:json");
                    Assert.True(delete.ExitCode == 0, delete.Stderr + delete.Stdout);
                    created = false;
                    Assert.True((bool)Data(delete)["deleted"]!);
                    var gone = Run($"workspaces {name} /format:json");
                    Assert.Equal(1, gone.ExitCode);
                }
            }
            finally
            {
                if (created)
                {
                    using (ProtectionLevelScope.Set(3))
                    {
                        Run($"workspace /delete {name} /noprompt");
                    }
                }

                try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        [IntegrationFact]
        public void Workfold_edits_changeset_edit_shelveset_checkin_unshelve_move_and_permission_change()
        {
            fixture.EnsureReady();
            string folder = CiFolder("p4");
            string argument = CiArgument("p4");
            string serverPath = CiServerPath("p4");
            string owner = (string)Data(Run("whoami /format:json"))["identity"]!["displayName"]!;
            string shelveset = "tfp-ci-p4-" + RunId;
            bool committed = false;
            bool allowed = false;
            try
            {
                // Mapping edits on a path of this run (the cloak and the map do not touch the fixture's items).
                string cloakPath = serverPath + "-cloak";
                var cloak = Run($"workfold /cloak {cloakPath} /format:json");
                Assert.True(cloak.ExitCode == 0, cloak.Stderr + cloak.Stdout);
                Assert.Contains((JArray)Data(cloak)["workspace"]!["mappings"]!, m => (string)m["serverPath"]! == cloakPath && (bool)m["cloaked"]!);
                var decloak = Run($"workfold /decloak {cloakPath} /format:json");
                Assert.True(decloak.ExitCode == 0, decloak.Stderr + decloak.Stdout);
                Assert.DoesNotContain((JArray)Data(decloak)["workspace"]!["mappings"]!, m => (string)m["serverPath"]! == cloakPath);
                string mapLocal = Path.Combine(fixture.WorkspaceRoot, "CI", RunId + "-mapped");
                var map = Run($"workfold /map {serverPath}-map \"{mapLocal}\" /format:json");
                Assert.True(map.ExitCode == 0, map.Stderr + map.Stdout);
                Assert.Contains((JArray)Data(map)["workspace"]!["mappings"]!, m => (string)m["serverPath"]! == serverPath + "-map");
                var unmap = Run($"workfold /unmap {serverPath}-map /format:json");
                Assert.True(unmap.ExitCode == 0, unmap.Stderr + unmap.Stdout);
                Assert.DoesNotContain((JArray)Data(unmap)["workspace"]!["mappings"]!, m => (string)m["serverPath"]! == serverPath + "-map");
                var notMapped = Run($"workfold /unmap {serverPath}-nope");
                Assert.Equal(120, notMapped.ExitCode);

                // A checkin to edit afterwards.
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "a.txt"), "a\r\n");
                var add = Run($"add {argument} /recursive /noprompt");
                Assert.True(add.ExitCode == 0, add.Stderr + add.Stdout);
                var checkin = Run($"checkin {argument} /recursive /comment:\"CI phase 4 ({RunId})\" /associate:1 /override:\"CI phase 4\" /noprompt /format:json");
                committed = true;
                Assert.True(checkin.ExitCode == 0, checkin.Stderr + checkin.Stdout);
                int changeset = (int)Data(checkin)["changeset"]!;

                var edited = Run($"changeset {changeset} /comment:\"CI phase 4 edited ({RunId})\" /format:json");
                Assert.True(edited.ExitCode == 0, edited.Stderr + edited.Stdout);
                Assert.Equal($"CI phase 4 edited ({RunId})", (string)Data(edited)["changeset"]!["comment"]!);
                Assert.Equal($"CI phase 4 edited ({RunId})", (string)Data(Run($"changeset {changeset} /format:json"))["changeset"]!["comment"]!);

                // shelve /validate reports the policies; shelve /move parks the edit; checkin /shelveset commits it.
                var checkout = Run($"checkout {argument}\\a.txt /noprompt");
                Assert.True(checkout.ExitCode == 0, checkout.Stderr + checkout.Stdout);
                File.AppendAllText(Path.Combine(folder, "a.txt"), "shelved\r\n");
                var validated = Run($"shelve {shelveset} {argument} /recursive /validate /comment:\"CI shelveset ({RunId})\" /noprompt /format:json");
                Assert.True(validated.ExitCode == 0 || validated.ExitCode == 1, validated.Stderr + validated.Stdout);
                Assert.True((bool)Data(validated)["validated"]!);
                var moved = Run($"shelve {shelveset} {argument} /recursive /replace /move /comment:\"CI shelveset ({RunId})\" /noprompt /format:json");
                Assert.True(moved.ExitCode == 0, moved.Stderr + moved.Stdout);
                Assert.True((bool)Data(moved)["moved"]!);
                Assert.Empty((JArray)Data(Run($"status {argument} /recursive /nodetect /format:json"))["changes"]!);

                var fromShelveset = Run($"checkin /shelveset:{shelveset} /noprompt /format:json");
                Assert.True(fromShelveset.ExitCode == 0, fromShelveset.Stderr + fromShelveset.Stdout);
                int second = (int)Data(fromShelveset)["changeset"]!;
                Assert.True(second > changeset);
                Assert.Equal(100, Run($"shelvesets {shelveset} /format:json").ExitCode);
                // The shelveset was checked in on the server only; a get brings the content down.
                var getAfter = Run($"get {argument} /recursive /noprompt");
                Assert.True(getAfter.ExitCode == 0, getAfter.Stderr + getAfter.Stdout);
                Assert.Contains("shelved", File.ReadAllText(Path.Combine(folder, "a.txt")));

                // unshelve /move: the shelveset disappears after a clean unshelve.
                var checkout2 = Run($"checkout {argument}\\a.txt /noprompt");
                Assert.True(checkout2.ExitCode == 0, checkout2.Stderr + checkout2.Stdout);
                File.AppendAllText(Path.Combine(folder, "a.txt"), "again\r\n");
                var shelved = Run($"shelve {shelveset} {argument} /recursive /move /comment:\"CI shelveset 2 ({RunId})\" /noprompt");
                Assert.True(shelved.ExitCode == 0, shelved.Stderr + shelved.Stdout);
                var unshelved = Run($"unshelve {shelveset} /move /noprompt /format:json");
                Assert.True(unshelved.ExitCode == 0, unshelved.Stderr + unshelved.Stdout);
                Assert.True((bool)Data(unshelved)["deleted"]!);
                Assert.Equal(100, Run($"shelvesets {shelveset} /format:json").ExitCode);
                Assert.NotEmpty((JArray)Data(Run($"status {argument} /recursive /nodetect /format:json"))["changes"]!);
                var undo = Run($"undo {argument} /recursive /noprompt");
                Assert.True(undo.ExitCode == 0, undo.Stderr + undo.Stdout);

                // A permission change on the run folder for the CI user, then its removal (phase 4 captures permission/allow-lock, remove).
                using (ProtectionLevelScope.Set(3))
                {
                    var allow = Run($"permission /allow:Lock \"/user:{owner}\" {serverPath} /format:json");
                    allowed = allow.ExitCode == 0;
                    var denied = ((JArray)JObject.Parse(allow.Stdout)["errors"]!).FirstOrDefault(e => (string?)e["tfCode"] == "TF14098");
                    if (denied != null)
                    {
                        // The CI user has no AdminProjectRights on the test project (a server setting, docs/DEVELOPER-TODO.md): the refusal must be reported per item.
                        Assert.Equal(1, allow.ExitCode);
                        Assert.Equal("TFP0501", (string)denied["code"]!);
                        Assert.Equal(serverPath, (string)denied["item"]!);
                        Assert.Single((JArray)Data(allow)["skipped"]!);
                        Assert.Empty((JArray)Data(allow)["changes"]!);
                    }
                    else
                    {
                        Assert.True(allow.ExitCode == 0, allow.Stderr + allow.Stdout);
                        Assert.Empty((JArray)Data(allow)["skipped"]!);
                        var change = ((JArray)Data(allow)["changes"]!).Single();
                        Assert.Equal(serverPath, (string)change["serverItem"]!);
                        Assert.Equal(new[] { "Lock" }, change["allow"]!.Select(t => (string)t!));
                        var listing = Run($"permission {serverPath} /format:json");
                        Assert.True(((JArray)Data(listing)["items"]!).Single()["entries"]!.Any(e => e["allow"]!.Any(p => (string?)p == "Lock")), "No explicit Lock allow in the listing: " + listing.Stdout);
                        var remove = Run($"permission /remove:Lock \"/user:{owner}\" {serverPath}");
                        Assert.True(remove.ExitCode == 0, remove.Stderr + remove.Stdout);
                        allowed = false;
                        Assert.Contains($"{serverPath}: {owner}", remove.Stdout);
                    }
                }
            }
            finally
            {
                Run($"undo {argument} /recursive /noprompt");
                DeleteShelvesetQuietly(shelveset);
                if (allowed)
                {
                    using (ProtectionLevelScope.Set(3))
                    {
                        Run($"permission /remove:* \"/user:{owner}\" {serverPath} /noprompt");
                    }
                }

                if (committed)
                {
                    Run($"get {argument} /recursive /noprompt");
                    Run($"delete {argument} /recursive /noprompt");
                    var cleanup = Run($"checkin {argument} /recursive /comment:\"CI cleanup ({RunId})\" /override:\"CI cleanup\" /noprompt");
                    if (cleanup.ExitCode != 0)
                    {
                        Run($"undo {argument} /recursive /noprompt");
                    }
                }

                DeleteScratch(folder);
            }
        }

        [IntegrationFact]
        public void Destroy_needs_a_preview_then_removes_the_item_and_its_history()
        {
            fixture.EnsureReady();
            string folder = CiFolder("destroy");
            string argument = CiArgument("destroy");
            string serverPath = CiServerPath("destroy");
            bool committed = false;
            bool destroyed = false;
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "gone.txt"), "gone\r\n");
                var add = Run($"add {argument} /recursive /noprompt");
                Assert.True(add.ExitCode == 0, add.Stderr + add.Stdout);
                var checkin = Run($"checkin {argument} /recursive /comment:\"CI destroy ({RunId})\" /associate:1 /override:\"CI destroy\" /noprompt /format:json");
                committed = true;
                Assert.True(checkin.ExitCode == 0, checkin.Stderr + checkin.Stdout);

                var blocked = Run($"destroy /preview {serverPath}/gone.txt");
                Assert.Equal(100, blocked.ExitCode);
                Assert.StartsWith("ERROR TFP0004: Command 'destroy' needs protection level 3.", blocked.Stderr);

                using (ProtectionLevelScope.Set(3))
                {
                    var noPreview = Run($"destroy {serverPath}/gone.txt /noprompt");
                    Assert.Equal(100, noPreview.ExitCode);
                    Assert.StartsWith("ERROR TFP0410: destroy needs a preview of the same items and switches first", noPreview.Stderr);

                    var preview = Run($"destroy /preview {serverPath}/gone.txt /noprompt /format:json");
                    if (preview.ExitCode != 0 && (preview.Stderr + preview.Stdout).IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // The CI identity lacks the destroy permission on the test project (the server checks it for the preview too): the gate was exercised.
                        return;
                    }

                    Assert.True(preview.ExitCode == 0, preview.Stderr + preview.Stdout);
                    Assert.True((bool)Data(preview)["preview"]!);
                    Assert.Contains((JArray)Data(preview)["items"]!, i => (string)i["serverPath"]! == serverPath + "/gone.txt");

                    // Different arguments do not count as the preview.
                    var otherArguments = Run($"destroy {serverPath}/gone.txt /keephistory /noprompt");
                    Assert.Equal(100, otherArguments.ExitCode);
                    Assert.Contains("TFP0410", otherArguments.Stderr);

                    var real = Run($"destroy {serverPath}/gone.txt /noprompt /format:json");
                    if (real.ExitCode != 0 && (real.Stderr + real.Stdout).IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // The CI identity lacks AdminProjectRights on the test project: the gate and the marker were exercised, the destroy itself cannot be.
                        return;
                    }

                    Assert.True(real.ExitCode == 0, real.Stderr + real.Stdout);
                    destroyed = true;
                    Assert.Contains((JArray)Data(real)["items"]!, i => (string)i["serverPath"]! == serverPath + "/gone.txt");
                    Assert.Equal(100, Run($"destroy {serverPath}/gone.txt /noprompt").ExitCode);

                    var history = Run($"history {serverPath}/gone.txt /format:json");
                    Assert.True(history.ExitCode == 0, history.Stderr + history.Stdout);
                    Assert.Empty((JArray)Data(history)["changesets"]!);
                    var get = Run($"get {argument} /recursive /noprompt");
                    Assert.True(get.ExitCode == 0, get.Stderr + get.Stdout);
                    Assert.False(File.Exists(Path.Combine(folder, "gone.txt")));
                }
            }
            finally
            {
                Run($"undo {argument} /recursive /noprompt");
                if (committed)
                {
                    Run($"get {argument} /recursive /noprompt");
                    Run($"delete {argument} /recursive /noprompt");
                    var cleanup = Run($"checkin {argument} /recursive /comment:\"CI cleanup ({RunId})\" /override:\"CI cleanup\" /noprompt");
                    if (cleanup.ExitCode != 0)
                    {
                        Run($"undo {argument} /recursive /noprompt");
                    }
                }

                DeleteScratch(folder);
                if (destroyed)
                {
                    // Nothing else to do: the item and its history are gone.
                }
            }
        }

        private void DeleteShelvesetQuietly(string name)
        {
            using (ProtectionLevelScope.Set(3))
            {
                Run($"shelve /delete {name} /noprompt");
            }
        }

        [IntegrationFact]
        public void Property_set_is_checked_in_read_back_written_out_deleted_and_undone()
        {
            fixture.EnsureReady();
            string folder = CiFolder("prop");
            string argument = CiArgument("prop");
            string serverPath = CiServerPath("prop");
            string file = argument + "\\p.txt";
            string outputFile = Path.Combine(Path.GetTempPath(), "tfp-ci-prop-" + RunId + ".bin");
            bool committed = false;
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "p.txt"), "p\r\n");
                File.WriteAllBytes(Path.Combine(folder, "icon.bin"), new byte[] { 0, 1, 2, 255 });
                var add = Run($"add {file} /noprompt");
                Assert.True(add.ExitCode == 0, add.Stderr + add.Stdout);
                var checkin = Run($"checkin {argument} /recursive /comment:\"CI property ({RunId})\" /associate:1 /override:\"CI property\" /noprompt /format:json");
                committed = true;
                Assert.True(checkin.ExitCode == 0, checkin.Stderr + checkin.Stdout);

                // The write form is level 2: a property set and a binary value from a file, as one pending change.
                var set = Run($"property /setvalues:tfp-owner=team-a;;b;tfp-icon=@{Path.Combine(folder, "icon.bin")} {file} /format:json");
                Assert.True(set.ExitCode == 0, set.Stderr + set.Stdout);
                var change = ((JArray)Data(set)["changes"]!).Single();
                Assert.Contains("property", change["changeTypes"]!.Select(t => (string?)t));
                Assert.Contains((JArray)Data(Run($"status {file} /nodetect /format:json"))["changes"]!, c => c["changeTypes"]!.Any(t => (string?)t == "property"));
                var checkinProperties = Run($"checkin {file} /comment:\"CI property values ({RunId})\" /associate:1 /override:\"CI property\" /noprompt /format:json");
                Assert.True(checkinProperties.ExitCode == 0, checkinProperties.Stderr + checkinProperties.Stdout);

                var all = Run($"property {file} /format:json");
                Assert.True(all.ExitCode == 0, all.Stderr + all.Stdout);
                var properties = ((JArray)Data(all)["items"]!).Single()["properties"]!;
                Assert.Equal("team-a;b", (string?)properties["tfp-owner"]);
                Assert.Equal("<binary, 4 bytes>", (string?)properties["tfp-icon"]);
                var text = Run($"property {file}");
                Assert.True(text.ExitCode == 0, text.Stderr + text.Stdout);
                Assert.Contains("tfp-owner = team-a;b", text.Stdout);
                Assert.Contains("tfp-icon = <binary, 4 bytes>", text.Stdout);
                var filtered = Run($"property {file} /propertynames:tfp-owner /format:json");
                Assert.True(filtered.ExitCode == 0, filtered.Stderr + filtered.Stdout);
                var filteredProperties = (JObject)((JArray)Data(filtered)["items"]!).Single()["properties"]!;
                Assert.Equal(new[] { "tfp-owner" }, filteredProperties.Properties().Select(p => p.Name));
                var written = Run($"property {file} /propertynames:tfp-icon \"/output:{outputFile}\" /format:json");
                Assert.True(written.ExitCode == 0, written.Stderr + written.Stdout);
                Assert.Equal(outputFile, (string?)Data(written)["output"]);
                Assert.Equal(new byte[] { 0, 1, 2, 255 }, File.ReadAllBytes(outputFile));
                var missing = Run($"property {file} /propertynames:tfp-none \"/output:{outputFile}.none\" /format:json");
                Assert.True(missing.ExitCode == 0, missing.Stderr + missing.Stdout);
                Assert.False(File.Exists(outputFile + ".none"));
                Assert.NotEmpty((JArray)JObject.Parse(missing.Stdout)["warnings"]!);

                // Deletions are pending changes too; undo takes them back.
                var deleteOne = Run($"property /deletevalues:tfp-owner {file} /format:json");
                Assert.True(deleteOne.ExitCode == 0, deleteOne.Stderr + deleteOne.Stdout);
                Assert.Single((JArray)Data(deleteOne)["changes"]!);
                var undo = Run($"undo {file} /noprompt /format:json");
                Assert.True(undo.ExitCode == 0, undo.Stderr + undo.Stdout);
                var deleteAll = Run($"property /deleteall {file}");
                Assert.True(deleteAll.ExitCode == 0, deleteAll.Stderr + deleteAll.Stdout);
                Assert.Contains("p.txt", deleteAll.Stdout);
                var undoAll = Run($"undo {file} /noprompt /format:json");
                Assert.True(undoAll.ExitCode == 0, undoAll.Stderr + undoAll.Stdout);
                Assert.Empty((JArray)Data(Run($"status {file} /nodetect /format:json"))["changes"]!);
                var stillThere = Run($"property {file} /format:json");
                Assert.Equal("team-a;b", (string?)((JArray)Data(stillThere)["items"]!).Single()["properties"]!["tfp-owner"]);

                // The write form is blocked at level 1 before the tool connects.
                using (ProtectionLevelScope.Set(1))
                {
                    var blocked = Run($"property /setvalues:x=y {file}");
                    Assert.Equal(100, blocked.ExitCode);
                    Assert.StartsWith("ERROR TFP0004: Switch '/setvalues' of 'property' needs protection level 2.", blocked.Stderr);
                }
            }
            finally
            {
                Run($"undo {argument} /recursive /noprompt");
                if (committed)
                {
                    Run($"get {argument} /recursive /noprompt");
                    Run($"delete {argument} /recursive /noprompt");
                    var cleanup = Run($"checkin {argument} /recursive /comment:\"CI cleanup ({RunId})\" /override:\"CI cleanup\" /noprompt");
                    if (cleanup.ExitCode != 0)
                    {
                        Run($"undo {argument} /recursive /noprompt");
                    }
                }

                try { File.Delete(outputFile); } catch (IOException) { }
                DeleteScratch(folder);
            }
        }

        private static void DeleteScratch(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }

                    Directory.Delete(directory, true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
