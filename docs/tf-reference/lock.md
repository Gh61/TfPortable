# lock (tf.exe 18.10.12215.6)

Read from: `TF/Microsoft.TeamFoundation.VersionControl.CommandLine/CommandLock.cs` (`Run`),
`Microsoft.TeamFoundation.VersionControl.Controls/.../ClientHelper.cs` (`ParseLockOption`),
`Microsoft.TeamFoundation.VersionControl.Client/.../Workspace.cs` (`SetLock`); 2026-10-10.

## Switches and defaults

- Options: `/collection`, `/lock:none|checkin|checkout`, `/recursive`, `/workspace`, `/server`, `/silent`.
- At least one item spec and `/lock` are required (exit 100 otherwise). `/lock` without a value is
  `LockLevel.Unchanged`; an unknown value is an argument error.

## Library calls (question 20)

- The workspace comes from `/workspace:name[;owner]` when given, otherwise from the item specs (a mapped local
  path) or the current directory. The collection comes from that workspace unless `/collection` is given.
- `VersionControlServer.GetWorkspace(name, owner).SetLock(paths, lockLevel, recursion, silent)`. That is a
  `PendChanges` request of type Lock; the library resolves the workspace from the server when it is not a local
  one, so another user's or another computer's workspace works the same way. tf.exe does no permission check
  of its own; the server answers.
- Lock changes print through the library's `NewPendingChange` event as `lock: <item>` lines grouped by folder
  (stdout).

## `lock /lock:none` on an unlocked item (question 19)

- No special case in tf.exe: the request goes to the server, which answers with a per-item failure; the
  failure arrives through `NonFatalError` (stderr, exit 1) and nothing is listed for the item. This is the
  behavior behind the extra2 capture (exit 1).

## Consequences for TfPortable

- compat.md §16/§20 (`lock /lock:none` of an unlocked item exits 1 with TFP0301 per item; `lock /workspace`
  through the other workspace's `SetLock`): confirmed.
