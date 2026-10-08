using System;
using System.IO;
using Microsoft.TeamFoundation.VersionControl.Client;
using TfPortable.Cli;
using TfPortable.Errors;
using Xunit;

namespace TfPortable.Tests
{
    public class ItemSpecParserTests
    {
        [Theory]
        [InlineData("$/Project/Main/a.cs", "$/Project/Main/a.cs")]
        [InlineData("$\\Project\\Main\\a.cs", "$/Project/Main/a.cs")]
        [InlineData("$/Project/Main/", "$/Project/Main")]
        [InlineData("$/", "$/")]
        public void Server_paths_use_forward_slashes(string text, string expected)
        {
            var spec = ItemSpecParser.Parse(text);
            Assert.True(spec.IsServerPath);
            Assert.Equal(expected, spec.Path);
            Assert.Null(spec.Version);
            Assert.Equal(0, spec.DeletionId);
        }

        [Fact]
        public void Version_and_deletion_suffixes_are_split_off()
        {
            var spec = ItemSpecParser.Parse("$/Project/a.cs;C1234");
            Assert.Equal("$/Project/a.cs", spec.Path);
            Assert.Equal(1234, Assert.IsType<ChangesetVersionSpec>(spec.Version).ChangesetId);
            Assert.Equal("C1234", spec.VersionText);

            var bare = ItemSpecParser.Parse("$/Project/a.cs;1234");
            Assert.Equal(1234, Assert.IsType<ChangesetVersionSpec>(bare.Version).ChangesetId);

            var deleted = ItemSpecParser.Parse("$/Project/a.cs;X18");
            Assert.Equal(18, deleted.DeletionId);
            Assert.Null(deleted.Version);

            var both = ItemSpecParser.Parse("$/Project/a.cs;X18;T");
            Assert.Equal(18, both.DeletionId);
            Assert.IsType<LatestVersionSpec>(both.Version);
        }

        [Fact]
        public void Two_version_suffixes_are_an_error()
        {
            var error = Assert.Throws<TfpException>(() => ItemSpecParser.Parse("$/Project/a.cs;C1;C2"));
            Assert.Equal(ErrorCodes.InvalidVersionSpec, error.Code);
        }

        [Fact]
        public void Local_paths_are_absolute_with_the_platform_separator()
        {
            string cwd = Directory.GetCurrentDirectory();
            var relative = ItemSpecParser.Parse("src/a.cs");
            Assert.False(relative.IsServerPath);
            Assert.Equal(Path.Combine(cwd, "src", "a.cs"), relative.Path);

            var dot = ItemSpecParser.Parse(".");
            Assert.Equal(cwd.TrimEnd(Path.DirectorySeparatorChar), dot.Path);
        }

        [Fact]
        public void Local_paths_from_bash_with_forward_slashes_are_normalized()
        {
            if (Path.DirectorySeparatorChar != '\\')
            {
                return; // Windows path semantics only.
            }

            var spec = ItemSpecParser.Parse("D:/Projects/App/BaseController.cs;C12");
            Assert.Equal(@"D:\Projects\App\BaseController.cs", spec.Path);
            Assert.Equal(12, Assert.IsType<ChangesetVersionSpec>(spec.Version).ChangesetId);

            var trailing = ItemSpecParser.Parse(@"D:\Projects\App\");
            Assert.Equal(@"D:\Projects\App", trailing.Path);

            var root = ItemSpecParser.Parse(@"D:\");
            Assert.Equal(@"D:\", root.Path);
        }

        [Fact]
        public void Wildcard_is_kept_in_the_last_segment()
        {
            string cwd = Directory.GetCurrentDirectory();
            var spec = ItemSpecParser.Parse("src/*.cs");
            Assert.True(spec.HasWildcard);
            Assert.Equal(Path.Combine(cwd, "src", "*.cs"), spec.Path);

            var bare = ItemSpecParser.Parse("*");
            Assert.Equal(Path.Combine(cwd, "*"), bare.Path);

            var error = Assert.Throws<TfpException>(() => ItemSpecParser.Parse("src/*/a.cs"));
            Assert.Equal(ErrorCodes.InvalidSwitch, error.Code);
        }

        [Fact]
        public void Library_item_spec_carries_recursion_and_deletion_id()
        {
            var spec = ItemSpecParser.Parse("$/Project/a.cs;X3").ToItemSpec(RecursionType.Full);
            Assert.Equal("$/Project/a.cs", spec.Item);
            Assert.Equal(RecursionType.Full, spec.RecursionType);
            Assert.Equal(3, spec.DeletionId);
        }

        [Fact]
        public void Empty_itemspec_is_TFP0002()
        {
            var error = Assert.Throws<TfpException>(() => ItemSpecParser.Parse("  "));
            Assert.Equal(ErrorCodes.InvalidSwitch, error.Code);
        }
    }
}
