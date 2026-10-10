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

- compat.md §21 and SPEC.md say "no write form in tf.exe 18.10": **wrong**; the write form is
  `/setvalues`/`/deletevalues`/`/deleteall` and it is a pending change (level 2 by the protection model). Whether
  tfp offers it is a design decision for the developer (recorded under "Needs the user" in PROGRESS.md).
- `/propertynames` and `/output` are read-side switches tfp does not have; optional.
