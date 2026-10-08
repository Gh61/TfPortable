# TfPortable – progress

Read this first in every session. Update it at the end of every session (see CLAUDE.md).

## Status

| # | Milestone | Where | Status | Gate |
| --- | --- | --- | --- | --- |
| 0 | Spec refinement from golden files (`docs/compat.md`, SPEC.md updates) | local | done (Gate A passed 2026-10-07; help capture still to be reviewed) | user approves SPEC.md |
| 1 | Spike: `tfp whoami` + `tfp status` with PAT, Costura single exe, clean machine | local | done (clean-machine test passed, Gate B approved 2026-10-08) | user approves architecture |
| 2 | Core: parser, workspace context, errors, JSON envelope, `login`/`logout`/`whoami` | cloud + CI | not started (next; `docs/KICKOFF-CLOUD.md`) | – |
| 3 | Workspace commands: status, add, checkout, delete, rename, undo, get, resolve, reconcile, localversions, workfold, workspaces | cloud + CI | not started | – |
| 4 | History and content: history (incl. search), changeset (/diff), view, diff, dir, properties, labels, branches, merge /candidate | cloud + CI | not started | – |
| 5 | Integration: CLAUDE.md template for users, compat tests, GitHub release of `tfp.exe` | cloud + CI | not started | user approves release |
| – | Phase 2 commands | – | not planned | separate brief |

Statuses: not started, in progress, blocked, done.

## Next steps

1. Cloud: follow `docs/KICKOFF-CLOUD.md`, milestone 2 (branch `m2-core`, pull request, CI loop).
2. Before that the developer: push `main`, set the secrets (see "Needs the user").
2. When the developer has run `tools/capture-tf-help.ps1`: review `tests/golden/help/` for personal
   data, derive the per-command switch and alias lists, put them into SPEC.md (or a new
   `docs/commands.md`) and `docs/json-schema.md` (`help` data). Commit.
3. After the first push: check the CI run on GitHub Actions (`.github/workflows/ci.yml`).

## Needs the user

- Push `main` to GitHub so that the CI workflow runs for the first time (nothing has been pushed
  from the local sessions; the agent does not push on its own). Check the first run.
- Before milestone 2 (cloud): GitHub Secrets `TFP_TEST_COLLECTION` and `TFP_TEST_PAT`. The PAT must
  have **Full access** (TFVC SOAP API) and should belong to a dedicated user with access only to the
  test project, or to a separate test organization.
- Cloud phase: review and merge the pull requests of milestones 2–5; decide on branch protection.


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
| 2026-10-07 | Exit codes follow the original per command and case (`docs/compat.md` §1.1): not found is 1 for `add`/`delete`/`view`/`dir`/`undelete`, 100 for `rename`/`changeset`/`label`, 0 for `history`; `resolve` exits 1 while conflicts remain; `workspaces` with no match exits 1; `difference` exits 0 when files differ. The SPEC.md catalog exit column becomes the default, compat.md lists overrides. Only 110 (PAT) and 120 (workspace/collection) stay as extensions | Scripts and golden tests depend on the original codes; the two extensions tell the AI what to do next |
| 2026-10-07 | Streams follow the original: per-item errors and conflicts on stderr, empty-result messages on stdout even with exit 1 (`dir/not-found`, `workspaces/other-computer`) | Minimal deviation; JSON mode puts everything in one document anyway |
| 2026-10-07 | `history`: default mode is slot mode (golden `history/renamed-file` equals `/slotmode`); `/format:xml` stays unsupported (TFP0002), `json` added; unknown path → exit 0 with empty result | Matches tf.exe 18.10 |
| 2026-10-07 | `add`: `.tfignore` applies to wildcard and `/recursive` expansion only; an explicitly named file is always added (golden `add/ignored-file` exit 0) | Matches the original |
| 2026-10-07 | `merges /extended` is rejected by tf.exe 18.10 ("option not allowed", exit 100); dropped from the phase 2 switch list. `annotate` does not exist in tf.exe (exit 2); stays a TfPortable extension | Golden `merges/extended`, `annotate/attempt` |
| 2026-10-07 | `rollback /changeset:C85` works without an item spec (golden `rollback/changeset`), as SPEC.md assumed | Open question closed |
| 2026-10-07 | Original codes for conflicts: `get` conflict exit 1, `resolve` with conflicts left exit 1, `merge` conflict exit 1, `checkin` blocked by lock / out of date exit 100 (TF10141), checkout lock in local workspace exit 1 (TF400022) | Open question closed; `checkin`/`lock` stay out of scope |
| 2026-10-07 | `view` always writes UTF-8 and decodes the item by its server encoding; golden `view/czech-name` (mojibake of a BOM-less UTF-8 file) is a bug of the original and is not reproduced | Correctness over byte-compatibility |
| 2026-10-07 | `difference /format:context` is not supported (TFP0002); `properties` prints no deprecation warning; `tfp`/`tfp help` print the command catalog instead of the banner | Not needed by the AI; cleaner output |
| 2026-10-07 | Golden tests compare exit code, stream emptiness, items, changeset numbers (mapped through the manifests), change types (Czech → English map) and trailer counts; never texts or dates (`docs/golden-format.md` §7–8). Cases of `index-extra*.json` for phase 1 commands are compared for shape only because their server state came from out-of-scope commands | Golden files are Czech; server state cannot be recreated by tfp |
| 2026-10-07 | SPEC.md refined (step 2): exit-code table rewritten with per-command semantics, catalog exit column is a default with compat.md overrides, new TFP0303 (binary content needs `/output`), versionspec date parsing order (ISO → current culture → invariant), collection URL normalization drops `/DefaultCollection` for Services, `CredEnumerateW` added for the single-PAT detection, section "Tvar textového výstupu", warnings format `WARNING TFPxxxx:`, JSON envelope details (`truncated` object, message shape), `merges /extended` removed, milestone 5 typo `tf.exe` → `tfp.exe` | Decisions of `docs/compat.md` applied; the implementer should not have to guess |
| 2026-10-07 | `docs/json-schema.md` written as a draft (envelope, shared types Item/Changeset/Change/PendingChange/Conflict/Mapping/Workspace, enumerations in lower case, `data` per phase 1 command) | Kickoff step 2 asks for a proposed structure; Gate A approves it |
| 2026-10-07 | New script `tools/capture-tf-help.ps1` captures `tf vc help <cmd>` and `tf <cmd> /?` into `tests/golden/help/` | Golden `general/help-status` shows that `tf help <cmd>` prints only a banner in VS 18, so switch lists and aliases were missing; the developer runs it (rule: capture scripts are run by the user) |
| 2026-10-07 | Gate A, developer approved: (1) the last table column is never truncated and local paths in grouped listings are absolute; (3) `docs/json-schema.md` is the basis for milestone 2, on the condition that its types stay derived from what the golden outputs show (PendingChange from `status /format:detailed`, Item from `properties`, Changeset/Change from `changeset`, Workspace/Mapping from `workspaces /format:detailed` and `workfold`); (4) CI uses the same collection and the same test project as the golden capture, so changeset numbers match the manifests | Developer's answers in the local session |
| 2026-10-07 | Gate A (2): `view /format:json` returns the content of a text file inline as a JSON string in `data.content`; a binary file needs `/output:<file>` (then `content` is null), otherwise TFP0303. Gate A passed | Developer's answer; one call gives the AI metadata and content |
| 2026-10-07 | Scaffolding (step 3): `TfPortable.slnx` + `Directory.Build.props`; packages Fody 6.9.3, Costura.Fody 6.2.0, Microsoft.NETFramework.ReferenceAssemblies 1.0.3, xunit 2.9.3, xunit.runner.visualstudio 3.1.5, Microsoft.NET.Test.Sdk 17.14.1, Newtonsoft.Json 13.0.3 (JSON output; already a transitive dependency of ExtendedClient, so no new dependency). Version: CI passes `/p:Version` from the tag, local builds are `0.1.0-dev` | KICKOFF-LOCAL step 3; latest stable versions on 2026-10-07; xunit.runner.visualstudio 4.x targets xunit v3 |
| 2026-10-07 | Integration tests skip via a `FactAttribute` subclass (`IntegrationFact`) that sets `Skip` when `TFP_TEST_COLLECTION`/`TFP_TEST_PAT` are missing; no extra package. `GoldenIndexTests` validate `tests/golden` integrity (index ↔ file headers, no unredacted personal data) and run without a server | CLAUDE.md: skipped, not failed |
| 2026-10-07 | `.gitattributes`: `tests/golden/**` is `-text` so Git never normalizes the byte-exact golden files; everything else `text=auto` | Golden files are CRLF/UTF-8 captures |
| 2026-10-07 | First Release build on the developer's machine: `tfp.exe` 7,753,728 bytes with 78 embedded assemblies (nothing excluded yet); `tfp version` exit 0, unknown command exit 2 | Baseline for the spike's exclusion list |
| 2026-10-07 | Spike measurements: Costura with no exclusions 78 assemblies / 7.8 MB; with the exclusion list in `FodyWeavers.xml` (SqlClient, MSAL/Entra identity stack, all non-VC Azure DevOps clients) 34 assemblies / 3.6 MB. The exe alone in an empty folder, without `.config`, runs `version`, `whoami`, `logout` and the error paths; the live-server check of the exclusions is pending `tfp login` | KICKOFF-LOCAL step 4 |
| 2026-10-07 | Workspace detection step 2 works across client versions: the 20.256 client library finds the workspace that tf.exe 18.10 created (`Workstation.Current.GetLocalWorkspaceInfo`), collection URL `https://<org>.visualstudio.com/` normalized to `https://dev.azure.com/<org>` | Observed on the developer's machine |
| 2026-10-07 | `app.manifest`: asInvoker, Windows 8.1–11 supportedOS (real OS version in `tfp version`), longPathAware, UTF-8 active code page | Portability and correct Czech file names in arguments |
| 2026-10-07 | MSYS protection verified from Git Bash: `tfp status /recursive` reports TFP0002 naming `C:/Program Files/Git/recursive` and the `-recursive` fix; detection requires MSYSTEM or EXEPATH in the environment and a Git-looking prefix that exists on disk | SPEC.md "Ochrana proti převodu cest" |
| 2026-10-07 | PAT for TfPortable must have all scopes (Full access). Found in the spike: a PAT with 'Code: Read & write' passes REST `connectionData` (HTTP 200) but the SOAP endpoints the TFVC client uses answer 401 (`WWW-Authenticate: Basic`), which the client library reports as TF30063 from `TfsHttpWebRequest.EnsureTokenProvider`; a PAT sent as Bearer is refused the same way. Confirmed by the Azure Repos VS Code extension README ("TFVC repositories require tokens with All Scopes") and the tf.exe `/loginType:OAuth` discussions. Consequences: prompt text asks for Full access; `login` verifies over REST first and stores, SOAP failure is a TFP0103 warning; `whoami`/`status` re-check the token over REST after TF30063 to report TFP0103 instead of TFP0102; the connection uses `VssCredentials(WindowsCredential(false), VssBasicCredential, DoNotPrompt)` | Spike finding; read-only PATs are therefore impossible for TFVC |
| 2026-10-07 | Never enable System.Net or TeamFoundation tracing in a build or document it for users without masking: the trace writes the `Authorization` header with the PAT in clear text. The spike did this once on the developer's machine; the log files were deleted and the developer was asked to regenerate the token | Security lesson from the spike |
| 2026-10-08 | Spike verified against the test collection with a full-access PAT: `whoami` (identity, collection, PAT date, workspace with mappings) and `status` (pending and detected changes in the tf.exe table shape, trailer `Changes: 1, detected changes: 1`) work from PowerShell, from Git Bash with `-switch` syntax, and from `tfp.exe` copied alone into an empty folder (3.6 MB, 34 embedded assemblies, no `.config`); JSON envelopes correct. In a local workspace the client library pends an edit of a tracked file automatically during the scan (README.md appeared as pending `edit`, the new file as detected `add`) and un-pends it when the content is restored | Milestone 1 result; architecture holds |
| 2026-10-08 | `name;owner` uses the owner's display name (`Workspace.OwnerDisplayName`), as in the golden files; `Workspace.OwnerName` is an identity id (GUID) on Azure DevOps Services | Golden `<WORKSPACE>;<OWNER>` |
| 2026-10-08 | Rules added to CLAUDE.md on the developer's request: search the web after a few failed attempts before guessing further; never enable System.Net/TeamFoundation tracing with a PAT in use. README states that the PAT must have Full access | Lessons from the spike |
| 2026-10-08 | Gate B passed: `tfp.exe` alone on a clean Windows Server (10.0.20348) without Visual Studio ran `version`, `login` (full-access PAT), `whoami`, `logout`; the developer approved the architecture (single Costura exe, PAT in Credential Manager, SOAP client with Basic auth). Two notes for milestone 2: `logout` without `/collection` outside a workspace should fall back to the single stored PAT like `whoami`; an interrupted PAT prompt (Ctrl+C) ends silently | Developer's test on 2026-10-08 |
| 2026-10-08 | Help of tf.exe 18.10 captured (`tests/golden/help/`, 87 files, `index-help.json`; the developer ran the script from another directory, files were moved into the repo; no personal data). `docs/commands.md` lists every command's switches and what TfPortable does with them. Corrections to SPEC.md from the help: `add`/`checkout` use `/encoding:`, not `/type:`; `get` has `/noautoresolve`; `resolve /auto` canonical values are `AutoMerge|AutoMergeForced|TakeTheirs|KeepYours|OverwriteLocal|DeleteConflict|KeepYoursRenameTheirs` with the documentation's `Accept*` names as aliases (golden captures used `AcceptYours`/`AcceptTheirs`/`AcceptMerge` successfully); `status`/`workspaces`/`shelvesets` know `/format:xml` (not supported, `json` instead); `view` has `/console` (accepted, no-op) and `/recursive`; `/lock:checkin|checkout` on `add`/`checkout`/`delete`/`rename` is rejected because locks write to the server | KICKOFF-LOCAL step 2 follow-up; the implementer no longer guesses switch lists |
| 2026-10-08 | README structure requested by the developer: why the project exists, how it is used, examples of basic commands, a command table with one-line descriptions, links to `docs/commands.md` / `docs/compat.md` / `docs/json-schema.md`. Rewritten accordingly; KICKOFF-CLOUD milestone 5 keeps it in sync with each milestone | Developer's request |
| 2026-10-07 | Default output of tfp is text in the shape of tf.exe; JSON only with `/format:json` (tf.exe has no JSON at all, `/format:` accepts only `brief`/`detailed`, plus `unified`/`context` for `difference`; anything else is TF10120, exit 100) | Confirmed with the developer |

## Open questions

Closed on 2026-10-07 by the golden analysis (see "Decisions" and `docs/compat.md`): `merges /extended`
(switch rejected by tf.exe 18.10), `history` of an unknown path (exit 0) and `add` of an ignored
file (exit 0, explicit path), `rollback /changeset:` without item spec (works), exit codes for
locks, out-of-date checkin and conflicts.

Open:

- `add <folder> /recursive` with items skipped by `.tfignore`: the seed script observed exit 1, no
  golden file exists. Follow the original (exit 1, skipped items listed) until the developer captures
  a case.
- `reconcile /promote <folder> /recursive` promoted nothing in the golden run (`reconcile/promote`),
  although `status` showed two candidates. Check in the milestone 3 integration test whether `/adds`
  and `/deletes` must be given explicitly.
- No golden case for TFP0402 (`get` over a writable file without pending change) or for a PAT
  rejection; both stay as specified in SPEC.md until captured.
- `index-extra.json` (run 2610071920) has no run suffix although the script writes
  `index-extra-<run>.json`; harmless, but the golden test loader must accept both names.

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
- Step 1 done: all 203 golden cases (86 + 76 + 41) compared with SPEC.md; `docs/compat.md` (per-command
  tables, exit-code table, cross-cutting decisions) and `docs/golden-format.md` (file format,
  placeholders, manifests, normalization, what is compared, replayable cases) written; four open
  questions closed, five new ones recorded.
- Step 2 done: SPEC.md updated with the compat.md decisions, `docs/json-schema.md` drafted,
  `tools/capture-tf-help.ps1` written (syntax-checked, not run), Gate A questions listed under
  "Needs the user".
- Gate A passed (all four questions answered, recorded under "Decisions").
- Step 3 done: solution, three projects, `Directory.Build.props`, `FodyWeavers.xml`, CI workflow
  (`.github/workflows/ci.yml`: restore, build Release, unit + integration tests with secrets, artifact,
  release on `v*` tags), README, `.gitattributes`. Release build and both test projects pass locally
  (unit 3/3; integration 4 passed, 1 skipped without secrets).
- Step 4 (spike) in progress: Auth (Credential Manager P/Invoke, URL normalization, PAT prompt,
  connection), Context (workspace detection), Cli (minimal parser with MSYS detection), Commands (help,
  version, login, logout, whoami, status), Errors, Output (text + JSON envelope); 28 unit tests. Offline
  paths verified from PowerShell, Git Bash and from the exe alone in an empty folder; exe 3.6 MB after
  exclusions. Known small gaps for milestone 2: JSON `collection` is null when the error happens before the
  context is resolved; `status /format:detailed`, `/user`, `/shelveset` return TFP0002 in this build.
- Next: developer runs `tfp login`; then server verification, clean-machine test, spike report, Gate B.
- Blockers: `tfp login` (developer); `tests/golden/help/` not yet captured; repository not yet pushed.

### 2026-10-08 – local session 1 continued, spike verification (milestone 1)

- Done: root cause of TF30063 found (TFVC SOAP API accepts only full-access PATs; REST accepts custom
  scopes) by web search, decompilation and an HTTP trace; connection switched back to Basic auth without
  prompting; `login` verifies over REST first; TFP0103 explains the scope case. Developer created a
  full-access PAT; `whoami` and `status` verified from PowerShell, Git Bash and the exe alone in an empty
  folder, including pending and detected changes. CLAUDE.md and README updated with the lessons.
- Spike report: Costura single exe works (3.6 MB, 34 assemblies after exclusions, nothing forced a design
  change); Credential Manager P/Invoke works; workspace detection reuses the tf.exe 18 cache with the
  20.256 client; one design consequence: PAT must have Full access (read-only PAT impossible).
- Next: clean-machine test by the developer, Gate B, then step 5 (`docs/KICKOFF-CLOUD.md`).
- Blockers: Gate B (developer); `tests/golden/help/` not yet captured; repository not yet pushed.
- Later the same day: Gate B passed on a clean Windows Server; help captured and summarized in
  `docs/commands.md` (SPEC.md corrected: `/encoding`, `/noautoresolve`, resolve names, `/format:xml`,
  `view /console`); step 5 done: `docs/KICKOFF-CLOUD.md` written, "Needs the user" lists the cloud
  prerequisites. Local phase complete.
- Help capture re-run from the repository root with the developer's consent (no sign-in needed):
  88 cases, committed; integrity test passes.
- Next: developer pushes `main` and sets the secrets; cloud agent starts milestone 2.
- Blockers: none.

### 2026-10-07 – specification and golden files (claude.ai conversation)

- Done: SPEC.md, license decision, test project seeded, golden files captured (basic, pending changes,
  server-writing scenarios, long history).
- Next: local session per `docs/KICKOFF-LOCAL.md`.
- Blockers: none.
