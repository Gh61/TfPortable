# permission (tf.exe 18.10.12215.6)

Read from: `TF/Microsoft.TeamFoundation.VersionControl.CommandLine/CommandPermission.cs` (`Run`,
`DisplayPermissions`, `DisplayChanges`), `TFCommand.cs` (`DisplayAccessEntries`, `DisplayStrings`),
`AccessControlDisplay.cs`; `Microsoft.TeamFoundation.VersionControl.Client/.../AccessEntry.cs`,
`Microsoft.TeamFoundation.VersionControl.Common/.../VersionedItemPermissions.cs`; 2026-10-10.

## Switches

- Options: `/allow`, `/collection`, `/deny`, `/group`, `/inherit:yes|no`, `/remove`, `/user`, `/server`,
  `/recursive`, `/global`. `/allow`, `/deny`, `/remove`, `/user`, `/group` may repeat (every value is collected).
- Listing mode is chosen when none of `/allow`, `/deny`, `/remove`, `/inherit` is present.

## Listing (question 22)

- `VersionControlServer.GetPermissions(identities, items, recursion)`; identities = the `/user` and `/group`
  values concatenated (null = everyone); no item → `$/` with full recursion; otherwise recursion `None` unless
  `/recursive`.
- Shape per item (`ItemSecurity`): a line of `=` (console width minus one); one line with "Server item:
  <path>" and "Inherit: Yes|No"; then either "No permissions" or, per access entry, indented by two spaces:
  "Identity: <name>", then "Allow:", "Deny:", "Allow (inherited):", "Deny (inherited):" with the headings padded
  to a common width and the permission names comma-separated and wrapped at the console width. Entries are
  separated by a blank line, items by a blank line. Everything is stdout.
- The order of the permission names is the bit order of `VersionedItemPermissions`: Read, PendChange,
  Checkin, Label, Lock, ReviseOther, UnlockOther, UndoOther, LabelOther, AdminProjectRights, CheckinOther,
  Merge, ManageBranch (the library translates the bit mask to names in that order). The order of the entries is
  the server's.

## Changes (question 23)

- `/recursive` is refused with a change ("option not allowed"). `/inherit` cannot be combined with `/allow`,
  `/deny`, `/remove` or an identity. A permission change needs at least one identity and at least one path
  (exit 100 otherwise).
- Identities are passed **verbatim**: `/user` and `/group` values are merged into one list; tf.exe does not
  resolve display names or unique names itself, the server does. The distinction between user and group is
  lost before the call.
- For every path × identity one `PermissionChange(path, identity, allow[], deny[], remove[])`; for every path
  with `/inherit` one `InheritanceChange(path, bool)`. One call
  `VersionControlServer.SetPermissions(SecurityChange[])`; the result is the list of applied changes.
- Output: per applied change a stdout line "<path>: <identity>" (or "<path>: Inherit = Yes|No"). **An empty
  result sets exit 100**; failures of single changes come through the library's `NonFatalError` (stderr,
  exit 1).
- `/global`: `GetGlobalPermissions(identities)` / `SetGlobalPermissions(changes on "$/")`; `/recursive` and
  `/inherit` are refused there.

## Consequences for TfPortable

- compat.md §21: shape and the verbatim identities confirmed; "identities sorted by name" in tfp is a deviation
  from the server order (harmless). tfp reports unapplied changes as per-part item errors (exit 1, JSON
  `skipped[]`), which matches the library's `NonFatalError`; the original additionally sets exit 100 when the
  result list is empty (nothing applied at all). Whether tfp returns 1 or 100 in that case is not covered by a
  test (the CI user cannot change permissions); recorded as an open question.
