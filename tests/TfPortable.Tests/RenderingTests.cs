using System;
using System.Collections.Generic;
using System.Linq;
using TfPortable.Auth;
using TfPortable.Commands;
using TfPortable.Context;
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
            var lines = HistoryCommand.Render(changesets, false).ToList();
            Assert.Equal("Changeset User      Date                      Comment", lines[0]);
            Assert.Equal("83        Jan Novak 2026-10-07T19:07:27" + Offset(Date) + " Fix null check", lines[2]);
            Assert.Equal("82        Jan Novak 2026-10-07T19:07:27" + Offset(Date), lines[3]);

            var truncated = HistoryCommand.Render(changesets, true).ToList();
            Assert.Contains("Showing the newest 256 changesets. Use /stopafter:<n> for more, or /version:C81 to continue from the next one.", truncated);

            var json = HistoryCommand.ToJson(changesets, true);
            Assert.Equal(2, (int)json["count"]!);
            Assert.Equal("slot", (string)json["mode"]!);
            Assert.Equal(83, (int)json["changesets"]![0]!["id"]!);
            JsonAssert.Null(json["changesets"]![1]!["comment"]!);

            Assert.Equal(new[] { "No history entries were found for the item and version combination." }, HistoryCommand.Render(new List<ChangesetInfo>(), false));
        }

        private static string Offset(DateTime date) => new DateTimeOffset(date).ToString("zzz");
    }
}
