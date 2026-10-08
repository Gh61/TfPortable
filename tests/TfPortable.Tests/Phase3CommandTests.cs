using System;
using System.IO;
using System.Linq;
using TfPortable.Cli;
using TfPortable.Commands;
using TfPortable.Context;
using TfPortable.Errors;
using Xunit;

namespace TfPortable.Tests
{
    /// <summary>Argument handling of the phase 3 commands that needs no server: comments, work item ids, shelveset and label specs, rendering.</summary>
    [Collection("ToolSettings")]
    public class Phase3CommandTests
    {
        private static (int Exit, string Out, string Err) Run(params string[] args)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            int exit = Program.Run(args, stdout, stderr, "tfp");
            return (exit, stdout.ToString(), stderr.ToString());
        }

        [Fact]
        public void Checkin_without_a_comment_is_TFP0002_before_any_connection()
        {
            var (exit, _, stderr) = Run("checkin", "Main", "/recursive");
            Assert.Equal(100, exit);
            Assert.StartsWith("ERROR TFP0002: checkin needs /comment:<text>.", stderr);

            var (exit2, _, stderr2) = Run("checkin", "/comment:x", "/associate:abc");
            Assert.Equal(100, exit2);
            Assert.StartsWith("ERROR TFP0002: /associate:abc is not a work item id.", stderr2);
        }

        [Fact]
        public void Comment_can_come_from_a_file()
        {
            string path = Path.Combine(Path.GetTempPath(), "tfp-comment-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, "From a file\r\n");
            try
            {
                var line = CommandLine.Parse(new[] { "checkin", "/comment:@" + path });
                Assert.Equal("From a file", CommentText.Required(line, "tfp", "x"));
            }
            finally
            {
                File.Delete(path);
            }

            var missing = CommandLine.Parse(new[] { "shelve", "x", "/comment:@" + path });
            var error = Assert.Throws<TfpException>(() => CommentText.Optional(missing, "tfp"));
            Assert.Equal("TFP0002", error.Code);
        }

        [Fact]
        public void Work_item_ids_are_comma_separated_positive_integers()
        {
            var line = CommandLine.Parse(new[] { "checkin", "/comment:x", "/associate:1,42" });
            Assert.Equal(new[] { 1, 42 }, CheckinCommand.WorkItemIds(line, "tfp"));
            Assert.Empty(CheckinCommand.WorkItemIds(CommandLine.Parse(new[] { "checkin", "/comment:x" }), "tfp"));
            Assert.Throws<TfpException>(() => CheckinCommand.WorkItemIds(CommandLine.Parse(new[] { "checkin", "/comment:x", "/associate:0" }), "tfp"));
        }

        [Fact]
        public void Shelveset_and_label_specs_split_name_owner_and_scope()
        {
            Assert.Equal(("MyWork", (string?)null), ShelvesetSpec.Parse("MyWork", "tfp"));
            Assert.Equal(("MyWork", (string?)"user@example.com"), ShelvesetSpec.Parse("MyWork;user@example.com", "tfp"));
            Assert.Throws<TfpException>(() => ShelvesetSpec.Parse(";owner", "tfp"));

            Assert.Equal(("v1.0", (string?)null), LabelSpec.Parse("v1.0", "tfp"));
            Assert.Equal(("v1.0", (string?)"$/Project"), LabelSpec.Parse("v1.0@$/Project", "tfp"));
            Assert.Equal("TFP0002", Assert.Throws<TfpException>(() => LabelSpec.Parse("v1.0@Project", "tfp")).Code);
            Assert.Equal("$/Project", LabelSpec.DefaultScope(new[] { new ItemQuery("$/Project/Main/a.txt", Microsoft.TeamFoundation.VersionControl.Client.RecursionType.None) }));
        }

        [Fact]
        public void Argument_errors_of_shelve_unshelve_label_and_lock_are_TFP0002()
        {
            Assert.StartsWith("ERROR TFP0002: shelve needs the shelveset name.", Run("shelve").Err);
            Assert.StartsWith("ERROR TFP0002: unshelve needs the shelveset name.", Run("unshelve").Err);
            Assert.StartsWith("ERROR TFP0002: label needs the label name", Run("label").Err);
            Assert.StartsWith("ERROR TFP0002: unlabel needs the label name", Run("unlabel", "v1").Err);
            Assert.StartsWith("ERROR TFP0002: lock needs /lock:none", Run("lock", "a.txt").Err);

            // /delete is protection level 3: blocked at the default level, argument errors behind it.
            Assert.StartsWith("ERROR TFP0004: Switch '/delete' of 'shelve' needs protection level 3.", Run("shelve", "/delete", "x", "y").Err);
            string path = Path.Combine(Path.GetTempPath(), "tfp-tests-" + Guid.NewGuid().ToString("N") + ".json");
            var previous = TfPortable.Settings.ToolSettings.PathProvider;
            try
            {
                File.WriteAllText(path, "{ \"protectionLevel\": 3 }");
                TfPortable.Settings.ToolSettings.PathProvider = () => path;
                Assert.StartsWith("ERROR TFP0002: shelve /delete takes only the shelveset name.", Run("shelve", "/delete", "x", "y").Err);
                Assert.StartsWith("ERROR TFP0002: label /delete needs the scope", Run("label", "/delete", "v1").Err);
            }
            finally
            {
                TfPortable.Settings.ToolSettings.PathProvider = previous;
                File.Delete(path);
            }
        }

        [Fact]
        public void Shelvesets_render_a_table_and_detailed_blocks()
        {
            var list = new[]
            {
                new ShelvesetInfo("Work-B", "Jane Doe", "jane@example.com", new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc), "second"),
                new ShelvesetInfo("Work-A", "Jane Doe", "jane@example.com", new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc), null),
            };
            var brief = ShelvesetsCommand.RenderBrief(list).ToList();
            Assert.StartsWith("Shelveset", brief[0]);
            Assert.StartsWith("Work-A", brief[2]);
            Assert.StartsWith("Work-B", brief[3]);

            var detailed = ShelvesetsCommand.RenderDetailed(list).ToList();
            Assert.Equal(new string('=', 79), detailed[0]);
            Assert.Contains("Shelveset: Work-A", detailed);
            Assert.Contains("  second", detailed);
            Assert.Equal(new[] { "No shelvesets found." }, ShelvesetsCommand.RenderBrief(Array.Empty<ShelvesetInfo>()));
        }

        [Fact]
        public void Checkin_policies_are_evaluated_natively_or_reported_as_unknown()
        {
            var policies = new[]
            {
                new CheckinPolicyInfo("Work Items", "Microsoft.TeamFoundation.VersionControl.Controls.Checkin.WorkItemPolicy"),
                new CheckinPolicyInfo("Changeset Comments Policy", "Microsoft.TeamFoundation.VersionControl.Controls.Checkin.CheckForCommentsPolicy"),
                new CheckinPolicyInfo("Builds", "Microsoft.TeamFoundation.Build.Controls.BuildPolicy"),
                new CheckinPolicyInfo("Work Item Query Policy", "Microsoft.TeamFoundation.PowerTools.CheckinPolicies.WorkItemQueryPolicy"),
                new CheckinPolicyInfo("Code Review Comments", "Contoso.Policies.CodeReviewCommentsPolicy"),
                new CheckinPolicyInfo("Work Items", null),
                new CheckinPolicyInfo("Pracovní položky", null),
            };

            var withoutWorkItem = CheckinPolicies.Evaluate(policies, "fix", Array.Empty<int>());
            Assert.Equal(new[] { PolicyStatus.Failed, PolicyStatus.Passed, PolicyStatus.Unknown, PolicyStatus.Unknown, PolicyStatus.Unknown, PolicyStatus.Failed, PolicyStatus.Unknown }, withoutWorkItem.Select(p => p.Status));
            Assert.Contains("/associate", withoutWorkItem[0].Message);

            var withWorkItem = CheckinPolicies.Evaluate(policies, "fix", new[] { 1 });
            Assert.Equal(new[] { PolicyStatus.Passed, PolicyStatus.Passed, PolicyStatus.Unknown, PolicyStatus.Unknown, PolicyStatus.Unknown, PolicyStatus.Passed, PolicyStatus.Unknown }, withWorkItem.Select(p => p.Status));

            var evaluation = new CheckinEvaluation(Array.Empty<CheckinConflictInfo>(), Array.Empty<string>(), withoutWorkItem);
            Assert.Equal(2, evaluation.PolicyFailures.Count());
            Assert.Equal(4, evaluation.UnknownPolicies.Count());
            Assert.Equal(6, evaluation.OverridableFailures.Count());
        }

        [Fact]
        public void Protection_gate_raises_only_the_delete_switches()
        {
            var line = CommandLine.Parse(new[] { "checkin", "/comment:x", "/override:why" });
            Assert.Equal((2, (string?)null), ProtectionLevels.Required(line.Spec!, line));
            var delete = CommandLine.Parse(new[] { "shelve", "/delete", "x" });
            Assert.Equal((3, "delete"), ProtectionLevels.Required(delete.Spec!, delete));
            var lockLine = CommandLine.Parse(new[] { "lock", "a.txt", "/lock:checkin" });
            Assert.Equal((2, (string?)null), ProtectionLevels.Required(lockLine.Spec!, lockLine));
        }
    }
}
