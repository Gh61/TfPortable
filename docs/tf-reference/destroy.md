# destroy (tf.exe 18.10.12215.6)

Read from: `TF/Microsoft.TeamFoundation.VersionControl.CommandLine/CommandDestroy.cs` (`Run`), 2026-10-10.

## Switches and defaults (question 21)

- Options: `/collection`, `/keephistory`, `/startcleanup`, `/preview`, `/silent`, `/stopat`, `/server`,
  `/version`. At least one item spec (exit 100 otherwise); every item must be a server path; `$/` is refused.
- `/keephistory` default: **off** (the flag is only added when the switch is present). Without it the item and
  its history are destroyed; with it the history stays and the content is removed.
- `/stopat:<versionspec>` is valid **only with `/keephistory`** (otherwise exit 100). It is parsed as a single
  version spec; a label or workspace spec is refused ("invalid stopat spec"); changeset, date and `T` work. It
  is the version up to which content is removed.
- `/startcleanup` simply adds `DestroyFlags.StartCleanup`; tf.exe does not check the server type, so whether
  Azure DevOps Services accepts it is the server's answer.
- The item's version is `;version` on the item or `/version`, default Latest (a range is refused).
- `/silent` adds `DestroyFlags.Silent` and suppresses the final "no items to destroy" line.

## Library calls and output

- Interactive, or with `/preview`, and without `/keephistory`: a first call
  `VersionControlServer.Destroy(itemSpec(full recursion), itemVersion, stopAt, flags | AffectedChanges | Preview,
  out affectedPendingChanges, out affectedShelvedChanges)` only to learn the affected pending and shelved
  changes. `AffectedChanges` is used **only together with `Preview`**, never on the real call.
- `/preview` prints, per item, "pending changes affected by destroying <item>" and "shelved changes affected by
  destroying <item>" followed by the brief pending-change table with the user column, each when non-empty
  (stdout). With `/keephistory` the preview prints no affected changes (the first call is skipped).
- Then, unless `/preview`: the question "destroy <item>?" (yes/no, or yes/no/all for several items) when
  interactive; a "no" sets exit 1 and skips the item. The real call `Destroy(itemSpec, itemVersion, stopAt,
  flags)`; every destroyed item prints through the library's `Destroyed` event as a stdout line with the item
  (and `;X<deletion id>` when deleted). **The same event fires in the preview**, which is why the capture
  `destroy/preview-file` shows the "destroyed" line for a preview.
- When nothing was destroyed by any item and nothing was declined and `/silent` is absent: stdout line "no
  items to destroy" (exit stays 0).
- A missing item: the server's exception propagates, exit 100 (the capture shows VS403405).

## Consequences for TfPortable

- compat.md §21: `Would destroy:` wording in the preview is a deviation from the original's identical
  "destroyed" line (now explained: the library event does not distinguish); `/keephistory` with one version
  reporting nothing: follows. tfp's handling of `AffectedChanges` (preview only) matches tf.exe.
- `/stopat` without `/keephistory` is an error in tf.exe; tfp refuses it the same way (TFP0002, `ServerAdminCommands`).
