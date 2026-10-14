# Changelog

One section per release. The CI release step copies the section of the released tag into the
GitHub release notes, so write the section before the release is run. The newest release comes first.

## v1.2.1 (2026-10-14)

Everything of v1.2.0. Content is read as UTF-8 by default.

- Content is read as UTF-8 by default: `view`, `difference`, `changeset /diff` and `annotate` decode a file by its BOM,
  else as UTF-8 when the bytes are valid UTF-8, else by the item's server encoding when it is a code page, else by the
  ANSI code page of the user's culture (`windows-1250` for cs-CZ). The server encoding of a BOM-less item is not reliable (older tools recorded `windows-1250`
  for UTF-8 files, the client library records `utf-8` for every BOM-less file), so it no longer decides alone. An
  explicit `difference /encoding:` still wins.

## v1.2.0 (2026-10-13)

Everything of v1.1.0. A read-only protection level.

- Protection level 0: reading only. Nothing is written to the workspace or to disk; the listings, `history`,
  `changeset`, `view` to the console, `difference`, `dir`, `merge /candidate`, `merges`, `folderdiff`,
  `annotate`, `shelvesets`, the listings of `permission`, `workspace` and `property`, `resolvepath`, `docs`,
  `whoami`, `help`, `version`, `login`, `logout` and `protection` run at level 0. `view /output` and
  `property /output` write a file and need level 1, as does every command that changes the workspace.
- `tfp protection set 0` lowers the level without a confirmation; the settings file accepts 0 to 3.
  Levels 1, 2 and 3 are unchanged; the default stays 2.
- The README shows the latest release through a badge instead of a version number.

## v1.1.0 (2026-10-13)

Everything of v1.0.0. The boundary between protection levels 1 and 2 is the server.

- Protection level 1 now allows everything that changes nothing on the server: `add`, `checkout`, `delete`,
  `rename`, `undo`, `get`, `resolve`, `reconcile`, `merge`, `branch`, `rollback`, `undelete`, `unshelve` and the
  `property` writes (`/setvalues`, `/deletevalues`, `/deleteall`) run at level 1; they only pend changes in the
  own local workspace, update its files or restore a shelveset that stays on the server.
- Level 2 keeps the writes to the server: `checkin`, `shelve`, `label`, `unlabel`, `lock`, `unshelve /move`
  (deletes the shelveset), `/lock:checkin|checkout` on the commands that pend changes (the lock is written to
  the server; `/lock:none` does not raise the level), the `workfold` mapping edits and `changeset /comment /notes`.
- Level 3 is unchanged. `whoami`, `protection` and `help` call level 1 "no server writes" instead of "read-only".

## v1.0.0 (2026-10-11)

Everything of v0.3.0. The first stable version: every version-control command of tf.exe is implemented or
answers `TFP0005`/`TFP0006` with the reason, the CI covers the checkin policies and the permission changes of
the test project, and the behavior is verified against tf.exe 18.10 (captures and decompiled sources).

- `/lock:checkin` and `/lock:checkout` on `add`, `checkout`, `delete`, `rename`, `merge`, `branch`,
  `rollback`, `undelete` and `property`: the pended item carries the lock on the server, as in tf.exe.
- `checkin /notes:"Name=Value;..."` (or `@file`): checkin notes are evaluated and stored; `changeset`
  shows them (`Check-in Notes:`, JSON `notes`).
- `TFP0006`: a switch of tf.exe that TfPortable does not support yet answers this code with the reason
  (`checkin /bypass`, `/format:xml`, `workspace /newowner`, ...); `tfp help <command>` marks them.
  A typo stays `TFP0002`.
- `permission` exits 100 when the server applied none of the requested changes (tf.exe's behavior).
- Checkin policies: the "Work Items" and "Changeset Comments" policies as Visual Studio 18 stores them
  (`WorkItemCheckinPolicy`, `CheckForCommentsCheckinPolicy`) are recognized and evaluated; the policies of a
  project are listed once, not once per item; `checkin /format:json` shows the class of every policy.

## v0.3.0 (2026-10-10)

Everything of v0.2.0 plus phase 4: every tf.exe version-control command is now implemented or answers
`TFP0005` with the reason, so no command returns `TFP0003` any more.

- `destroy` (level 3) needs a `/preview` of the same items and switches in the last 10 minutes; without it
  the command ends with `TFP0410`. `/keephistory`, `/stopat`, `/startcleanup`, `/silent`.
- `permission`: the ACL listing at level 1, `/allow`, `/deny`, `/remove`, `/inherit` with `/user`/`/group`
  at level 3; a change the server did not apply is `TFP0501` per item with the server's text.
- `workspace /new`, `/delete` and the edit form (level 3); `property` listing with `/propertynames` and
  `/output`, and the write form of tf.exe (`/setvalues`, `/deletevalues`, `/deleteall`) as a pending
  change at level 2; `resolvepath`; `docs [command] [/url]` with `msdn` as its alias.
- Completions: `workfold /map /unmap /cloak /decloak`, `changeset /comment /notes`, `checkin /shelveset`,
  `unshelve /move`, `shelve /validate`, `lock` and `undo` on another workspace (level 3).
- `proxy`, `configure`, `certificates` and the `git`/`settings` groups answer `TFP0005`.
- JSON messages carry `serverMessage`; `docs/errors.md` lists every error code with its cause and next
  step; `docs/tf-reference/` holds the notes verified from the decompiled tf.exe 18.10.
- Fix: the destroy preview marker was read as stale (or in the future) on every machine outside UTC.

## v0.2.0 (2026-10-09)

Everything of v0.1.0 (phase 1) plus phases 2 and 3.

- Protection levels (`tfp protection`, `tfp protection set <1|2|3>`): 1 read only, 2 everyday work
  (default), 3 everything including irreversible and foreign changes. A command above the level ends
  with `TFP0004` before the tool connects.
- Phase 3, the everyday write path: `checkin` (with `/validate`, `/override:<reason>`, `/force`, the
  `/associate:<work item>` extension and native evaluation of the "Work Items" and "Changeset Comments"
  checkin policies, `TFP0407`/`TFP0409`), `shelve`, `shelvesets`, `unshelve`, `label`, `unlabel`,
  `lock`. `shelve /delete` and `label /delete` need level 3.
- Phase 2, branching as pending changes: `merge` (with `/preview`, `/discard`, `/force`, `/baseless`,
  version ranges), `branch`, `rollback` (`/changeset`, `/toversion`), `undelete`; `merges` and
  `folderdiff` read only; the `annotate` extension (who wrote each line).
- Behavior compared with tf.exe 18.10 captures of every phase: conflict lines,
  `merges /format:detailed`, `difference` option semantics, exit codes of `branches`, `labels`, `lock`
  and `checkin` corrected to the original; the deliberate deviations are listed in `docs/compat.md`
  section 20.
- `docs/CLAUDE.md`: a ready-made section and permission rules for Claude Code in a TFVC project.

## v0.1.0 (2026-10-08)

Phase 1: reading and workspace work. Everything it changes is local and reversible with `undo`.

- Sign-in with a Personal Access Token only (`login`, `logout`, `whoami`); the token lives in the
  Windows Credential Manager, no dialog is ever shown.
- Workspace commands: `status`, `add`, `checkout`/`edit`, `delete`, `rename`, `undo`, `get`, `resolve`,
  `reconcile`, `localversions`, `workfold`, `workspaces` (local workspaces only).
- History and content: `history` (with the search extensions `/comment`, `/changetype`, `/workitem`,
  `/files`, `/contains`), `changeset` (with `/diff`), `view`, `difference`, `dir`, `properties`,
  `labels`, `branches`, `merge /candidate`.
- `help`, `version`; `/format:json` on every command (one JSON document with `ok`, `exitCode`, `data`,
  `errors[]` with stable `TFPxxxx` codes and a next-step hint); tf.exe command names, switches and
  exit codes.
- Later phases answer `TFP0003`.
