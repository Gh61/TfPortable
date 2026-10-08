# TfPortable vs. tf.exe – compatibility notes

Result of KICKOFF-LOCAL step 1: every golden case in `tests/golden/index.json`,
`index-extra.json` (run 2610071920) and `index-extra-2610071925.json` compared with SPEC.md.
Golden files were captured with tf.exe 18.10.12215.6 (Visual Studio 18, Czech UI); see
`docs/golden-format.md` for the file format and `docs/PROGRESS.md` for the decisions log.

Legend for the "Decision" column:

- **follow** – TfPortable behaves like the original (exit code, streams, shape of output).
- **deviate** – TfPortable deliberately differs; the reason is given. Golden tests must
  account for it.
- **n/a** – command is out of scope (`TFP0003 NotSupported`) or phase 2; the row documents the
  original for later.

Message texts are never compared (Czech golden vs. English tfp). Czech change-type words used
below: `přidat` add, `upravit` edit, `odstranit` delete, `přejmenovat` rename, `větev` branch,
`sloučit` merge, `obnovit` undelete, `vrátit zpět` rollback, `uzamknout` lock,
`odstranit, přejmenování zdroje` delete + source rename.

## 1. Cross-cutting findings

### 1.1 Exit codes observed in the original

| Situation | Command (case) | Exit | Message stream |
| --- | --- | --- | --- |
| Unknown command | `general/unknown-command`, `annotate/attempt` | 2 | stderr |
| Unknown switch | `general/unknown-switch` | 100 | stderr |
| Switch not allowed for this command/mode | `merges/extended`, `workfold/unmap` (`/collection`) | 100 | stderr |
| Unsupported value of a switch | `history/format-xml` (TF10120) | 100 | stderr |
| Not inside a workspace | `general/outside-workspace` | 100 | stderr |
| Item not found: `add` | `add/not-found` | 1 | stderr |
| Item not found: `delete` | `delete/not-found` | 1 | stderr |
| Item not found: `rename` | `rename/not-found` | **100** | stderr |
| Item not found: `view` | `view/not-found` | 1 | stderr |
| Item not found: `dir` | `dir/not-found` | 1 | **stdout** |
| Item not found: `history` | `history/not-found` | **0** | **stdout** |
| Changeset not found | `changeset/not-found` (TF14019) | 100 | stderr |
| Item not found: `undelete` | `undelete/not-found` (TF14022) | 1 | stderr |
| Item not found: `label` | `label/not-found-item` (VS403405) | 100 | stderr |
| Item not in label: `unlabel` | `unlabel/not-labeled` (TF203060) | 1 | stderr |
| Identity not found | `history/user-not-found` (TF14045) | 100 | stderr |
| Shelveset not found | `shelvesets/not-found`, `unshelve/not-found` | 100 | stderr |
| Item already versioned | `add/already-versioned` | 1 | stderr |
| Nothing to undo | `undo/nothing-to-undo` | 1 | stderr |
| No workspace matches | `workspaces/other-computer` | 1 | **stdout** |
| `get` produced a conflict | `get/conflict` | 1 | stderr (folder header on stdout) |
| `resolve` with conflicts left (no `/auto`, `/preview`, failed AcceptMerge) | `resolve/list*`, `resolve/accept-theirs-preview`, `resolve/accept-merge` | 1 | stderr (AcceptMerge summary on stdout) |
| `resolve` with nothing to resolve | `resolve/no-conflicts` | 0 | stdout |
| `merge` produced a conflict | `merge/conflict` | 1 | stderr |
| Checkout lock in a local workspace | `lock/checkout-lock` (TF400022) | 1 | stderr |
| `checkin` with nothing pending / blocked by lock / out of date | `checkin/nothing-pending`, `checkin/blocked-by-lock`, `checkin/out-of-date` (TF10141) | 100 | stderr |
| Files differ (`difference`) | `difference/*` | 0 | stdout |
| Empty result (clean status, no candidates, up to date) | `status/clean`, `get/with-pending`, `merge/candidate` | 0 | stdout |

**Decision: follow.** The original is not consistent (not found is 1, 100 or 0 depending on
the command), but scripts and habits depend on these codes, and golden tests compare them.
TfPortable returns the exit code of the original for every case in this table; the `TFPxxxx`
code in the message stays the same regardless of the exit code. SPEC.md's error catalog gives
the *default* exit code of each TFP code; this table is the list of per-command overrides
(step 2 updates SPEC.md accordingly).

Only two exit codes are deliberate extensions and stay as specified:

| Situation | Original | TfPortable | Why |
| --- | --- | --- | --- |
| PAT missing / rejected / Credential Manager error | 100 (sign-in dialog or TF30063) | **110** | The AI must tell the user to run `tfp login`; a distinct code makes that unambiguous |
| Not inside a workspace, path not mapped, server workspace | 100 (`general/outside-workspace`) | **120** | Same reason: the fix is `/collection` or `cd`, not a retry |

### 1.2 stdout vs. stderr

The original prints per-item errors and conflicts on stderr, and "empty result" messages
(clean status, no history, no matching items for `dir`, no workspaces, nothing to resolve) on
stdout, even when the exit code is 1. **Decision: follow** in text mode. In `/format:json`
mode the whole result is one JSON document on stdout and stderr stays empty except for
`/verbose` diagnostics.

### 1.3 Shape of output

| Topic | Original | Decision |
| --- | --- | --- |
| Dates | Short `7. 10. 2026` in tables, long `středa 7. října 2026 19:07:27` in detail blocks (local time, no offset) | **deviate**: ISO 8601 with offset everywhere (`2026-10-07T19:07:27+02:00`), as in SPEC.md |
| Table width | Fitted to 79 columns; the last column (comment) is cut (`history/short-version`: `Fix null ch`) | **deviate**: columns are kept, but the last column is never truncated. Truncation loses information the AI needs and the layout of the other columns does not change. (Approved at Gate A, 2026-10-07.) |
| Grouping of local items | Items are grouped under their folder: `Main\src:` + names, relative to the current directory for `add`, `checkout`, `delete`, `rename`, `undo`, `unshelve`, `shelve`, `checkin`; absolute for `get`, `localversions`; when the folder equals the current directory the header line is omitted (`checkout/server-workspace`, `undo/server-workspace`) | **deviate**: paths are printed absolute (SPEC.md "Cesty ve výstupu jsou vždy absolutní") and the folder header is always printed, also when the folder is the current directory (SPEC.md "Tvar textového výstupu"; milestone 3 follows SPEC.md here, an earlier draft of this row said the omission rule was kept) |
| Server items | `$/...` paths; deleted items carry a deletion id `;X18`; versions as `;C83` | follow |
| Encoding | Output in the ANSI code page; `view` of a BOM-less UTF-8 file produced mojibake (`view/czech-name`) because the bytes were written raw | **deviate**: UTF-8 on stdout always; `view` decodes the item by its server encoding (`utf-8`, `windows-1250`, …) and re-encodes to UTF-8. `view/czech-name` is a bug of the original, the golden text is not reproduced |
| Deprecation warning | `properties` prints "properties is deprecated, use tf info" on stderr; `info` does not | **deviate**: no warning for either alias |
| Banner | `tf`, `tf help`, `tf help status` print only the copyright banner and point to `tf vc help` | **deviate**: `tfp` and `tfp help` print the command catalog, `tfp help <command>` prints the command's switches; exit 0 |

## 2. General (`tests/golden/general`)

| Case | Original | SPEC.md | Decision |
| --- | --- | --- | --- |
| `no-arguments`, `help`, `help-status` | exit 0, banner on stdout | `help [<command>]` | deviate (useful help, see 1.3) |
| `unknown-command` | exit 2, stderr | TFP0001, exit 2 | follow |
| `unknown-switch` | exit 100, stderr | TFP0002, exit 100 | follow |
| `vc-prefix` | `tf vc status` works | `vc` prefix skipped | follow |
| `dash-switches`, `short-switches` | `-recursive -noprompt`, `-R -I` work | same | follow |
| `outside-workspace` | exit 100, stderr "cannot determine the source control server" | TFP0201, exit 120 | deviate (exit 120, see 1.1) |

Milestone 2 (2026-10-08): all nine cases are replayed by `GeneralGoldenTests`; the six that need no
server run on every CI build, the three `status` cases inside the fixture's workspace when the
secrets exist. Further details of the parser that the golden files do not show:

| Topic | Original | TfPortable |
| --- | --- | --- |
| Unknown switch value (`status /format:xml`) | TF10120, exit 100 | TFP0002, exit 100, the hint lists the valid values |
| Switch of another command (`status -X`) | "option not allowed", exit 100 | TFP0002, exit 100 (short forms are resolved per command) |
| Out-of-scope command with switches (`checkin /comment:x`) | runs | TFP0003, exit 100; the switches are not validated |
| Command of a later milestone (`add` before milestone 3, `annotate` before phase 2) | runs (`annotate`: exit 2) | TFP0003, exit 100 with the phase in the message; `help` marks them "(planned)" |
| `tf git ...`, `tf settings ...` | other command groups | TFP0003, exit 100 |
| `@file` arguments | supported | supported: UTF-8, one argument per line, blank lines skipped, relative to the current directory; a missing file is TFP0002 |
| `/login:user,pat` | signs in for the run | used for the run only, never stored, with a warning that the token stays in the shell history |
| Ctrl+C in the PAT prompt | – | TFP0101, exit 110 (the prompt is a TfPortable feature) |

## 3. Workspace context: `workspaces`, `workfold`, `workspace`

| Case | Original | SPEC.md | Decision |
| --- | --- | --- | --- |
| `workspaces/list` | exit 0; header `Collection: <url>/`; table Workspace, Owner, Computer, Comment | `workspaces` phase 1 | follow (ISO dates not involved) |
| `workspaces/detailed` | exit 0; blocks separated by `====` lines; fields Workspace, Owner, Computer, Comment, Collection (`<org>.visualstudio.com\DefaultCollection` display form), Permission, Location (`Místní`/`Server`), File time; then `Working folders:` list | `/format:detailed` | follow; Collection is printed as the normalized URL instead of the display form (deviate, trivial) |
| `workspaces/other-computer` | exit 1; message on stdout | not specified | follow: exit 1, message on stdout, JSON `data.workspaces: []` |
| `workspaces/with-server-workspace` | lists server workspaces too, Location `Server` | only local workspaces are *used* | follow: listing shows all workspaces including server ones; only commands that need a workspace refuse server ones (TFP0204) |
| `workfold/current` | exit 0; `====` line, `Workspace: <name> (<owner>)`, `Collection: <url>/`, mappings ` $/path: <local>` | `workfold` phase 1 | follow |
| `workfold/list-cloaked` | cloaked mapping printed as ` (skrytá) $/path:` | – | follow (`(cloaked) $/path:`) |
| `workfold/cloak`, `decloak`, `map`, `map-already-mapped` | exit 0, no output (mapping an already mapped folder is silent) | phase 2 | n/a; follow when implemented |
| `workfold/unmap` | exit 100 "option collection is not allowed" (`/unmap` rejects `/collection`) | phase 2 | n/a; TfPortable will accept `/collection` for every subcommand (extension, no semantic change) |
| `workspace/new-local`, `new-server`, `delete-local`, `delete-server` | exit 0, no output | out of scope (TFP0003) | n/a |

## 4. `status`

| Case | Original | SPEC.md | Decision |
| --- | --- | --- | --- |
| `clean`, `clean-detailed`, `server-path`, `server-workspace` | exit 0; "There are no pending changes." on stdout | – | follow (message on stdout, exit 0; JSON `data.changes: []`) |
| `pending` (brief) | table `File name / Change / Local path`, items grouped under their server folder (`$/…/Main`), blank line between groups, trailer `Changes: 9`; renamed item shown under its new name | brief format | follow; local paths absolute |
| `pending-detailed` | one block per item: `$/path;C83` (no version for adds), User, Date, Lock, Change, Workspace, Source item (renames), Local item `[<COMPUTER>] <path>`, File type (encoding, `Binary`); trailer `Changes: 9` | detailed format | follow; ISO date |
| `detected-adds`, `candidates`, `after-undo`, `after-promote` | section `Detected changes:` between dashed lines, same table, candidates `přidat` (new file) and `odstranit` (file deleted on disk); trailer `Changes: 0, detected changes: 2` | candidates "zvlášť označené" | follow |
| `all-users` (`/user:*`) | extra column `User`; lock shown as `* uzamknout`; warning on stderr that local-workspace changes are not shown with `/user` unless the workspace is given | `/user:` | follow, including the warning |
| `shelveset` | column `Shelveset` replaces `Local path` | `/shelveset:` | follow |
| `with-conflict`, `after-resolve`, `after-checkin-conflict` | a conflicting item is still listed as plain `upravit`; `status` does not show conflicts | – | follow; `tfp resolve` lists conflicts |
| `pending-merge`, `merge-conflict`, `after-merge-resolve` | combined change types `sloučit, větev`, `sloučit, upravit` | – | follow (comma-separated change list) |
| `pending-branch`, `pending-undelete`, `pending-rollback*` | `větev`, `obnovit`, `upravit, vrátit zpět`, `přejmenovat, upravit, vrátit zpět` | phase 2 | follow when implemented |
| `server-workspace-pending` | works in the original | TFP0204 | deviate (local workspaces only) |

## 5. Pending changes: `add`, `checkout`/`edit`, `delete`, `rename`, `undo`

| Case | Original | SPEC.md | Decision |
| --- | --- | --- | --- |
| `add/folder-recursive` | exit 0; grouped list: `Main:` + `_golden`, then `Main\_golden:` + files | – | follow |
| `add/already-versioned` | exit 1; stderr "item already exists" | TFP0401, exit 1 | follow |
| `add/ignored-file` | exit 0; a file matched by `.tfignore` (`bin`) is **added** when named explicitly | "respektuje .tfignore" | follow: `.tfignore` applies to wildcard and `/recursive` expansion only; an explicit path is always added (as in the original). The seed script notes that `add Main /recursive` returned exit 1 and reported the ignored `bin` folder as skipped; no golden file exists for that case (open question) |
| `add/not-found` | exit 1; stderr "<path>: No matching files." | TFP0301 | follow (exit 1 for `add`) |
| `checkout/file`, `edit-alias` | exit 0; grouped list | alias `edit` | follow |
| `checkout/locked-by-other` | exit 0; a *checkin* lock held elsewhere does not block checkout | – | follow |
| `checkout/server-workspace` | works; no folder header when the folder is the cwd | TFP0204 | deviate (TFP0204); header rule noted in 1.3 |
| `delete/file` | exit 0; grouped list; the local file is deleted | – | follow |
| `delete/not-found` | exit 1; stderr "could not be found in your workspace" | TFP0301 | follow (exit 1) |
| `rename/file` | exit 0; grouped list showing the **new** name | – | follow |
| `rename/not-found` | exit **100**; stderr, same text as `delete/not-found` | TFP0301 | follow (exit 100 for `rename`) |
| `undo/recursive` | exit 0; grouped `Undoing <change>: <name>` lines; a renamed item is undone under its new name | – | follow |
| `undo/nothing-to-undo` | exit 1; stderr "No pending changes were found for <path>" | – | follow |
| `undo/server-workspace` | works | TFP0204 | deviate |

## 6. `get`

| Case | Original | SPEC.md | Decision |
| --- | --- | --- | --- |
| `preview`, `up-to-date` (clean workspace) | exit 0; `<LOCAL_ROOT>:` + `Getting TfsPortableTestRepo` (the root folder item had never been fetched); otherwise silent | `/preview` | follow |
| `with-pending` (nothing new on the server) | exit 0; "All files are up to date." on stdout | – | follow |
| `conflict` (server edit vs. local pending edit) | exit 1; stdout prints the folder header only, stderr `Conflict c.txt: cannot get because you have a conflicting edit`; a conflict is registered | TFP0403, exit 1 | follow |
| `specific-version`, `back-to-latest`, `force`, `all` | exit 0; grouped `Replacing <name>` lines under absolute folder headers | `/version` `/force` `/all` | follow |
| `date-version-preview` (`/version:D2025-10-07`, before the folder existed) | exit 0; `Deleting <absolute path>` lines, no folder grouping, children before the folder | `/preview` | follow (deletions are listed flat) |
| `after-cloak` | exit 0; `Deleting` lines for the cloaked subtree | phase 2 | follow when implemented |
| `server-workspace` | `Getting` lines grouped | TFP0204 | deviate |
| Writable file without pending change | no golden case | TFP0402, exit 1 | keep SPEC.md; capture a golden case later |

## 7. `resolve` and `reconcile`

| Case | Original | SPEC.md | Decision |
| --- | --- | --- | --- |
| `resolve/no-conflicts` | exit 0; "There are no conflicts to resolve." on stdout | – | follow |
| `resolve/list`, `list-preview`, `merge-conflict-list` | exit 1; each conflict on stderr: `<relative path>: You have a conflicting pending change.` / `Both source and target have changes.`; nothing on stdout | "bez /auto jen vypíše konflikty" | follow (exit 1 while conflicts remain, list on stderr); JSON `data.conflicts[]` with type |
| `resolve/accept-theirs-preview` | exit 1; the conflict is listed, nothing resolved | `/preview` | follow |
| `resolve/accept-yours` | exit 0; stdout `Item <path> is resolved as KeepYours.` | `/auto:AcceptYours` | follow; resolution names are the server's (`KeepYours`, `TakeTheirs`, `AcceptMerge`) |
| `resolve/accept-theirs` | exit 0; `resolved as TakeTheirs.` then `Replacing README.md` under the folder header | `/auto:AcceptTheirs` | follow |
| `resolve/accept-merge` (both sides changed the same line) | exit 1; stdout `<path>: local: 0, server: 0, both: 0, conflicting: 1.`; stderr `Both source and target have changes.`; conflict stays | `/auto:AcceptMerge` | follow |
| `reconcile/promote-preview`, `promote` | exit 0; "No matching pending changes were found."; `status` afterwards still shows the two candidates, nothing was promoted | `/promote` | follow the Microsoft docs when implementing; verify in an integration test why nothing was promoted (open question: `/adds` `/deletes` may be required) |
| `reconcile/clean-preview`, `clean` | exit 0; grouped `Deleting untracked.txt`; the candidate delete (`c.txt` missing on disk) is not reported | `/clean` needs `/preview` or `/noprompt` (TFP0405) | follow output; TFP0405 is an intended deviation (the original ran with `/noprompt`) |

Milestone 3 (2026-10-08) notes on §3–§7, found while implementing against the client library:

| Topic | Original | TfPortable |
| --- | --- | --- |
| `undo` with nothing to undo | exit 1, stderr | TFP0301 per item (exit 1); `data.notFound` lists the items |
| `add`/`checkout`/`delete` of a missing item | exit 1, stderr | TFP0301 per item (exit 1), the command continues with the other items; `rename` stops with TFP0301 and exit 100 as the original |
| `add` of an item already under version control | exit 1 | TFP0401 per item (exit 1) |
| `.tfignore` | applies to `/recursive` and wildcards, not to an explicit path | the same: the library's exclusion filter is enabled only when the add expands folders or wildcards |
| `status /user` | warning about local workspaces on stderr, `*` before changes of other workspaces | the same (English warning with a hint; `* ` marks changes whose workspace is not the current one) |
| `status /format:detailed` | Czech labels, long date, `[<COMPUTER>] path` | English labels, ISO date, `File type: Binary` for binary items, `Source item` for renames |
| `get` | `Getting`/`Replacing` grouped, `Deleting` flat | the same; a writable file without pending change is TFP0402 (exit 1) with the `/overwrite` hint; conflicts TFP0403 (exit 1) with the conflict line on stderr and the folder header on stdout |
| `resolve /auto:AutoMergeForced` | accepts the merge with conflict markers | `AcceptMergeWithConflicts` of the client library |
| `reconcile /promote` | promoted nothing in the golden run | pends the detected adds and deletes (`/adds`, `/deletes` restrict), `/preview` lists them as `Would promote <change>: <name>`; `/exclude` patterns match the file name or the whole path |
| `reconcile /clean` | deletes untracked files (ran with `/noprompt`) | the same list (`Deleting <name>` grouped), TFP0405 without `/preview` or `/noprompt`; `/ignore`, `/unmapped`, `/diff` are accepted without effect (the detection already honors `.tfignore`) |
| `workspaces /format:detailed` | `Collection` in the display form, `File time: Current` | normalized collection URL, `Last access` (ISO) instead of the file-time setting |
| `workfold <folder>` | the matching mapping | the same; an unmapped folder is TFP0203 |
| `localversions` | leading blank line | no leading blank line; the rest identical |
| Sorting of listings | culture order (`_golden` before `README.md`) | invariant-culture, case-insensitive order |

## 8. `localversions`, `properties`/`info`

| Case | Original | SPEC.md | Decision |
| --- | --- | --- | --- |
| `localversions/recursive`, `after-old-get` | exit 0; leading blank line; absolute folder headers; `name;C75`; trailer `Items: 19` | brief | follow (drop the leading blank line) |
| `localversions/detailed` | full paths `path;C75`, trailer | `/format:detailed` | follow |
| `properties/local-file`, `info-alias`, `czech-name`, `server-version`, `locked-file`, `server-workspace` | exit 0; two sections `Local information:` (Local path, Server path, Changeset, Change, Type) and `Server information:` (Server path, Changeset, Deletion ID, Lock, Lock owner, Last modified, Type, File type, Size); folders have no File type/Size; `/version:C75` shows the local version in the first section and the requested version in the second; lock shows `Change: uzamknout`, `Lock: vrátit se změnami` (checkin), owner | `properties` (`info`) | follow; ISO date; no deprecation warning (1.3) |
| `properties/recursive` | blocks separated by a blank line | `/recursive` | follow |

## 9. `history`

| Case | Original | SPEC.md | Decision |
| --- | --- | --- | --- |
| `brief` (folder, `/recursive`) | exit 0; table Changeset, User, Date, Comment; newest first | brief | follow; ISO date; comment not truncated (1.3) |
| `renamed-file`, `short-version`, `vc-dash`, `itemmode`, `slotmode`, `long-*` (single file) | the table gains a `Change` column (change type of that item in the changeset) | – | follow |
| `detailed-stopafter`, `detailed-with-merge` | dashed separator line, then the same block as `changeset`; ends with an extra blank line | `/format:detailed` | follow |
| `range` (`C76~C78`) | inclusive on both ends | versionspec `<od>~<do>` | follow |
| `date-range` (`D2026-10-06~T`) | works | – | follow |
| `short-version` (`/v:C81`) | `/v:` alias works; shows history up to and including C81 | `/v:` | follow |
| `sort-ascending` + `/stopafter:5` | oldest 5 | `/sort:` | follow |
| `not-found` | exit **0**; "No history entries were found for the item and version combination." on stdout | TFP0301 (exit 100) | follow (exit 0, message on stdout, JSON `data.changesets: []`) – an unknown path is an empty result for `history` |
| `user-not-found` | exit 100; TF14045 on stderr | – | follow |
| `format-xml` | exit 100; TF10120 "value xml is not supported for option format" | `/format:json` extension | follow for `xml`; `json` is accepted (TFP0002 for anything else) |
| `renamed-file` (default) vs. `itemmode` vs. `slotmode` | default and `/slotmode` show only C78 (the rename) for `TextHelper.cs`; `/itemmode` follows the item through the rename (C78, C77, C75) | `/slotmode` `/itemmode` | follow: default is slot mode |
| `long-default` | 301 entries without `/stopafter` (the original has no limit) | max 256 + notice | **deviate**: TfPortable returns the newest 256 entries and a trailer saying how to get more. Golden test compares the first 256 entries |
| `long-stopafter-256` | exactly 256 entries (C393…C138) | – | follow |
| `long-project-recursive` | 320 entries incl. C74 "Created team project folder" | – | deviate (256 limit) |

## 10. `changeset`

| Case | Original | SPEC.md | Decision |
| --- | --- | --- | --- |
| `initial`, `edit`, `rename`, `delete`, `branch`, `merge`, `latest`, `vc-dash`, `merge-many-items` | exit 0; block `Changeset: N`, `User:`, `Date:`, blank, `Comment:` + two-space indented text, blank, `Items:` + two-space indented `<change> <server path>`; change types padded to the widest one in the list; deleted items carry `;X17`; sections for check-in notes, policy warnings and work items are omitted when empty | `changeset <n>`, `/latest` | follow; ISO date; `Work items:` section when present |
| `not-found` | exit 100; TF14019 on stderr | TFP0301, exit 100 | follow |
| `edit-comment` (`/comment:` edits the changeset) | exit 0; block + "Changeset 84 has changed." | not supported | deviate: TFP0003 for `/comment:` (write to the server) |
| `after-comment-edit` | the comment edit is visible | – | n/a |
| `changeset <n> /diff` | no golden case (extension) | unified diff of the changeset | extension, see SPEC.md |

## 11. `view`

| Case | Original | SPEC.md | Decision |
| --- | --- | --- | --- |
| `server-version`, `semicolon-version`, `changeset-no-c`, `label-version`, `local-path`, `forward-slashes`, `deleted-file` (`/version:C75` of a later deleted file), `golden-label`, `shelveset` | exit 0; file content on stdout, nothing else | `view` | follow; `/output:<file>` extension |
| `utf8-bom`, `cp1250` | content shown correctly in the console code page | UTF-8 output | follow (re-encoded to UTF-8) |
| `czech-name` (BOM-less UTF-8) | mojibake, bytes written raw | – | deviate: correct UTF-8 (bug of the original) |
| `not-found` | exit 1; stderr "<path>: No matching files." | TFP0301 | follow (exit 1 for `view`) |

## 12. `difference`/`diff`

| Case | Original | SPEC.md | Decision |
| --- | --- | --- | --- |
| `server-unified` (`/version:C75~C81`) | exit 0 although the files differ; block `File: <local relative path>`, `===` line, `--- Server: Program.cs;75`, `+++ Server: Program.cs;81`, hunks, `===` line | unified | follow (exit 0 when different; GNU-style exit 1 is not used) |
| `server-brief` | exit 0; `<path>: files differ` | `/format:brief` | follow |
| `no-local-change` | exit 0; only `Comparing local with latest: <path>` | – | follow |
| `local-edit` | `edit: <absolute local path>` line, then the block with `--- Server: $/path;C81` and `+++ Local: <relative path>` | – | follow (paths absolute) |
| `shelveset` | per item: `<change>: $/path;C87` then the block; adds show `--- [no source file]` and hunk `@@ -1,0 +1,1 @@` | `/shelveset:` | follow |
| `two-branches` (two server paths) | exit 0; both sides labelled `Local:` with mapped relative paths | – | follow, but label server items as `Server:` with their server path (deviate, trivial: the original's labels are misleading) |
| `context-format` (`/format:context`) | supported by the original | only `unified` and `brief` | deviate: TFP0002 for `context` (not needed by the AI; can be added later) |

## 13. `dir`

| Case | Original | SPEC.md | Decision |
| --- | --- | --- | --- |
| `folder`, `recursive`, `folders-only`, `at-version`, `deleted`, `golden-deleted` | exit 0; `$/path:` header per folder, folders prefixed `$` first, then files, ordinal-ignore-case order (`obsolete.md` before `Příručka.md`); deleted items `;X18` with `/deleted`; trailer `Items: N` (the listed folder itself is not counted) | `dir` | follow |
| `not-found` | exit 1; "No items match $/path." on **stdout** | TFP0301 | follow (exit 1, stdout) |

## 14. `labels`, `branches`, `merge /candidate`, `merges`

| Case | Original | SPEC.md | Decision |
| --- | --- | --- | --- |
| `labels/all` | exit 0; table Label, Owner, Date | `labels` | follow; ISO date |
| `labels/detailed`, `golden-detailed` | leading blank line; fields Label, Scope, Owner, Date, Comment; table Changeset, Item (deleted items `;X18`) | `/format:detailed` | follow (drop the leading blank line) |
| `branches/main`, `after-feature` | exit 0; `>> $/path <<` then one tab-indented line per branch: `$/path<TAB>Branched from version 75` | `branches` | follow |
| `merge/candidate`, `candidate-before-merge` | exit 0; history-style table; `*` after a changeset number marks a partial candidate (`87*`, `84*`) | `merge /candidate` phase 1 | follow, including the `*` marker (JSON `partial: true`) |
| `merges/main-to-release`, `showall` | exit 0; table Changeset, Merged in changeset, Author, Date; numbers right-aligned; `*` marks partial merges | phase 2 | n/a; follow when implemented |
| `merges/extended` | exit 100 "option extended is not allowed" – tf.exe 18.10 does not accept `/extended` | `/extended` listed | resolve open question: not a bug, the switch is rejected; drop `/extended` from the phase 2 list |

Milestone 4 (2026-10-08) notes on §9–§14, found while implementing against the client library:

| Topic | Original | TfPortable |
| --- | --- | --- |
| `history` of a single file | Change column | the same; the item is looked up first (`GetItem`), which also turns an unknown path into the empty result |
| `history` search (`/comment`, `/changetype`, `/workitem`) | – | client-side filter over at most 5000 changesets; `/stopafter` counts matches; the JSON reports `scanned` and `truncated` names the changeset to continue from; `/workitem` fetches the work items per candidate changeset |
| `changeset /diff` | – | every file against its previous version (`C<n-1>` of the same or the source item); adds against nothing, deletes to nothing, binary items as "Binary files differ." |
| `view` encoding | console code page | the server encoding (`Item.Encoding`) decodes the bytes, a BOM wins; output is UTF-8 |
| `view /recursive` | each file preceded by its path | `$/path;C<n>:` before each file |
| `difference` labels | `--- Server: Program.cs;75` / `+++ Server: Program.cs;81` for two versions, `--- Server: $/path;C81` / `+++ Local: <path>` for a local edit, `Local:` for two server paths | the same shapes; two server paths are labelled `Server:` (compat §12); the `File:` line names the mapped local path when the workspace knows it, else the server path; the diff is TfPortable's own Myers line diff with three lines of context (hunk boundaries may differ from the original in rare cases); an added file is `@@ -0,0 +1,N @@` where the original prints `@@ -1,0 +1,N @@` |
| `difference` with no pending change | `Comparing local with latest: <path>` | the same, after comparing the file with the latest server version |
| `dir` sorting | ordinal-ignore-case, folders first | the same; the queried folder heads the listing and is not counted |
| `properties` outside a workspace | needs a workspace | works with a server path: only the `Server information:` section |
| `labels` with extra labels on the server | – | the golden brief listing compares shape only (later golden runs added labels) |
| `branches` of a path that is not branched | error | TFP0301 (exit 100) |
| `merge /candidate /format:detailed` | blocks | the `changeset` block per candidate, `*` after the number of a partial one |

## 15. Phase 2 commands (reference only)

| Case | Original | Decision |
| --- | --- | --- |
| `merge/preview`, `merge/all` | exit 0; lines `merge, branch: $/src;C84 -> $/dst`, `merge, edit: $/src;C83~C83 -> $/dst;C80` | follow when implemented (pending changes only) |
| `merge/conflict` | exit 1; stderr `Conflict (merge, edit): $/src;C92~C92 -> $/dst;C93`; a pending merge with a conflict is created | follow |
| `branch/create` | exit 0; prints the target name and the branched tree grouped like `add` | follow |
| `undelete/file` (`;X19`) | exit 0; grouped list | follow |
| `undelete/not-found` | exit 1; TF14022 on stderr | follow |
| `rollback/changeset` (`/changeset:C85`, **no item spec**) | exit 0; `edit, rollback: $/a.txt;C85~C85 -> $/a-renamed.txt;C87` | resolves open question: works without an item spec |
| `rollback/to-version` (`/toversion:C84 <folder> /recursive`) | exit 0; one line per item incl. `undelete, rollback` | follow |
| `shelvesets/list` | exit 0; table Shelveset, Owner, Comment | follow |
| `shelvesets/detailed` | `====` line, fields Shelveset, Owner, Date, Comment (comment on its own indented line) | follow; ISO date |
| `shelvesets/not-found` | exit 100; stderr | follow |
| `unshelve/basic` | exit 0; grouped `Unshelving <change>: <name>` | follow |
| `unshelve/not-found` | exit 100; stderr (English TF text) | follow |
| `folderdiff/main-release` | exit 0; three `====` sections (only in source, only in target, different content), pairs `src - ` + tab-indented target, summary line with counts | follow |
| `annotate/attempt` | exit 2 "Unrecognized command: annotate" – tf.exe has no `annotate` | TfPortable adds it as an extension |

## 16. Out-of-scope commands (TFP0003, reference only)

| Case | Original |
| --- | --- |
| `checkin/*` success | grouped `Checking in <change>: <name>` lines, then `Changeset #85 checked in.` |
| `checkin/nothing-pending` | exit 100; stderr "No pending changes match the items." + "No files checked in." |
| `checkin/blocked-by-lock` | exit 100; stdout lists the item, stderr English "item is locked in workspace X;owner" + "No files checked in." |
| `checkin/out-of-date` | exit 100; stderr `Conflict: A newer version of … exists on the server.` + TF10141 |
| `lock/checkin-lock`, `lock/remove` | exit 0; grouped list |
| `lock/checkout-lock` | exit 1; TF400022 checkout locks are not supported in local workspaces |
| `label/create` | exit 0; "Created label X@$/scope" |
| `label/not-found-item` | exit 100; VS403405 |
| `unlabel/item` | exit 0; "Updated label X@$/scope" |
| `unlabel/not-labeled` | exit 1; TF203060 |
| `shelve/create` | exit 0; grouped `Shelving <change>: <name>`, then "Shelveset X created." |
| `shelve/delete` | exit 0; silent |
| `permission/project` | exit 0; `====` block per server item with identities and Allow/Deny/inherited lists |
| `destroy/preview` | exit 0; "Destroyed: $/path" even in preview. TfPortable refuses `destroy` entirely, including `/preview` |
| `workspace/*` | exit 0; silent |

## 17. Open questions raised by this analysis

Recorded in `docs/PROGRESS.md`:

1. `add <folder> /recursive` with items skipped by `.tfignore`: the seed script observed exit 1,
   but there is no golden file. Follow the original (exit 1, skipped items listed) until captured.
2. `reconcile /promote <folder> /recursive` promoted nothing in the golden run. Check whether
   `/adds` and `/deletes` are required, in the integration test of milestone 3.
3. Gate A (2026-10-07): the two output deviations proposed here – no truncation of the last table
   column, and absolute local paths in grouped listings – were approved by the developer.
4. No golden case exists for TFP0402 (writable file not overwritten by `get`) and for a PAT
   failure; both stay as specified.
