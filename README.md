# TfPortable

**TfPortable** (`tfp.exe`) is a portable replacement for the TFVC part of Microsoft's `tf.exe`:
one file, no Visual Studio, no dialogs, sign-in with a Personal Access Token, and output that is
easy to read for people and easy to parse for scripts and AI coding agents.

> Status: phases 1 to 3 (reading and workspace work, branching as pending changes, the everyday write
> path) are released as [v0.2.0](https://github.com/Gh61/TfPortable/releases/tag/v0.2.0). Later phases (see
> "Phases and progress") answer `TFP0003` until they are implemented. Day-to-day state: `docs/PROGRESS.md`.

## Why this project exists

Teams that keep their code in TFVC (Team Foundation Version Control) and want to work with it
from a plain terminal, a build box or an AI coding agent hit the same wall every day:

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
- every error with a stable `TFPxxxx` code ([docs/errors.md](docs/errors.md)), the original `TFxxxxx` code when there is one, and the
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
is described in [docs/json-schema.md](docs/json-schema.md).

## Phases and progress

TfPortable grows in phases (the authoritative definition is in `docs/SPEC.md`, the day-to-day
state in [docs/PROGRESS.md](docs/PROGRESS.md)). A command of a later phase is recognized and
answers `TFP0003` with the phase it is planned for; from version 1.0 nothing does.

| Phase | Scope | Status |
| --- | --- | --- |
| 1 | Reading and workspace work: the commands in the table above (PAT sign-in, status, add, checkout, delete, rename, undo, get, resolve, reconcile, localversions, workfold, workspaces, history, changeset, view, difference, dir, properties, labels, branches, merge /candidate). Everything it changes is local and reversible with `undo` | **done**, released as [v0.1.0](https://github.com/Gh61/TfPortable/releases/tag/v0.1.0) (2026-10-08) |
| 2 | Branching as pending changes: `merge`, `branch`, `rollback` and `undelete` (reversible with `undo`, protection level 2); `merges`, `folderdiff` and the `annotate` extension read only. Content search in history came with phase 1 | done ([PR #12](https://github.com/Gh61/TfPortable/pull/12)), released as [v0.2.0](https://github.com/Gh61/TfPortable/releases/tag/v0.2.0) (2026-10-09) |
| 3 | The everyday write path: `checkin`, `label`/`unlabel`, `shelve` with `shelvesets` and `unshelve` (`shelve /delete` and `label /delete` at protection level 3), `lock`/`unlock`, so that work can be checked in, labelled, parked on the server and taken up again. Comes with the protection levels (`docs/SPEC.md`, "Úrovně ochrany") | done ([PR #9](https://github.com/Gh61/TfPortable/pull/9)), released as [v0.2.0](https://github.com/Gh61/TfPortable/releases/tag/v0.2.0) (2026-10-09) |
| 4 | The rest of `tf.exe` that still makes sense for a portable, PAT-only tool: `destroy` (level 3, a `/preview` run first), `permission` (listing at level 1, changes at level 3), `workspace /new`, `/delete` and edits (level 3), `property` and `resolvepath` (read), `workfold /map /unmap /cloak /decloak`, `changeset /comment /notes`, `lock` and `undo` on another workspace (level 3), `checkin /shelveset`, `unshelve /move`, `shelve /validate`, `docs` (alias `msdn`). `proxy`, `configure` and `certificates` are never offered (`TFP0005` explains why). From version 1.0 no tf.exe command answers `TFP0003` | done ([PR #17](https://github.com/Gh61/TfPortable/pull/17)) |

## Protection levels

TfPortable runs at one of three protection levels. Every command and switch has the level it
needs; a command above the configured level ends with `TFP0004` (exit 100) before the tool
connects, and the hint says how to raise the level. The level is stored in
`%APPDATA%\TfPortable\settings.json` and changed only by `tfp protection set <1|2|3>` in an
interactive terminal (raising it asks for a typed confirmation). `tfp protection` and
`tfp whoami` show it. It is a guard rail and a clear contract for scripts and agents, not a
security boundary: the agent's own permission rules (for Claude Code, [docs/CLAUDE.md](docs/CLAUDE.md))
remain the real enforcement.

| Level | Allows | Commands |
| --- | --- | --- |
| 1 | Reading only; the only write to disk is `view /output` | `status`, `history`, `changeset`, `view`, `difference`, `dir`, `properties`, `localversions`, `workfold`, `workspaces`, `labels`, `branches`, `merge /candidate`, `merges`, `folderdiff`, `annotate`, `shelvesets`, `help`, `version`, `whoami`, `login`, `logout`, `protection` |
| 2 (default) | Everyday work: pending changes, workspace updates and the everyday writes to the server | level 1 plus `add`, `checkout`, `delete`, `rename`, `undo`, `get`, `resolve`, `reconcile`, `merge`, `branch`, `rollback`, `undelete`, `checkin`, `shelve`, `unshelve`, `label`, `unlabel`, `lock` |
| 3 | Everything, including irreversible changes and other people's workspaces | level 2 plus `shelve /delete`, `label /delete`, `undo /workspace:<name>`, `lock /workspace:<name>`, `destroy`, `permission` changes (`/allow`, `/deny`, `/remove`, `/inherit`), `workspace /new`, `/delete` and edits |

The full table with the per-switch levels is in `docs/SPEC.md` ("Úrovně ochrany") and
`docs/commands.md`.

## For AI agents

TfPortable works with any agent that runs shell commands: every command has a stable exit code,
a `TFPxxxx` error code with a next step ([docs/errors.md](docs/errors.md)), and `-format:json`. For Claude Code,
[docs/CLAUDE.md](docs/CLAUDE.md) is a ready-made section for the `CLAUDE.md` of a TFVC project
plus a permissions snippet for `.claude/settings.json`: which commands to use, dash switches,
`-format:json`, what exit codes 110 and 120 mean, and that the agent never asks for or enters a
PAT. Read-only commands can be allowed without confirmation; `get`, `undo`, `reconcile`,
`resolve` and `delete` should ask. Other agents can use the same text as a starting point.

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
