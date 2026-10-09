# TfPortable – progress

Read this first in every session. Update it at the end of every session (see CLAUDE.md).

## Status

| # | Milestone | Where | Status | Gate |
| --- | --- | --- | --- | --- |
| 0 | Spec refinement from golden files (`docs/compat.md`, SPEC.md updates) | local | done (Gate A passed 2026-10-07; help capture still to be reviewed) | user approves SPEC.md |
| 1 | Spike: `tfp whoami` + `tfp status` with PAT, Costura single exe, clean machine | local | done (clean-machine test passed, Gate B approved 2026-10-08) | user approves architecture |
| 2 | Core: parser, workspace context, errors, JSON envelope, `login`/`logout`/`whoami` | cloud + CI | done: [PR #1](https://github.com/Gh61/TfPortable/pull/1) merged into `main` on 2026-10-08 after an independent review | – |
| 3 | Workspace commands: status, add, checkout, delete, rename, undo, get, resolve, reconcile, localversions, workfold, workspaces | cloud + CI | done: [PR #2](https://github.com/Gh61/TfPortable/pull/2) merged into `main` on 2026-10-08 after an independent review (findings under Decisions) | – |
| 4 | History and content: history (incl. search), changeset (/diff), view, diff, dir, properties, labels, branches, merge /candidate | cloud + CI | done: [PR #3](https://github.com/Gh61/TfPortable/pull/3) merged into `main` on 2026-10-08 (merge commit ff64062) after an independent review (findings under Decisions) | – |
| 5 | Integration: CLAUDE.md template for users, compat tests, GitHub release of `tfp.exe` | cloud + CI | done: [PR #4](https://github.com/Gh61/TfPortable/pull/4) (docs/CLAUDE.md, compatibility tests, embedded-assembly check, THIRD-PARTY-NOTICES) reviewed, CI green and merged into `main` on 2026-10-08; release [v0.1.0](https://github.com/Gh61/TfPortable/releases/tag/v0.1.0) published the same day (developer approved; `tfp.exe` + SHA-256 from the CI run on 2a1c007) | user approved the release on 2026-10-08 |
| P2 | Phase 2: `merge`, `branch`, `rollback`, `undelete` as pending changes, `merges`, `folderdiff`, `annotate` | cloud + CI | done: [PR #12](https://github.com/Gh61/TfPortable/pull/12) reviewed by an independent agent (findings under Decisions), CI green (195 unit and 86 integration tests) and merged into `main` on 2026-10-08 with a merge commit | – |
| P3 | Phase 3: protection levels, `checkin`, `shelve`/`shelvesets`/`unshelve`, `label`/`unlabel`, `lock` | cloud + CI | done: [PR #9](https://github.com/Gh61/TfPortable/pull/9) merged into `main` on 2026-10-08 (merge commit 3c92ea2) after an independent review (findings under Decisions); 187 unit and 82 integration tests green | release `v0.2.0` (developer) |
| GC | Golden comparison: tfp against the captures of phases 2, 3, 4 and the extra2 gaps (2026-10-09) | cloud + CI | done: [PR #14](https://github.com/Gh61/TfPortable/pull/14) reviewed by an independent agent (findings under Decisions), CI green (197 unit and 90 integration tests) and merged into `main` on 2026-10-09 with a merge commit; fixes and recorded deviations in `docs/compat.md` §20 | – |

Statuses: not started, in progress, blocked, done.

## Next steps

0. The golden comparison is merged ([PR #14](https://github.com/Gh61/TfPortable/pull/14)): the code fixes and
   the recorded deviations are in `docs/compat.md` §20; the captures of all four runs are committed
   (`index-phase2-2610091535.json`, `index-extra2-2610091539.json`, `index-phase3-2610091137.json`,
   `index-phase4-2610091138.json`).
1. Phase 3 is merged ([PR #9](https://github.com/Gh61/TfPortable/pull/9)). Release `v0.2.0` is the
   developer's gate (step 3).
2. Optional follow-ups the comparison surfaced (not started): golden tests that load the phase 2 and extra2
   cases explicitly (the golden runner only runs cases listed by `group/name`); the `get` over a writable
   file case (TFP0402) is still uncaptured.
3. Release `v0.2.0` (developer gate): run the CI workflow manually on `main` with `release_tag`.
4. If a package changes later, update `src/TfPortable/embedded-assemblies.txt` from the output of
   the CI step "Check the embedded assemblies against THIRD-PARTY-NOTICES" and THIRD-PARTY-NOTICES.
5. Phase 4 is undecided (README "Phases and progress", `docs/DEVELOPER-TODO.md`).

## Needs the user

The checklist with the details lives in `docs/DEVELOPER-TODO.md` (release `v0.2.0`, checkin policies on the
test project, the golden captures of phases 3 and 4 in one sitting, the permanent shelveset, the decisions
before phase 4). The items below are kept for history.

- A lawyer's look at LICENSE-EXCEPTION and THIRD-PARTY-NOTICES (Decisions 2026-10-07) is still
  open; `v0.1.0` was released with the current texts.
- Phase 3 answers of 2026-10-08: CI may check in under `$/TfsPortableTestRepo/CI/<run>` (done, with
  a delete checkin at the end), the developer cannot easily run tf.exe for new captures (so phase 3
  follows the existing captures and the Microsoft docs; `tools/capture-tf-golden-phase3.ps1` is ready
  for later), and work item #1 (a bug) exists for `checkin /associate`.
- Release `v0.2.0` (see "Next steps" 3). The golden captures of 2026-10-09 are done and compared.
- The permanent shelveset `tfp-golden-shelveset` was created by the developer on 2026-10-08;
  `ShelvesetTests` finds it by name (any owner) and fails with the visible shelveset names when it
  does not.
- Set up the `main` ruleset the developer agreed to on 2026-10-08: pull request required, the CI
  check `Build, test, package (windows-latest)` required (branch up to date), force pushes and
  deletions blocked, no required approvals (the agent merges after an independent review), no
  linear-history requirement (merge commits are the convention).
- Later releases: run the CI workflow manually on `main` with `release_tag` (Actions → CI → Run
  workflow), or push a tag `v*` from a machine that can push tags.
- The golden fixture creates a temporary local workspace named `tfp-golden-<id>` on the CI runner
  (owner: the PAT's user) and deletes it at the end; a run that dies mid-way may leave one behind
  in the collection (harmless, but visible in `tf workspaces /owner:*`). Delete such leftovers
  occasionally.
- The CI workflow now runs once per commit on a PR branch (`push` is limited to `main`, PR #9) and
  runs queue up behind each other (`concurrency` group): the integration tests of two runs at the same
  time interfere through the shared test collection.


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
| 2026-10-08 | Gate B passed: `tfp.exe` alone on a clean Windows Server (10.0.20348) without Visual Studio ran `version`, `login` (full-access PAT), `whoami`, `logout`; the developer approved the architecture (single Costura exe, PAT in Credential Manager, SOAP client with Basic auth). Note for milestone 2: an interrupted PAT prompt (Ctrl+C) ends silently. (`logout`/`login` without `/collection` now fall back to the single stored PAT, and with several stored PATs list them; done 2026-10-08) | Developer's test on 2026-10-08 |
| 2026-10-08 | Help of tf.exe 18.10 captured (`tests/golden/help/`, 87 files, `index-help.json`; the developer ran the script from another directory, files were moved into the repo; no personal data). `docs/commands.md` lists every command's switches and what TfPortable does with them. Corrections to SPEC.md from the help: `add`/`checkout` use `/encoding:`, not `/type:`; `get` has `/noautoresolve`; `resolve /auto` canonical values are `AutoMerge|AutoMergeForced|TakeTheirs|KeepYours|OverwriteLocal|DeleteConflict|KeepYoursRenameTheirs` with the documentation's `Accept*` names as aliases (golden captures used `AcceptYours`/`AcceptTheirs`/`AcceptMerge` successfully); `status`/`workspaces`/`shelvesets` know `/format:xml` (not supported, `json` instead); `view` has `/console` (accepted, no-op) and `/recursive`; `/lock:checkin|checkout` on `add`/`checkout`/`delete`/`rename` is rejected because locks write to the server | KICKOFF-LOCAL step 2 follow-up; the implementer no longer guesses switch lists |
| 2026-10-08 | README structure requested by the developer: why the project exists, how it is used, examples of basic commands, a command table with one-line descriptions, links to `docs/commands.md` / `docs/compat.md` / `docs/json-schema.md`. Rewritten accordingly; KICKOFF-CLOUD milestone 5 keeps it in sync with each milestone | Developer's request |
| 2026-10-08 | `history` added to the spike build on the developer's request (to verify a PAT's read access): brief table and JSON, `/recursive`, `/stopafter`, `/version` (single and range), `/user`, `/sort`, `/slotmode`/`/itemmode`, default slot mode, 256-entry limit with notice and `truncated`, local paths mapped through the workspace (TFP0203 otherwise). Verified against golden `history/range`, `short-version`, `renamed-file`, `itemmode`, `sort-ascending`, `not-found` (exit 0), `long-default`/`long-stopafter-256` (ends at C138), `user-not-found` (exit 100). Sorting must be done by the server (`QueryHistory(..., sortAscending)`), the limit applies after it. `/format:detailed` and the search extensions remain milestone 4 | Developer's request; milestone 4 extends it |
| 2026-10-07 | Default output of tfp is text in the shape of tf.exe; JSON only with `/format:json` (tf.exe has no JSON at all, `/format:` accepts only `brief`/`detailed`, plus `unified`/`context` for `difference`; anything else is TF10120, exit 100) | Confirmed with the developer |
| 2026-10-08 | Switch table lives in code (`Cli/CommandCatalog.cs`): every tf.exe command with aliases, phase (1, 2, null for tool and out-of-scope commands), status (implemented, planned, out-of-scope) and switches (name, short form, value rule, allowed values, extension flag); `help` text and JSON, switch validation and the MSYS detection all derive from it. Out-of-scope and planned commands answer TFP0003 (exit 100) with the phase in the message; an unknown name stays TFP0001 (exit 2); `tf git`/`tf settings` groups are TFP0003 | One source for help, parser and docs; `docs/commands.md` is the human copy |
| 2026-10-08 | Switch values are validated against the catalog (`status /format:xml` → TFP0002 listing `brief, detailed, json`); short forms are per command (`-X` is `/slotmode` of `history` only); `/format:json` is accepted by every command. A switch without its required value or with an unexpected value is TFP0002 | Matches the original's exit 100 for TF10120 and "option not allowed"; the hint tells the AI what is valid |
| 2026-10-08 | `@file` argument files: UTF-8, one argument per line, blank lines skipped, path relative to the current directory, not nested; missing file is TFP0002 with the path in `item` | SPEC.md "Společné konvence CLI" |
| 2026-10-08 | Versionspec parsing is TfPortable's own (`Cli/VersionSpecParser.cs`), producing the library's `VersionSpec` objects: dates try ISO 8601 formats first (`yyyy-MM-dd`, with `T` time, with offset), then the current culture, then the invariant culture; a bare `W` needs a workspace (TFP0302 otherwise); TFP0302 names the reason and the syntax | SPEC.md "Versionspec"; the library's `ParseSingleSpec` uses the current culture only |
| 2026-10-08 | Itemspec parsing (`Cli/ItemSpecParser.cs`): `$\a\b` → `$/a/b`, trailing separator removed; local paths made absolute with the platform separator, wildcards only in the last segment (TFP0002 otherwise, because `Path.GetFullPath` rejects them on .NET Framework); `;X<n>` and one `;<versionspec>` suffix in any order | SPEC.md "Itemspec" |
| 2026-10-08 | Per-command exit codes are applied in one place (`ErrorCodes.ExitCodeFor` in `Program.Run`) from the compat.md §1.1 table: TFP0301 is 1 for add/delete/view/dir/undelete/unlabel, 100 for rename/changeset/label, 0 for history; every other code keeps its catalog default. Per-item errors go through `OutputWriter.ItemError` (stderr at once in text mode, collected in JSON mode) and force exit 1 | SPEC.md "Pravidla"; commands never hard-code exit numbers |
| 2026-10-08 | ActivityId is taken from the exception data (any key containing `ActivityId`, walking inner exceptions) or from an `ActivityId: <guid>` text in the message; null otherwise. The client library does not expose it as a property | Spike observation; real server responses will show whether the key appears |
| 2026-10-08 | The JSON envelope's `collection` and `workspace` are set by `ContextResolver` as soon as they are known (before authentication, before the server-workspace check), so error envelopes carry them; `OutputWriter.Command` is set from `CommandLine.PeekCommand(args)` before parsing, so a parse error names the command too | Milestone 2 gap from the spike closed |
| 2026-10-08 | Client library behind `ITfvcClient`/`ITfvcWorkspace` (`Context/`) returning DTOs (`IdentityInfo`, `WorkspaceSummary`, `MappingInfo`, `PendingChangeInfo`, `ChangesetInfo`); `ContextResolver.ClientFactory` is replaceable for tests; commands keep static `Render`/`ToJson` functions over DTOs so text and JSON shape have unit tests without a server. The local workspace cache (`Workstation.Current`) stays a direct call: it is local and needs no server | SPEC.md "Požadavky na implementaci" (`ITfvcClient`) |
| 2026-10-08 | `/login:user,pat` is honored for one run (the user part is ignored, the PAT identifies the user), never stored, with a warning; `whoami` then prints "PAT stored : no" and `patStoredAt` is null. `/workspace:name;owner` goes through `GetWorkspace(name, owner)` (cache first, then server), an unknown one is TFP0201 with a hint to `workspaces /owner:*` | SPEC.md "Autentizace" (compatibility switch) |
| 2026-10-08 | Ctrl+C in the hidden PAT prompt: `Console.TreatControlCAsInput` is set during the prompt and Ctrl+C becomes TFP0101 (exit 110) with a message | Milestone 2 note from Gate B (an interrupted prompt ended silently) |
| 2026-10-08 | Golden test infrastructure in `tests/TfPortable.IntegrationTests/Golden/`: `GoldenCase` (index + case file), `TfpRunner` (separate process, stdin closed, `TFP_COLLECTION`/`MSYSTEM`/`EXEPATH` removed from the environment, `TFP_EXE` override), `GoldenText` (normalization and extraction of §7–8), `GoldenComparison` with `GoldenChecks` (`Shape` = exit code + stream emptiness for help and error cases; `All` adds items, changeset numbers, deletion ids, change types, counts), `GoldenWorkspaceFixture` (collection fixture; creates a local workspace with the client library in `%TEMP%\tfp-golden-<id>\TfsPortableTestRepo`, runs `Get`, stores the PAT through `tfp login /pat:-`, deletes everything at the end; nothing happens until `EnsureReady()`). `general/outside-workspace` expects 120 per compat.md | KICKOFF-CLOUD milestone 2 definition of done; milestones 3 and 4 reuse it |
| 2026-10-08 | CI runs the packaged `tfp.exe` copied alone into an empty folder: `version` (0), `help /format:json` (0, parsed, ≥ 20 commands), `status` outside a workspace (120), unknown command (2) | KICKOFF-CLOUD milestone 2; excluded Costura assemblies are still in `bin/` |
| 2026-10-08 | Newtonsoft types a null string assigned through the implicit conversion as `JTokenType.String` with a null value; it still serializes as `null`. Tests compare through `JsonAssert.Null`; the JSON contract is unaffected | First CI run of milestone 2 |
| 2026-10-08 | The developer authorized the cloud agent to merge its own pull requests after an independent review: every PR is reviewed by a separate agent without session context (diff of the branch against `main`, rules of CLAUDE.md, SPEC.md and compat.md), the blocking and should-fix findings are fixed, CI must be green, then the agent merges with a merge commit and continues with the next milestone. The agent stops only when it is completely done or when a decision of the developer is needed (gates, secrets, legal) | Developer's instruction of 2026-10-08 |
| 2026-10-08 | Review of PR #1 (independent agent) found and the fixes were applied before the merge: (1) a switch value is never echoed for `/pat` and `/login` (`SwitchSpec.Secret`), the MSYS message prints only the path part; (2) a global switch error without a command (`tfp /format:xml`) was a NullReferenceException; (3) `history` of an unknown path renders the empty result itself (the `history:TFP0301 → 0` exit override is gone, an error envelope can no longer have `ok: true`); (4) `OutputWriter.IsoDate(DateTime)` returns null for default dates instead of throwing in UTC+ zones (detected changes carry no creation date); (5) `/collection` of another collection ignores the cached workspace of the current folder; (6) `;X<n>` of a `history` itemspec reaches `QueryHistory`; (7) `/format:json` inside an `@file` switches the envelope on; (8) `PatPrompt.IsInteractive` keys on stdin only, as SPEC.md step 3 says | Review findings; CI cannot see (4) because the runner is UTC |
| 2026-10-08 | The cloud environment can install the .NET 10 SDK from the Ubuntu package feed (`apt-get install dotnet-sdk-10.0`; `dot.net` and `builds.dotnet.microsoft.com` are blocked by the proxy) and restore from nuget.org, so `dotnet build` of the whole solution works on Linux; unit tests still need Windows (net48). API shapes of the client library were checked with a small MetadataLoadContext dump tool in the session scratchpad (not committed) | Working method of the cloud sessions |

| 2026-10-08 | Milestone 3 shapes: the client library's events (`NewPendingChange`, `UndonePendingChange`, `Getting`, `NonFatalError`) are collected per operation by `TfvcClient.EventCollector` and become `PendResult`/`GetResult` DTOs; per-item failures map through `WorkspaceCommandSupport.ToError` (not found → TFP0301, already versioned/pending → TFP0401, locked → TFP0404, else TFP0501) and are reported with `OutputWriter.ItemError` (exit 1); `rename` turns a failure into a fatal TFP0301 (exit 100, compat §1.1). Grouped listings always print the absolute folder header (SPEC.md wins over the compat.md draft row, corrected). Listings are sorted in invariant-culture, case-insensitive order so that `_golden` precedes `README.md` as in the original. `.tfignore` is passed to `PendAdd` only for `/recursive` or wildcard adds | KICKOFF-CLOUD milestone 3 |
| 2026-10-08 | Golden comparison gained `GoldenChecks.Lines` (every stdout line after a Czech → English dictionary of the golden vocabulary, relative folder headers resolved against the capture's cwd, label spacing and column padding collapsed) and `ItemsSubset` (golden items ⊆ tfp items, for listings whose server state the later golden runs extended, e.g. `localversions` after C84+). The milestone 3 scenario of `index.json` runs as one ordered test (`WorkspaceGoldenTests`) that recreates the capture's files (`Main\_golden\*`, `Main\src\bin\Debug\ignored.txt`, the Program.cs edit) and ends with `undo` | docs/golden-format.md §6–8 |
| 2026-10-08 | The single-file `tfp.exe` embeds 33 assemblies plus Costura's loader resources (CI run 37787396329); the list is committed as `src/TfPortable/embedded-assemblies.txt` and CI fails when it drifts. THIRD-PARTY-NOTICES section 3 now lists the embedded .NET Foundation libraries (MIT) and no longer lists Microsoft.Data.SqlClient and System.IdentityModel.Tokens.Jwt, which Costura excludes | KICKOFF-CLOUD milestone 5 release preparation |
| 2026-10-08 | Review of PR #2 (independent agent, no blocking findings) and the fixes applied before the merge: (1) `reconcile` without an itemspec covers the current folder with the depth of `/recursive`, never the whole workspace (`/clean` deletes files); (2) a server workspace is TFP0204 only for commands that need a workspace, the others run with the collection alone as compat.md §3 says; (3) the second argument of `GetPendingChangesWithCandidates` is `includeDownloadInfo`, so `/nodetect` now calls `GetPendingChanges` and the default no longer asks for download URLs; (4) "No pending changes were found" of `undo` maps to TFP0301 in `ToError`; (5) `resolve` prints the `Getting`/`Replacing` lines of the resolution under the folder header as the original (`ResolveResult.Operations`); (6) a failure without an item no longer starts with ": " | Review findings |

| 2026-10-08 | Milestone 4: `difference` uses TfPortable's own Myers line diff (`Output/UnifiedDiff.cs`, three lines of context) instead of the client library's diff classes, so the output does not depend on their formatting; `view` decodes by the server encoding with BOM priority (`Output/TextContent`); `history` looks the item up first (`GetItem`) to decide on the Change column and to turn an unknown path into the empty result; the search extensions filter on the client over at most 5000 changesets; `changeset /diff` compares each file with `C<n-1>` of the same or the source item | KICKOFF-CLOUD milestone 4; docs/compat.md milestone 4 notes |
| 2026-10-08 | Golden comparison gained `ChangesetsSubset` and a relative-path resolution for `File:` / `+++ Local:` lines of diffs; the milestone 4 cases compare line by line where the seeded state is stable (changeset blocks, view content, `dir /version:C75`, `labels /format:detailed v1.0`, `properties` at C75, `difference` of C75~C81) and shape plus subsets where later golden runs extended the server (history of Main, dir of Main, branches, merge candidates, labels list) | docs/golden-format.md §6 |
| 2026-10-08 | First CI run of milestone 4 (18 failures) fixed: (1) the seeded changesets belong to the developer, so the fixture maps the owner display name of the oldest changeset of `$/TfsPortableTestRepo` to `<OWNER>` as well as the CI identity; (2) the Czech → English dictionary of `GoldenText` is applied longest first and on whole words only (`Typ` no longer turns tfp's `Type` into `Typee`); (3) `properties` cases skip the stream check (the original's deprecation warning on stderr is an intended deviation, compat §1.3); (4) "Item … was not found in source control at version …" of the client library (no TF code, not an `ItemNotFoundException`) maps to TFP0301, so `view` exits 1 and `history` prints the empty result; (5) `changeset` lists items by parent folder, then name, as tf.exe (a folder's direct children before its subfolders' items); (6) the unified diff matches the common prefix and suffix before running Myers, as tf.exe: a block inserted in front of a repeated `}` is reported before that line; (7) `history/brief` and `merge/candidate` compare shape only: the 300 padding changesets of the `long-default` capture push the seed changesets past the 256 limit, and changeset 83 was merged to Release by the later golden changeset 91 | CI log of run 37785311952 |
| 2026-10-08 | Review of PR #3 (independent agent, no blocking findings) and the fixes applied before the merge: (1) linked work items are read only for `changeset` and `history /format:detailed` (`Changeset.AssociatedWorkItems` is a server call per access), `/workitem` resolves the work item's links once through `ILinking.GetReferencingArtifacts` with a per-changeset fallback; (2) `changeset /diff` compares a merged or branched file with its own previous version, only a rename looks under the old name; (3) `history` and `view` accept wildcards and `history` of a deleted item works (lookup through `GetItems` with deleted items); (4) the unified diff treats CRLF/LF and a missing final newline as differences unless `/ignoreeol` (`\ No newline at end of file` marker) and is now linear-space Myers (middle snake, deletes before inserts in a block; the trace version needed O(D·(N+M)) memory); (5) `difference /shelveset` prints the change line and the `File:` line as the original and the local side is decoded with the server encoding; (6) a single `/version` with two itemspecs applies to both sides; (7) the history continuation hint uses the scanned range and the sort order, and the 256 limit is detected by fetching one row more; (8) `dir <file>` lists the file; `merge /candidate /format:detailed` reads the items per candidate; `properties` outside a workspace reports no lock; the client no longer fabricates a `TF14019` message; `view` strips the final CRLF. Not done (nits recorded as open questions): N+1 downloads of `view /recursive` and `difference /shelveset`, shelved content through a temp file, `branches` of a non-root item prints only the subtree | Review findings; the linear-space diff was cross-checked against a DP edit distance on 3000 random inputs in a scratch .NET 10 project |
| 2026-10-08 | Phase 3 brief agreed with the developer: `lock`/`unlock`, `shelvesets` and `unshelve` move into phase 3; the protection levels are specified in SPEC.md ("Úrovně ochrany"): L1 read-only, L2 everyday work (default), L3 everything, per command and switch, stored in `%APPDATA%\TfPortable\settings.json`, no environment variable (CI writes the file), changed only by `tfp protection set <n>` in an interactive terminal, blocked commands answer the new TFP0004 (exit 100). Message texts carry no semicolons and do not speak of "a person". README no longer presents the tool as Claude Code first: it is a tool for people, scripts and any AI agent | Developer's notes of 2026-10-08 |
| 2026-10-08 | Phases 3 and 4 defined by the developer: phase 3 is the everyday write path (`checkin`, `label`/`unlabel`, `shelve` without `/delete`), phase 4 the rest of tf.exe including irreversible server changes (undecided whether the tool should offer them). SPEC.md and README ("Phases and progress") describe them; the catalog carries the phase number of every refused command so TFP0003 names it. A protection-level model (L1 read-only, L2 everyday work, L3 everything; default L2; switchable only by a person in an interactive terminal) was proposed by the developer and is under discussion (see "Needs the user") | Developer's brief of 2026-10-08 |
| 2026-10-08 | Open questions closed after the release (branch `open-questions`): `/encoding` values are whatever the client library's `FileType` parser accepts (names, code pages, `binary`/`text`/`auto`; decompiled), the dead `ParseEncoding` is gone; `reconcile /promote` promotes adds and deletes without `/adds`/`/deletes` (the capture's empty result is recorded as a deviation); a recursive `add` leaves `.tfignore` matches out silently with exit 0; a modified file in a local workspace makes `get` of an older version a TFP0403 conflict; `history /workitem` is answered by the linking service with a PAT; the golden loader finds cases in every `index*.json`. Review nits of PR #3 done: `view` downloads through one listing (`DownloadItems`) and treats `/output` as a directory for `/recursive` and wildcards, shelved content streams without a temp file and without a second query (`PendingChangeInfo.Handle`), `branches` prints the whole tree with the requested item marked, `merge /candidate /version` filters by changeset number, `difference /encoding` overrides the decoding. All verified by `tests/TfPortable.IntegrationTests/OpenQuestionTests.cs` against the test collection | Developer's request to close the open questions |
| 2026-10-08 | Review of the open-questions branch (independent agent): `difference /encoding` resolves the value once (name or code page → web name, `binary`; unknown → TFP0002, the silent UTF-8 fallback is gone); SPEC.md's `add` row says exit 0 with `.tfignore` matches left out; a shelved change without content (rename-only, property, lock) no longer crashes `difference /shelveset` or `view /shelveset` (`DownloadShelved` returns null); the conflict test resolves with `TakeTheirs`, undoes, gets the latest and asserts that no conflict or pending change is left; the golden loader searches `index-extra.json` before the suffixed runs | Review findings |
| 2026-10-08 | The developer approved the release of milestone 5 (`v0.1.0`). A tag cannot be pushed from the cloud session (the session's git proxy accepts branch pushes only and refuses writes to the tags API), so the CI workflow gained a `workflow_dispatch` input `release_tag`: a manual run publishes the release for that tag from the chosen branch and creates the tag on its head when it does not exist; the tag push that follows re-runs the job, which finds the release and does nothing. A pushed tag still releases as before | Developer's instruction of 2026-10-08; the cloud environment's git proxy |
| 2026-10-08 | Protection levels implemented (PR #9): `Cli/ProtectionLevels` maps every catalog command to a level (a unit test checks that none is missing) and the switches that raise it (`checkin /override`, `undo /workspace`, `label /delete`, `shelve /delete` → 3; `workfold /map`, `/unmap`, `/cloak`, `/decloak` → 2; `merge /candidate` lowers `merge` to 1); `Settings/ToolSettings` reads `%APPDATA%\TfPortable\settings.json` (missing file → level 2, an invalid or unreadable file → TFP0004); `Program` loads the file only when a command needs more than reading and throws TFP0004 with the SPEC.md wording; `tfp protection` shows the level, `tfp protection set <n>` changes it in an interactive terminal only (`PatPrompt.IsInteractive`), raising asks to type the level number; `help <command>` and `whoami` print the level. The agent template denies `tfp protection set` | SPEC.md "Úrovně ochrany" |
| 2026-10-08 | Phase 3 commands (PR #9): `checkin` evaluates the changes first (`EvaluateCheckin2` with conflicts, notes and policies): conflicts are TFP0406, policy or note failures TFP0407 unless `/override:<reason>` (level 3) records a `PolicyOverrideInfo`; a policy that tfp cannot evaluate (its assembly is not embedded) counts as a failure, as in tf.exe without the policy installed; `/validate` stops after the evaluation; `/comment` is required (`@file` accepted); `/associate:<ids>` links the work items after the checkin over the REST work item API (`WorkItemTrackingHttpClient`, a "Fixed in Changeset" artifact link), because the SOAP checkin links work items only through the work item object model that tfp does not embed, so `Microsoft.TeamFoundation.WorkItemTracking.WebApi` is now embedded (250 KB); a failed link is TFP0501 with exit 1 and the changeset stays. `shelve` needs the name, accepts `/comment`, `/replace`, `/move`, `/recursive`, reports TFP0408 for an existing name and `/delete` at level 3; `shelvesets` defaults to the authenticated user (`/owner:*` everyone), a named shelveset that is missing is TFP0301 exit 100 as tf.exe; `unshelve` needs the name, merges with local changes unless `/nomerge`, open conflicts are TFP0403 (exit 1), a local change in the way (`UnshelveException`) TFP0401 with exit 100; `label` defaults the scope to the team project of the first item and the version to the workspace version for local paths (latest for server paths), `/child:fail` is the default, `/delete` is level 3; `unlabel` removes at the label's version (`LabelVersionSpec`, the library's documented choice), an item not in the label is TFP0301 exit 1; `lock` needs `/lock:` and lists the lock changes from the library's events. The `/lock:checkin|checkout` switches of `add`, `checkout`, `delete`, `rename` stay refused (unchanged scope) | docs/compat.md §16, docs/commands.md "Phase 3" |
| 2026-10-08 | CI tests of the write path (`Phase3Tests`) check in under `$/TfsPortableTestRepo/CI/<run>-<test>` and delete the folder with a last checkin; shelvesets and labels carry the run id and are deleted through `shelve /delete` and `label /delete` at level 3 (`ProtectionLevelScope` writes the runner's `%APPDATA%\TfPortable\settings.json` for the block and restores it), with the client library as the fallback cleanup. The `CI` folder itself stays on the server | Developer's answer (1) of 2026-10-08 |
| 2026-10-08 | CI runs are serialized (`concurrency: tfvc-test-collection`, no cancellation) and the `push` trigger is limited to `main`: the first phase 3 runs showed the twin runs of one commit interfering through the test collection (a lock of the other run visible in `properties`, the other run's CI checkin making the golden `get` not up to date). The lock test also locks a file of its own run under the CI folder instead of the shared `Main\data\counter.txt` | CI runs 37817091995 and 37818392002 |
| 2026-10-08 | Checkin policies (developer's decision, variant 3): tfp no longer asks the client library to evaluate policies (it would load them from a Visual Studio installation). `CheckinPolicies` reads the enabled policies of the team projects touched (`GetCheckinPoliciesForServerPaths`, the implementing class name through reflection because the display name is localized by the configuring Visual Studio) and evaluates "Work Items" (`/associate`) and "Changeset Comments" (`/comment`) itself; every other policy is TFP0409 "cannot be evaluated by tfp", a failed one TFP0407. `/override:<reason>` moved to level 2: Visual Studio shows policy failures as warnings and checks in after a reason is typed, and tfp records the same `PolicyOverrideInfo`. `checkin /format:json` lists every policy with its status so that the integration tests adapt to whatever the test project has configured (no policy = policy assertions skipped) | Developer's decision of 2026-10-08 |
| 2026-10-08 | Review of PR #11 (independent agent, one blocking finding): the policies were read from the legacy annotation key (`GetCheckinPoliciesForServerPaths`, obsolete) while current Visual Studio versions write the "client policies" key, so a policy configured today would have been invisible; tfp now reads the client key first and falls back to the legacy one, as the library's own evaluation does. Also: a policy is recognized by its implementing class (`.WorkItemPolicy`, `.CheckForCommentsPolicy`) and by the exact display name only when the class is unknown, so a "Work Item Query Policy" or a custom comments policy stays unknown; the phase 3 and 4 capture scripts no longer rely on `/associate` (not in the captured help) and the shelveset form of checkin carries no extra switches; commands.md no longer calls `/override` level 3; the cleanup checkins of the integration tests pass the override only, so work item 1 collects fewer links per run | Review findings |
| 2026-10-08 | Phase 2 started on the developer's brief ("implement phase 2, I thought it was done"): `merge`, `branch`, `rollback` and `undelete` pend changes only (level 2, reversible with `undo`), `merges`, `folderdiff` and `annotate` read (level 1). SPEC.md "Fáze 2" rewritten with the switch scope; the catalog rows of `shelvesets` and `unshelve` corrected to phase 3 | Developer's decision of 2026-10-08; the earlier phase table listed phase 2 as "planned, separate brief" |
| 2026-10-08 | `merge /preview` is the server's preview through the library's `NoMerge` merge option (the first draft listed the merge candidates, which ignored `/version` and the flags): the operations come from the library's Merging event (the only event it raises for merge and rollback operations) and are printed in the merge line shape (`preview: true` in JSON, same `changes` shape as a real merge); recorded in compat.md §15 and question 27 of `docs/tf-reference/questions.md` | Review of PR #12 (finding 2); CI run 117 showed that merge and rollback results were empty without the Merging event |
| 2026-10-08 | Review of PR #12 (independent agent, 1 blocking, 5 should-fix, 8 nits) and the fixes: (1) `merge /discard` was mapped to the library's preview option (`NoMerge`) and pended nothing, now `AlwaysAcceptMine`; (2) preview through `NoMerge` (above); (3) conflicts the server counted but the conflict query did not return are a summary TFP0403 as in `get`, so a merge or rollback with conflicts never exits 0; (4) `annotate` of a missing item exits 1 as documented (exit override); (5) `folderdiff` of a missing server folder is TFP0301 (exit 100, like the local side) instead of a clean diff; (6) `rollback /changeset` accepts changeset numbers only (TFP0302 otherwise); nits: a changeset without the item no longer crashes `annotate`, `merges /format:detailed` prints no detailed block for an empty list, failures reported both as event and as result are kept once, `branch /silent`, the 79-wide rule and the short annotate date are recorded in compat.md §15; not changed: README wording (true after the merge), merge conflict and `/discard` integration cases (added: `/discard`, `rollback /changeset` without itemspec, `folderdiff /filter`; a conflict needs checkins on both seeded branches and stays for the developer's capture) | Review findings |
| 2026-10-08 | `folderdiff` compares by content hash (the server's MD5 per item, computed for local files, `$tf` skipped) and relative path; one path compares a folder with its workspace counterpart; sections and the `Summary:` line follow the golden shape; `/filterLocalPathsOnly` is not supported | The golden capture shows the sections only; hash comparison needs no download |
| 2026-10-08 | `annotate` (extension) attributes each line through tfp's own Myers line diff over the item's history (oldest first, renames followed by item id, deletes skipped); columns Changeset, Owner, Date, Line; folder TFP0002, binary TFP0303, missing item TFP0301 with exit 1 | tf.exe has no annotate; the server has no blame API for TFVC |
| 2026-10-08 | `merges /format:detailed` prints the target changesets in the `history /format:detailed` shape (one `GetChangeset` per changeset); the brief table is Changeset, Merged in Changeset, Author, Date with `*` after a partial source changeset, sorted by source then target changeset | Golden `merges/*` show the brief table; the detailed shape is not captured |
| 2026-10-08 | `rollback`: exactly one of `/changeset` and `/toversion`; `/toversion` needs an itemspec; `/changeset` without items rolls back the whole workspace through the library's workspace-wide overload; nothing to do prints `There is nothing to roll back.`; conflicts are TFP0403 item errors with exit 1 like `merge` | Golden `rollback/changeset` works without an itemspec; the Microsoft docs require items for `/toversion` |
| 2026-10-08 | CI loop of PR #12 (runs 114–120): the client library raises only its Merging event for merge and rollback operations (neither NewPendingChange nor Getting), so the collector listens to it; the library's workspace-wide `Rollback` overload pends nothing on a local workspace, so `rollback /changeset` without items rolls back the mapped folders recursively; `rollback /toversion:<v>` needs `v` as the lower bound and Latest as the upper bound (the other order pends nothing, a null version is refused); `annotate` must match lines with line endings ignored; the folderdiff `/view` switch is validated by the command (the catalog enumeration rejected a comma-separated list); the phase 2 unit tests join the `ToolSettings` collection (the protection gate reads the settings file another class swaps) | CI runs on windows-latest; recorded in compat.md §15 and questions 27–33 |
| 2026-10-09 | `tools/capture-tf-golden-extra2.ps1` (117 cases under `$/<Project>/Main/goldenx-<run>`) captures the gaps of phases 1 and 3 that compat.md and the open questions still mark as assumed: `.tfignore` with `add /recursive`, the checkin policy failure shape, `difference` options and edge cases, `view`/`dir`/`properties`/`history` forms, labels of server paths, locks, partial `unshelve`, `reconcile /promote` variants, `get` over a modified file (TFP0402), every `resolve /auto` value on merge and get conflicts; wired into the runner after phase 2 (`-SkipExtra2`) | The developer asked for everything tf.exe can show while signed in |
| 2026-10-09 | `tools/capture-tf-golden-phase2.ps1` (103 cases under `$/<Project>/Main/golden2-<run>`: branch, merge previews and flags, a conflict, merges, rollback modes, undelete forms, folderdiff views and filters) added to the all-in-one runner ahead of phases 3 and 4; the manifest it needs can be rebuilt from `tests/golden/manifests/seed-manifest.json` by filling in the four placeholders (the scripts read only collection, project, serverRoot, localRoot, projectLocal and workspace) | The developer no longer has the original manifest and wants one capture sitting for everything |
| 2026-10-08 | Pending changes carry `mergeSources` (JSON) and a `MergeLine` text form (`merge, edit: $/src;C83~C83 -> $/dst;C80`, `branch: $/src;C84 -> $/dst`), used by `merge`, `rollback` and the integration tests | Golden `merge/all`, `merge/conflict`, `rollback/*` |
| 2026-10-08 | Review of PR #9 (independent agent, no blocking findings, 8 should-fix, 4 nits) and the fixes: (1) help of a command (`/?`, `-h`, `help <cmd>`) is never gated by the protection level; (2) `checkin` reports every conflict (TFP0406) and policy failure (TFP0407) as an item error, so JSON `errors[]` carries the server paths and messages, and `/validate` returns the evaluation with exit 100 instead of throwing; (3) `checkin /force` sets `AllowUnchangedContent`; (4) `undo /workspace:<name>` raises the level for every value, recorded in SPEC.md and compat.md §18 (the gate runs before the connection); (5) SPEC.md and README now say `shelve /delete` and `label /delete` are phase 3 at level 3; (6) `lock /lock:checkout` in a local workspace maps TF400022 to a TFP0501 item error with exit 1 as the golden capture; (7) a non-integer `protectionLevel` in the settings file is TFP0004, not TFP0900; (8) the "blocked at level 2" assertions of the integration tests run inside a level-2 settings scope; nits: `overridden` is consistent, the nothing-pending message no longer mirrors the golden text, hints lost their semicolons | Review findings |
| 2026-10-09 | Golden redaction of the phase 3 and 4 captures (runs 2610091137 and 2610091138, 75 files + 2 indexes) before commit: the organization name in identities of `permission/folder*` → `[<ORG>]\...` and `Build Service (<ORG>)` (4 files, 16 occurrences); in `workspaces/two-of-owner` the names of the developer's three unrelated workspaces → `<OTHER_WORKSPACE_1..3>` and their computer → `<OTHER_COMPUTER>` (6 occurrences). Nothing else changed; UTF-8 without BOM and CRLF preserved. Checked and clean: owner's name (already `<OWNER>` from the script), e-mails, `C:\Users\<USER>`, collection URL, GUIDs, token-like strings | Same reason as the step 0 redaction of 2026-10-07: the capture scripts replace only the collection URL, the owner, the computer and the user name, so bare identities and other workspaces of the owner slip through. The developer asked that the full name and computer names never appear |
| 2026-10-08 | Review of PR #4 (independent agent, one should-fix: PROGRESS.md was stale): status table, next steps and "Needs the user" rewritten for the hand-over; `docs/CLAUDE.md` keeps only the `-candidate` permission rule (the `/candidate` form never arrives from Git Bash) and says the switch must follow `merge`, names the three meanings of exit 120 (no workspace, path not mapped, server workspace), notes that `view -output:` writes a file and that aliases (`hist`, `stat`, ...) are not in the allow list; the compatibility test checks the `undo` and that no detected change is left; the CI step trims the list lines before the `#` check; SPEC.md points to `docs/CLAUDE.md` as the maintained template | Review findings |
| 2026-10-09 | Golden comparison (`docs/compat.md` §20) – fixes: merge and rollback conflicts are printed as the original's `Conflict (merge, edit): $/src;C601~C601 -> $/dst;C600` line (TFP0403 item error per conflict, built from the library's Merging event; the summary count only for conflicts no event described); `merges /format:detailed` lists the merged items (`Merged items:` + `source;Cn -> target;Cm`, `QueryMergesWithDetails`, JSON `items`) instead of changeset blocks; `rollback /changeset:` accepts any version spec (`T`, a date) as tf.exe does (TFP0302 check removed, the server answers "nothing to roll back"); `folderdiff` with one local path takes the server folder as the source and lists folder pairs under `/view:same`; `difference /ignorespace` no longer hides a CRLF/LF change and `/ignoreeol` no longer hides a missing final newline; `branches` of an unbranched item exits 0 with a message and `branches: []`; `labels <name>` without a match exits 100 (TFP0301); `lock /lock:none` of an unlocked item exits 1 (TFP0301 per item); the three messages are tfp's own wording, not the golden texts; `unshelve` over a writable local file reports TFP0402 (exit 1; the library signals it as a Getting event with the TargetWritable status, mapped to a failure); `checkin` of an unchanged edit exits 1 instead of 100 (as an item error: a thrown TFP0301 of `checkin` is exit 100 by the per-command table). Independent review of the branch: 3 blocking (the new unit test, the ineffective `checkin` exit code, the `merges` integration assertion), 4 should-fix (a stale §14 row, golden texts reworded, the `folderdiff` root pair recorded as a deviation, `/discard` keeps the conflict lines), 5 nits; all fixed except the root pair | tf.exe 18.10 captures of 2026-10-09 (phase 2 run 2610091535, extra2 run 2610091539, phase 3 run 2610091137) |
| 2026-10-09 | Golden comparison – recorded deviations (kept on purpose): `add /recursive` with `.tfignore` matches exits 0 and lists nothing (tf.exe: exit 1 and one stderr line); `folderdiff /filter` honors a `!` exclusion (tf.exe 18.10 ignores it) and accepts one server path (tf.exe: TF206007); `undelete ;X<wrong id>` is TFP0301 (tf.exe restores the item anyway); `view` of a wildcard or `/recursive` keeps the `$/path;C<n>:` header per file and refuses binaries on the console (tf.exe dumps raw content); two server folders in `difference` get per-file headers only; `reconcile /promote` without `/adds`/`/deletes` promotes both (tf.exe promotes nothing); `shelve /replace` says "replaced"; `shelve /validate` combines with `/noprompt`; `checkin /associate` and `history /workitem` are extensions (not switches of tf.exe 18.10); `merge /discard` prints no "resolved as KeepYours" line | Agent-friendly output and documented Microsoft behavior win over the quirks of one tf.exe version; every deviation is in `docs/compat.md` §20 |

## Open questions

Closed on 2026-10-07 by the golden analysis (see "Decisions" and `docs/compat.md`): `merges /extended`
(switch rejected by tf.exe 18.10), `history` of an unknown path (exit 0) and `add` of an ignored
file (exit 0, explicit path), `rollback /changeset:` without item spec (works), exit codes for
locks, out-of-date checkin and conflicts.

Closed on 2026-10-08 by `OpenQuestionTests` against the test collection and by decompiling the client library
(see "Decisions"): `add /recursive` with `.tfignore` matches (exit 0, ignored items left out), `reconcile /promote`
without `/adds`/`/deletes` (promotes; the capture's empty result is a recorded deviation), a PAT rejection (TFP0102,
exit 110, the PAT never echoed), `get` of an older version over a modified file (TFP0403 in a local workspace), the
golden loader and `index-extra*.json` (every `index*.json` is searched), `history /workitem` on Azure DevOps Services
(the linking service answers), `/encoding` values (the library parses names, code pages and `binary`), and the review
nits of PR #3 (`view` downloads, shelved content, `branches` tree, `merge /version`, `difference /encoding`).

Closed on 2026-10-09 by the captures of phases 2, 3, 4 and extra2 (`docs/compat.md` §20, questions 27–33 of
`docs/tf-reference/questions.md`): the phase 2 shapes (merge previews, conflicts, `merges /format:detailed`,
rollback specs, undelete ids, folderdiff sides and views), the phase 3 shapes (`checkin /validate`, unchanged
edit, `shelve /replace`, `unshelve` with a local file in the way, `label` without a scope, `label /delete`, `lock`
edge cases), `add /recursive` with `.tfignore` (tf.exe exits 1; tfp keeps 0), `reconcile /promote` without
switches (promotes nothing in tf.exe; tfp keeps promoting), TFP0402 for `unshelve`.

Open:

- No golden case exists for TFP0402 from `get` (the library's "writable" status): local workspaces report a
  conflict instead; `unshelve/local-file-in-the-way` is the only capture of a writable file in the way.
- A checkin policy failure is still uncaptured (the test project has no policy); `checkin /associate` is a
  tfp extension, so the "Work Items" policy shape stays assumed.
- Checkin policies: tfp evaluates "Work Items" and "Changeset Comments" itself and reports the rest as unknown
  (TFP0409); `/override` is level 2 (decided 2026-10-08). The test project has no policy yet, so the policy
  assertions of `Phase3Tests` are skipped until the developer configures one (see `docs/DEVELOPER-TODO.md`).

## Golden files – facts

- Captured on 2026-10-07 with tf.exe 18.10.12215.6 (Visual Studio 18 Community, Czech UI).
- Redirected tf.exe output is in the ANSI code page (windows-1250); the capture scripts decode it and store UTF-8.
- Placeholders: `<COLLECTION>`, `<WORKSPACE>`, `<OWNER>`, `<EMAIL>`, `<COMPUTER>`, `<USER>`, `<LOCAL_ROOT>`,
  `<WORKSPACE_ROOT>`, and `_B` / `_S` variants for the second and the server workspace. Manual redaction adds
  `<ORG>`, `<OTHER_WORKSPACE_ROOT>`, `<OTHER_WORKSPACE_1..3>` and `<OTHER_COMPUTER>` (full list in
  `docs/golden-format.md` §5).
- Phase 3 and 4 captures on 2026-10-09 (same tf.exe): `index-phase3-2610091137.json` and
  `index-phase4-2610091138.json`, 75 case files, redacted before commit (see Decisions 2026-10-09).
- Phase 2 and extra2 captures on 2026-10-09 (same tf.exe): `index-phase2-2610091535.json` (102 cases under
  `$/<Project>/Main/golden2-2610091535`) and `index-extra2-2610091539.json` (115 cases under
  `$/<Project>/Main/goldenx-2610091539`), committed in bcb807a; compared with tfp in `docs/compat.md` §20.
- `index.json` (read-only and pending-change cases) and `index-extra-<run>.json` (server-writing cases)
  list every case with its command and exit code. Two extra runs exist: `index-extra.json` (run
  2610071920, 76 cases, no run suffix in the file name) and `index-extra-2610071925.json` (41 cases).
  `index.json` has 86 cases; 205 files in total.
- Test project: seeded by `tools/seed-tfvc-test-repo.ps1` (changesets C75–C83), extended by
  `tools/capture-tf-golden-extra.ps1` (from C84, including history padding).

## Session log

### 2026-10-09 – cloud session, golden comparison (branch `golden-compare`)

- Done (earlier in the session): PR #13 merged (ac08eaf): `tools/capture-tf-golden-phase2.ps1`,
  `tools/capture-tf-golden-extra2.ps1`, the all-in-one runner order, the phase 2 script fix after the
  developer's first run. The developer then captured all four runs and committed them (bcb807a).
- Done: every case of the phase 2 and extra2 runs (and the phase 3 and 4 runs) compared with tfp. Code fixes
  and recorded deviations under Decisions 2026-10-09 and in `docs/compat.md` §20; `commands.md`,
  `json-schema.md`, `tf-reference/questions.md` (27–33, 3, 25, 26), `DEVELOPER-TODO.md` updated. Unit tests
  for the conflict lines, the merged-items list and the `difference` option semantics; the
  labels-after-delete integration assertion now expects exit 100.
- CI loop (runs 129–132): the new conflict-line unit test exposed a pended-count line for conflict-only merges
  (fixed) and the stderr form of an item error (no item prefix); the review (3 blocking, 4 should-fix, 5 nits)
  found the ineffective `checkin` exit code and the stale `merges` integration assertion; all fixed, CI green,
  merged with a merge commit.
- Next: the developer's checklist (release `v0.2.0`); phase 4 waits for the scope decision.
- Blockers: none.

### 2026-10-08 – cloud session, phase 2 (branch `p2-branching`, PR #12)

- Done (before this entry, same session): PR #11 merged (a26a2a7): native checkin policies (variant 3),
  `/override` at level 2, TFP0409, `docs/DEVELOPER-TODO.md`, the capture scripts for phases 3 and 4 and the
  all-in-one script, `tools/decompile-tf.ps1` with `docs/tf-reference/` (rules, template, questions) and the
  CLAUDE.md rule on decompiled sources.
- Done: phase 2 on the developer's brief. Client: `Merge`, `PendBranch`, `PendUndelete`, `Rollback`,
  `QueryMerges`, merge sources on pending changes, item hash, `UnifiedDiff.MatchingLines`. Commands:
  `merge` (pending, `/preview` through the server's `NoMerge` option, flags), `branch`, `undelete`, `rollback`
  (`/changeset`, `/toversion`), `merges` (brief and detailed), `folderdiff` (hash comparison, filters,
  views, single-path form), `annotate` (extension). Catalog rows implemented, help section "Phase 2",
  unit tests (`Phase2CommandTests`), integration tests (`Phase2Tests`: branch and undo, preview and merge
  and undo, rollback and undelete on the CI folder, merges/folderdiff/annotate on the seeded branches).
  Docs: SPEC.md "Fáze 2" and catalog rows, commands.md, json-schema.md, compat.md §15, questions 27–32,
  README phase table, the agent template (permissions and the pending-only note).
- CI loop (runs 114–120) and review: three unit failures on the first run (annotate line endings, the help
  footer, the `/view` enumeration), then the integration findings recorded under Decisions (Merging event,
  rollback scope and `/toversion` order, the seeded merge conflict in the preview) and the review findings
  (1 blocking, 5 should-fix, 8 nits) fixed in the same loop. Merged with a merge commit.
- Next: the developer's checklist (`docs/DEVELOPER-TODO.md`); phase 4 waits for the scope decision.
- Blockers: none.

### 2026-10-08 – cloud session, phase 3 (branch `p3-protection`, PR #9)

- Done: protection levels (settings file, catalog map, `protection` command, TFP0004, level in `help` and
  `whoami`); `checkin` (evaluation, `/validate`, `/override`, `/associate` over REST), `shelve` (`/replace`,
  `/move`, `/delete`), `shelvesets`, `unshelve`, `label` (`/child`, `/delete`), `unlabel`, `lock`; error codes
  TFP0406–TFP0408; unit tests (`ProtectionTests`, `Phase3CommandTests`); CI tests (`Phase3Tests`) with the
  CI folder and the cleanup; docs (commands.md, json-schema.md, compat.md §16 and §18, SPEC.md error rows and
  tool commands, the agent template with `checkin`/`shelve` guidance and the `protection set` deny rule);
  `tools/capture-tf-golden-phase3.ps1` for the developer. The first CI runs failed on four unit tests (the
  help catalog had no section for implemented phase 3 commands, the no-argument run hit the protection gate
  with a null spec, the error catalog test and a `/delete` argument test lacked the new codes and the gate):
  fixed in 9a072bd.
- CI loop: the twin runs of one commit interfered through the shared test collection (lock, `get`), so CI
  runs are serialized and `push` is limited to `main`; the lock test uses a file of its own run. Independent
  review: 0 blocking, 8 should-fix, 4 nits, all fixed (Decisions). PR #9 merged (3c92ea2) with 187 unit and
  82 integration tests green.
- Next: the developer releases `v0.2.0` (Actions → CI → Run workflow with `release_tag`) and, when tf.exe is
  at hand, runs `tools/capture-tf-golden-phase3.ps1`; then compare tfp against the new captures.
- Blockers: none.

### 2026-10-08 – cloud session 1 continued, milestones 3–5 to the merge

- Done: PR #2 (milestone 3) reviewed by an independent agent, four should-fix items and two nits
  applied, CI green, merged into `main` (merge commit 106c574). Milestone 4 CI brought from 18
  failures to green (owner placeholder of the seed changesets, whole-word Czech → English phrase
  mapping, properties without the stream check, `was not found in source control` → TFP0301,
  changeset item order by parent folder, diff prefix/suffix matching; two golden cases that later
  golden runs invalidated compare shape or items only). PR #3 reviewed (13 should-fix items, no
  blockers) and the fixes applied (commit d099adb; linear-space Myers diff, work items on request,
  `/workitem` through the linking service, wildcards and deleted items in `history`/`view`, CRLF
  and final-newline differences, shelveset diff lines, `dir <file>`, merge candidates detailed).
  Milestone 5 on `m5-integration` ([PR #4](https://github.com/Gh61/TfPortable/pull/4)):
  `docs/CLAUDE.md`, `CompatibilityTests`, the embedded-assembly CI check with
  `src/TfPortable/embedded-assemblies.txt` (33 assemblies + Costura resources), THIRD-PARTY-NOTICES
  section 3 updated; reviewed, findings applied.
- Next: PR #3 merged (ff64062); PR #4 merged (bd1fa08); release `v0.1.0` published through the new
  `workflow_dispatch` input (PR #5, the git proxy of the cloud session refuses tag pushes). Then the
  open questions: branch `open-questions`, `OpenQuestionTests`, review nits of PR #3; see "Decisions".
  Phases 3 and 4 defined (branch `docs-phases`); phase 3 starts on the developer's brief.
- Blockers: none. The developer sets up the `main` ruleset (PR required, CI check required, no
  force pushes; no required approvals so that the agent can merge).

### 2026-10-08 – cloud session 1 continued, review of PR #1 and milestone 3

- Done: independent review of PR #1 (findings and fixes under "Decisions"), fixes pushed to `m2-core`.
  Milestone 3 on `m3-workspace` (branched from `m2-core`, merged after each fix): `ITfvcWorkspace`
  gained PendAdd/PendEdit/PendDelete/PendRename/Undo/Get/QueryConflicts/Resolve/GetLocalVersions,
  `ITfvcClient` QueryWorkspaces/RemoveCachedWorkspaces/QueryPendingSets/QueryShelvedChanges;
  commands add, checkout, delete, rename, undo (`Commands/PendCommands.cs`), get, resolve, reconcile,
  localversions, workfold, workspaces (`Commands/WorkspaceInfoCommands.cs`), status detailed/`/user`/
  `/shelveset`; shared helpers in `Commands/WorkspaceCommandSupport.cs`. Rendering unit tests;
  `WorkspaceGoldenTests` scenario; docs (`json-schema.md` rows, `compat.md` §1.3 and milestone 3
  notes, README status).
- CI of `m3-workspace` went green after three fixes (undo names a rename as pended, golden item
  suffixes stripped, scenario cleanup; a label-width unit test). Independent review of PR #2 started.
- Milestone 4 on `m4-history` ([PR #3](https://github.com/Gh61/TfPortable/pull/3)): client methods
  for changesets, items, content, labels, branches and merge candidates; commands history (full),
  changeset, view, difference, dir, properties, labels, branches, merge /candidate; own unified
  diff; unit tests; `HistoryGoldenTests`; docs (json-schema rows, compat.md milestone 4 notes,
  README status). CLAUDE.md gained the review-and-merge loop on the developer's request.
- Next: review findings of PR #2 → fix → merge; CI of `m4-history` → review → merge; milestone 5.
- Blockers: none.

### 2026-10-08 – cloud session 1, milestone 2 (Claude Code on Linux, CI on windows-latest)

- Done: branch `m2-core`, draft [PR #1](https://github.com/Gh61/TfPortable/pull/1). Code of
  milestone 2 complete: `Cli/CommandCatalog.cs` (every command, alias, phase, status, switch),
  `Cli/CommandLine.cs` (table-driven parser, `@file`, short forms per command, value validation,
  `PeekCommand`), `Cli/VersionSpecParser.cs`, `Cli/ItemSpecParser.cs`; `Errors/` catalog with
  defaults and compat.md overrides, ActivityId; `Output/` item errors, envelope context on errors,
  block helpers; `Context/` `ITfvcClient`, `TfvcClient`, DTOs, resolver with `/workspace`,
  `/login`, TFP0203 helper; `help` from the catalog (text + JSON), TFP0003 for planned and
  out-of-scope commands, Ctrl+C in the PAT prompt. 144 unit tests (parser, catalog, versionspec,
  itemspec, errors, output, dispatch, rendering). Golden infrastructure and `GeneralGoldenTests`;
  CI step for the standalone exe. Docs: `docs/json-schema.md` (`help` with `status`, `login`,
  `whoami`), `docs/compat.md` §2 parser notes, README status.
- CI: run 3 failed on five unit tests (Newtonsoft null typing, cs-CZ parsing "10:00 PM", out-of-scope
  switches, envelope command name before parsing); fixed in the second push. Run 5: 144 unit and
  18 integration tests passed, none skipped (the secrets are set): the fixture created a local
  workspace with the client library, `tfp login /pat:-` stored the PAT, the `status` golden cases
  and `whoami` ran against the test collection; the standalone exe ran `version`, `help
  /format:json` (48 commands), `status` outside a workspace (120) and an unknown command (2), but
  the step failed on PowerShell's handling of the last exit code (fixed: `exit 0` at the end).
  Runs 11 and 12 (head `1e8017e`): green. PR #1 marked ready for review.
- Next: see "Next steps" 1–3.
- Blockers: PR review by the developer.

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
