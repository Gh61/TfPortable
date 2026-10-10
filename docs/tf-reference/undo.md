# undo (tf.exe 18.10.12215.6)

Read from: `TF/Microsoft.TeamFoundation.VersionControl.CommandLine/CommandUndo.cs` (`Run`,
`PromptUserForEditedFiles`), 2026-10-10.

## Switches and defaults

- Options: `/collection`, `/recursive`, `/workspace`, `/server`. At least one item spec (exit 100 otherwise).
- The workspace comes from `/workspace:name[;owner]`, otherwise from the item specs or the current directory.

## `/workspace:<other>` (question 25)

- `VersionControlServer.GetWorkspace(name, owner).Undo(itemSpecs)`: the library resolves a workspace that is
  not local from the server. tf.exe performs **no permission check of its own**; when the caller lacks the
  "undo other users' changes" permission, the server's failure arrives through the library's `NonFatalError`
  event (stderr, exit 1), or as an exception (exit 100) when nothing could be undone. The exact server message
  is not in the client code (it is a TFxxxxx text of the server) and remains uncaptured.
- Interactively, in a local workspace, every edit with a content change is confirmed one by one (yes/no/all);
  a declined edit sets exit 1. With `/noprompt` nothing is asked.
- No pending change matches → stderr error "no changes to undo", exit 100. Undone changes print through the
  library's `UndonePendingChange` event (stdout, grouped by folder); a writable file in the way is a stderr
  warning with exit 1.

## Consequences for TfPortable

- compat.md §18 (`undo /workspace` is level 3 and goes through the other workspace): confirmed; the missing
  permission case stays server-defined.
