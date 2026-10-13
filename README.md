# TfPortable

**TfPortable** (`tfp.exe`) is a portable replacement for the TFVC part of Microsoft's `tf.exe`:
one file, no Visual Studio, no dialogs, sign-in with a Personal Access Token, and output that is
easy to read for people and easy to parse for scripts and AI coding agents.

> Status: every version-control command of tf.exe is implemented, or answers `TFP0005` (never offered) or
> `TFP0006` (a switch left out, with the reason). The current release is
> [v1.0.0](https://github.com/Gh61/TfPortable/releases/tag/v1.0.0). Day-to-day state: `docs/PROGRESS.md`.

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
  tables are never cut to 79 columns (only `history` stops at 256 changesets without `/stopafter`
  and says how to continue).

It is meant for local workspaces. Every version-control command of `tf.exe` is there, from the
daily loop of an agent (see what changed, read history and files, diff, add, edit, rename, delete,
undo, get) through checkin, shelving, labels, locks and branching to server administration
(`destroy`, `permission`, `workspace`, `property`). What a command may do is decided by the
protection level (below): level 1 allows everything that changes nothing on the server, the
irreversible commands need level 3, which a person sets in their own terminal. Only `proxy`, `configure` and `certificates` are never offered (`TFP0005`), and a few
switches of `tf.exe` answer `TFP0006` with the reason (marked ✗ in [docs/commands.md](docs/commands.md)).

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
tfp checkin Main /recursive /comment:"Fix" /associate:1234
tfp shelve MyWork Main /recursive /comment:"Parked"
tfp status /recursive /format:json          the same data as JSON
```

Switches accept `/` and `-`. From Git Bash (and therefore from Claude Code on Windows) write them
with a dash, so that MSYS does not turn `/recursive` into a path: `tfp status -recursive`.

Exit codes: `0` success, `1` partial success or a per-item failure, `2` unknown command,
`100` nothing done, `110` sign-in problem (run `tfp login`), `120` no workspace or collection found.

## Commands

| Command | What it does |
| --- | --- |
| `login`, `logout`, `whoami` | Store, remove and check the PAT; show identity, collection, workspace, mappings and protection level |
| `status` (`stat`) | Pending changes, plus changes detected on disk in a local workspace |
| `add`, `checkout` (`edit`), `delete` (`del`), `rename` (`move`) | Create pending changes in the workspace (`/lock:` for a server lock) |
| `undo` | Revert pending changes, also in another workspace (`/workspace:`, level 3) |
| `get` | Update the workspace to a version; `/preview` shows what would happen |
| `resolve` | List conflicts, or resolve them with `/auto:` |
| `reconcile` | Promote detected changes to pending changes, or clean untracked files |
| `localversions` (`lv`) | Which version of each item the workspace has |
| `workfold`, `workspaces` | Mappings of the current workspace (`/map`, `/unmap`, `/cloak`, `/decloak` edit them); workspaces of the collection |
| `workspace` | Show, create (`/new`), edit and delete a workspace |
| `history` (`hist`) | Changesets of an item or folder, with filters by comment, change type and work item |
| `changeset` | Details of one checkin; `/diff` prints its unified diff; `/comment:` and `/notes:` edit it |
| `view` | Content of a file at a version, written to the console or to `/output:` |
| `difference` (`diff`) | Unified or brief diff of a local change, two versions, two items or a shelveset |
| `folderdiff`, `annotate` | Compare two folders; who changed each line of a file |
| `dir` | Server-side listing of a folder |
| `properties` (`info`), `property` | Local and server information about an item; the item's properties (listing, `/setvalues`, `/deletevalues`) |
| `resolvepath` | The server path of a local path and vice versa |
| `labels`, `label`, `unlabel` | Labels of the collection; attach a label to items or remove it |
| `branches`, `branch`, `merge`, `merges` | Branch tree of an item; branch an item; merge between branches (`/candidate`, `/preview`); merge history |
| `rollback`, `undelete` | Pend the reverse of a changeset; bring deleted items back |
| `checkin` (`ci`) | Check in pending changes with comment, work items and notes; `/validate` runs the checkin policies without writing |
| `shelve`, `shelvesets`, `unshelve` | Park pending changes on the server, list shelvesets, take one back |
| `lock` | Lock or unlock items on the server (`/lock:checkin`, `/lock:none`) |
| `destroy` | Remove items and their history from the server for good; a `/preview` of the same arguments is required first |
| `permission` | Show and change item permissions (`/allow`, `/deny`, `/remove`, `/inherit`) |
| `protection` | Show the protection level; `protection set <1\|2\|3>` changes it in an interactive terminal |
| `docs` (`msdn`) | Open the documentation of a command in the browser, or print its URL |
| `help`, `version` | Command catalog (`/format:json` for agents); versions of tfp and the client libraries |

`proxy`, `configure` and `certificates` are never offered (`TFP0005`). The switches of `tf.exe` that
TfPortable leaves out answer `TFP0006` with the reason.

Every switch of every command, and what TfPortable does with it, is in
[docs/commands.md](docs/commands.md). Where TfPortable deliberately differs from `tf.exe`
(exit codes, dates, truncation, encoding) is in [docs/compat.md](docs/compat.md). The JSON output
is described in [docs/json-schema.md](docs/json-schema.md).

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
| 1 | Nothing changes on the server: reads, and the work in your own workspace that stays local until a checkin (pending changes, which `undo` takes back; the files on disk; a shelveset restored into it) | `status`, `history`, `changeset`, `view`, `difference`, `dir`, `properties`, `localversions`, `workfold`, `workspaces`, `labels`, `branches`, `merges`, `folderdiff`, `annotate`, `shelvesets`, `resolvepath`, the listings of `permission` and `workspace`, `property` (listing and `/setvalues /deletevalues /deleteall`), `docs`, `help`, `version`, `whoami`, `login`, `logout`, `protection`, `add`, `checkout`, `delete`, `rename`, `undo`, `get`, `resolve`, `reconcile`, `merge`, `branch`, `rollback`, `undelete`, `unshelve` |
| 2 (default) | The everyday writes to the server | level 1 plus `checkin`, `shelve`, `unshelve /move`, `label`, `unlabel`, `lock`, `/lock:checkin\|checkout` on the commands that pend changes, `workfold /map /unmap /cloak /decloak`, `changeset /comment /notes` |
| 3 | Everything, including irreversible changes and other people's workspaces | level 2 plus `shelve /delete`, `label /delete`, `undo /workspace:<name>`, `lock /workspace:<name>`, `destroy`, `permission` changes (`/allow`, `/deny`, `/remove`, `/inherit`), `workspace /new`, `/delete` and edits |

The full table with the per-switch levels is in `docs/SPEC.md` ("Úrovně ochrany") and
`docs/commands.md`.

## For AI agents

TfPortable works with any agent that runs shell commands: every command has a stable exit code,
a `TFPxxxx` error code with a next step ([docs/errors.md](docs/errors.md)), and `-format:json`. For Claude Code,
[docs/CLAUDE.md](docs/CLAUDE.md) is a ready-made section for the `CLAUDE.md` of a TFVC project
plus a permissions snippet for `.claude/settings.json`: which commands to use, dash switches,
`-format:json`, what exit codes 110 and 120 mean, and that the agent never asks for or enters a
PAT. Read-only commands run without confirmation; commands that change the workspace or write to
the server (`get`, `undo`, `checkin`, `shelve`, `merge`, `permission`, ...) ask first; `destroy` and
`protection set` are denied, a person runs them in their own terminal. Other agents can use the
same text as a starting point.

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
