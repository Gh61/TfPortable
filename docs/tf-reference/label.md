# label, unlabel, labels (tf.exe 18.10.12215.6)

Read from: `TF/Microsoft.TeamFoundation.VersionControl.CommandLine/CommandLabel.cs` (`CreateOrUpdateLabel`,
`DeleteLabel`), `CommandUnlabel.cs` (`Run`), `CommandLabels.cs` (`Run`), `VersionControlCommand.cs`
(`DetermineVersionFromPathType`, `RemoveUnmappedItemSpecs`, `ReportLabelResults`);
`Microsoft.TeamFoundation.VersionControl.Client/.../VersionControlServer.cs` (`CreateLabel`, `DeleteLabel`,
`UnlabelItem`); 2026-10-10.

## label

- Options: `/child:merge|replace`, `/collection`, `/comment`, `/delete`, `/owner`, `/recursive`, `/server`,
  `/version`. First free argument `name[@scope]` (required), the rest are item specs with optional `;version`.
- Default `/child` (question 16): **Fail** (`LabelChildOption.Fail`); `merge` and `replace` are the only values.
- Default scope (question 14): tf.exe passes the scope **as given, or null** when `@scope` is absent, into
  `new VersionControlLabel(server, name, owner, scope, comment)` and `VersionControlServer.CreateLabel(label,
  items, childOption)`. The default is chosen by the server (documented as the team project of the items); the
  client does not compute it.
- Default version of an item (question 15): a local path gets the **workspace version** of the workspace the
  paths belong to (`WorkspaceVersionSpec(name, owner)`), a server path gets **Latest**; an explicit `;version`
  or `/version` wins. A version range on an item is reduced to its end with a stderr warning and exit 1; a
  deletion id on an item is ignored with a warning and exit 1.
- Items are sent as `LabelItemSpec(ItemSpec(path, recursion, 0), version, exclude: false)`; recursion is
  `Full` with `/recursive`, otherwise `None`.
- Without items, `/comment` and `/owner` ("not enough arguments") → exit 100. Items, a comment or an owner alone
  update the label.
- Result: one stdout line per `LabelResult` (created / updated / deleted, with `name@scope`); an empty result
  array → exit 100 with no message of its own.
- `/delete` (question 18): refuses item specs, `/child`, `/version`, `/owner`, `/comment`, `/recursive`; the
  scope defaults to `$/`; **`VersionControlServer.DeleteLabel(name, scope)` only**, no query first; an empty
  result → exit 100.

## unlabel (question 17)

- Options: `/collection`, `/recursive`, `/server`. Arguments: `name[@scope]` and at least one item spec.
- Local item specs that are not mapped are dropped with a stderr warning and exit 1; items from a second
  workspace likewise; no item left → exit 100 without a message.
- Library call: `VersionControlServer.UnlabelItem(name, scope, items, new LabelVersionSpec(name, scope))` —
  the version passed is the label's own version. Results print as for `label`.

## labels (question 34)

- Options: `/collection`, `/server`, `/format:brief|detailed`, `/owner`. At most one free argument
  `name[@scope]`; wildcards allowed in the name; the scope defaults to `$/` here.
- Owner: `/owner` or `.` (the authenticated user); `/owner:*` → every owner.
- `VersionControlServer.QueryLabels(name, scope, owner, includeItems: detailed)`. **An empty result, exact or
  wildcard name alike, is a stderr error "no labels found" with exit 100.**
- Brief: columns Label, Owner, Date. Detailed: `=` separators, Label/Scope/Owner/Date/Comment, then a
  Changeset/Item table of the labelled items sorted by server path.

## Consequences for TfPortable

- compat.md §16: `/child:fail` default, workspace version for local paths and latest for server paths,
  `LabelVersionSpec` in `unlabel`, `DeleteLabel` only: all confirmed. tfp's "scope defaults to the team project
  of the first item" matches what the server does with a null scope (the phase 3 capture `label` without a
  scope agrees); tfp computes it on the client, tf.exe leaves it to the server.
- compat.md §20 `labels <name>` without a match exits 100: confirmed, and a wildcard without a match is 100 as
  well (tfp already does both).
