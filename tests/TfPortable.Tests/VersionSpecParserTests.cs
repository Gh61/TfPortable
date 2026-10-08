using System;
using System.Globalization;
using System.Threading;
using Microsoft.TeamFoundation.VersionControl.Client;
using TfPortable.Cli;
using TfPortable.Errors;
using Xunit;

namespace TfPortable.Tests
{
    public class VersionSpecParserTests
    {
        [Theory]
        [InlineData("C81", 81)]
        [InlineData("c81", 81)]
        [InlineData("81", 81)]
        [InlineData(" 1234 ", 1234)]
        public void Changeset_with_or_without_prefix(string text, int expected)
        {
            var spec = Assert.IsType<ChangesetVersionSpec>(VersionSpecParser.ParseSingle(text, null, null));
            Assert.Equal(expected, spec.ChangesetId);
        }

        [Fact]
        public void T_is_latest()
        {
            Assert.IsType<LatestVersionSpec>(VersionSpecParser.ParseSingle("T", null, null));
            Assert.IsType<LatestVersionSpec>(VersionSpecParser.ParseSingle("t", null, null));
        }

        [Fact]
        public void Label_with_optional_scope()
        {
            var plain = Assert.IsType<LabelVersionSpec>(VersionSpecParser.ParseSingle("LRelease-2.1", null, null));
            Assert.Equal("Release-2.1", plain.Label);
            Assert.Null(plain.Scope);

            var scoped = Assert.IsType<LabelVersionSpec>(VersionSpecParser.ParseSingle("LRelease-2.1@$/Project", null, null));
            Assert.Equal("Release-2.1", scoped.Label);
            Assert.Equal("$/Project", scoped.Scope);
        }

        [Fact]
        public void Workspace_uses_the_current_workspace_when_no_name_is_given()
        {
            var current = Assert.IsType<WorkspaceVersionSpec>(VersionSpecParser.ParseSingle("W", "DEV-PC", "Jan Novak"));
            Assert.Equal("DEV-PC", current.Name);
            Assert.Equal("Jan Novak", current.OwnerName);

            var named = Assert.IsType<WorkspaceVersionSpec>(VersionSpecParser.ParseSingle("WOther;someone", "DEV-PC", "Jan Novak"));
            Assert.Equal("Other", named.Name);
            Assert.Equal("someone", named.OwnerName);

            var error = Assert.Throws<TfpException>(() => VersionSpecParser.ParseSingle("W", null, null));
            Assert.Equal(ErrorCodes.InvalidVersionSpec, error.Code);
        }

        [Fact]
        public void Iso_date_is_parsed_first_and_a_date_without_time_is_midnight_local()
        {
            var culture = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("cs-CZ");
                var date = Assert.IsType<DateVersionSpec>(VersionSpecParser.ParseSingle("D2026-09-30", null, null));
                Assert.Equal(new DateTime(2026, 9, 30, 0, 0, 0), date.Date);
                Assert.Equal(DateTimeKind.Local, date.Date.Kind);

                var withTime = Assert.IsType<DateVersionSpec>(VersionSpecParser.ParseSingle("D2026-09-30T14:05", null, null));
                Assert.Equal(new DateTime(2026, 9, 30, 14, 5, 0), withTime.Date);

                // The current culture (cs-CZ) comes second: day.month.year.
                var czech = Assert.IsType<DateVersionSpec>(VersionSpecParser.ParseSingle("D7. 10. 2026", null, null));
                Assert.Equal(new DateTime(2026, 10, 7), czech.Date);

            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = culture;
            }
        }

        [Fact]
        public void Iso_date_with_offset_is_converted_to_local_time()
        {
            var date = Assert.IsType<DateVersionSpec>(VersionSpecParser.ParseSingle("D2026-09-30T12:00:00+00:00", null, null));
            Assert.Equal(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero).LocalDateTime, date.Date);
        }

        [Fact]
        public void Range_gives_from_and_to()
        {
            var range = VersionSpecParser.ParseRange("C1200~C1300", null, null);
            Assert.True(range.IsRange);
            Assert.Equal(1200, Assert.IsType<ChangesetVersionSpec>(range.From).ChangesetId);
            Assert.Equal(1300, Assert.IsType<ChangesetVersionSpec>(range.To).ChangesetId);

            var single = VersionSpecParser.ParseRange("T", null, null);
            Assert.False(single.IsRange);
            Assert.Null(single.From);
            Assert.IsType<LatestVersionSpec>(single.To);
        }

        [Theory]
        [InlineData("")]
        [InlineData("C")]
        [InlineData("C0")]
        [InlineData("Cabc")]
        [InlineData("-5")]
        [InlineData("Dnot-a-date")]
        [InlineData("L")]
        [InlineData("Q1")]
        [InlineData("T5")]
        public void Invalid_specs_are_TFP0302_with_exit_100(string text)
        {
            var error = Assert.Throws<TfpException>(() => VersionSpecParser.ParseSingle(text, null, null));
            Assert.Equal(ErrorCodes.InvalidVersionSpec, error.Code);
            Assert.Equal(ExitCodes.GeneralError, error.ExitCode);
            Assert.Contains("C<changeset>", error.Hint);
        }
    }
}
