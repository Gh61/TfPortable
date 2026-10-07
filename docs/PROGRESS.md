# TfPortable – progress

Read this first in every session. Update it at the end of every session (see CLAUDE.md).

## Status

| # | Milestone | Where | Status | Gate |
| --- | --- | --- | --- | --- |
| 0 | Spec refinement from golden files (`docs/compat.md`, SPEC.md updates) | local | in progress (KICKOFF-LOCAL step 0 done, step 1 next) | user approves SPEC.md |
| 1 | Spike: `tfp whoami` + `tfp status` with PAT, Costura single exe, clean machine | local | not started | user approves architecture |
| 2 | Core: parser, workspace context, errors, JSON envelope, `login`/`logout`/`whoami` | cloud + CI | not started | – |
| 3 | Workspace commands: status, add, checkout, delete, rename, undo, get, resolve, reconcile, localversions, workfold, workspaces | cloud + CI | not started | – |
| 4 | History and content: history (incl. search), changeset (/diff), view, diff, dir, properties, labels, branches, merge /candidate | cloud + CI | not started | – |
| 5 | Integration: CLAUDE.md template for users, compat tests, GitHub release of `tfp.exe` | cloud + CI | not started | user approves release |
| – | Phase 2 commands | – | not planned | separate brief |

Statuses: not started, in progress, blocked, done.

## Next steps

1. `docs/KICKOFF-LOCAL.md` step 1: golden analysis → `docs/compat.md`, `docs/golden-format.md`
   (list the placeholders, including `<ORG>` and `<OTHER_WORKSPACE_ROOT>`, and the manifests in
   `tests/golden/manifests/`).

## Needs the user

- Before milestone 2: GitHub Secrets `TFP_TEST_COLLECTION` and `TFP_TEST_PAT`. The PAT should belong to a
  dedicated user with access only to the test project, or to a separate test organization
  (Azure DevOps PATs cannot be limited to one project).

## Decisions

| Date | Decision | Why |
| --- | --- | --- |
| 2026-10-07 | Executable is `tfp.exe`, project TfPortable, repository `tfportable` | Same name as tf.exe would hide which implementation runs |
| 2026-10-07 | License GPL v3 with an additional permission (section 7) for the Microsoft client libraries; files LICENSE, LICENSE-EXCEPTION, THIRD-PARTY-NOTICES | Microsoft's library license forbids subjecting it to a copyleft license; to be checked by a lawyer before the first release |
| 2026-10-07 | All tool output (messages, hints, prompts) in English; original TFS messages kept verbatim | Universal tool; searchable error codes |
| 2026-10-07 | Local workspaces only; a server workspace returns TFP0204 | Simpler, AI-friendly (changes are detected automatically) |
| 2026-10-07 | Azure DevOps Services and Azure DevOps Server / TFS 2017+; PAT prompt explains both | Same client libraries; PAT exists since TFS 2017 |
| 2026-10-07 | Unknown command exits 2, invalid switch exits 100 | Matches the original (golden `general/unknown-switch`) |
| 2026-10-07 | `history` returns at most 256 entries without `/stopafter` | Protects the agent's context; documented deviation |
| 2026-10-07 | Optional `vc` prefix is skipped (`tfp vc changeset 1` = `tfp changeset 1`) | Existing Claude Code instructions use it |
| 2026-10-07 | Golden tests compare exit codes, items, changeset numbers and order, not texts | Golden files come from a Czech VS; `VSLANG=1033` is ignored by tf.exe |
| 2026-10-07 | Golden redaction (step 0): owner's full name → `<OWNER>` (24 files, 53 occurrences; the first capture run had no owner value, the extra runs already used `<OWNER>`); Azure DevOps organization name → `<ORG>` (3 files, 10 occurrences: `<ORG>.visualstudio.com\DefaultCollection` in `workspaces/detailed`, `workspaces/with-server-workspace`, and `[<ORG>]\...` identities in `permission/project`); local root of the developer's other workspace → `<OTHER_WORKSPACE_ROOT>` (2 files). Nothing else changed; UTF-8 without BOM and CRLF preserved | KICKOFF-LOCAL step 0. The capture scripts replace only the collection URL, so the display form `<org>.visualstudio.com\DefaultCollection` and bare identities slipped through. `<ORG>` and `<OTHER_WORKSPACE_ROOT>` are new placeholders; `docs/golden-format.md` (step 1) must list them |
| 2026-10-07 | The developer's default workspace is named after the computer, so in `workspaces/*` its name shows as `<COMPUTER>`; left as is | Blind replacement by the capture script; harmless for golden tests, which compare items and codes |
| 2026-10-07 | THIRD-PARTY-NOTICES reproduces the "Microsoft .NET Library" license text shipped in the four 20.256.2 packages (identical SHA-256 in all four), references the WebApi.Client EULA by URL, and lists the MIT components (Microsoft.Data.SqlClient, System.IdentityModel.Tokens.Jwt, Newtonsoft.Json, Fody/Costura) | Required by the Microsoft terms (section 3.a.ii) and by the MIT notice clause; all of them end up inside the single tfp.exe |
| 2026-10-07 | `.gitignore` ignores `seed-manifest.json` and `extra-manifest-*.json` anywhere except `tests/golden/manifests/` | Defense in depth: the originals contain the collection URL and local paths |
| 2026-10-07 | Sanitized manifests stored in `tests/golden/manifests/`: in `seed-manifest.json` the collection URL → `<COLLECTION>`, `localRoot` → `<LOCAL_ROOT>`, `projectLocal` → `<WORKSPACE_ROOT>`, `workspace` → `<WORKSPACE>`; the two extra manifests contained nothing personal and are verbatim copies. Developer confirmed that the redaction found everything (no other workspaces, projects or hosts to hide) | KICKOFF-LOCAL step 0; the originals stay outside the repo |
| 2026-10-07 | Reaffirmed by the developer: every tfp.exe message is English even though the golden files are Czech; golden text is never copied or translated into tfp. Rule added to CLAUDE.md | The tool is universal; golden files only document behavior of a Czech tf.exe |

## Open questions

- `merges /extended` (golden `merges/extended`) exits 100: invalid switch combination or a bug? Read the file.
- `history` of a non-existent path exits 0 and `add` of an ignored file exits 0: confirm and record in `docs/compat.md`.
- Does `tf rollback /changeset:` without an item spec behave as SPEC.md assumes? See golden `rollback/*`.
- Which exit code and message does the original use for a locked file, an out-of-date checkin and conflicts? See golden `checkin/*`, `get/conflict`, `resolve/*`.

## Golden files – facts

- Captured on 2026-10-07 with tf.exe 18.10.12215.6 (Visual Studio 18 Community, Czech UI).
- Redirected tf.exe output is in the ANSI code page (windows-1250); the capture scripts decode it and store UTF-8.
- Placeholders: `<COLLECTION>`, `<WORKSPACE>`, `<OWNER>`, `<EMAIL>`, `<COMPUTER>`, `<USER>`, `<LOCAL_ROOT>`,
  `<WORKSPACE_ROOT>`, and `_B` / `_S` variants for the second and the server workspace.
- `index.json` (read-only and pending-change cases) and `index-extra-<run>.json` (server-writing cases)
  list every case with its command and exit code. Two extra runs exist: `index-extra.json` (run
  2610071920, 76 cases, no run suffix in the file name) and `index-extra-2610071925.json` (41 cases).
  `index.json` has 86 cases; 205 files in total.
- Test project: seeded by `tools/seed-tfvc-test-repo.ps1` (changesets C75–C83), extended by
  `tools/capture-tf-golden-extra.ps1` (from C84, including history padding).

## Session log

### 2026-10-07 – local session 1, KICKOFF-LOCAL step 0 (Claude Code, Windows)

- Done: repository checked (LICENSE is GPL v3, LICENSE-EXCEPTION present); created THIRD-PARTY-NOTICES
  (license texts taken from the NuGet packages themselves) and `.gitignore`; scanned `tests/golden/` for
  personal data and redacted it (see "Decisions"); backup of the unredacted files in the scratchpad.
  Developer provided the manifests (sanitized copies in `tests/golden/manifests/`) and confirmed the
  redaction; first commit made.
- Next: step 1 (`docs/compat.md`, `docs/golden-format.md`).
- Blockers: none.

### 2026-10-07 – specification and golden files (claude.ai conversation)

- Done: SPEC.md, license decision, test project seeded, golden files captured (basic, pending changes,
  server-writing scenarios, long history).
- Next: local session per `docs/KICKOFF-LOCAL.md`.
- Blockers: none.
