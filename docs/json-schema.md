# `/format:json` output – schema

**Status: implemented for every phase 1 command (milestones 2–4, 2026-10-08); the field names below
are the contract of the first release.** Approved at Gate A (2026-10-07) as the basis for milestone 2, on the condition that
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
| `collection` | string or null | Normalized collection URL, null when the command needed none (`help`, `version`, `login` failure before connecting). Filled as soon as the context resolver knows it, so an error envelope (PAT rejected, workspace not found) carries it too |
| `workspace` | string or null | `<name>;<owner>` of the workspace used (owner = display name, as tf.exe prints it), null when none was needed or found. Filled as soon as it is known, also on errors |
| `data` | object | Command-specific payload (section 3). Always an object, `{}` when there is nothing |
| `truncated` | object or null | `{ "returned": 256, "limit": 256, "hint": "Use /stopafter:<n> or /version:C137~C1 to continue." }` when a result was cut (history default limit, search scan limit) |
| `warnings` | array of Message | Non-fatal findings (e.g. `/user` ignored for local workspaces, PAT without Code scope) |
| `errors` | array of Message | What failed. Empty when `ok`. Per-item errors that did not stop the command come first (the exit code is then 1), a fatal error is the last element |

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
| **PendingChange** | `changeTypes`, `serverPath`, `localPath`, `isFolder`, `version` (changeset the change is based on, 0 for adds), `sourceServerPath`, `lock` (LockLevel), `encoding`, `date`, `owner`, `workspace`, `detected` (bool: candidate not yet pended), `mergeSources` (array of `{ serverPath, versionFrom, versionTo, isRename }` for pending merges, branches and rollbacks, null otherwise) |
| **Conflict** | `type` (`"get"`, `"checkin"`, `"merge"`, `"local"`), `serverPath`, `localPath`, `yourVersion`, `theirVersion`, `baseVersion`, `yourChangeTypes`, `theirChangeTypes`, `resolution` (null or Resolution), `canAutoMerge` |
| **Mapping** | `serverPath`, `localPath` (null when cloaked), `cloaked`, `recursionType` (`"full"`, `"onelevel"`) |
| **Workspace** | `name`, `owner`, `ownerUniqueName`, `computer`, `comment`, `collection`, `location` (`"local"`, `"server"`), `permission` (`"private"`, `"publiclimited"`, `"public"`), `lastAccess`, `mappings` |

Enumerations:

- ChangeType: `add`, `edit`, `delete`, `rename`, `branch`, `merge`, `undelete`, `rollback`,
  `lock`, `encoding`, `property`, `sourcerename`.
- LockLevel: `none`, `checkin`, `checkout`.
- Resolution (the names of the tf.exe help, lower-case): `automerge`, `automergeforced`,
  `taketheirs`, `keepyours`, `overwritelocal`, `deleteconflict`, `keepyoursrenametheirs`. The
  documentation aliases `AcceptMerge`, `AcceptYours`, `AcceptTheirs`, `AcceptYoursRenameTheirs` are
  accepted on input and reported under the canonical name.
- Location: `local`, `server`.

## 3. `data` per command (phase 1)

| Command | `data` |
| --- | --- |
| `login` | `{ "collection", "identity": { "displayName", "uniqueName", "id" }, "stored": true, "storedAt", "codeScope": true\|false\|null, "versionControlConnection": bool }`. `identity` comes from the REST check; `versionControlConnection` is whether the TFVC (SOAP) connection also succeeded (false with a warning `TFP0103` when the PAT lacks full access) |
| `logout` | `{ "collection", "removed": bool }` |
| `whoami` | `{ "collection", "identity": {…}, "patStoredAt" (ISO date or null when the PAT came from `/login`), "protectionLevel": 1\|2\|3, "workspace": Workspace or null, "cwd" }` |
| `protection` | `{ "protectionLevel": 1\|2\|3, "settingsPath" }`; `protection set <n>` adds `"previous"` (the level before). Levels: 1 read-only, 2 everyday work, 3 everything (SPEC.md "Úrovně ochrany") |
| `version` | `{ "version", "informationalVersion", "clientLibraries": { "Microsoft.TeamFoundationServer.ExtendedClient": "20.256.2", … }, "runtime" }` |
| `help` | `{ "commands": [ { "name", "aliases": [], "phase": 1\|2\|null, "supported": bool, "status": "implemented"\|"planned"\|"out-of-scope", "summary", "protectionLevel": 1\|2\|3, "switches": [ { "name", "short", "value": "none"\|"optional"\|"required", "values": [], "summary", "extension": bool } ] } ] }`; with `help <command>` the array has one element. `phase` is null for the tool's own commands (`login`, `help`, …) and for out-of-scope commands; `supported` is true only for commands that run in this build (`status` is "implemented"); `switches` include the global ones; `values` lists the accepted values of an enumeration switch (`format` always contains `json`), empty for free text |
| `status` | `{ "changes": [PendingChange], "detectedChanges": [PendingChange], "counts": { "changes", "detected" } }` |
| `add`, `checkout`, `delete`, `rename` | `{ "changes": [PendingChange], "skipped": [ { "localPath", "reason": "alreadyVersioned"\|"notFound"\|"locked"\|"error" } ] }`. Every skipped item is also a Message in `errors` (exit 1); `rename` of a missing source is a fatal TFP0301 (exit 100, docs/compat.md §1.1). The `ignored` reason of the draft is not produced: the client library skips ignored items silently during `/recursive` and wildcard expansion |
| `undo` | `{ "undone": [PendingChange], "notFound": [string] }`; nothing to undo is a TFP0301 in `errors` with exit 1 |
| `get` | `{ "operations": [ { "operation": "getting"\|"replacing"\|"deleting", "localPath", "serverPath", "version", "isFolder" } ], "conflicts": [Conflict], "skipped": [ { "localPath", "reason": "writable" } ], "counts": { "getting", "replacing", "deleting", "conflicts", "skipped" }, "preview": bool }`; conflicts are TFP0403 and writable files TFP0402 in `errors` (exit 1) |
| `resolve` | `{ "conflicts": [Conflict] (still open), "resolved": [ { "localPath", "serverPath", "resolution" } ], "preview": bool }`; open conflicts are TFP0403 in `errors` (exit 1). Conflict also carries `message` (the client library's description) |
| `reconcile` | `{ "promoted": [PendingChange], "deleted": [string], "differences": [ { "localPath", "status": "modified"\|"added"\|"deleted" } ], "preview": bool }`; `differences` lists every detected change, `promoted` what `/promote` pended, `deleted` what `/clean` removed |
| `localversions` | `{ "items": [ { "localPath", "serverPath", "changesetId", "isFolder" } ], "count" }` |
| `workfold` | `{ "workspace": Workspace }` (mappings included; with a folder argument the text shows the matching mapping only, the JSON the whole workspace) |
| `workspaces` | `{ "workspaces": [Workspace] }`; no match is exit 1 with an empty array. `/remove:` returns `{ "removed": [string], "workspaces": [] }` |
| `history` | `{ "changesets": [Changeset], "count", "mode": "slot"\|"item", "scanned": n (search extensions only) }`. `changes` and `workItems` of a Changeset are filled with `/format:detailed`, `/files`, `/changetype` and for a single file (the Change column), null otherwise |
| `changeset` | `{ "changeset": Changeset (with `changes` and `workItems`), "diff": string or null }` (`diff` only with `/diff`: one unified diff per file, binary files summarized) |
| `view` | `{ "item": Item, "version", "content": string, "output": null or path }`; binary items need `/output`, then `content` is null. With `/recursive` the first file fills these fields and `items` lists every file |
| `difference` | `{ "files": [ { "path", "source": { "kind": "server"\|"local"\|"shelveset", "path", "version" }, "target": {…}, "status": "identical"\|"different"\|"binary", "diff": string or null } ], "format": "unified"\|"brief" }` |
| `dir` | `{ "folders": [ { "serverPath", "items": [Item] } ], "count" }` |
| `properties` | `{ "items": [ { "local": { "localPath", "serverPath", "changesetId", "changeTypes", "isFolder" } or null (outside a workspace), "server": Item and `lock`, `lockOwner` } ] }`; a missing item is TFP0301 in `errors` (exit 1) |
| `labels` | `{ "labels": [ { "name", "scope", "owner", "date", "comment", "items": [ { "serverPath", "changesetId", "deletionId" } ] } ] }` (`items` only with `/format:detailed`) |
| `branches` | `{ "root": "$/...", "branches": [ { "serverPath", "parent", "branchedFromVersion", "children": [...] } ] }` |
| `merge /candidate` | `{ "source", "target", "candidates": [ Changeset + "partial": bool ] }` (`changes` filled with `/format:detailed`) |
| `merge` | `{ "source", "target", "preview": bool, "changes": [PendingChange], "conflicts": [Conflict] }`; with `/preview` the changes are the operations the server would pend (same shape, `owner`/`workspace`/`date` are placeholders) and nothing is pended; a conflict of a real merge is TFP0403 in `errors` (exit 1) and the pending merge stays, as in tf.exe; conflicts the server counted but did not list (a preview) are one summary TFP0403 |
| `branch` | `{ "source", "target", "changes": [PendingChange], "skipped": [...] }` (the branched tree as pending branches) |
| `undelete` | `{ "changes": [PendingChange], "skipped": [...] }`; an item that is not deleted is TFP0301 in `errors` (exit 1) |
| `rollback` | `{ "mode": "changeset"\|"toversion", "from": versionspec, "to": versionspec, "changes": [PendingChange], "conflicts": [Conflict] }` (`from` and `to` as passed to the server: the changeset range, or the target version and `T` in `toversion` mode); conflicts are TFP0403 in `errors` (exit 1) |
| `merges` | `{ "source": string or null, "target", "merges": [ { "sourceChangeset", "targetChangeset": Changeset, "partial": bool } ] }` (`changes` of the target changeset filled with `/format:detailed`) |
| `folderdiff` | `{ "source", "target", "sourceOnly": [path], "targetOnly": [path], "different": [ { "source", "target" } ], "same": [ { "source", "target" } ] or null (only with `/view:same`), "counts": { "folders", "files", "sourceOnly", "targetOnly", "different", "same" } }`; paths are server paths or absolute local paths as given |
| `annotate` | `{ "item", "version": changeset id, "lines": [ { "number", "changeset", "owner", "date", "text" } ], "changesets": [Changeset] }` (the changesets that contributed a line) |
| `checkin` | `{ "changeset": n or null (with `/validate`), "validated": bool, "changes": [PendingChange], "conflicts": [ { "serverPath", "message", "resolvable" } ], "policies": [ { "name", "status": "passed"\|"failed"\|"unknown", "message" } ], "policyFailures": [string], "unknownPolicies": [string], "overridden": bool, "workItems": [ { "id", "linked": bool } ] }`; every conflict is a TFP0406, every failed policy or missing note a TFP0407 and every policy tfp cannot evaluate a TFP0409 entry in `errors` (with `/validate` the data is returned and the exit is 100, without it a summary error follows and `data` is empty); a work item that could not be linked after a successful checkin is TFP0501 in `errors` (exit 1) |
| `shelve` | `{ "shelveset": { "name", "owner" }, "changes": [PendingChange], "replaced": bool, "moved": bool }`; `shelve /delete` returns `{ "shelveset", "deleted": true }` |
| `shelvesets` | `{ "shelvesets": [ { "name", "owner", "ownerUniqueName", "date", "comment" } ] }` |
| `unshelve` | `{ "shelveset": { "name", "owner" }, "changes": [PendingChange], "conflicts": [Conflict] }`; open conflicts are TFP0403 in `errors` (exit 1) |
| `label`, `unlabel` | `{ "label": { "name", "scope" }, "results": [ { "name", "scope", "status": "created"\|"updated"\|"deleted" } ], "skipped": [ { "localPath", "reason" } ] }`; a skipped item is TFP0301 in `errors` (exit 1) |
| `lock` | `{ "lock": "none"\|"checkout"\|"checkin", "changes": [PendingChange], "count", "skipped": [...] }` |

Phase 2 commands (`merges`, `annotate`, `folderdiff`, `undelete`, `rollback`, `branch`, `merge`)
add their sections when implemented.

## 4. Open points for Gate A

1. `view /format:json` returns the text content inline as a JSON string; a binary item needs
   `/output` (then `content` is null), otherwise TFP0303 (approved at Gate A).
2. `workspace` in the envelope stays a `name;owner` string as in SPEC.md; the full object is in
   `whoami`/`workfold` data (approved with the draft).
3. Enumeration casing: lower-case (`keepyours`) for stability; the text output keeps the
   server's casing (`KeepYours`) (approved with the draft).
