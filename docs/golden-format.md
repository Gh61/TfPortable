# Golden files – format and how golden tests use them

`tests/golden/` holds the captured behavior of the original tf.exe. The files are produced only
by the scripts in `tools/` (see CLAUDE.md: never edit them by hand, except to redact). This
document describes the file format, the placeholders, the index and manifest files, and how a
golden test normalizes and compares TfPortable's output against them.

## 1. Where the files come from

| Script | Output | What it does |
| --- | --- | --- |
| `tools/seed-tfvc-test-repo.ps1` | `seed-manifest.json` (outside the repo) | Seeds the test project: changesets C75–C83, label `v1.0`, branch `Release`, files with various encodings |
| `tools/capture-tf-golden.ps1` | `index.json`, `<command>/<case>.txt` | Read-only cases and pending-change cases (undone at the end) |
| `tools/capture-tf-golden-extra.ps1` | `index-extra-<run>.json`, `extra-manifest-<run>.json`, `<command>/<case>.txt` | Server-writing scenarios: checkin, label, lock, shelve, branch, merge, rollback, second and server workspace, long history padding |

| `tools/capture-tf-help.ps1` | `index-help.json`, `help/<command>.txt`, `help/<command>-qmark.txt` | Built-in help (`tf vc help <command>` and `tf <command> /?`) of every command TfPortable implements, recognizes or refuses; no sign-in needed. Summarized in `docs/commands.md` |

Captured on 2026-10-07 (help on 2026-10-08) with tf.exe 18.10.12215.6 (Visual Studio 18 Community, Czech UI).
Two extra runs exist: run `2610071920` (index stored as `index-extra.json`, without the run
suffix) and run `2610071925` (`index-extra-2610071925.json`). The sanitized manifests are in
`tests/golden/manifests/`.

## 2. Case file format

One UTF-8 file without BOM, CRLF line endings, `tests/golden/<group>/<case>.txt`:

```
# case: <group>/<case>
# command: tf <arguments as typed, placeholders applied>
# cwd: <working directory, placeholder>
# exit: <exit code; -1 when timed out>
# timed-out: true|false
# duration-ms: <n>
# console-encoding: windows-1250
# vslang: 1033                 (first run only; tf.exe ignored it)
# tf-version: 18.10.12215.6 built by: ...
# run: 2610071920              (extra runs only)
==== stdout ====
<stdout, decoded from the ANSI code page, placeholders applied>
==== stderr ====
<stderr, same>
```

Rules for a parser:

- Header lines start with `# ` and run until the first `==== stdout ====` line.
- stdout is everything between `==== stdout ====` and `==== stderr ====`; stderr is the rest.
  Both end with the CRLF that joined the sections, so trim one trailing line break.
- The `# command:` line is for humans and for the index; the authoritative command is in the
  index (`command` field, identical text). Arguments with spaces or quotes are quoted the way
  the Windows command line expects (`"/comment:Golden: add files (2610071920)"`).
- `# cwd:` is the directory tf.exe ran in. Relative paths in the output are relative to it.

## 3. Index files

`index.json`, `index-extra.json`, `index-extra-2610071925.json`:

```json
{
  "capturedAt": "2026-10-07T19:10:04+02:00",
  "run": "2610071920",                 // extra runs only
  "tfVersion": "18.10.12215.6 ...",
  "consoleEncoding": "windows-1250",
  "culture": "cs-CZ",
  "manifest": { "changesets": {...}, "label": "v1.0@$/TfsPortableTestRepo" },   // index.json
  "extra": { "changesets": {...}, "label": "...", "shelveset": "...", "featureBranch": "..." }, // extra
  "cases": [
    { "group": "status", "name": "clean", "command": "tf status /recursive /noprompt",
      "exitCode": 0, "timedOut": false, "file": "status/clean.txt" }
  ]
}
```

Cases are listed in capture order. In the extra runs the order matters: later cases depend on
the server state left by earlier ones (for example `status/pending-undelete` follows
`undelete/file`).

`index-help.json` has the same shape without `manifest`/`extra`; its cases have `group: "help"`
and `cwd` is `%TEMP%` (`help/vc-help.txt` is `tf vc help`, `help/root-qmark.txt` is `tf /?`,
`help/<command>.txt` is `tf vc help <command>`, `help/<command>-qmark.txt` is `tf <command> /?`).
The help files are reference material for `docs/commands.md`, not replayed by golden tests;
`GoldenIndexTests` checks their integrity like the other index files.

## 4. Manifests

`tests/golden/manifests/seed-manifest.json` maps symbolic names to the changeset numbers and
server paths created by the seed script:

| Key | Value in the golden collection |
| --- | --- |
| `changesets.initial` … `changesets.unmerged` | C75 … C83 |
| `label` | `v1.0@$/TfsPortableTestRepo` |
| `files.*` | server paths of the seeded files (renamed, deleted, Czech name, encodings, binary, ignored) |
| `collection`, `localRoot`, `projectLocal`, `workspace` | placeholders `<COLLECTION>`, `<LOCAL_ROOT>`, `<WORKSPACE_ROOT>`, `<WORKSPACE>` |

`extra-manifest-<run>.json` adds the changesets of that run (`add-files` C84 … `merge` C91 for
run 2610071920; `main-readme` C92, `release-readme` C93 and the history padding C94–C393 for run
2610071925), the golden folder `$/TfsPortableTestRepo/Main/golden-<run>`, the label
`golden-<run>@$/TfsPortableTestRepo`, the shelveset `Golden-<run>` and the feature branch
`$/TfsPortableTestRepo/Feature-<run>`.

## 5. Placeholders

Applied by the capture scripts (longest value first, case-insensitive), then by the manual
redaction of step 0. All of them may appear in the header, stdout and stderr.

| Placeholder | Meaning | Source |
| --- | --- | --- |
| `<COLLECTION>` | Collection URL as passed to `/collection:` (no trailing slash) | script |
| `<ORG>` | Azure DevOps organization name; appears in the display form `<ORG>.visualstudio.com\DefaultCollection` and in identities `[<ORG>]\...` | step 0 redaction |
| `<WORKSPACE>` | Name of the main test workspace | script |
| `<WORKSPACE_B>`, `<WORKSPACE_S>` | Second local workspace and the server workspace of the extra runs | script |
| `<OWNER>` | Display name of the workspace owner | script (extra runs) / step 0 (first run) |
| `<EMAIL>` | Any e-mail address (regex) | script |
| `<COMPUTER>` | Computer name. The developer's default workspace is named after the computer, so a workspace *name* `<COMPUTER>` also appears in `workspaces/*` | script |
| `<USER>` | Windows user name (`C:\Users\<USER>\...`) | script |
| `<LOCAL_ROOT>` | Parent folder of the test workspace (`localRoot` in the manifest) | script |
| `<WORKSPACE_ROOT>` | Local root of the main workspace (`projectLocal`), also in forward-slash form | script |
| `<LOCAL_ROOT_B>`, `<LOCAL_ROOT_S>` | Parent folders of workspace B and S | script |
| `<WORKSPACE_B_ROOT>`, `<WORKSPACE_S_ROOT>` | Local roots of workspace B and S | script |
| `<OTHER_WORKSPACE_ROOT>` | Local root of the developer's unrelated workspace listed by `workspaces` | step 0 redaction |
| `<OTHER_WORKSPACE_1>`, `<OTHER_WORKSPACE_2>`, `<OTHER_WORKSPACE_3>` | Names of the developer's unrelated workspaces listed by `workspaces/two-of-owner` (phase 4 run) | manual redaction |
| `<OTHER_COMPUTER>` | Name of the developer's other computer that owns those workspaces | manual redaction |
| `<REDACTED>` | Anything passed to the scripts' `-Redact` parameter (unused so far) | script |

Not replaced (they are test data, not personal data): the project name `TfsPortableTestRepo`,
the C# namespace `TfpTest`, the run ids `2610071920` / `2610071925`, changeset numbers, dates.

## 6. What a golden test does

A golden test lives in `tests/TfPortable.IntegrationTests` and runs only when
`TFP_TEST_COLLECTION` and `TFP_TEST_PAT` are set (otherwise it is skipped). The fixture:

1. Reads the index files and the manifests.
2. Creates a local workspace for the test project in a temporary folder, with the same mapping
   as the golden run (`$/TfsPortableTestRepo` → `<WORKSPACE_ROOT>`), using the PAT from the
   environment. The fixture, not `tfp`, creates the workspace (workspace management is out of scope).
3. For every selected case, builds the command line from the index: `tf` → path to the built
   `tfp.exe`, placeholders → actual values (`<COLLECTION>` from `TFP_TEST_COLLECTION`,
   `<WORKSPACE_ROOT>`/`<LOCAL_ROOT>` from the fixture, `<WORKSPACE>` from the fixture's workspace
   name), and runs it in the case's `cwd` (also placeholder-mapped) with stdin redirected and closed.
4. Normalizes both the golden output and the actual output (section 7) and compares (section 8).

### Which cases are replayable

| Source | Replayable | Notes |
| --- | --- | --- |
| `index.json` read-only cases (`general`, `workspaces`, `workfold`, `status/clean*`, `dir`, `localversions`, `properties`, `history`, `changeset`, `view`, `difference` server cases, `labels`, `branches`, `merge/candidate`, `merges`, `get/preview`, `get/up-to-date`, `resolve/no-conflicts`) | yes | Server state of the seeded project is stable (C75–C83 are never changed; later runs only add) |
| `index.json` pending-change cases (`status/detected-adds` … `status/after-undo`) | yes, as one ordered scenario | The fixture creates the same local files (`Main\_golden\new-file.txt`, `file with spaces.txt`), runs the cases in index order and finishes with `undo`. Everything stays local |
| `index-extra*.json` cases of phase 1 commands (`status`, `get`, `resolve`, `reconcile`, `localversions`, `properties`, `history`, `changeset`, `view`, `difference`, `dir`, `labels`, `branches`, `merge/candidate`) | shape only | Their server state (locks, conflicts, shelvesets, workspace B) was created by out-of-scope commands. They are compared for output shape and exit code using the recorded state; the fixture cannot recreate the state with `tfp`. Milestone 3 may recreate it with the client library directly in the fixture |
| `index-extra*.json` cases of out-of-scope commands (`checkin`, `lock`, `label`, `unlabel`, `shelve`, `permission`, `destroy`, `workspace`) | no | `tfp` returns TFP0003, exit 100; a separate unit test checks that |
| `history/long-*` | yes | Padding changesets C94–C393 exist on the server; compare against the first 256 rows (compat.md §9) |

Each test case is tagged with the milestone that must make it pass (`docs/KICKOFF-CLOUD.md`
lists the mapping).

## 7. Normalization

Applied to the golden text and to `tfp` output alike before comparison:

| What | How |
| --- | --- |
| Line endings | CRLF → LF; trailing whitespace on each line removed; trailing blank lines removed |
| Placeholders | Actual values in `tfp` output are replaced back by the placeholders of section 5 (longest first). `<OWNER>` ↔ the display name of the authenticated identity; `<COMPUTER>` ↔ `Environment.MachineName` |
| Changeset numbers | CI runs against the same collection and the same test project as the golden capture (Gate A, 2026-10-07), so the numbers compare directly. The manifests still document which number means what (`75` = `initial`), and a test may map through them to produce readable failure messages. Numbers in `;C75`, `;75`, `C75~C81`, bare table cells and `Changeset: 75` lines are all covered; deletion ids `;X18` likewise |
| Dates | Any Czech short (`7. 10. 2026`) or long (`středa 7. října 2026 19:07:27`) date and any ISO 8601 date are replaced by `<DATE>` |
| Durations, activity ids | Removed (`# duration-ms`, `ActivityId:` lines) |
| Message texts | Not compared (see section 8); the comparison works on extracted data, not on lines of text |
| Table columns | Cells are split on two or more spaces; column padding is ignored |
| Local paths | Relative paths in golden grouped listings are resolved against `# cwd:` so that the absolute paths `tfp` prints compare equal |

## 8. What is compared

For every case:

1. **Exit code**: must equal the golden `exitCode`, unless `docs/compat.md` records a deviation
   for that case (the deviation table in the test lists the expected code instead).
2. **Streams**: whether stdout and stderr are empty or not, as in the original (compat.md §1.2).
3. **Items**: the list of server paths (`$/...`), local paths and names extracted from the
   output, in order. Folder headers (`Main\src:`, `$/...:`) count as items.
4. **Changeset numbers**: all numbers (after symbolic mapping), in order, including `*` partial
   markers in `merge /candidate` and `merges`.
5. **Change types**: the Czech change words of the golden output are mapped to the English words
   `tfp` prints (`přidat` → `add`, `upravit` → `edit`, `odstranit` → `delete`, `přejmenovat` → `rename`,
   `větev` → `branch`, `sloučit` → `merge`, `obnovit` → `undelete`, `vrátit zpět` → `rollback`,
   `uzamknout` → `lock`, `přejmenování zdroje` → `source rename`), then compared in order.
6. **Counts**: trailer numbers (`Changes: 9`, `Items: 19`, `detected changes: 2`).

Never compared: message wording, column headers, dates, durations, banner lines, the deprecation
warning of `properties`, help text.

`/format:json` output has its own tests against `docs/json-schema.md`; golden files are not
used for JSON.

## 9. Adding new golden cases

Only the developer can capture new cases, on Windows with the original tf.exe:

1. Add the case to `tools/capture-tf-golden.ps1` (read-only or pending-change cases) or
   `tools/capture-tf-golden-extra.ps1` (server-writing scenarios; a new run id and a new
   `golden-<run>` folder are created automatically).
2. Run the script against the test collection; review the output for personal data (step 0 of
   `docs/KICKOFF-LOCAL.md` lists what to look for) and apply the placeholders of section 5.
3. Store the sanitized manifest in `tests/golden/manifests/`, never the original.
4. Record what the case shows in `docs/compat.md`.

Wanted but not yet captured (see compat.md §17): `add <folder> /recursive` with ignored items,
`get` over a writable file without a pending change (TFP0402), a PAT rejection.
