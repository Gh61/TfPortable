# rollback (tf.exe 18.10.12215.6)

Read from: `TF/Microsoft.TeamFoundation.VersionControl.CommandLine/CommandRollback.cs` (`Run`, `GetOptions`,
`GetVersionRange`), 2026-10-10.

## Switches and defaults

- Options: `/recursive`, `/changeset`, `/keepmergehistory`, `/lock`, `/silent`, `/toversion`, `/version`,
  `/noautoresolve`. Item specs are optional. Exactly one of `/changeset` and `/toversion` (none → exit 100,
  both → invalid combination).
- `/version` (the version of the items to roll back in) is honored only when item specs are given; a range is
  "ambiguous version".
- `/changeset:<spec>` accepts one spec or a range `A~B` (reordered when reversed); a label or workspace spec is
  refused. One spec → from = to = that spec.

## Library calls (questions 30 and 33)

- `Workspace.Rollback(paths, recursion, itemVersion, versionFrom, versionTo, lockLevel, RollbackOptions, null)`
  with `paths` = the item specs **as given, an empty array when none**; the library's workspace-wide behavior
  decides what an empty array means (the whole workspace). So `/changeset:C85~C90` without items rolls back the
  whole workspace from C85 to C90 in one call.
- `/toversion:<v>`: **versionFrom = v, versionTo = Latest**, with `RollbackOptions.ToVersion`. tf.exe itself
  does not check for an itemspec, but the server refuses the empty list with TF203065 (exit 100; golden
  `rollback/toversion-no-item`), so an itemspec is required in effect. `/keepmergehistory` →
  `KeepMergeHistory`, `/silent` → `Silent`, `/noautoresolve` → `NoAutoResolve`.
- Operations print through the `Merging` event (same shapes as `merge`); failures without operations → exit
  100; nothing to do → stdout "no changes to roll back" unless `/silent`.
- The summary rule of `merge` applies (10 or more operations with warnings or conflicts); conflicts are always
  repeated in it (`showConflicts: true`).

## Consequences for TfPortable

- compat.md §15: `v` as the lower bound and Latest as the upper bound for `/toversion`, the whole workspace for
  `/changeset` without items: confirmed exactly.
- tfp requires an itemspec for `/toversion` (TFP0002, exit 100) where tf.exe lets the server refuse it (TF203065,
  exit 100): same outcome, kept (decided with the developer on 2026-10-10).
