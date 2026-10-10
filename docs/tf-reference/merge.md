# merge (tf.exe 18.10.12215.6)

Read from: `TF/Microsoft.TeamFoundation.VersionControl.CommandLine/CommandMerge.cs` (`Run`, `GetVersionRange`,
`GetMergeOptions`, `DoMergeCandidateQuery`), `VersionControlCommand.cs` (`Merging`, `DisplayMergeEvent`,
`DisplayMergeSummary`, `ShouldDisplaySummary`), 2026-10-10.

## Switches and defaults

- Options: `/recursive`, `/force`, `/candidate`, `/conservative`, `/discard`, `/version`, `/lock`, `/preview`,
  `/baseless`, `/nosummary`, `/silent`, `/noimplicitbaseless`, `/format:brief|detailed`, `/noautoresolve`.
  Exactly two free arguments (source, target).
- Version range: `source;C1~C2` or `/version` (both → "ambiguous version", exit 100); none → from null to
  Latest; one version → from null to that version. `/force` requires a version.
- `/format` default is `detailed` under `/noprompt`, `brief` interactively; it only decides whether the
  conflicts are repeated in the summary.

## Library calls (question 27)

- `Workspace.Merge(ItemSpec(source, recursion, deletionId), target, versionFrom, versionTo, lockLevel,
  MergeOptionsEx)`. Flags: `/force` → `ForceMerge`, `/baseless` → `Baseless`, **`/preview` → `NoMerge`**,
  `/discard` → `AlwaysAcceptMine`, `/silent` → `Silent`, `/noimplicitbaseless` → `NoImplicitBaseless`,
  `/conservative` → `Conservative`, `/noautoresolve` → `NoAutoResolve`.
- Every operation prints through the library's `Merging` event: the event's own message as a stdout line;
  a conflict status as a stderr warning with exit 1; a writable or pending target as the message plus a
  stderr warning with exit 1. The preview uses the same event, so preview lines equal real merge lines.
- `/candidate`: `VersionControlServer.GetMergeCandidates(source, target, Baseless?)`; refuses `/force`,
  `/discard`, `/lock`, `/version`, `/nosummary`; prints the candidates newest first as a Changeset/User/Date/
  Comment table, `*` after a partial changeset; none → "no changes to merge" (stdout, unless `/silent`).

## Exit and summary (question 28)

- Failures without any operation → exit 100. Nothing to do (`NoActionNeeded`) → stdout "no changes to merge"
  unless `/silent`, exit 0.
- The summary (`/nosummary` suppresses it) is printed **only when there were warnings, conflicts or errors and
  at least 10 operations** (and `TFSVC_NOSUMMARY` is not set): a blank line and one stdout info line with the
  counts of conflicts, warnings and errors, then the collected warnings and conflicts again (conflicts only with
  `/format:detailed`), then the non-fatal errors. Small merges therefore print no summary at all, which is why
  `merge/nosummary` and `merge/all-run` captured identical output.
- Interactively the resolve dialog opens for conflicts, except after `/preview`.

## Consequences for TfPortable

- compat.md §15: preview through `NoMerge` and the `Merging` event: confirmed. tfp prints no summary line
  either; for merges of 10 or more operations with conflicts the original adds the count line (shape only, no
  need to copy).
