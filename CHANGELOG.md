# Changelog

One section per release. The CI release step copies the section of the released tag into the
GitHub release notes, so write the section before the release is run. The newest release comes first.

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
