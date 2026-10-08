using System;
using System.Collections.Generic;
using System.Linq;
using TfPortable.Auth;
using TfPortable.Commands;
using TfPortable.Context;
using TfPortable.Output;
using Xunit;

namespace TfPortable.Tests
{
    /// <summary>Text and JSON shape of the commands, on plain data (no server).</summary>
    public class RenderingTests
    {
        private static readonly DateTime Date = new DateTime(2026, 10, 7, 19, 7, 27, DateTimeKind.Local);

        private static WorkspaceSummary Workspace() => new WorkspaceSummary("DEV-PC", "Jan Novak", "jan@contoso.com", "DEV-PC", null,
            "https://dev.azure.com/contoso", true, "private", Date, new[]
            {
                new MappingInfo("$/TfsPortableTestRepo", @"C:\Src\Repo", false, false),
                new MappingInfo("$/TfsPortableTestRepo/Hidden", null, true, false),
            });

        [Fact]
        public void Whoami_text_and_json()
        {
            var identity = new IdentityInfo("Jan Novak", "jan@contoso.com", "guid");
            var pat = new StoredPat("TfPortable:https://dev.azure.com/contoso", "https://dev.azure.com/contoso", "secret", "jan@contoso.com", "2026-10-07T10:00:00+02:00");
            var lines = WhoamiCommand.Render(identity, "https://dev.azure.com/contoso", pat, Workspace(), @"C:\Src\Repo").ToList();
            Assert.Equal("Identity   : Jan Novak (jan@contoso.com)", lines[0]);
            Assert.Equal("Collection : https://dev.azure.com/contoso", lines[1]);
            Assert.Equal("PAT stored : 2026-10-07T10:00:00+02:00 (Windows Credential Manager, TfPortable:https://dev.azure.com/contoso)", lines[2]);
            Assert.Equal("Workspace  : DEV-PC;Jan Novak (local, computer DEV-PC)", lines[3]);
            Assert.Equal(@"  $/TfsPortableTestRepo: C:\Src\Repo", lines[4]);
            Assert.Equal("  (cloaked) $/TfsPortableTestRepo/Hidden:", lines[5]);
            Assert.DoesNotContain("secret", string.Join("\n", lines));

            var json = WhoamiCommand.ToJson(identity, "https://dev.azure.com/contoso", pat, Workspace(), @"C:\Src\Repo");
            Assert.Equal("Jan Novak", (string)json["identity"]!["displayName"]!);
            Assert.Equal("2026-10-07T10:00:00+02:00", (string)json["patStoredAt"]!);
            var workspace = json["workspace"]!;
            Assert.Equal("local", (string)workspace["location"]!);
            Assert.Equal("private", (string)workspace["permission"]!);
            Assert.Equal(2, workspace["mappings"]!.Count());
            Assert.True((bool)workspace["mappings"]![1]!["cloaked"]!);
            JsonAssert.Null(workspace["mappings"]![1]!["localPath"]!);
            Assert.DoesNotContain("secret", json.ToString());

            var none = WhoamiCommand.Render(identity, "https://dev.azure.com/contoso", null, null, @"C:\Temp").ToList();
            Assert.Equal("PAT stored : no (the PAT of this run came from /login)", none[2]);
            Assert.Equal(@"Workspace  : none maps C:\Temp", none[3]);
        }

        private static PendingChangeInfo Change(string changeText, string serverPath, string localPath, bool detected = false) =>
            new PendingChangeInfo(changeText, serverPath, localPath, false, 81, null, "none", "utf-8", Date, "Jan Novak", "DEV-PC", detected);

        [Fact]
        public void Status_text_groups_by_server_folder_and_prints_the_trailer()
        {
            var changes = new PendingChangeSet(
                new[]
                {
                    Change("edit", "$/Repo/Main/src/b.cs", @"C:\Src\Repo\Main\src\b.cs"),
                    Change("add", "$/Repo/Main/a.txt", @"C:\Src\Repo\Main\a.txt"),
                    Change("merge, edit", "$/Repo/Main/src/a.cs", @"C:\Src\Repo\Main\src\a.cs"),
                },
                new[] { Change("add", "$/Repo/Main/new.txt", @"C:\Src\Repo\Main\new.txt", detected: true) });

            var lines = StatusCommand.Render(changes).ToList();
            Assert.Equal("File name Change      Local path", lines[0]);
            Assert.StartsWith("--------- ----------- ------", lines[1]);
            Assert.Equal("$/Repo/Main", lines[2]);
            Assert.Equal(@"a.txt     add         C:\Src\Repo\Main\a.txt", lines[3]);
            Assert.Equal("", lines[4]);
            Assert.Equal("$/Repo/Main/src", lines[5]);
            Assert.Equal(@"a.cs      merge, edit C:\Src\Repo\Main\src\a.cs", lines[6]);
            Assert.Equal(@"b.cs      edit        C:\Src\Repo\Main\src\b.cs", lines[7]);
            Assert.Equal("", lines[8]);
            Assert.Equal(new string('-', 79), lines[9]);
            Assert.Equal("Detected changes:", lines[10]);
            Assert.Equal("Changes: 3, detected changes: 1", lines.Last());

            var json = StatusCommand.ToJson(changes);
            Assert.Equal(3, json["changes"]!.Count());
            Assert.Equal(new[] { "merge", "edit" }, json["changes"]![2]!["changeTypes"]!.Select(t => (string)t!).ToArray());
            Assert.True((bool)json["detectedChanges"]![0]!["detected"]!);
            Assert.Equal(1, (int)json["counts"]!["detected"]!);
        }

        [Fact]
        public void Status_with_nothing_pending()
        {
            var lines = StatusCommand.Render(new PendingChangeSet(Array.Empty<PendingChangeInfo>(), Array.Empty<PendingChangeInfo>())).ToList();
            Assert.Equal(new[] { "There are no pending changes." }, lines);
        }

        [Fact]
        public void History_table_and_truncation_notice()
        {
            var changesets = new List<ChangesetInfo>
            {
                new ChangesetInfo(83, "Jan Novak", "jan@contoso.com", Date, "Fix null check\r\nsecond line"),
                new ChangesetInfo(82, "Jan Novak", "jan@contoso.com", Date, null),
            };
            var lines = HistoryCommand.Render(changesets, false, null, false).ToList();
            Assert.Equal("Changeset User      Date                      Comment", lines[0]);
            Assert.Equal("83        Jan Novak 2026-10-07T19:07:27" + Offset(Date) + " Fix null check", lines[2]);
            Assert.Equal("82        Jan Novak 2026-10-07T19:07:27" + Offset(Date), lines[3]);

            var truncated = HistoryCommand.Render(changesets, true, null, false).ToList();
            Assert.Contains("Showing the newest 256 changesets. Use /stopafter:<n> for more, or /version:C81 to continue from the next one.", truncated);

            var json = HistoryCommand.ToJson(changesets, true, false, null);
            Assert.Equal(2, (int)json["count"]!);
            Assert.Equal("slot", (string)json["mode"]!);
            Assert.Equal(83, (int)json["changesets"]![0]!["id"]!);
            JsonAssert.Null(json["changesets"]![1]!["comment"]!);

            Assert.Equal(new[] { "No history entries were found for the item and version combination." }, HistoryCommand.Render(new List<ChangesetInfo>(), false, null, false));
        }

        private static string Offset(DateTime date) => new DateTimeOffset(date).ToString("zzz");

        [Fact]
        public void Status_detailed_blocks_and_variants()
        {
            var workspace = Workspace();
            var edit = new PendingChangeInfo("edit", "$/Repo/Main/README.md", @"C:\Src\Repo\Main\README.md", false, 83, null, "none", "windows-1250", Date, "jan@contoso.com", "DEV-PC", false);
            var add = new PendingChangeInfo("add", "$/Repo/Main/_golden", @"C:\Src\Repo\Main\_golden", true, 0, null, "none", null, Date, "jan@contoso.com", "DEV-PC", false);
            var rename = new PendingChangeInfo("rename", "$/Repo/Main/data/new.txt", @"C:\Src\Repo\Main\data\new.txt", false, 75, "$/Repo/Main/data/old.txt", "none", "binary", Date, "jan@contoso.com", "DEV-PC", false);
            var lines = StatusCommand.RenderDetailedSet(new PendingChangeSet(new[] { edit, add, rename }, Array.Empty<PendingChangeInfo>()), workspace).ToList();
            // Labels are aligned on the widest one of the block; compare with the padding collapsed.
            var collapsed = lines.Select(l => System.Text.RegularExpressions.Regex.Replace(l, @":\s+", ": ")).ToList();
            Assert.Equal("$/Repo/Main/_golden", collapsed[0]);
            Assert.Equal("  User: Jan Novak", collapsed[1]);
            Assert.Equal("  Date: " + OutputWriter.IsoDate(Date), collapsed[2]);
            Assert.Equal("  Lock: none", collapsed[3]);
            Assert.Equal("  Change: add", collapsed[4]);
            Assert.Equal("  Workspace: DEV-PC", collapsed[5]);
            Assert.Equal(@"  Local item: [DEV-PC] C:\Src\Repo\Main\_golden", collapsed[6]);
            Assert.Equal("", collapsed[7]);
            Assert.Equal("$/Repo/Main/README.md;C83", collapsed[8]);
            Assert.Contains("  File type: windows-1250", collapsed);
            Assert.Contains("  Source item: $/Repo/Main/data/old.txt", collapsed);
            Assert.Contains("  File type: Binary", collapsed);
            Assert.Equal("Changes: 3", collapsed.Last());

            var users = StatusCommand.RenderAllUsers(new[] { edit, new PendingChangeInfo("lock", "$/Repo/Main/c.txt", null, false, 84, null, "checkin", "utf-8", Date, "someone@contoso.com", "OTHER", false) }, workspace).ToList();
            Assert.Equal("File name Change User                Local path", users[0]);
            Assert.Contains(@"README.md edit   Jan Novak           C:\Src\Repo\Main\README.md", users);
            Assert.Contains("c.txt     * lock someone@contoso.com", users);
            Assert.Equal("Changes: 2", users.Last());

            var shelved = StatusCommand.RenderShelveset(new[] { new PendingChangeInfo("add", "$/Repo/Main/shelved.txt", null, false, 0, null, "none", "utf-8", Date, "jan@contoso.com", "Golden-1", false) }).ToList();
            Assert.Equal("File name   Change Shelveset", shelved[0]);
            Assert.Equal("shelved.txt add    Golden-1", shelved[3]);
        }

        [Fact]
        public void Get_lines_group_getting_and_list_deleting_flat()
        {
            var result = new GetResult(new[]
            {
                new GetOperationInfo("getting", @"C:\Src\Repo\Main\a.txt", "$/Repo/Main/a.txt", 90, false, false),
                new GetOperationInfo("replacing", @"C:\Src\Repo\Main\b.txt", "$/Repo/Main/b.txt", 91, false, false),
                new GetOperationInfo("deleting", @"C:\Src\Repo\Main\old\c.txt", "$/Repo/Main/old/c.txt", 0, false, true),
                new GetOperationInfo("writable", @"C:\Src\Repo\Main\w.txt", "$/Repo/Main/w.txt", 92, false, false),
            }, Array.Empty<ConflictInfo>(), Array.Empty<FailureInfo>(), false, 4, 0, 1);
            var lines = GetCommand.Render(result).ToList();
            Assert.Equal(new[] { @"C:\Src\Repo\Main:", "Getting a.txt", "Replacing b.txt", "", @"Deleting C:\Src\Repo\Main\old\c.txt" }, lines);

            var json = GetCommand.ToJson(result, true, new Newtonsoft.Json.Linq.JArray());
            Assert.Equal(3, json["operations"]!.Count());
            Assert.Equal(1, (int)json["counts"]!["getting"]!);
            Assert.Equal(1, (int)json["counts"]!["deleting"]!);
            Assert.True((bool)json["preview"]!);

            var nothing = new GetResult(Array.Empty<GetOperationInfo>(), Array.Empty<ConflictInfo>(), Array.Empty<FailureInfo>(), true, 0, 0, 0);
            Assert.Equal(new[] { "All files are up to date." }, GetCommand.Render(nothing));

            var conflict = new GetResult(new[] { new GetOperationInfo("conflict", @"C:\Src\Repo\Main\c.txt", "$/Repo/Main/c.txt", 90, false, false) },
                new[] { new ConflictInfo("get", "$/Repo/Main/c.txt", @"C:\Src\Repo\Main\c.txt", 84, 90, 84, "edit", "edit", null, true, "You have a conflicting pending change.", false, null) },
                Array.Empty<FailureInfo>(), false, 1, 1, 0);
            Assert.Equal(new[] { @"C:\Src\Repo\Main:" }, GetCommand.Render(conflict));
        }

        [Fact]
        public void Localversions_brief_and_detailed()
        {
            var versions = new[]
            {
                new LocalVersionInfo(@"C:\Src\Repo\Main\src\Program.cs", "$/Repo/Main/src/Program.cs", 81, false),
                new LocalVersionInfo(@"C:\Src\Repo\Main", "$/Repo/Main", 75, true),
                new LocalVersionInfo(@"C:\Src\Repo\Main\README.md", "$/Repo/Main/README.md", 83, false),
                new LocalVersionInfo(@"C:\Src\Repo\Main\_golden", "$/Repo/Main/_golden", 84, true),
            };
            var brief = LocalVersionsCommand.Render(versions, false).ToList();
            Assert.Equal(new[] { @"C:\Src\Repo:", "Main;C75", "", @"C:\Src\Repo\Main:", "_golden;C84", "README.md;C83", "", @"C:\Src\Repo\Main\src:", "Program.cs;C81", "", "Items: 4" }, brief);
            var detailed = LocalVersionsCommand.Render(versions, true).ToList();
            Assert.Equal(@"C:\Src\Repo\Main;C75", detailed[0]);
            Assert.Equal("Items: 4", detailed.Last());
        }

        [Fact]
        public void Workfold_and_workspaces_listings()
        {
            var w = Workspace();
            var workfold = WorkfoldCommand.Render(w, w.Mappings).ToList();
            Assert.Equal(new string('=', 79), workfold[0]);
            Assert.Equal("Workspace: DEV-PC (Jan Novak)", workfold[1]);
            Assert.Equal("Collection: https://dev.azure.com/contoso/", workfold[2]);
            Assert.Equal(@" $/TfsPortableTestRepo: C:\Src\Repo", workfold[3]);
            Assert.Equal(" (cloaked) $/TfsPortableTestRepo/Hidden:", workfold[4]);

            var brief = WorkspacesCommand.RenderBrief("https://dev.azure.com/contoso", new[] { w }).ToList();
            Assert.Equal("Collection: https://dev.azure.com/contoso/", brief[0]);
            Assert.Equal("Workspace Owner     Computer Comment", brief[1]);
            Assert.Equal("DEV-PC    Jan Novak DEV-PC", brief[3]);

            var detailed = WorkspacesCommand.RenderDetailed(new[] { w }).ToList();
            Assert.Equal(new string('=', 79), detailed[0]);
            Assert.Equal("Workspace:   DEV-PC", detailed[1]);
            Assert.Contains("Permission:  Private", detailed);
            Assert.Contains("Location:    Local", detailed);
            Assert.Contains("Working folders:", detailed);
        }

        [Fact]
        public void Resolve_names_are_canonicalized()
        {
            Assert.Equal("automerge", ResolveCommand.Canonical("AcceptMerge"));
            Assert.Equal("automerge", ResolveCommand.Canonical("AutoMergeForced"));
            Assert.Equal("keepyours", ResolveCommand.Canonical("AcceptYours"));
            Assert.Equal("taketheirs", ResolveCommand.Canonical("TakeTheirs"));
            Assert.Equal("keepyoursrenametheirs", ResolveCommand.Canonical("AcceptYoursRenameTheirs"));
            Assert.Throws<TfPortable.Errors.TfpException>(() => ResolveCommand.Canonical("Nope"));
            Assert.Matches(ReconcileCommand.Pattern("*.txt"), "untracked.txt");
            Assert.DoesNotMatch(ReconcileCommand.Pattern("*.txt"), "a.cs");
        }

        [Fact]
        public void Failures_map_to_the_catalog()
        {
            var notFound = WorkspaceCommandSupport.ToError(new FailureInfo(@"C:\x\a.txt", null, "No matching files.", "ItemNotFoundException", false), "tfp");
            Assert.Equal(TfPortable.Errors.ErrorCodes.ItemNotFound, notFound.Code);
            Assert.Equal(@"C:\x\a.txt", notFound.Item);
            var exists = WorkspaceCommandSupport.ToError(new FailureInfo(null, "$/x/a.txt", "The item $/x/a.txt already exists.", "ItemExistsException", false), "tfp");
            Assert.Equal(TfPortable.Errors.ErrorCodes.PendingChangeConflict, exists.Code);
            var other = WorkspaceCommandSupport.ToError(new FailureInfo(null, null, "TF14098: Access denied.", null, false), "tfp");
            Assert.Equal(TfPortable.Errors.ErrorCodes.ServerError, other.Code);
            Assert.Equal("TF14098", other.TfCode);
        }
    }
}
