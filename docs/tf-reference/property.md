# property (tf.exe 18.10.12215.6)

Read from: `TF/Microsoft.TeamFoundation.VersionControl.CommandLine/CommandProperty.cs` (`Run`, `RunList`,
`RunSet`, `RunDelete`, `DisplayProperties`), `TFCommand.cs` (`ParsePropertyValues`), 2026-10-10.

## Switches (question 26)

- **tf.exe 18.10 does have a write form.** Options: `/setvalues:name=value[;name=value...]`,
  `/deletevalues:name` (repeatable), `/deleteall`, `/propertynames:name` (repeatable), `/output:<file>`,
  `/recursive`, `/lock`, `/silent`, `/version`, `/collection`. The help capture is empty because the command has
  no help text; `/setproperty` (the phase 4 capture) is simply not the name.
- Combinations refused (exit 100): `/setvalues` with `/deletevalues`, `/output`, `/deleteall`, `/version`,
  `/propertynames`, `/collection`; `/deletevalues` or `/deleteall` with `/output`, `/version`, `/propertynames`,
  `/collection`; `/output` with `/deleteall`. At least one item spec is required.
- `/setvalues` value syntax: `name=value;name2=value2`, a doubled separator escapes it; a value starting with
  `@` reads the file's bytes as a binary property; an empty name or value is an argument error.

## Reading

- `VersionControlServer.GetItems(itemSpec, version, DeletedState.Any, ItemType.Any, GetItemsOptions.None,
  propertyFilters, null)` per item; the property filter is `/propertynames` or `*`; the default version is the
  workspace version when a workspace is known, otherwise Latest.
- Output per item: a dashed line (console width minus one), "Server item: <path>", then per property "Name:"
  and "Value:" (binary values print a placeholder), each followed by a blank line; no property → "no
  properties on item" line. A missing item is a stderr warning and stops the loop (exit stays 0).
- `/output:<file>` needs exactly one `/propertynames` and no recursion; writes the first property's value to
  the file (bytes for binary, text otherwise); a missing property is a stderr warning.

## Writing

- `/setvalues` and `/deletevalues`/`/deleteall` **pend a property change** in the workspace of the item specs:
  `Workspace.PendPropertyChange(paths, PropertyValue[], recursion, lockLevel, PendChangesOptions.Silent|None,
  null, null)`; delete passes properties with a null value (`/deleteall`: one property with a null name). The
  change is checked in like any other pending change.

## Consequences for TfPortable

- compat.md §21 and SPEC.md said "no write form in tf.exe 18.10": **wrong**; the write form is
  `/setvalues`/`/deletevalues`/`/deleteall` and it is a pending change. The developer decided on 2026-10-10 that
  tfp implements it for tf.exe compatibility (cloud agent, next milestone); until then TFP0002.

## Implementation brief for TfPortable (decided 2026-10-10)

What properties are: free metadata on a versioned item, a dictionary `name → string | byte[]` stored by the
server next to content and encoding. Microsoft uses them internally (for example the executable bit for
cross-platform clients); Visual Studio has no UI for them. A property change is a pending change of change type
"property" and is checked in like any other.

Switches to add to the `property` catalog row (names exactly as tf.exe, see "Switches" above):

| Switch | Value | Level | Meaning |
| --- | --- | --- | --- |
| `/setvalues:` | `name=value[;name=value...]`; `;;` and `==` escape the separators; a value `@file` reads the file bytes as a binary value | 2 | pend a property set |
| `/deletevalues:` | `name`, repeatable | 2 | pend a property delete (value null) |
| `/deleteall` | none | 2 | pend the deletion of every property (one `PropertyValue` with a null name and null value) |
| `/propertynames:` | `name`, repeatable | 1 | reading: filter instead of `*` |
| `/output:` | file path | 1 | reading: write the value of the single requested property to the file (bytes for binary, text otherwise); needs exactly one `/propertynames` and no `/recursive` |
| `/lock:`, `/silent` | as on `add` | 2 | passed to the pend call; `/lock` with a server lock stays refused as on the other pend commands |

Invalid combinations (TFP0002, exit 100, as in tf.exe): `/setvalues` with `/deletevalues`, `/deleteall`,
`/output`, `/version`, `/propertynames` or `/collection`; `/deletevalues` or `/deleteall` with `/output`,
`/version`, `/propertynames` or `/collection`; `/output` with `/deleteall`; an empty name or value in
`/setvalues`.

Library call for the write forms: `Workspace.PendPropertyChange(string[] paths, PropertyValue[] properties,
RecursionType recursion, LockLevel lockLevel, PendChangesOptions options, null, null)` on the workspace of the
item specs (`PendChangesOptions.Silent` for `/silent`). The pended changes arrive through the library's
`NewPendingChange` event (change type `property`), so the output is the usual grouped `property: <item>` listing
of the pend commands and `status` shows them as `property`. `undo` reverts them.

Protection level (as of v1.2.0): the command reads at level 0; `/setvalues`, `/deletevalues`, `/deleteall` and
`/output` raise it to level 1 (same mechanism as `workfold /map`, which raises to level 2). JSON: reading keeps `items[]`; writing returns the usual
`changes: [PendingChange]`. Documentation to update: SPEC.md catalog row and phase 4 table, `docs/commands.md`,
`docs/json-schema.md`, `docs/compat.md` §21, `docs/CLAUDE.md` permission snippet (`tfp property` moves from
`allow` to the same treatment as `tfp permission`: listing allowed, writes ask). Integration test: set, read back
(`/propertynames`), `/output`, delete, `/deleteall`, undo, under the CI folder; the phase 4 capture script should
use `/setvalues` instead of `/setproperty` when the developer re-captures.
