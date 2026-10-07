# Kickoff – first local Claude Code session

You run on the developer's Windows machine, in the `tfportable` repository. The developer is
present and answers questions. Read `CLAUDE.md` and `docs/PROGRESS.md` before you start.

Goal of the local phase: a complete, verified specification and a working spike, so that the
cloud agent can implement milestones 2–5 without further questions.

Work through the steps in order. Update `docs/PROGRESS.md` after each step. Stop at the two
gates and wait for the developer.

## Step 0 – Check the repository

Expected content:

```
CLAUDE.md
LICENSE                 full text of GNU GPL v3
LICENSE-EXCEPTION       additional permission for the Microsoft client libraries
THIRD-PARTY-NOTICES     Microsoft license terms of Microsoft.TeamFoundationServer.ExtendedClient
docs/SPEC.md
docs/PROGRESS.md
docs/KICKOFF-LOCAL.md
tests/golden/           <command>/<case>.txt, index.json, index-extra-<run>.json
tools/                  seed-tfvc-test-repo.ps1, capture-tf-golden.ps1, capture-tf-golden-extra.ps1
```

- Create what is missing: LICENSE from gnu.org; THIRD-PARTY-NOTICES with the license text from
  the NuGet package page of ExtendedClient 20.256.2 and of its Microsoft dependencies.
- Ask the developer for `seed-manifest.json` and `extra-manifest-*.json` (outside the repo, next to
  the test workspace). Store sanitized copies in `tests/golden/manifests/`: replace the collection
  URL with `<COLLECTION>` and local paths with the placeholders used in the golden files. Never commit
  the originals.
- Scan `tests/golden/` (and the manifests) for sensitive data the capture scripts may have missed. Ask
  the developer for the values to look for: full name, e-mail addresses, organization name (also inside
  URLs such as `<org>.visualstudio.com` or `dev.azure.com/<org>`), computer name, user name, local paths.
  Also look for things the scripts cannot know about: mappings and names of the developer's other
  workspaces and projects (e.g. in `workspaces/detailed.txt` and `workspaces/with-server-workspace.txt`),
  IP addresses, internal host names, tokens or anything that looks like a secret.
- Replace every finding with a placeholder in the same style (`<OWNER>`, `<EMAIL>`, `<COLLECTION>`,
  `<COMPUTER>`, `<USER>`, `<LOCAL_ROOT>`, `<OTHER_WORKSPACE>`, `<OTHER_PROJECT>`, `<REDACTED>`), across all
  files consistently. Do not change anything else in the golden files. List what you replaced (by
  placeholder and file count, not the original values) under "Decisions" in PROGRESS.md. Re-running the
  capture is not an option: it needs the interactive tf.exe sign-in and writes new data to the server.
- Nothing from `tests/golden/` or the manifests may be committed or pushed before this scan is done and
  the developer has confirmed the result.
- Add `.gitignore` for .NET (bin, obj, *.user, .vs).

## Step 1 – Golden analysis → docs/compat.md

For every case in `index.json` and `index-extra-*.json`, compare the original behavior with SPEC.md:

- exit code (SPEC.md tables "Exit kódy" and the error catalog),
- what is printed on stdout vs stderr,
- the shape of the output (columns, headers, item lists, empty results),
- behavior (what happened, what was skipped, what was refused).

Write `docs/compat.md`: a table per command with case, original behavior, SPEC.md behavior, and a
decision (follow the original / keep the deviation and why). Known starting points are listed under
"Open questions" in PROGRESS.md.

Also write `docs/golden-format.md`: the file format, placeholders, how a golden test normalizes output
(dates, changeset numbers from the manifests, placeholders) and what it compares.

## Step 2 – Refine SPEC.md

- Apply the decisions from step 1 to SPEC.md (exit codes, error catalog, output shapes).
- Walk through SPEC.md and list everything an implementer would have to guess: unclear switches, JSON
  fields per command (`docs/json-schema.md` does not exist yet – propose its structure), edge cases.
- Ask the developer the questions in one batch, apply the answers, record them under "Decisions".

**Gate A:** show the developer a short summary of what changed in SPEC.md and wait for approval.

## Step 3 – Scaffolding

- Solution and projects as in SPEC.md ("Architektura a build"): `src/TfPortable`, `tests/TfPortable.Tests`,
  `tests/TfPortable.IntegrationTests`.
- `Directory.Build.props` with shared settings, `FodyWeavers.xml`, `<AssemblyName>tfp</AssemblyName>`.
- GitHub Actions workflow on `windows-latest`: restore, build Release, unit tests, integration and golden
  tests with `TFP_TEST_COLLECTION` and `TFP_TEST_PAT` from secrets (skipped when missing), upload `tfp.exe`
  as an artifact; on tag `v*` publish a release with `tfp.exe` and its SHA-256.
- README: what it is, install, `tfp login`, license and the statement that it is not a Microsoft product.

## Step 4 – Milestone 1: spike

- Implement just enough: Credential Manager store, `tfp login`, `tfp whoami`, `tfp status` (local workspace
  detection per SPEC.md "Detekce workspace a kolekce").
- Build the single `tfp.exe` with Costura. Exclude what TFVC does not need (SPEC.md lists candidates).
- Verify on this machine: `tfp login` against the test collection, then `tfp whoami` and `tfp status` in
  the test workspace (ask the developer for its path), from PowerShell and from Git Bash with `-switch` syntax.
- Verify portability: ask the developer to copy `tfp.exe` to a machine or Windows Sandbox without Visual
  Studio and run the same commands.
- Report: works or not, exe size, excluded assemblies, anything that forces a design change.

**Gate B:** wait for the developer to approve the architecture.

## Step 5 – Prepare the cloud phase

- Write `docs/KICKOFF-CLOUD.md`: milestones 2–5 in order, what to read, the CI loop (branch, pull request,
  `gh run view --log-failed`), stop conditions from CLAUDE.md, and which golden cases belong to which milestone.
- Make sure "Needs the user" in PROGRESS.md lists everything the cloud agent cannot do itself (secrets set,
  branch protection, reviewing pull requests).
- Update PROGRESS.md: milestones 0 and 1 done, next step "cloud: docs/KICKOFF-CLOUD.md".
