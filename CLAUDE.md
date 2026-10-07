# TfPortable – instructions for Claude Code

TfPortable (`tfp.exe`) is a portable replacement for the TFVC part of Microsoft's `tf.exe`.
It signs in only with a Personal Access Token stored in Windows Credential Manager, never
shows a dialog and is built to be driven by AI agents (clear errors, JSON output).
It keeps tf.exe command names and switches.

## Sources of truth

| File | What it is |
| --- | --- |
| `docs/SPEC.md` | The specification (in Czech). Behavior, commands, errors, exit codes, milestones. |
| `docs/compat.md` | Where TfPortable deliberately differs from the original tf.exe, and why. |
| `tests/golden/` | Captured output of the original tf.exe (stdout, stderr, exit code) per command and case. |
| `docs/PROGRESS.md` | Current state: milestones, decisions, open questions, session log. |
| `tools/` | PowerShell scripts that seeded the test project and captured the golden files. |

When they disagree: SPEC.md wins over your assumptions; golden files win over SPEC.md on
how the original behaves; `docs/compat.md` records every intended deviation.

## Start and end of every session

1. Read `docs/PROGRESS.md` first: current milestone, next steps, open questions, what needs the user.
2. Read only the SPEC.md sections you need for the task.
3. At the end of the session, or whenever a milestone changes state, update `docs/PROGRESS.md`:
   status table, a new session log entry (date, done, next, blockers), new decisions, new
   open questions. Write "Next steps" so that an agent with no context can continue.
4. Commit `docs/PROGRESS.md` together with the work it describes.

## Stack

- C#, .NET Framework 4.8, SDK-style csproj (`<TargetFramework>net48</TargetFramework>`).
- `Microsoft.TeamFoundationServer.ExtendedClient` 20.256.2 for all TFVC access.
- `Microsoft.NETFramework.ReferenceAssemblies` (PrivateAssets all) so the code builds on Linux.
- Costura.Fody: the release is one file, `tfp.exe`, no `.config` next to it.
- Credential Manager via P/Invoke (`CredReadW`, `CredWriteW`, `CredDeleteW`, `CredEnumerateW`), no extra library.

## Where code can run

| Environment | Can do | Cannot do |
| --- | --- | --- |
| Local Windows (Claude Code on the developer's machine) | build, all tests, run `tfp.exe` | – |
| Claude Code in the cloud (Linux) | write code, `dotnet build` | run `tfp.exe`, Costura, Credential Manager, integration/golden tests |
| GitHub Actions `windows-latest` | build, unit, golden and integration tests, release artifact | interactive `tfp login` |

In the cloud, CI is your only real test run: push to a branch, open a pull request, read
failures with `gh run view --log-failed`.

## Rules

- Change only what the task needs. Do not reformat, reorder or rename code that the task
  does not touch.
- Code, comments, commit messages, tool output and docs other than SPEC.md are in English.
- Never put a PAT in code, tests, logs, commits, issues or chat. Tests read
  `TFP_TEST_COLLECTION` and `TFP_TEST_PAT` from the environment and are skipped (not failed)
  when they are missing. Never ask the user to paste a PAT into the conversation; ask them
  to run `tfp login` in their own terminal.
- Integration tests and anything that writes to a server run only against
  `TFP_TEST_COLLECTION`. Never against another collection.
- Do not edit `tests/golden/` by hand. New golden files come from the scripts in `tools/`,
  run by the user on Windows with the original tf.exe. The only exception is redacting sensitive
  data: replace it with a placeholder (`<OWNER>`, `<EMAIL>`, `<REDACTED>`, ...) and change nothing else.
- Golden files come from a Czech Visual Studio: compare exit codes, items, changeset numbers
  and order, never message texts. TfPortable messages are English and come from SPEC.md.
  Never copy or translate a message text from `tests/golden/` into TfPortable. The tool is
  universal: every message, hint, prompt, error, help text and JSON value it produces is
  English, regardless of the user's Windows or Visual Studio language. Only texts that the
  server itself returns (TFxxxxx messages) are passed through verbatim.
- Every change of behavior that SPEC.md describes updates SPEC.md in the same commit and adds
  an entry to "Decisions" in `docs/PROGRESS.md`.
- If SPEC.md is unclear: check the golden files, then Microsoft's TFVC command docs. If it is
  still unclear, choose the behavior closest to the original tf.exe, record it under
  "Decisions", and add the question to "Open questions".
- When you are stuck on a problem for more than a couple of attempts (an API that refuses to
  behave, an error code you cannot explain), search the web before trying more variants. Someone
  has usually hit it before. Example from the spike: TF30063 with a valid PAT was a PAT scope
  rule of the TFVC SOAP API, documented in a README of another Microsoft project, not a bug in
  our connection code.
- Never enable System.Net or TeamFoundation tracing while a PAT is in use, and never log
  request headers: the trace writes the `Authorization` header in clear text.

## When to stop and report

Stop, write the reason under "Needs the user" in `docs/PROGRESS.md`, and end the session when:

- the same CI failure happens three times in a row,
- SPEC.md contradicts itself or the golden files in a way that changes the design,
- something needs the user: secrets, Azure DevOps settings, a run of the `tools/` scripts,
  a license or legal question, approval at a milestone gate (see SPEC.md, milestones).

## Definition of done for a command

- Parser and switches covered by unit tests.
- Golden test passes, or the difference is recorded in `docs/compat.md`.
- `/format:json` output documented in `docs/json-schema.md`.
- Errors only from the catalog in SPEC.md.
- `tfp help <command>` describes it.
