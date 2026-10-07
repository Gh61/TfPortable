# `/format:json` output – schema

**Status: approved at Gate A (2026-10-07) as the basis for milestone 2**, on the condition that
the types stay derived from what the original prints in the golden outputs (PendingChange from
`status /format:detailed`, Item from `properties`, Changeset/Change from `changeset`,
Workspace/Mapping from `workspaces /format:detailed` and `workfold`). The implementer fills in
details per command (milestones 2–4) and keeps this file in sync; every change of a field name
after the first release is a breaking change. All open points in section 4 are decided.

Rules (from SPEC.md, "Rozšíření pro AI"):

- Exactly one JSON document on stdout, also on error; stderr is empty except `/verbose`.
- Field names are English, camelCase, stable. Unknown fields may be added, never renamed.
- Dates are ISO 8601 strings with offset (`2026-10-07T19:07:27+02:00`). Changeset numbers are
  JSON numbers. Paths are absolute (local) or server paths (`$/...`). Enumerations are
  lower-case strings listed below. Missing values are `null`, never omitted, never `""`.
- `exitCode` equals the process exit code.

## 1. Envelope

```json
{
  "ok": true,
  "command": "status",
  "exitCode": 0,
  "collection": "https://dev.azure.com/contoso",
  "workspace": "DEV-PC;jan.novak@contoso.com",
  "data": { },
  "truncated": null,
  "warnings": [],
  "errors": []
}
```

| Field | Type | Meaning |
| --- | --- | --- |
| `ok` | bool | `exitCode == 0` |
| `command` | string | Canonical command name (`status`, not `stat`; `difference`, not `diff`) |
| `exitCode` | int | Process exit code |
| `collection` | string or null | Normalized collection URL, null when the command needed none (`help`, `version`, `login` failure before connecting) |
| `workspace` | string or null | `<name>;<owner>` of the workspace used, null when none was needed |
| `data` | object | Command-specific payload (section 3). Always an object, `{}` when there is nothing |
| `truncated` | object or null | `{ "returned": 256, "limit": 256, "hint": "Use /stopafter:<n> or /version:C137~C1 to continue." }` when a result was cut (history default limit, search scan limit) |
| `warnings` | array of Message | Non-fatal findings (e.g. `/user` ignored for local workspaces, PAT without Code scope) |
| `errors` | array of Message | What failed. Empty when `ok` |

Message:

```json
{
  "code": "TFP0402",
  "tfCode": "TF10141",
  "message": "File C:\\Src\\App\\Config.xml is writable and has no pending change; get did not overwrite it.",
  "hint": "Review local changes with 'tfp diff'. Use 'tfp get /overwrite' to overwrite it.",
  "item": "C:\\Src\\App\\Config.xml",
  "activityId": null
}
```

`tfCode`, `hint`, `item`, `activityId` are null when not applicable. `item` is the local path when
the command worked with a local item, otherwise the server path.

## 2. Shared object types

| Type | Fields |
| --- | --- |
| **Item** | `serverPath`, `localPath` (null if not mapped), `isFolder`, `changesetId`, `deletionId` (0 = not deleted), `encoding` (`"utf-8"`, `"windows-1250"`, `"binary"`, null for folders), `size` (bytes, null for folders), `lastModified` |
| **Changeset** | `id`, `owner` (display name), `ownerUniqueName` (e-mail/UPN or null), `date`, `comment`, `changes` (array of Change, only in detailed/`changeset`/`/files`), `workItems` (array of `{ id, title }`, only in `changeset` and detailed history) |
| **Change** | `changeTypes` (array of ChangeType, e.g. `["merge","edit"]`), `serverPath`, `changesetId` (item version), `deletionId`, `sourceServerPath` (for `rename`, `branch`, `merge`; else null) |
| **PendingChange** | `changeTypes`, `serverPath`, `localPath`, `isFolder`, `version` (changeset the change is based on, 0 for adds), `sourceServerPath`, `lock` (LockLevel), `encoding`, `date`, `owner`, `workspace`, `detected` (bool: candidate not yet pended) |
| **Conflict** | `type` (`"get"`, `"checkin"`, `"merge"`, `"local"`), `serverPath`, `localPath`, `yourVersion`, `theirVersion`, `baseVersion`, `yourChangeTypes`, `theirChangeTypes`, `resolution` (null or Resolution), `canAutoMerge` |
| **Mapping** | `serverPath`, `localPath` (null when cloaked), `cloaked`, `recursionType` (`"full"`, `"onelevel"`) |
| **Workspace** | `name`, `owner`, `ownerUniqueName`, `computer`, `comment`, `collection`, `location` (`"local"`, `"server"`), `permission` (`"private"`, `"publiclimited"`, `"public"`), `lastAccess`, `mappings` |

Enumerations:

- ChangeType: `add`, `edit`, `delete`, `rename`, `branch`, `merge`, `undelete`, `rollback`,
  `lock`, `encoding`, `property`, `sourcerename`.
- LockLevel: `none`, `checkin`, `checkout`.
- Resolution: `acceptmerge`, `acceptyours`, `accepttheirs`, `acceptyoursrenametheirs`,
  `overwritelocal`, `deleteconflict`.
- Location: `local`, `server`.

## 3. `data` per command (phase 1)

| Command | `data` |
| --- | --- |
| `login` | `{ "collection", "identity": { "displayName", "uniqueName", "id" }, "stored": true, "storedAt", "codeScope": true\|false\|null }` |
| `logout` | `{ "collection", "removed": bool }` |
| `whoami` | `{ "collection", "identity": {…}, "patStoredAt", "workspace": Workspace or null, "cwd" }` |
| `version` | `{ "version", "informationalVersion", "clientLibraries": { "Microsoft.TeamFoundationServer.ExtendedClient": "20.256.2", … }, "runtime" }` |
| `help` | `{ "commands": [ { "name", "aliases": [], "phase", "supported": bool, "summary", "switches": [ { "name", "short", "value": "none"\|"optional"\|"required", "values": [], "summary", "extension": bool } ] } ] }`; with `help <command>` the array has one element |
| `status` | `{ "changes": [PendingChange], "detectedChanges": [PendingChange], "counts": { "changes", "detected" } }` |
| `add`, `checkout`, `delete`, `rename` | `{ "changes": [PendingChange], "skipped": [ { "localPath", "reason": "ignored"\|"alreadyVersioned"\|"notFound"\|"locked" } ] }` |
| `undo` | `{ "undone": [PendingChange], "notFound": [string] }` |
| `get` | `{ "operations": [ { "operation": "getting"\|"replacing"\|"deleting", "localPath", "serverPath", "version" } ], "conflicts": [Conflict], "skipped": [ { "localPath", "reason": "writable" } ], "counts": { "getting", "replacing", "deleting", "conflicts", "skipped" }, "preview": bool }` |
| `resolve` | `{ "conflicts": [Conflict], "resolved": [ { "localPath", "serverPath", "resolution" } ], "preview": bool }` |
| `reconcile` | `{ "promoted": [PendingChange], "deleted": [string], "differences": [ { "localPath", "status": "modified"\|"added"\|"deleted" } ], "preview": bool }` |
| `localversions` | `{ "items": [ { "localPath", "serverPath", "changesetId", "isFolder" } ], "count" }` |
| `workfold` | `{ "workspace": Workspace }` (mappings included) |
| `workspaces` | `{ "workspaces": [Workspace] }` |
| `history` | `{ "changesets": [Changeset], "count", "mode": "slot"\|"item", "scanned": n (search extensions only) }` |
| `changeset` | `{ "changeset": Changeset, "diff": string or null }` (`diff` only with `/diff`) |
| `view` | `{ "item": Item, "version", "content": string, "output": null or path }`; binary items need `/output`, then `content` is null |
| `difference` | `{ "files": [ { "path", "source": { "kind": "server"\|"local"\|"shelveset", "path", "version" }, "target": {…}, "status": "identical"\|"different"\|"binary", "diff": string or null } ], "format": "unified"\|"brief" }` |
| `dir` | `{ "folders": [ { "serverPath", "items": [Item] } ], "count" }` |
| `properties` | `{ "items": [ { "local": { "localPath", "serverPath", "changesetId", "changeTypes", "isFolder" } or null, "server": Item and `lock`, `lockOwner` } ] }` |
| `labels` | `{ "labels": [ { "name", "scope", "owner", "date", "comment", "items": [ { "serverPath", "changesetId", "deletionId" } ] } ] }` (`items` only with `/format:detailed`) |
| `branches` | `{ "root": "$/...", "branches": [ { "serverPath", "parent", "branchedFromVersion", "children": [...] } ] }` |
| `merge /candidate` | `{ "source", "target", "candidates": [ Changeset + "partial": bool ] }` |

Phase 2 commands (`merges`, `annotate`, `folderdiff`, `shelvesets`, `unshelve`, `undelete`,
`rollback`, `branch`, `merge`) add their sections when implemented.

## 4. Open points for Gate A

1. `view /format:json` returns the text content inline as a JSON string; a binary item needs
   `/output` (then `content` is null), otherwise TFP0303 (approved at Gate A).
2. `workspace` in the envelope stays a `name;owner` string as in SPEC.md; the full object is in
   `whoami`/`workfold` data (approved with the draft).
3. Enumeration casing: lower-case (`acceptyours`) for stability; the text output keeps the
   server's casing (`AcceptYours`) (approved with the draft).
