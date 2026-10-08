# Kickoff – cloud phase (milestones 2–5)

You are Claude Code running in the cloud (Linux) in the `tfportable` repository. You can write
code and run `dotnet build`, but you cannot run `tfp.exe`, Costura, the Credential Manager or any
test that talks to Windows or to the test collection. **GitHub Actions on `windows-latest` is your
only real test run.** The developer reviews pull requests and is not available for questions in
real time; everything you need is in the repository.

Read before you start, in this order:

1. `CLAUDE.md` (rules), `docs/PROGRESS.md` (state, decisions, open questions, what needs the user).
2. `docs/SPEC.md` (Czech): for milestone 2 the sections "Autentizace", "Detekce workspace",
   "Společné konvence CLI", "Rozšíření pro AI", "Chybové hlášky"; for 3 and 4 "Katalog příkazů"
   plus the per-command rows; for 5 "Integrace s Claude Code" and "Testování".
3. `docs/compat.md` (how the original behaves, what TfPortable does differently),
   `docs/commands.md` (switches per command), `docs/json-schema.md` (JSON output),
   `docs/golden-format.md` (golden files and how tests compare).
4. The code of the spike in `src/TfPortable/` (milestone 1): `Auth/`, `Cli/`, `Context/`,
   `Commands/`, `Errors/`, `Output/`. It works end to end for `login`, `logout`, `whoami`,
   `status` (brief) and is the base to extend, not to rewrite.

## Working loop

- One branch and one pull request per milestone (`m2-core`, `m3-workspace`, `m4-history`,
  `m5-integration`); commit in small steps with clear messages. Open the PR early as a draft so
  that CI runs on every push.
- After each push: `gh run list --branch <branch> --limit 1`, then
  `gh run view <id> --log-failed` for failures. Fix, push, repeat.
- Integration and golden tests run only when the secrets `TFP_TEST_COLLECTION` and `TFP_TEST_PAT`
  exist in the repository (see "Needs the user"); without them they are skipped and CI stays green,
  which is **not** a pass for milestones 3 and 4. Check the test summary in the run for "Skipped".
- At the end of every session (or when a milestone changes state) update `docs/PROGRESS.md`:
  status table, session log entry, decisions, open questions, "Next steps" written for an agent
  with no context. Commit it with the work.
- Stop and write the reason under "Needs the user" in PROGRESS.md when (CLAUDE.md): the same CI
  failure happens three times in a row; SPEC.md contradicts the golden files in a way that changes
  the design; something needs secrets, Azure DevOps settings, a run of a `tools/` script, a legal
  decision or a gate approval.
- Never put a PAT in code, tests, logs, commits, issues or output. Never enable System.Net or
  TeamFoundation tracing (it logs the `Authorization` header). Never run anything against a
  collection other than `TFP_TEST_COLLECTION`.

## What the spike established (do not re-discover)

- The PAT must have **Full access**; the TFVC SOAP endpoints refuse custom-scope tokens
  (TF30063 from `TfsHttpWebRequest.EnsureTokenProvider`). REST accepts them, which is why
  `login` verifies over REST first. `TFP0103` explains the case. Details: PROGRESS.md decisions,
  SPEC.md "Scope PAT".
- Connection: `new VssCredentials(new WindowsCredential(false), new VssBasicCredential("", pat), CredentialPromptType.DoNotPrompt)`
  into `TfsTeamProjectCollection`; `EnsureAuthenticated()` inside `ContextResolver.Resolve`.
- Workspace detection reads the local cache written by tf.exe 18 with the 20.256 client
  (`Workstation.Current.GetLocalWorkspaceInfo`, walking up to the drive root); fallback to
  `TFP_COLLECTION`, the single stored PAT, then `QueryWorkspaces` by computer name.
- In a local workspace the client library pends an edit of a tracked file during the scan;
  adds and deletes stay candidates until `add`/`delete`/`reconcile /promote`.
- Costura: the exclusion list in `FodyWeavers.xml` is verified for `login`, `whoami`, `status`.
  A new command that needs an excluded assembly fails with `FileNotFoundException` naming it;
  remove it from the list. CI should run the packaged `tfp.exe` **copied alone into an empty
  folder** for at least `version` and the golden tests, because excluded assemblies are still
  present in `bin/`.
- Exit codes follow the original per command (`docs/compat.md` §1.1); only 110 and 120 are
  extensions. Output is text in the tf.exe shape by default, JSON only with `/format:json`.
- Known small gaps left by the spike (milestone 2 items): JSON envelope `collection` is null when
  the error happens before the context is resolved; `logout` without `/collection` outside a
  workspace should fall back to the single stored PAT like `whoami`; an interrupted PAT prompt
  (Ctrl+C) ends silently; `status /format:detailed`, `/user`, `/shelveset` return TFP0002 in the
  spike build; the parser is minimal (no `@file`, no per-command alias table, no versionspec or
  itemspec types).

## Milestone 2 – core

Goal: everything a command needs, so that milestones 3 and 4 only add commands.

- **Parser** (`Cli/`): tokenizer and switch table per command from `docs/commands.md`; `/` and `-`;
  case-insensitive names; short forms (`-R`, `-I`, `-V`/`/v:`, `-F`, `-U`, `-W`, `-S`, `-O`, `-P`,
  `-N`, `-K`, `-Y`, `-M`, `-X`, `-?`/`-H`); aliases (`stat`, `edit`/`co`, `del`/`rm`, `move`/`ren`,
  `diff`, `info`/`properties`, `hist`, `lv`); optional `vc` prefix; `@file` argument files (UTF-8,
  one argument per line, relative to the current directory); `VersionSpec` (`C`, bare number, `D`
  with the parsing order of SPEC.md, `L[@scope]`, `T`, `W[name[;owner]]`, ranges `a~b`);
  `ItemSpec` (local path normalization `/`→`\`, server paths `$/`, wildcards, `;version` suffix,
  `;Xn` deletion ids); MSYS converted-path detection (already in `CommandLine`, keep the tests).
  Unknown command exit 2, unknown switch or value exit 100, both listing what is valid.
- **Context** (`Context/`): keep the detection order; add `/workspace:name[;owner]` with display
  name or unique name; `TFP0203` for a local path outside the mappings; `TFP0204` for server
  workspaces (exists). Commands that need only a collection (`history`, `changeset`, `view`,
  `dir`, `labels`, `branches`, `merge /candidate`, `merges`, `workspaces`) must work outside a
  workspace with `/collection` or a single stored PAT.
- **Errors** (`Errors/`): the full catalog from SPEC.md with default exit codes; per-command
  overrides from `docs/compat.md` §1.1; `TfCode` regex `\b(TF|VS)\d{5,6}\b`; `ActivityId` from
  server responses; per-item errors that do not stop the command (exit 1); `TFP0900` with
  `%TEMP%\TfPortable\last-error.txt`; `/verbose` prints the stack trace (never headers).
- **Output** (`Output/`): text writer with the table and block helpers of SPEC.md "Tvar textového
  výstupu"; ISO 8601 dates with offset; JSON envelope per `docs/json-schema.md` with `collection`
  and `workspace` filled as soon as they are known (also on errors); warnings `WARNING TFPxxxx:`.
- **Commands**: `login` (`/pat:-`, compatibility `/login:user,pat` with the warning), `logout`
  (fallback to the single stored PAT), `whoami`, `help` (full catalog incl. `/format:json`),
  `version`. `help <command>` from the switch table.
- **Testability**: access to the client library only through an interface (e.g. `ITfvcClient`)
  so that parser, formatting and error mapping have unit tests without a server.
- **CI**: add a step that copies `tfp.exe` alone into an empty folder and runs `tfp version` and
  `tfp help /format:json`; keep the artifact.
- Definition of done (CLAUDE.md): parser unit tests; golden cases of the `general` group pass
  (`no-arguments`, `help`, `help-status`, `unknown-command`, `unknown-switch`, `vc-prefix`,
  `dash-switches`, `short-switches`, `outside-workspace` with exit 120 per compat.md);
  `docs/json-schema.md` updated for `login`/`logout`/`whoami`/`help`/`version`.

## Milestone 3 – workspace commands

`status` (detailed, `/user`, `/shelveset`), `add`, `checkout`/`edit`, `delete`, `rename`/`move`,
`undo`, `get`, `resolve`, `reconcile`, `localversions`, `workfold` (listing), `workspaces`.

- Behavior and output shape: SPEC.md rows, `docs/compat.md` §3–§8, `docs/commands.md`.
- `get`: `TFP0402` for writable files without pending change (list + `/overwrite` hint),
  `TFP0403` for conflicts (exit 1 as the original), `/preview` prints `Getting`/`Replacing`/
  `Deleting` lines as the original.
- `reconcile /clean` without `/preview` requires `/noprompt` (`TFP0405`). Verify in an
  integration test why `/promote` promoted nothing in the golden run (`reconcile/promote`).
- `add /recursive` with `.tfignore` matches: skipped items listed, exit 1 (unverified, open question).
- Golden fixture (`tests/TfPortable.IntegrationTests`): creates a local workspace for the test
  project with the client library (not with tfp), maps `$/TfsPortableTestRepo` to a temp folder,
  runs the cases of `index.json` in order, undoes everything at the end. Normalization and
  comparison per `docs/golden-format.md` §7–8.
- Golden cases: `status/*` from `index.json` (`clean`, `clean-detailed`, `server-path`,
  `detected-adds`, `pending`, `pending-detailed`, `after-undo`), `add/*`, `checkout/*`,
  `delete/*`, `rename/*`, `undo/*`, `get/preview`, `get/up-to-date`, `get/with-pending`,
  `resolve/no-conflicts`, `localversions/*`, `workfold/current`, `workspaces/list`,
  `workspaces/detailed`, `workspaces/other-computer`. Shape-only (state from out-of-scope
  commands): `status/*` of the extra runs, `get/*`, `resolve/*`, `reconcile/*` of the extra runs,
  `workspaces/with-server-workspace`, `workfold/list-cloaked`.

## Milestone 4 – history and content

`history` (with the search extensions and the 256 limit + `truncated`), `changeset` (+ `/diff`),
`view` (encoding handling, binary via `/output`, `TFP0303`), `difference`/`diff` (unified and
brief; `Microsoft.TeamFoundation.VersionControl.Client.Difference` or an own Myers diff),
`dir`, `properties`/`info`, `labels`, `branches`, `merge /candidate`.

- Behavior and shape: SPEC.md rows, `docs/compat.md` §9–§14, `docs/commands.md`.
- `history`: default slot mode; unknown path exit 0 with the empty-result message; unknown
  `/user` exit 100 (TF14045); `/format:xml` rejected, `json` accepted; comment never truncated.
- `view/czech-name`: produce correct UTF-8 (the golden text is a bug of the original).
- Golden cases: `history/*` (long cases compare the first 256 rows), `changeset/*` except
  `edit-comment`/`after-comment-edit` (TFP0003 for `/comment`), `view/*`, `difference/*` except
  `context-format` (TFP0002), `dir/*`, `properties/*`, `labels/*`, `branches/*`,
  `merge/candidate`, `merge/candidate-before-merge`, `merges/*` (phase 2, shape only).

## Milestone 5 – integration and release

- `docs/CLAUDE.md`: the template for users from SPEC.md "Šablona do CLAUDE.md" plus the
  permissions snippet.
- README.md keeps the structure the developer asked for: why the project exists (the main reason
  is the unreliable, often impossible sign-in of `tf.exe`; portability and output come second), install, sign-in
  with Full access, usage examples, the command table with one line per command, links to
  `docs/commands.md` (every switch), `docs/compat.md` and `docs/json-schema.md`. When a milestone
  adds or changes a command, update the README table and `docs/commands.md` in the same pull
  request; the README must never list a command as available before it is implemented
  (the "Status" note says which milestone is done).
- Compatibility test set from SPEC.md "Testování" (the commands of the current Claude Code
  instructions: `vc changeset <n> -noprompt`, `vc view "D:/…" -version:C<n> -noprompt`,
  `vc workspaces -noprompt`, `status -recursive`, `diff`, `add`, `delete`, `rename`).
- `tfp help` complete; `tfp version`; all errors from the catalog; `docs/json-schema.md` final.
- Release: tag `v0.1.0` after the developer's approval (gate); the workflow publishes `tfp.exe`
  and its SHA-256. Check THIRD-PARTY-NOTICES against the embedded assemblies
  (`FodyWeavers.xml` exclusions change what is embedded).
- Update PROGRESS.md for the handover to phase 2 (separate brief).

## Needs the user (cannot be done from the cloud)

- Repository secrets `TFP_TEST_COLLECTION` (`https://dev.azure.com/<org>`) and `TFP_TEST_PAT`
  (a **Full access** PAT of a dedicated user who has access only to the test project, or a
  separate test organization). Without them milestones 3 and 4 cannot be verified.
- Review and merge the pull requests; decide on branch protection for `main`.
- Any new golden case (`docs/compat.md` §17 lists wanted ones) must be captured on Windows with
  the original tf.exe.
- Gate before the release of milestone 5.
