# Error codes

Every error TfPortable reports carries a stable `TFPxxxx` code, the original `TFxxxxx` or `VSxxxxxx`
code when the server produced one, the server message verbatim, a one-sentence next step and the
process exit code. In text mode the error goes to stderr:

```
ERROR TFP0301 [TF14019]: Item $/Contoso/App/Old.cs does not exist at version C1200.
  Server message: TF14019: The item $/Contoso/App/Old.cs could not be found at version C1200.
  Next step: Check the path with 'tfp dir $/Contoso/App /version:C1200'.
```

With `/format:json` the same error is a Message in `errors[]` of the envelope
(`code`, `tfCode`, `message`, `hint`, `item`, `activityId`; [json-schema.md](json-schema.md) section 1).
An item error (one item failed, the others were processed) does not stop the command: the exit code
is 1 and every item error is listed. A fatal error ends the command with the exit code of the table.

The exit code in the table is the default; where the original tf.exe uses another code for the same
situation, TfPortable follows it per command ([compat.md](compat.md) section 1.1, for example TFP0301 is
1 for `add`, `delete`, `view`, `dir`, `undelete` and 100 for `rename`, `changeset`, `label`). Exit codes
2, 110 and 120 tell an agent what to do next without parsing the text: unknown command, a PAT problem,
no workspace or collection.

## Command line (TFP00xx)

| Code | Situation | What to do | Exit |
| --- | --- | --- | --- |
| TFP0001 | Unknown command. | `tfp help` lists the commands; aliases (`stat`, `co`, `hist`, ...) are accepted. | 2 |
| TFP0002 | A switch or a value the command does not accept, a missing required argument, or a switch converted by Git Bash (`/recursive` became `C:/Program Files/Git/recursive`). | `tfp help <command>` lists the switches. In Git Bash write switches with a dash: `-recursive`. | 100 |
| TFP0003 | A tf.exe command or switch that is recognized but not available in this build (planned for a later phase). | Use Visual Studio or the original tf.exe for it. From version 1.0 this code is never returned. | 100 |
| TFP0004 | The command or switch needs a higher protection level than the configured one (0 read-only, 1 nothing changes on the server, 2 everyday work, 3 everything). | A person raises the level with `tfp protection set <n>` in an interactive terminal. An agent reports it and leaves the level alone. | 100 |
| TFP0005 | A tf.exe command TfPortable never offers: `proxy`, `configure`, `certificates`, the `git` and `settings` groups (client configuration, dialogs, certificate sign-in). | The message says what the command does and why it has no place in a portable, PAT-only tool; use Visual Studio or tf.exe. | 100 |
| TFP0006 | A switch (or a switch value) of tf.exe that TfPortable does not support yet, e.g. `checkin /bypass`, `/format:xml`, `workspace /newowner` (the list is in `docs/commands.md`, marked ✗). A typo or an unknown switch is TFP0002, not this code. | The message says what the switch does; use Visual Studio or the original tf.exe for it, or ask for it to be added. `tfp help <command>` marks these switches. | 100 |

## Sign-in (TFP01xx, exit 110)

| Code | Situation | What to do | Exit |
| --- | --- | --- | --- |
| TFP0101 | No Personal Access Token is stored for the collection and there is no interactive terminal to ask for one. | A person runs `tfp login /collection:<url>` in their own terminal. Never paste a PAT into an agent conversation. | 110 |
| TFP0102 | The server rejected the token (expired, revoked, wrong collection). TF30063, TF400813, HTTP 401. | Create a new token with Scopes: Full access and run `tfp login` again. | 110 |
| TFP0103 | The token is valid but lacks the scope the TFVC API needs (REST accepted it, the SOAP endpoints answered 401). | Create a token with Scopes: Full access; a read-only or Code-scoped token cannot use TFVC. | 110 |
| TFP0104 | The Windows Credential Manager could not be read or written (Win32 error in the message). | Check the user profile and the Credential Manager; the error names the Win32 code. | 110 |

## Workspace and collection (TFP02xx)

| Code | Situation | What to do | Exit |
| --- | --- | --- | --- |
| TFP0201 | No local workspace maps the current folder and no collection is known (the message lists the searched folders and the workspaces on this computer). | Run the command inside a mapped folder, or pass `/collection:<url>` and `/workspace:<name>`; `tfp workspaces /collection:<url>` lists them. | 120 |
| TFP0202 | The server is unreachable (DNS, proxy, timeout). | Check the URL and the network; `/verbose` prints the diagnostics (never request headers). | 100 |
| TFP0203 | The path is not mapped in the workspace, or a local path was given outside any workspace. | Use a server path (`$/Project/...`) or run inside a mapped folder; `tfp workfold` shows the mappings; `tfp resolvepath` translates a path. | 120 |
| TFP0204 | The workspace is a server workspace; TfPortable supports local workspaces only. | Convert it in Visual Studio (Manage Workspaces > Edit > Advanced > Location: Local) or create a local one with `tfp workspace /new`. | 120 |

## Items and versions (TFP03xx)

| Code | Situation | What to do | Exit |
| --- | --- | --- | --- |
| TFP0301 | The item, version, label, shelveset, workspace or identity does not exist (or the user cannot see it). TF14019, TF14022, TF14045, VS403405. Also "nothing pending" for `checkin`, `shelve` and `undo`. | `tfp dir`, `tfp history` or `tfp status` to find it; for a deleted item use `;X<deletion id>` or `tfp dir /deleted`. | 100 (1 for `add`, `delete`, `view`, `dir`, `undelete`, `annotate`, `unlabel`, `undo`, `lock /lock:none`, an unchanged `checkin` edit) |
| TFP0302 | A versionspec the parser does not understand. | Use `C<n>`, `<n>`, `T`, `L<label>[@scope]`, `D<date>`, `W[name[;owner]]`, or a range `a~b`. | 100 |
| TFP0303 | The content is binary and cannot be printed on the console. | `tfp view ... /output:<file>` writes it to a file. | 100 |

## Workspace operations and checkins (TFP04xx)

| Code | Situation | What to do | Exit |
| --- | --- | --- | --- |
| TFP0401 | The item already has a conflicting pending change (an add of a versioned file, a delete of an edited file, ...). | `tfp status` shows it; `tfp undo` drops it. | 1 |
| TFP0402 | A writable local file without a pending change was not overwritten (`get`, `unshelve`). | `tfp diff` shows the local change; `tfp get /overwrite` overwrites it, or move the file away. | 1 |
| TFP0403 | The operation left conflicts (`get`, `merge`, `rollback`, `unshelve`, `resolve`). The pending changes stay. | `tfp resolve` lists them; `tfp resolve <item> /auto:TakeTheirs|KeepYours|AutoMerge` resolves them. | 1 |
| TFP0404 | The item is locked by another user or workspace. | The message names the lock owner; they release it with `tfp lock /lock:none`. | 1 (100 for `checkin`) |
| TFP0405 | `reconcile /clean` without `/noprompt`: deleting untracked files needs an explicit confirmation. Also a rename onto an existing item. | Run with `/preview` first, then with `/noprompt`. | 100 |
| TFP0406 | The checkin was stopped by a conflict: a newer version on the server or a lock. TF10141. | `tfp get` and `tfp resolve` for the item; a lock is released by its owner. | 100 |
| TFP0407 | A checkin policy TfPortable evaluates failed ("Work Items" without `/associate`, "Changeset Comments"), a required checkin note is missing, or the checkin is gated. | Fix it, or a person decides on `/override:<reason>` (the reason is recorded in the changeset). | 100 |
| TFP0408 | A shelveset of that name already exists. | Choose another name or pass `/replace`. | 100 |
| TFP0409 | A checkin policy only Visual Studio can evaluate (Builds, Code Analysis, custom). | Check it in Visual Studio, or a person decides on `/override:<reason>`. | 100 |
| TFP0410 | `destroy` without a `/preview` run with the same arguments in the last 10 minutes. | Run `tfp destroy /preview ...`, read the listing, then the same command without `/preview`. | 100 |

## Server and unexpected errors (TFP05xx, TFP09xx)

| Code | Situation | What to do | Exit |
| --- | --- | --- | --- |
| TFP0501 | The server answered with an error TfPortable does not map (HTTP 5xx, TF400022 checkout locks in local workspaces, a work item that could not be linked, ...). The server message and its ActivityId are in the error. | Read the server message; retry; search the TF code. | 100 (1 when one item of many failed) |
| TFP0900 | An unexpected exception. The stack trace is written to `%TEMP%\TfPortable\last-error.txt`; `/verbose` prints it on stderr. | Report it with the file and `tfp version`. | 100 |

## Warnings

Warnings do not change the exit code. In text mode they are `WARNING TFPxxxx: <text>` on stderr (the
code only when one applies, for example TFP0103 for a token without the full scope that still works
over REST); in JSON they are Messages in `warnings[]`.
