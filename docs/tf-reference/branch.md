# branch (tf.exe 18.10.12215.6)

Read from: `TF/Microsoft.TeamFoundation.VersionControl.CommandLine/CommandBranch.cs` (`Run`), 2026-10-10.

## Switches and defaults (question 29)

- Options: `/version`, `/noget`, `/lock`, `/silent`, `/checkin`, `/comment`, `/author`, `/notes`. Exactly two
  free arguments (source, target).
- Source version: `source;version` or `/version` (both → "ambiguous version"; a range → the same error).
  **Default: Latest, also for a local source path.** Only an explicit `W` spec without a name is turned into
  the workspace version of the source's workspace.
- Without `/checkin`: `Workspace.PendBranch(source, target, version, lockLevel, getLatest: !/noget,
  detectFolderAtTarget: true, silent)`; `/comment`, `/author`, `/notes` are refused ("not allowed on a pending
  branch", exit 100). The pended changes print through the `NewPendingChange` event (stdout, grouped by folder).
- With `/checkin`: `/lock` is refused; the check-in notes are evaluated against the target project's note
  definitions (failures block, stderr warnings); `VersionControlServer.CreateBranch(source, target, version,
  author, comment, notes, null, null)` commits on the server without a workspace; stdout "Changeset N checked in".

## Consequences for TfPortable

- Question 29 settled: the default is Latest, which is what tfp does (`BranchingCommands.BranchCommand`).
- `branch /checkin` is a server-side commit without pending changes; tfp does not offer it (phase 2 pends only),
  which is a documented scope decision, not a behavior difference.
