# What the developer still has to deliver or decide

Kept by the agent (CLAUDE.md "When to stop and report"); `docs/PROGRESS.md` "Needs the user" points here.
Tick an item by deleting it or by writing the date and the result next to it. Each item says when it is done.

## Status on 2026-10-11

| § | Item | Status |
| --- | --- | --- |
| 1 | Release `v0.2.0` | **DONE** 2026-10-09 |
| 2 | Checkin policies on the test project | **DONE** 2026-10-11 (Work Items + Changeset Comments, no build policy); the agent still has to see one CI run after it |
| 3 | Golden captures with tf.exe | **DONE** 2026-10-09 (all four runs); no further run, decided 2026-10-11 |
| 3b | Decompile tf.exe for the reference notes | **DONE** 2026-10-10 |
| 4 | Permanent shelveset `tfp-golden-shelveset` | **DECIDED** 2026-10-11: stays for good, nothing to do |
| 5 | Decisions before phase 4 | **DONE** 2026-10-09 (decided, implemented, released in `v0.3.0`; `main` ruleset set up) |
| 6 | Still open from earlier | **DONE**, only the recurring workspace cleanup remains |
| 7 | Manage permissions for the CI user | **DONE** 2026-10-11; the agent still has to see one CI run after it |

Nothing is waiting for the developer. The only open step is the agent's: read the first CI run after §2 and §7
(Actions → CI → Run workflow on `main` with an empty `release_tag`, or any pull request) and record its outcome
in §2, §7, PROGRESS.md "Open questions" and `docs/tf-reference/questions.md` (question 35). After that: the README
"Phases and progress" section goes, and `v1.0` is released on the developer's word.

## 1. Release `v0.2.0` (phases 2 and 3) – DONE 2026-10-09

Done on 2026-10-09: the developer asked for the release in the session and the agent ran the CI workflow on
`main` (d94a6a7) with `release_tag` = `v0.2.0`;
[v0.2.0](https://github.com/Gh61/TfPortable/releases/tag/v0.2.0) lists `tfp.exe` and `tfp.exe.sha256`.

## 2. Checkin policies on the test project – DONE 2026-10-11 (CI run to be seen by the agent)

Checkin policies belong to a **team project**, not to a branch or a folder: Visual Studio stores them as an
annotation of `$/TfsPortableTestRepo`, and every checkin into the project is subject to them. There is no
per-branch scope, so the policy must suit every CI checkin. The integration tests adapt to whatever is
configured (`tfp checkin /validate /format:json` lists the policies with `passed`, `failed` or `unknown`):

| Policy | Where | Effect on CI | What it proves |
| --- | --- | --- | --- |
| Work Items (required association) | Visual Studio → Team Explorer → Settings → Source Control → Check-in Policy → Add | every test checkin already passes `/associate:1`; a checkin without it must fail with TFP0407 | the native evaluation of the work item policy |
| Changeset Comments Policy | same place | none (`/comment` is always required) | the native evaluation of the comment policy |
| Builds (optional, later) | same place | every test checkin passes `/override:"CI run <id>: ..."` automatically | the TFP0409 path for a policy tfp cannot evaluate |

- Recommended now: Work Items + Changeset Comments. Without any policy the policy assertions of
  `Phase3Tests` are skipped (inconclusive), nothing fails.
- Done when a CI run after the change passes and its log shows `TFP0407` in the validate step of
  `Phase3Tests.Checkin_validates_commits_links_a_work_item_and_reports_nothing_pending`.

DONE: configured on 2026-10-11 by the developer: Work Items and Changeset Comments (no build policy). Still to see: the
first CI run after the change (Actions → CI → Run workflow on `main` with an empty `release_tag` runs the tests
without a release, or any pull request). The agent then records the result here and in PROGRESS.md.

## 3. Golden captures with the original tf.exe (one sitting, one sign-in) – DONE 2026-10-09, no further run

Kept for reference (how the capture is run); every run listed below was done on 2026-10-09. Run on a Windows machine with Visual Studio (tf.exe 18.x) and the seeded test workspace of
`tools/seed-tfvc-test-repo.ps1` (`seed-manifest.json`). tf.exe asks for the sign-in once at most; every
script checks the sign-in before it starts and never opens a dialog (`/noprompt` everywhere, a timeout kills a
stuck tf.exe).

```powershell
cd <repo>
.\tools\capture-tf-golden-all.ps1 -Manifest D:\TfpTest\seed-manifest.json -OutDir .\tests\golden -WorkItem 1
```

The runner calls, in this order and stops at the first failure:

| Script | Cases | Writes to the server |
| --- | --- | --- |
| `capture-tf-golden-phase2.ps1` | `branch` (pending, `/silent`, `/noget`, `/version`), `merge /candidate`, `/preview` (all, a range, a single version, with a conflict, `/nosummary`, `/format:detailed`), `merge` with a range, `/discard`, `/force`, `/baseless`, a conflict and its `resolve`, nothing to merge, `merges` (brief, detailed, no source, `/showall`, one file), `rollback` (`/changeset` with and without items, a range, `/toversion` with and without items, `T` and date specs, both switches, `/keepmergehistory`, of a merge changeset, nothing to roll back), `undelete` (file, `;X<id>`, wrong id, deleted twice, `/noget`, local path, folder with and without `/recursive`, not deleted, missing), `folderdiff` (sections, `/view:*`, `/filter`, local against server, one path, a version, missing folders, `/filterLocalPathsOnly`, same size and different content, a pending local edit) | folder `$/<Project>/Main/golden2-<run>` with a source folder, a branch of it and a sibling (deleted by a checkin at the end); every merge, rollback and undelete is undone after its capture |
| `capture-tf-golden-extra2.ps1` | the gaps of phases 1 and 3 that no run covered: `add /recursive` with `.tfignore` matches, `/noignore`, a wildcard; `checkin` without `/comment`, without a work item, with `/override` (the policy failure shape when the project has a policy), `/validate` with nothing pending; `checkout` twice and of a folder; `delete` with a pending edit and of a folder; `rename` of a folder, onto an existing name, into a folder, case only; `undo` of one file; `difference` with `/ignorespace`, `/ignorecase`, `/ignoreeol`, a missing final newline, CRLF against LF, a binary file, a folder, two server folders; `view` of a folder, a wildcard, `/recursive`, a binary file to `/output` and to the console; `dir` wildcards; `properties` of a folder, a version, a deleted item; `history` of a deleted item; `branches` of an unbranched item; `merge /candidate` of unrelated paths; `label` of a server path without a version, `/version`, `labels /owner:*`, a wildcard, `unlabel` of a folder; `lock /lock:none` on an unlocked item, a second lock, a level change, `checkout` of a file the workspace locked; `shelve` without a comment, `unshelve` of one item, with `;owner`, a local itemspec, `shelvesets /format:detailed`; `reconcile /promote` with `/adds`, `/deletes`, neither; `get` of an older version over a modified file with and without `/overwrite`, `/remap`; `resolve` with `AutoMerge` (preview, non-overlapping, overlapping), `AutoMergeForced`, `OverwriteLocal` (on a get conflict and on a merge conflict), `DeleteConflict`, `KeepYoursRenameTheirs /newname`, the `AcceptMerge` alias | folder `$/<Project>/Main/goldenx-<run>` with a folder `x` and a branch `y` (deleted by a checkin at the end), a label and a shelveset named `goldenx-<run>` / `Goldenx-<run>` (deleted) |
| `capture-tf-golden-phase3.ps1` | `checkin /validate`, `/associate`, comment file, unchanged edit, `shelve /replace`, `/move`, existing name, `unshelve` with a local file in the way, `/nomerge`, `label` without scope, `/child:*`, `label /delete`, `lock` edge cases | folder `$/<Project>/Main/golden3-<run>` (deleted by a checkin at the end), a shelveset and a label named `Golden3-<run>` / `golden3-<run>` (deleted) |
| `capture-tf-golden-phase4.ps1` | `property`, `permission` on the run folder, `lock /workspace`, `undo /workspace:<other>`, `status /workspace`, `checkin /shelveset`, `unshelve /move`, `shelve /validate`, `destroy /preview`, `destroy /keephistory`, `destroy`, history and `dir /deleted` of a destroyed item, `workspace /delete` | folder `$/<Project>/Main/golden4-<run>` (destroyed at the end), a second temporary workspace (deleted), a permission entry for the signed-in user on the run folder only (removed, then destroyed with the folder) |

The scripts are limited to their own run folder and their own temporary objects: nothing outside
`$/<Project>/Main/golden<phase>-<run>` is changed, no permission of an existing item is touched, nothing is
destroyed outside the run folder. `-WhatIf` is not available; read the "Scenarios" section of a script before
the first run if in doubt.

Afterwards:

1. Look through the new files under `tests/golden/<command>/` for personal data (the scripts replace the
   owner, user, computer, collection and paths by placeholders; e-mail addresses become `<EMAIL>`).
2. Commit `tests/golden/**` and the new `index-phase2-<run>.json`, `index-extra2-<run>.json`,
   `index-phase3-<run>.json`, `index-phase4-<run>.json`.
3. Tell the agent: it compares tfp against the captures, corrects `docs/compat.md` §15 and §16 where the
   assumed shapes differ, closes the open questions of §19 (the `.tfignore` exit code, `reconcile /promote`,
   TFP0402) and answers the questions of `docs/tf-reference/questions.md` the captures settle.

Before the run, if it is still open: configure the checkin policies "Work Items" and "Changeset Comments" on the
test project (section 2). The `checkin/no-comment` and `checkin/no-work-item` cases of extra2 then capture the
policy failure shape tfp only assumes (`docs/compat.md` §16).

DONE: all four index files were captured on 2026-10-09 (`index-phase2-2610091535.json`, 102 cases;
`index-extra2-2610091539.json`, 115 cases; `index-phase3-2610091137.json`; `index-phase4-2610091138.json`)
and compared with tfp (`docs/compat.md` §20, PR "golden-compare"). Still uncaptured: `get` over a writable
local file (TFP0402; local workspaces report a conflict instead), a checkin policy failure (section 2 is still
open), `undo /workspace:<other>` without permission.

DECIDED on 2026-10-11: no further capture run. The three uncaptured cases do not justify a fifth sitting:
the policy failure shape of tf.exe is known from the decompiled sources (`docs/tf-reference/checkin.md`: "Policy
violations:" on stderr, exit 100, `/validate` exit 100) and the live behavior is checked by CI now that the test
project has policies; the TFP0402 `get` case cannot happen in a local workspace; `undo /workspace:<other>` without
permission needs a second account. Golden tests never compare texts, so the missing captures cost nothing.

## 3b. Decompile tf.exe for the reference notes (agreed on 2026-10-08) – DONE 2026-10-10

- Run `.\tools\decompile-tf.ps1` on the machine with Visual Studio. It decompiles `TF.exe` and the version
  control assemblies next to it into `local\decomp\<version>\` (git-ignored) and writes an `index.md` there.
- Then either answer `docs/tf-reference/questions.md` yourself into `docs/tf-reference/<command>.md` (template
  in `docs/tf-reference/README.md`), or give the agent the `local\decomp\<version>\` folder in a session and
  let it write the notes for your review. The rules (reference only, no code or texts, version named) are in
  that README and in CLAUDE.md.
- Done when `questions.md` is empty, or every remaining question says why it cannot be answered.

DONE on 2026-10-10: tf.exe 18.10.12215.6 decompiled, 15 notes under `docs/tf-reference/`, every question answered
except 25a (needs a second account), 35 (answered by the first CI run after §7) and 36 (answered: the service
accepts `destroy /startcleanup`); each says why. Nothing else to do; `local\decomp\` stays on the developer's
machine and is recreated by the script when a new tf.exe version is to be compared.

## 4. Permanent shelveset `tfp-golden-shelveset` – DECIDED 2026-10-11, stays, nothing to do

`ShelvesetTests` reads it (`status`, `difference`, `view` with `/shelveset`). The earlier plan to rewrite that
test to create its own shelveset through `tfp shelve` is dropped.

DECIDED on 2026-10-11: the shelveset stays for good as the fixture of the read-only test (`ShelvesetTests` proves
that the shelveset reading commands work on a shelveset of another owner without writing anything). No rewrite
planned. Do not delete it.

## 5. Decisions before phase 4 – DONE 2026-10-09

DONE: decided on 2026-10-09 (phase 2 first: done; the scope table below with `resolvepath` added, `property` read-only,
`docs`/`msdn`, `proxy`/`configure`/`certificates` as `TFP0005`; `destroy` with the preview marker; the `main` ruleset
is set up). Everything in this section is implemented and released (`v0.3.0`, PR #17 and PR #18; `property`
gained the write form afterwards). Kept for history; SPEC.md "Fáze 4" is the current definition.

1. **Phase 2 first?** `merge`, `branch`, `rollback`, `undelete`, `merges`, `annotate`, `folderdiff` are
   captured (`tests/golden/`) and specified (SPEC.md "Fáze 2") but not implemented; the catalog answers
   TFP0003. They are everyday work in branching teams and fit protection level 2. Say whether they come
   before phase 4 (recommended) or are dropped.
2. **Scope of phase 4.** SPEC.md calls it hypothetical. Candidates and the agent's proposal:

   | Command | Proposal | Reason |
   | --- | --- | --- |
   | `destroy` | implement, level 3, `/preview` first | irreversible; captured; the tool is meant to replace tf.exe |
   | `permission` | implement read (`permission <item>` listing) at level 1, `/allow` `/deny` `/remove` at level 3 | listing is useful for agents; writes are administrative |
   | `workspace /new` and `/delete` | implement, level 3 | needed to set up a machine without Visual Studio |
   | `property` | implement, level 2 | rarely used; cheap |
   | `lock /workspace`, `undo /workspace:<other>` | implement, level 3 | foreign workspaces |
   | `checkin /shelveset`, `unshelve /move`, `shelve /validate` | implement, level 2 | complete the phase 3 commands |
   | `proxy`, `configure`, `certificates`, `msdn`, `resolvePath` | never (TFP0003) | dialogs, client configuration, nothing for a portable tool |

   Confirm or change the table; the agent writes "Fáze 4" in SPEC.md from it.
3. **`main` ruleset** (agreed on 2026-10-08, set up in GitHub on 2026-10-09): pull request required, the CI check
   `Build, test, package (windows-latest)` required, force pushes and deletions blocked, no required
   approvals, merge commits allowed.

## 6. Still open from earlier – DONE 2026-10-11

- ~~A lawyer's look at LICENSE-EXCEPTION and THIRD-PARTY-NOTICES~~: DONE (closed) on 2026-10-10, the developer
  decided not to have the texts reviewed; they stay as written, to the best of the developer's knowledge.
- Leftover workspaces `tfp-golden-<id>` in the collection after a CI run that died mid-way: delete them
  occasionally (`tf workspaces /owner:*`). A recurring chore, never "done"; nothing is known to be left now.
- ~~The permission of the CI user~~: DONE on 2026-10-11, see §7.

Nothing else is open in this section (2026-10-11).

## 7. "Manage permissions" for the CI user (agreed on 2026-10-10) – DONE 2026-10-11 (CI run to be seen by the agent)

The CI user `Test TfPortable` (the owner of `TFP_TEST_PAT`) had no **AdminProjectRights** on
`$/TfsPortableTestRepo`, so `permission /allow` in `Phase4Tests` was refused by the server (TF14098) and the test
only checked that the refusal is reported per item.

- Grant the user **Manage permissions** (that is AdminProjectRights) on the folder `$/TfsPortableTestRepo/CI`:
  Azure DevOps → Project settings → Repositories → the TFVC repository → the folder `CI` → Security → add
  `Test TfPortable` → Manage permissions: **Allow**. Nothing else is needed.
- Done when a CI run after the change passes and `Phase4Tests.Workfold_edits_changeset_edit_shelveset_checkin_unshelve_move_and_permission_change`
  takes the real-change branch (the listing shows the explicit `Lock` allow and the `remove` succeeds); that
  run also answers question 35 of `docs/tf-reference/questions.md` (the exit code when nothing is applied is
  already 100 in the code).

DONE: granted on 2026-10-11 by the developer (Manage permissions: Allow on `$/TfsPortableTestRepo/CI`). Still to see:
the first CI run after the change (same run as for §2). "Manage permissions" is the item permission TFVC calls
`AdminProjectRights`, which `destroy` checks on the item too, so
`Phase4Tests.Destroy_needs_a_preview_then_removes_the_item_and_its_history` may now run the real destroy under
the CI folder instead of returning at the permission check; the test handles both outcomes.
