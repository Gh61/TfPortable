using System;
using System.Linq;
using TfPortable.Cli;
using TfPortable.Commands;
using Xunit;

namespace TfPortable.Tests
{
    public class CommandCatalogTests
    {
        [Fact]
        public void Names_and_aliases_are_unique_and_lower_case()
        {
            var names = CommandCatalog.Commands.SelectMany(c => new[] { c.Name }.Concat(c.Aliases)).ToList();
            Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(names, n => Assert.Equal(n.ToLowerInvariant(), n));
        }

        [Fact]
        public void Switch_names_are_unique_per_command_and_short_forms_do_not_collide()
        {
            foreach (var command in CommandCatalog.Commands)
            {
                var switches = CommandCatalog.SwitchesOf(command).ToList();
                Assert.Equal(switches.Count, switches.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
                var shorts = switches.Where(s => s.ShortName != null).Select(s => s.ShortName!).ToList();
                Assert.True(shorts.Count == shorts.Distinct(StringComparer.OrdinalIgnoreCase).Count(), command.Name + ": duplicate short form");
            }
        }

        [Fact]
        public void Every_implemented_command_in_the_program_is_in_the_catalog_and_marked_implemented()
        {
            foreach (var command in Program.Commands())
            {
                var spec = CommandCatalog.Find(command.Name);
                Assert.NotNull(spec);
                Assert.Equal(CommandStatus.Implemented, spec!.Status);
            }

            foreach (var spec in CommandCatalog.Commands.Where(c => c.Status == CommandStatus.Implemented))
            {
                Assert.Contains(Program.Commands(), c => c.Name == spec.Name);
            }
        }

        [Fact]
        public void Phase_1_commands_of_the_specification_are_present()
        {
            string[] phase1 =
            {
                "status", "add", "checkout", "delete", "rename", "undo", "get", "resolve", "reconcile", "localversions", "workfold", "workspaces",
                "history", "changeset", "view", "difference", "dir", "properties", "labels", "branches", "merge",
            };
            foreach (string name in phase1)
            {
                var spec = CommandCatalog.Find(name);
                Assert.NotNull(spec);
                Assert.Equal(1, spec!.Phase);
            }

            foreach (string name in new[] { "checkin", "shelve", "shelvesets", "unshelve", "lock", "label", "unlabel" })
            {
                var spec = CommandCatalog.Find(name)!;
                Assert.Equal(CommandStatus.Implemented, spec.Status);
                Assert.Equal(3, spec.Phase);
            }

            foreach (string name in new[] { "destroy", "permission", "workspace" })
            {
                Assert.Equal(CommandStatus.OutOfScope, CommandCatalog.Find(name)!.Status);
            }
        }

        [Fact]
        public void Short_forms_follow_the_specification()
        {
            var history = CommandCatalog.Find("history")!;
            var switches = CommandCatalog.SwitchesOf(history).ToDictionary(s => s.Name);
            Assert.Equal("R", switches["recursive"].ShortName);
            Assert.Equal("V", switches["version"].ShortName);
            Assert.Equal("F", switches["format"].ShortName);
            Assert.Equal("U", switches["user"].ShortName);
            Assert.Equal("W", switches["workspace"].ShortName);
            Assert.Equal("S", switches["collection"].ShortName);
            Assert.Equal("Y", switches["login"].ShortName);
            Assert.Equal("I", switches["noprompt"].ShortName);
            Assert.Equal("X", switches["slotmode"].ShortName);
            Assert.Equal("?", switches["help"].ShortName);
        }

        [Fact]
        public void Help_lines_describe_the_switches_of_a_command()
        {
            var lines = HelpCommand.CommandLines(CommandCatalog.Find("status")!, "tfp").ToList();
            Assert.Contains(lines, l => l.StartsWith("status (stat) - ", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.StartsWith("Usage: tfp status [itemspec ...] [/recursive]", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.Contains("/format:brief|detailed|json (-F)"));
            Assert.Contains(lines, l => l.Contains("/collection:<value> (-S)"));
            Assert.Contains("Status: available in this build.", lines);
        }

        [Fact]
        public void Help_catalog_lists_every_command_once()
        {
            var text = string.Join("\n", HelpCommand.CatalogLines("tfp"));
            foreach (var spec in CommandCatalog.Commands)
            {
                Assert.Contains(spec.Name, text);
            }

            Assert.Contains("(planned)", text);
            Assert.Contains("Global switches:", text);
        }

        [Fact]
        public void Help_json_has_the_schema_fields()
        {
            var json = HelpCommand.CommandJson(CommandCatalog.Find("history")!);
            Assert.Equal("history", (string)json["name"]!);
            Assert.Equal("hist", (string)json["aliases"]![0]!);
            Assert.Equal(1, (int)json["phase"]!);
            Assert.True((bool)json["supported"]!);
            Assert.Equal("implemented", (string)json["status"]!);
            var comment = json["switches"]!.First(s => (string)s["name"]! == "comment");
            Assert.True((bool)comment["extension"]!);
            Assert.Equal("required", (string)comment["value"]!);
            var format = json["switches"]!.First(s => (string)s["name"]! == "format");
            Assert.Equal(new[] { "brief", "detailed", "json" }, format["values"]!.Select(v => (string)v!).ToArray());
            Assert.Equal("F", (string)format["short"]!);
        }
    }
}
