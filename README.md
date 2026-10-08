# TfPortable

**TfPortable** (`tfp.exe`) is a portable replacement for the TFVC part of Microsoft's `tf.exe`:
one file, no Visual Studio, no dialogs, sign-in with a Personal Access Token, and output that
both people and AI coding agents can read.

> Status: pre-alpha. The specification is finished, the behavior of the original is captured in
> `tests/golden/`, and the commands are being implemented milestone by milestone
> (`docs/PROGRESS.md`). Nothing is released yet.

## Why this project exists

Teams that keep their code in TFVC (Team Foundation Version Control) and want to use AI coding
agents such as Claude Code hit the same wall every day:

- **Signing in with `tf.exe` barely works anymore.** This is the main reason for this project.
  The sign-in window of `tf.exe` crashes on JavaScript errors, the whole sign-in process gets
  stuck, and there is practically no way to authenticate; the result is `TF30063: You are not
  authorized` over and over again, no matter how many times the token cache is cleared. When a
  sign-in does succeed (one attempt in fifty), it lasts only a few hours, about two or three, and
  then the whole dance starts again.
- **`tf.exe` is not portable.** It comes with Visual Studio or the Team Explorer installer and
  depends on the installation it was shipped with. A build box, a container or a colleague's
  laptop without Visual Studio cannot run it.
- **Sign-in is interactive.** `tf.exe` relies on cached Entra ID tokens that expire, on MFA
  prompts and on dialogs. An agent driving the tool from a terminal gets a hidden window or a
  hang instead of an answer. Even `tf.exe` with a PAT needs an undocumented switch.
- **Output is made for humans.** Tables are cut to 79 columns, dates follow the Windows locale,
  errors come in the language of the Visual Studio installation, and exit codes are inconsistent.
  An agent has to guess.

TfPortable keeps the command names, switches and output shape of `tf.exe`, so existing habits and
scripts work, and fixes the problems above:

- sign-in with a Personal Access Token, stored once in the Windows Credential Manager and used
  for every command; it never prompts unless a person runs `tfp login` in a terminal, and when
  the token expires the error says so and tells you to run `tfp login` again;
- a single `tfp.exe` that runs on any Windows with .NET Framework 4.8;
- every error with a stable `TFPxxxx` code, the original `TFxxxxx` code when there is one, and the
  next step to take; `/format:json` returns one JSON document per command; dates are ISO 8601;
  nothing is truncated.

It is meant for local workspaces and for the daily loop of an agent: see what changed, read
history and files, diff, add, edit, rename, delete, undo, get. Commands that write to the server
immediately and irreversibly (`checkin`, `shelve`, `lock`, `label`, `destroy`, `permission`,
workspace creation) are recognized and refused with a clear message; use Visual Studio or the
original `tf.exe` for them.

## Install

1. Download `tfp.exe` from the GitHub releases page (together with `tfp.exe.sha256`) and put it
   in a folder of your choice, for example `C:\Tools\TfPortable\`.
2. Add that folder to your `PATH`, or call it by its full path. It does not conflict with an
   installed `tf.exe`.
3. Requirements: Windows with .NET Framework 4.8 (part of Windows 10 1903 and later).

## Sign in

Run once, in your own terminal (never paste the PAT into a chat with an AI agent):

```
tfp login /collection:https://dev.azure.com/<organization>
```

The prompt explains where to create the token. The PAT is stored in the Windows Credential
Manager for your user profile. Check it with `tfp whoami`, remove it with `tfp logout`.

> **The PAT must have *Scopes: Full access*.** The TFVC API of Azure DevOps (the legacy SOAP
> endpoints that every TFVC client, including `tf.exe`, talks to) refuses tokens with custom
> scopes. A token limited to *Code: Read & write* is accepted by the REST API but gets `TF30063`
> from version control. TfPortable detects this case and reports it as `TFP0103`. A read-only PAT
> for TFVC is therefore not possible; limit the token's lifetime instead.

## Use

Inside a folder mapped to a local workspace, the commands look exactly like `tf.exe`:

```
tfp whoami                                  who am I, which collection, which workspace
tfp status /recursive                       pending changes and detected (uncommitted) changes
tfp history $/Project/Main /recursive /stopafter:20
tfp changeset 1234                          what a checkin contained
tfp changeset 1234 /diff                    the same as a unified diff
tfp view $/Project/Main/src/App.cs /version:C1234
tfp diff Main\src\App.cs                    local change against the server version
tfp add Main\src\New.cs
tfp checkout Main\src\App.cs
tfp rename Main\src\Old.cs Main\src\New.cs
tfp undo Main\src\App.cs
tfp get /preview                            what a get would do, without doing it
tfp status /recursive /format:json          the same data as JSON
```

Switches accept `/` and `-`. From Git Bash (and therefore from Claude Code on Windows) write them
with a dash, so that MSYS does not turn `/recursive` into a path: `tfp status -recursive`.

Exit codes: `0` success, `1` partial success or a per-item failure, `2` unknown command,
`100` nothing done, `110` sign-in problem (run `tfp login`), `120` no workspace or collection found.

## Commands

| Command | What it does |
| --- | --- |
| `login`, `logout`, `whoami` | Store, remove and check the PAT; show identity, collection, workspace and mappings |
| `status` (`stat`) | Pending changes, plus changes detected on disk in a local workspace |
| `add`, `checkout` (`edit`), `delete` (`del`), `rename` (`move`) | Create pending changes in the workspace |
| `undo` | Revert pending changes |
| `get` | Update the workspace to a version; `/preview` shows what would happen |
| `resolve` | List conflicts, or resolve them with `/auto:` |
| `reconcile` | Promote detected changes to pending changes, or clean untracked files |
| `localversions` (`lv`) | Which version of each item the workspace has |
| `workfold`, `workspaces` | Mappings of the current workspace; workspaces of the collection |
| `history` (`hist`) | Changesets of an item or folder, with filters by comment, change type and work item |
| `changeset` | Details of one checkin; `/diff` prints its unified diff |
| `view` | Content of a file at a version, written to the console or to `/output:` |
| `difference` (`diff`) | Unified or brief diff of a local change, two versions, two items or a shelveset |
| `dir` | Server-side listing of a folder |
| `properties` (`info`) | Local and server information about an item |
| `labels`, `branches` | Labels of the collection; branch tree of an item |
| `merge /candidate` | Changesets not yet merged between two branches |
| `help`, `version` | Command catalog (`/format:json` for agents); versions of tfp and the client libraries |

Every switch of every command, and what TfPortable does with it, is in
[docs/commands.md](docs/commands.md). Where TfPortable deliberately differs from `tf.exe`
(exit codes, dates, truncation, encoding) is in [docs/compat.md](docs/compat.md). The JSON output
is described in [docs/json-schema.md](docs/json-schema.md). Phase 2 (`merges`, `annotate`,
`folderdiff`, `shelvesets`, `unshelve`, `undelete`, `rollback`, `branch`, `merge`) comes later.

## For AI agents

`docs/CLAUDE.md` (milestone 5) is a ready-made section for the `CLAUDE.md` of a TFVC project:
which commands to use, dash switches, `-format:json`, what exit code 110 means, and that the
agent never asks for or enters a PAT. Read-only commands can be allowed without confirmation;
`get`, `undo`, `reconcile`, `resolve` and `delete` should ask.

## Build

```
dotnet build TfPortable.slnx -c Release
dotnet test tests/TfPortable.Tests -c Release
```

The code builds on Windows and Linux (reference assemblies), but it runs and is tested only on
Windows. Integration and golden tests need `TFP_TEST_COLLECTION` and `TFP_TEST_PAT` in the
environment and are skipped without them.

## License

TfPortable is free software under the **GNU General Public License v3** (`LICENSE`) with an
**additional permission** under GPL section 7 (`LICENSE-EXCEPTION`) that allows combining it with
the Microsoft Azure DevOps client libraries. Those libraries are embedded in `tfp.exe` and stay
under their own terms, reproduced in `THIRD-PARTY-NOTICES`.

TfPortable is an independent project. It is **not a Microsoft product** and is not affiliated
with, endorsed by or supported by Microsoft Corporation. *Microsoft*, *Azure DevOps*, *Team
Foundation Server* and *Visual Studio* are trademarks of Microsoft Corporation.
