using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TfPortable.Errors;
using TfPortable.Output;
using Xunit;

namespace TfPortable.Tests
{
    public class ErrorsTests
    {
        [Fact]
        public void Catalog_has_every_code_of_the_specification_with_its_default_exit()
        {
            var expected = new Dictionary<string, int>
            {
                ["TFP0001"] = 2, ["TFP0002"] = 100, ["TFP0003"] = 100,
                ["TFP0101"] = 110, ["TFP0102"] = 110, ["TFP0103"] = 110, ["TFP0104"] = 110,
                ["TFP0201"] = 120, ["TFP0202"] = 100, ["TFP0203"] = 120, ["TFP0204"] = 120,
                ["TFP0301"] = 100, ["TFP0302"] = 100, ["TFP0303"] = 100,
                ["TFP0401"] = 1, ["TFP0402"] = 1, ["TFP0403"] = 1, ["TFP0404"] = 1, ["TFP0405"] = 100,
                ["TFP0501"] = 100, ["TFP0900"] = 100,
            };
            Assert.Equal(expected.OrderBy(p => p.Key), ErrorCodes.DefaultExitCodes.OrderBy(p => p.Key));
        }

        [Theory]
        [InlineData("add", "TFP0301", 1)]
        [InlineData("delete", "TFP0301", 1)]
        [InlineData("view", "TFP0301", 1)]
        [InlineData("dir", "TFP0301", 1)]
        [InlineData("undelete", "TFP0301", 1)]
        [InlineData("rename", "TFP0301", 100)]
        [InlineData("changeset", "TFP0301", 100)]
        [InlineData("label", "TFP0301", 100)]
        [InlineData("history", "TFP0301", 0)]
        [InlineData("status", "TFP0301", 100)]
        [InlineData("status", "TFP0201", 120)]
        [InlineData("anything", "TFP9999", 42)]
        public void Exit_code_overrides_follow_compat_md(string command, string code, int expected)
        {
            Assert.Equal(expected, ErrorCodes.ExitCodeFor(command, code, 42));
        }

        [Theory]
        [InlineData("TF30063: You are not authorized to access https://x.", "TF30063")]
        [InlineData("Error VS403405: label not found", "VS403405")]
        [InlineData("TF400813: The user is not authorized.", "TF400813")]
        [InlineData("TF1234 too short", null)]
        [InlineData("nothing here", null)]
        [InlineData(null, null)]
        public void Tf_code_is_extracted_by_the_regex(string? text, string? expected)
        {
            Assert.Equal(expected, ErrorMapper.ExtractTfCode(text));
        }

        [Fact]
        public void Activity_id_comes_from_exception_data_or_message()
        {
            var withData = new InvalidOperationException("boom");
            withData.Data["ActivityId"] = "6f0e2c1a-1111-2222-3333-444444444444";
            Assert.Equal("6f0e2c1a-1111-2222-3333-444444444444", ErrorMapper.ExtractActivityId(withData));

            var inMessage = new InvalidOperationException("Server error. ActivityId: 0f0e2c1a-1111-2222-3333-444444444444");
            Assert.Equal("0f0e2c1a-1111-2222-3333-444444444444", ErrorMapper.ExtractActivityId(inMessage));

            var nested = new Exception("outer", withData);
            Assert.Equal("6f0e2c1a-1111-2222-3333-444444444444", ErrorMapper.ExtractActivityId(nested));

            Assert.Null(ErrorMapper.ExtractActivityId(new Exception("plain")));
            Assert.Null(ErrorMapper.ExtractActivityId(null));
        }

        [Fact]
        public void Tfp_exceptions_pass_through_and_aggregates_are_unwrapped()
        {
            var original = new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "x");
            Assert.Same(original, ErrorMapper.Map(original, "tfp", null));
            Assert.Same(original, ErrorMapper.Map(new AggregateException(original), "tfp", null));
        }

        [Fact]
        public void Unauthorized_message_with_TF30063_is_TFP0102_with_exit_110()
        {
            var error = ErrorMapper.Map(new Exception("TF30063: You are not authorized to access https://dev.azure.com/contoso."), "tfp", "https://dev.azure.com/contoso");
            Assert.Equal(ErrorCodes.PatRejected, error.Code);
            Assert.Equal(ExitCodes.Authentication, error.ExitCode);
            Assert.Equal("TF30063", error.TfCode);
            Assert.Contains("tfp login /collection:https://dev.azure.com/contoso", error.Hint);
            Assert.StartsWith("TF30063:", error.ServerMessage);
        }

        [Fact]
        public void Network_errors_are_TFP0202()
        {
            var error = ErrorMapper.Map(new System.Net.Sockets.SocketException(10061), "tfp", "https://x");
            Assert.Equal(ErrorCodes.ServerUnavailable, error.Code);
            Assert.Equal(ExitCodes.GeneralError, error.ExitCode);
        }

        [Fact]
        public void Item_not_found_codes_are_TFP0301_with_the_catalog_default()
        {
            var error = ErrorMapper.Map(new Exception("TF14019: The item $/x could not be found at version C1."), "tfp", "https://x");
            Assert.Equal(ErrorCodes.ItemNotFound, error.Code);
            Assert.Equal(ExitCodes.GeneralError, error.ExitCode);
            Assert.Equal("TF14019", error.TfCode);
        }

        [Fact]
        public void Unknown_identity_is_TFP0501()
        {
            var error = ErrorMapper.Map(new Exception("TF14045: The identity nobody is not a recognized identity."), "tfp", "https://x");
            Assert.Equal(ErrorCodes.ServerError, error.Code);
            Assert.Equal(ExitCodes.GeneralError, error.ExitCode);
        }

        [Fact]
        public void Unexpected_exceptions_are_TFP0900_with_the_log_file_in_the_hint()
        {
            var error = ErrorMapper.Map(new InvalidOperationException("bug"), "tfp", null);
            Assert.Equal(ErrorCodes.Unexpected, error.Code);
            Assert.Equal(ExitCodes.GeneralError, error.ExitCode);
            Assert.Contains("InvalidOperationException: bug", error.Message);
            string expectedPath = Path.Combine(Path.GetTempPath(), "TfPortable", "last-error.txt");
            Assert.Contains(expectedPath, error.Hint);
            Assert.Contains("InvalidOperationException", File.ReadAllText(expectedPath));
        }

        [Fact]
        public void Error_text_has_code_server_message_hint_and_activity_id()
        {
            var error = new TfpException("TFP0301", 100, "Item $/x does not exist at version C1200.", "Check the path.", "TF14019",
                "TF14019: The item $/x could not be found at version C1200.", "$/x", "6f0e2c1a-1111-2222-3333-444444444444");
            var lines = OutputWriter.ErrorLines(error).ToList();
            Assert.Equal("ERROR TFP0301 [TF14019]: Item $/x does not exist at version C1200.", lines[0]);
            Assert.Equal("  Server message: TF14019: The item $/x could not be found at version C1200.", lines[1]);
            Assert.Equal("  Next step: Check the path.", lines[2]);
            Assert.Equal("  ActivityId: 6f0e2c1a-1111-2222-3333-444444444444", lines[3]);
        }
    }

    public class OutputWriterTests
    {
        [Fact]
        public void Text_mode_writes_data_to_stdout_and_warnings_and_errors_to_stderr()
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var output = new OutputWriter(false, "status", stdout, stderr);
            output.Line("data");
            output.Warning("careful", "TFP0103", "do this");
            output.ItemError(new TfpException("TFP0301", 1, "item gone", item: "$/x"));
            output.Success(new JObject());

            Assert.Equal("data" + Environment.NewLine, stdout.ToString());
            Assert.Equal("WARNING TFP0103: careful" + Environment.NewLine + "  Next step: do this" + Environment.NewLine + "ERROR TFP0301: item gone" + Environment.NewLine, stderr.ToString());
        }

        [Fact]
        public void Json_mode_writes_one_envelope_with_context_warnings_and_errors()
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var output = new OutputWriter(true, "status", stdout, stderr) { Collection = "https://dev.azure.com/contoso", Workspace = "DEV-PC;Jan Novak" };
            output.Line("never printed");
            output.Warning("careful", "TFP0103");
            output.ItemError(new TfpException("TFP0301", 1, "item gone", "look", "TF14019", item: "$/x"));
            output.Success(new JObject { ["count"] = 1 });

            Assert.Equal(string.Empty, stderr.ToString());
            var envelope = JObject.Parse(stdout.ToString());
            Assert.False((bool)envelope["ok"]!);
            Assert.Equal("status", (string)envelope["command"]!);
            Assert.Equal(1, (int)envelope["exitCode"]!);
            Assert.Equal("https://dev.azure.com/contoso", (string)envelope["collection"]!);
            Assert.Equal("DEV-PC;Jan Novak", (string)envelope["workspace"]!);
            Assert.Equal(1, (int)envelope["data"]!["count"]!);
            JsonAssert.Null(envelope["truncated"]!);
            Assert.Equal("TFP0103", (string)envelope["warnings"]![0]!["code"]!);
            var error = envelope["errors"]![0]!;
            Assert.Equal("TFP0301", (string)error["code"]!);
            Assert.Equal("TF14019", (string)error["tfCode"]!);
            Assert.Equal("$/x", (string)error["item"]!);
            JsonAssert.Null(error["activityId"]!);
            Assert.Equal(new[] { "ok", "command", "exitCode", "collection", "workspace", "data", "truncated", "warnings", "errors" },
                envelope.Properties().Select(p => p.Name).ToArray());
        }

        [Fact]
        public void Fatal_error_envelope_keeps_the_collection_and_earlier_item_errors()
        {
            var stdout = new StringWriter();
            var output = new OutputWriter(true, "get", stdout, new StringWriter()) { Collection = "https://dev.azure.com/contoso" };
            output.ItemError(new TfpException("TFP0402", 1, "writable"));
            output.Error(new TfpException("TFP0102", 110, "rejected"));

            var envelope = JObject.Parse(stdout.ToString());
            Assert.Equal(110, (int)envelope["exitCode"]!);
            Assert.Equal("https://dev.azure.com/contoso", (string)envelope["collection"]!);
            JsonAssert.Null(envelope["workspace"]!);
            Assert.Equal(2, envelope["errors"]!.Count());
            Assert.Equal("TFP0102", (string)envelope["errors"]![1]!["code"]!);
            Assert.Equal(JTokenType.Object, envelope["data"]!.Type);
        }

        [Fact]
        public void Iso_dates_carry_the_offset()
        {
            var value = new DateTimeOffset(2026, 10, 7, 19, 7, 27, TimeSpan.FromHours(2));
            Assert.Equal("2026-10-07T19:07:27+02:00", OutputWriter.IsoDate(value));
        }

        [Fact]
        public void Table_pads_all_but_the_last_column()
        {
            var table = new TextTable("Changeset", "User", "Comment");
            table.Add("81", "Jan Novak", "A rather long comment that is never truncated");
            table.Add("1234", "X", "");
            var lines = table.Render().ToList();
            Assert.Equal("Changeset User      Comment", lines[0]);
            Assert.Equal("--------- --------- ---------------------------------------------", lines[1]);
            Assert.Equal("81        Jan Novak A rather long comment that is never truncated", lines[2]);
            Assert.Equal("1234      X", lines[3]);
        }

        [Fact]
        public void Grouped_listing_and_field_block_helpers()
        {
            var grouped = TextBlocks.Grouped(new[]
            {
                new KeyValuePair<string, string>(@"C:\Src\Main", "a.cs"),
                new KeyValuePair<string, string>(@"C:\Src\Main", "b.cs"),
                new KeyValuePair<string, string>(@"C:\Src\Other", "c.cs"),
            }).ToList();
            Assert.Equal(new[] { @"C:\Src\Main:", "a.cs", "b.cs", "", @"C:\Src\Other:", "c.cs" }, grouped);

            var fields = TextBlocks.Fields(new[]
            {
                new KeyValuePair<string, string?>("Changeset", "81"),
                new KeyValuePair<string, string?>("User", "Jan Novak"),
                new KeyValuePair<string, string?>("Date", null),
            }).ToList();
            Assert.Equal(new[] { "Changeset: 81", "User:      Jan Novak", "Date:" }, fields);
            Assert.Equal(new[] { "  line one", "  line two" }, TextBlocks.Indented("line one\r\nline two").ToList());
        }
    }
}
