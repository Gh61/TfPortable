# undelete (tf.exe 18.10.12215.6)

Read from: `TF/Microsoft.TeamFoundation.VersionControl.CommandLine/CommandUndelete.cs` (`Run`),
`VersionControlCommand.cs` (`ParseItemSpec`), 2026-10-10.

## Switches and defaults (question 31)

- Options: `/lock`, `/newname`, `/noget`, `/recursive`, `/silent`. At least one item spec.
- An item spec may carry `;X<deletion id>`; a version on the item is ignored with a stderr warning. Without
  `;X` the deletion id is 0 and the **server picks the latest deletion** (the client does not query).
- `/newname:<path>` needs exactly one item spec and no `/recursive`:
  `Workspace.PendUndelete(item, deletionId, newName, lockLevel, updateDisk: !/noget, detectFolderAtTarget: true,
  silent)`; otherwise `Workspace.PendUndelete(itemSpecs, lockLevel, updateDisk, true, silent)`.
- The pended changes print through the `NewPendingChange` event; a writable file in the way is a stderr warning
  with exit 1 and, interactively, opens the resolve dialog.
- A wrong `;X<n>` is passed through; the server's reaction (the capture shows the item restored anyway) is not
  a client decision.

## Consequences for TfPortable

- compat.md §20 (`undelete ;X<wrong id>` is TFP0301 in tfp, restored in tf.exe): the original simply forwards
  the id; the deviation stands.
