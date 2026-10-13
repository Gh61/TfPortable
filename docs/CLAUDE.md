# TfPortable in a project's CLAUDE.md

Copy the section below into the `CLAUDE.md` of a project that lives in TFVC, and the permissions
snippet into its `.claude/settings.json`. Both are written for Claude Code on Windows, which runs
commands through Git Bash: MSYS turns an argument such as `/recursive` into the path
`C:/Program Files/Git/recursive`, so every switch is written with a dash (`-recursive`), which
both tf.exe and TfPortable accept. TfPortable detects a converted switch and reports it as
`TFP0002` with the dash form in the hint.

Setup (once, by a person):

1. Put `tfp.exe` into a folder of its own, e.g. `C:\Tools\TfPortable\`, and add that folder to the
   user's `PATH` (it does not conflict with an installed `tf.exe`), or write the full path into
   the section below.
2. Run `tfp login /collection:https://dev.azure.com/<organization>` in your own terminal and
   paste a Personal Access Token with **Scopes: Full access**. The token goes into the Windows
   Credential Manager; the agent never sees it.

## Section for CLAUDE.md

```markdown
## Version control: TFVC through TfPortable

- The repository is in TFVC, not in Git. Use `tfp` (TfPortable), never git.
- Write switches with a dash: `tfp status -recursive`, not `/recursive`.
- Put server paths into single quotes: `'$/Project/src'`.
- Add `-format:json` when you process the output: one JSON document per command, `ok`,
  `exitCode`, `data`, `errors[]` with a stable `code` (TFPxxxx) and a `hint`.
- Start with `tfp whoami -format:json` to see the identity, the collection, the workspace and
  the protection level.
- The protection level (0 read-only, 1 nothing changes on the server, 2 everyday work, 3 everything) decides which commands run.
  Exit code 100 with `TFP0004` means the command is above the level: report it and leave the
  level alone. Never run `tfp protection set`; a person changes the level in their own terminal.
- Do not check in unless the task says so; leave the work as pending changes otherwise. When it
  does: `tfp checkin <folder> -recursive -comment:"<what and why>" -associate:<work item>` after
  a `-validate` run; `tfp shelve <name> <folder> -recursive -comment:"..."` parks the work on the
  server and `tfp unshelve <name>` takes it back. `TFP0407` means a checkin policy failed (usually
  a missing `-associate`), `TFP0409` a policy only Visual Studio can evaluate. Do not add
  `-override` on your own; the reason it records is a person's decision.
- Exit code 110 means a problem with the PAT: tell the user to run `tfp login` in their own
  terminal. Never ask for a PAT and never enter one yourself.
- Exit code 120 means TfPortable found no workspace or collection, the path is not mapped, or the
  workspace is a server workspace; follow the hint of the error (`-collection:<url>` or `cd` into a
  mapped folder for the first case).
- Use the canonical command names (`status`, `history`, ...): the permission rules below do not
  list the aliases (`stat`, `hist`, `lv`, `co`, `del`, ...), so an alias asks for confirmation.
- Before `tfp get`, `tfp undo` and `tfp reconcile` run the `-preview` variant first when the
  command has one.
- Do not pass `-lock:checkin` or `-lock:checkout` to `add`, `checkout`, `delete`, `rename` and the
  branching commands unless the task asks for a lock: a lock blocks other people on the server until the
  checkin or `undo`.
- `TFP0006` means a tf.exe switch TfPortable does not support yet; the message says what it does. Do not
  retry with another spelling; report it.
- `tfp history` returns at most 256 changesets without `-stopafter:<n>`; the output says how to
  continue. `tfp history ... -comment:<text>`, `-changetype:<types>` and `-workitem:<id>` search.
- `tfp changeset <n> -diff` shows what a checkin changed as a unified diff.
- `tfp merge`, `tfp branch`, `tfp rollback` and `tfp undelete` only pend changes in the workspace
  (`tfp undo` takes them back); `tfp merge -candidate` and `tfp merge ... -preview` show what a merge
  would bring first. Nothing reaches the server until a checkin.
- `tfp help <command>` lists the switches of a command; `tfp help -format:json` the whole catalog;
  `tfp docs <command> -url` gives the link to its documentation.
- `tfp resolvepath <path>` translates a local path to its server path and back; `tfp permission '$/...'`
  and `tfp property <path>` read permissions and properties. `tfp property -setvalues:name=value <path>`
  pends a property change (reversible with `tfp undo`, stored by `checkin`).
- Never run `tfp destroy`, `tfp permission` with `-allow`/`-deny`/`-remove`, or `tfp workspace -new`/`-delete`
  on your own: they need protection level 3 and a person's decision. `destroy` refuses to run without a
  `-preview` of the same arguments in the last 10 minutes (`TFP0410`).
```

## Permissions for `.claude/settings.json`

Read-only commands run without confirmation; commands that change the workspace ask first.
The rules are prefix matches: `merge -candidate` is allowed only with the switch directly after
`merge` (the order tf.exe uses). `view -output:<file>` writes a local file; move `view` to `ask`
if that matters in your project.

```json
{
  "permissions": {
    "allow": [
      "Bash(tfp status:*)", "Bash(tfp history:*)", "Bash(tfp changeset:*)",
      "Bash(tfp view:*)", "Bash(tfp diff:*)", "Bash(tfp difference:*)", "Bash(tfp dir:*)",
      "Bash(tfp info:*)", "Bash(tfp properties:*)", "Bash(tfp localversions:*)",
      "Bash(tfp workfold:*)", "Bash(tfp workspaces:*)", "Bash(tfp whoami:*)",
      "Bash(tfp labels:*)", "Bash(tfp branches:*)", "Bash(tfp merge -candidate:*)",
      "Bash(tfp merges:*)", "Bash(tfp folderdiff:*)", "Bash(tfp annotate:*)",
      "Bash(tfp shelvesets:*)", "Bash(tfp help:*)", "Bash(tfp version:*)", "Bash(tfp protection)",
      "Bash(tfp docs:*)", "Bash(tfp resolvepath:*)"
    ],
    "deny": [
      "Bash(tfp protection set:*)", "Bash(tfp destroy:*)"
    ],
    "ask": [
      "Bash(tfp get:*)", "Bash(tfp undo:*)", "Bash(tfp reconcile:*)",
      "Bash(tfp resolve:*)", "Bash(tfp delete:*)", "Bash(tfp checkin:*)", "Bash(tfp shelve:*)",
      "Bash(tfp unshelve:*)", "Bash(tfp label:*)", "Bash(tfp unlabel:*)", "Bash(tfp lock:*)",
      "Bash(tfp merge:*)", "Bash(tfp branch:*)", "Bash(tfp rollback:*)", "Bash(tfp undelete:*)",
      "Bash(tfp permission:*)", "Bash(tfp property:*)", "Bash(tfp workspace:*)", "Bash(tfp workfold -map:*)", "Bash(tfp workfold -unmap:*)",
      "Bash(tfp workfold -cloak:*)", "Bash(tfp workfold -decloak:*)"
    ]
  }
}
```

`add`, `checkout` and `rename` are reversible with `undo`, so a team may move them to `allow`.
`login` and `logout` stay with the person: the agent has no reason to run them. `permission` is in `ask`
because the same command lists (level 0) and changes (level 3) permissions, and `property` because
`-setvalues`/`-deletevalues`/`-deleteall` pend a change (level 1, reversible with `undo`); a team that only reads
them may move `tfp permission` and `tfp property` to `allow`. `destroy` is denied outright: a person runs it in their own terminal.

## What the agent gets

| Need | Command |
| --- | --- |
| Where am I, who am I | `tfp whoami -format:json` |
| What changed locally | `tfp status -recursive -format:json` (pending and detected changes) |
| What a checkin contained | `tfp changeset 1234 -format:json`, `tfp changeset 1234 -diff` |
| Recent history of a folder | `tfp history '$/Project/Main' -recursive -stopafter:20 -format:json` |
| Who touched a file and why | `tfp history 'Main\src\App.cs' -format:detailed` |
| A file at a version | `tfp view '$/Project/Main/src/App.cs' -version:C1234` |
| Who wrote each line | `tfp annotate 'Main\src\App.cs' -format:json` |
| What was merged between branches | `tfp merges '$/Project/Main' '$/Project/Release' -recursive`, `tfp folderdiff '$/Project/Main' '$/Project/Release' -recursive` |
| Local change against the server | `tfp diff 'Main\src\App.cs'` |
| Start editing, add, rename, delete | `tfp checkout`, `tfp add`, `tfp rename`, `tfp delete` (all pending, reversible with `tfp undo`) |
| Update the workspace | `tfp get -recursive -preview`, then `tfp get -recursive` |

Every error has a `TFPxxxx` code, the original `TFxxxxx` code when the server produced one, the
server message verbatim and a one-sentence next step. The codes, their causes and the next steps are
listed in [docs/errors.md](errors.md) (the source is `docs/SPEC.md`, "Chybové hlášky").
