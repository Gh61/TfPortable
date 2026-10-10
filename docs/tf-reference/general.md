# General: exit codes, streams, option parsing (tf.exe 18.10.12215.6)

Read from: `Microsoft.TeamFoundation.Client/Microsoft.TeamFoundation.Client.CommandLine/Command.cs` (`SetExitCode`),
`CommandLine.cs` (`RunCommand`), `ExitCode.cs`, `Options.cs` (`GetIDFromAlias`), `ArgumentParser.cs`;
`Microsoft.TeamFoundation.Client/Microsoft.TeamFoundation.Client/ConsoleHost.cs` (`Write`);
`TF/.../TFCommand.cs` (`NonFatalError`), `TFCommandLine.cs` (`RunCommand`, `LookupCommand`); 2026-10-10.

## Exit codes (question 1)

- The exit-code set is fixed: 0 success, 1 partial success, 2 unrecognized command, 3 not attempted, 100 failure
  (4 "reboot required" exists but no TFVC command uses it). The process exit code starts at -1 and becomes 0 when
  the command ends without having set anything.
- One rule, not a table per command: a command raises the exit code with `SetExitCode(code)`. A higher code
  replaces a lower one. A *different lower* code set later collapses the result to 1 (for example an item error
  that set 1, then an explicit "success" after the server call: the result stays 1).
- Per-item failures reported by the client library through its `NonFatalError` event set 1 when the failure has
  error severity (and print it on stderr); a warning-severity failure changes nothing (and prints on stderr as a
  warning). The same handler is used by every command, so "one item failed, the rest went through" is 1 everywhere.
- Any exception that escapes the command's `Run` is caught once, at the top: its message goes to stderr and the
  exit code is 100 (an unrecognized command 2, a "blocked" exception 3; `NullReferenceException` and
  `ArgumentNullException` crash the process). Server faults, argument errors and library exceptions therefore
  all end as 100.
- Commands set 100 themselves in "nothing matched" cases where the library did not throw (examples in the command
  notes: `labels` with no match, `label` with no result, `checkin` with no matching pending change, `unlabel`
  with no mapped item, `permission` change that applied nothing).
- Commands set 1 themselves for soft outcomes: `shelvesets` with an empty list, `checkin` that the server
  answered with changeset 0, `add` with `.tfignore` matches (see below), a declined interactive question,
  conflicts and writable-file warnings of `get`/`merge`/`rollback`.
- A `TeamFoundationServerUnauthorizedException` is retried up to three times with an interactive sign-in unless
  `/noprompt` is in effect or no collection is set; then it propagates (100).

## stdout versus stderr (question 2)

- One console host decides by message kind, not by command: `WriteLine`/`Write` and `WriteInfo` go to
  stdout; `WriteWarning` and `WriteError` go to stderr. Which kind a message is, is the command's choice, so an
  empty result can be a stdout line in one command (`shelvesets`, exit 1) and a stderr error in another
  (`labels`, exit 100). The exception message of a failed command (exit 100) is always stderr.
- Listings, tables, folder headers, "checked in" and "created" confirmations are stdout. Conflicts, policy
  failures, note failures, "nothing to do" warnings and the summary of ignored items are stderr.
- `/noprompt` is implied whenever stdout is redirected (a pipe or a file), unless the environment variable
  `TFS_IGNORESTDOUTREDIRECT=1` is set; `/prompt` forces the dialogs back on. The golden captures therefore ran as
  `/noprompt` even where the script did not pass it.
- Environment variables with an effect on output: `TFSVC_NOSUMMARY=1` suppresses the get/merge summary,
  `TFSVC_NOPROMPT=1` implies `/noprompt`, `TF_INTERNAL_OUTPUT_CP` sets the console code page.

## Option names

- An option name is the exact enum name of the option, compared case-insensitively (`/setvalues`, `/noignore`,
  `/filterlocalpathsonly`); there are no abbreviations except the fixed one-letter aliases `/?` `/h` help,
  `/b` buildUri, `/c` comment, `/d` delete, `/f` format, `/i` noprompt, `/k` lock, `/m` computer, `/n` newname,
  `/o` owner, `/p` force, `/r` recursive, `/s` server, `/t` template, `/u` user, `/v` version, `/w` workspace,
  `/x` slotmode, `/y` login. An unknown name is "unrecognized option" (exit 100). Every command declares its
  option list; a known option that the command did not declare is refused the same way.
- Option *values* are matched case-insensitively against the exact value names (`/format:Brief`, `/child:merge`,
  `/lock:checkout`); no prefix matching.
- `/collection` and `/server` are the same option (giving both is an error); `/login`, `/loginType`,
  `/impersonate`, `/exitcode`, `/noprompt`, `/prompt` are global.
- The command table knows these aliases: `del`→delete, `diff`→difference, `edit`→checkout, `hist`→history,
  `info`/`information`/`prop`→properties, `move`/`ren`→rename, `perm`→permission, `rec`→reconcile,
  `stat`→status, `submit`→checkin, `treediff`→folderdiff, `undel`→undelete. Lookup is a binary search over the
  sorted alias list, case-insensitive.

## `.tfignore` and `add` (question 3)

- tf.exe does not read `.tfignore` itself. `add` calls `Workspace.PendAdd(paths, recursive, encoding, lock,
  treatMissingItemsAsFiles: false, silent, applyLocalItemExclusions: !/noignore)`. The library's file-system walk
  applies the exclusions (the server's default exclusion set plus `.tfignore` files) while expanding folders and
  wildcards, collects the excluded paths, and at the end raises its `NonFatalError` event once with a
  warning-severity failure whose code is `ItemsIgnoredBecauseOfExclusions` and whose message lists the excluded
  paths joined by `;`.
- A warning alone would leave exit 0. `add` overrides the handler: when the failure code is
  `ItemsIgnoredBecauseOfExclusions` it sets exit 1. That is the whole mechanism behind the capture
  `add/recursive-with-tfignore` (stderr line, exit 1).
- `add` without any path: in a local workspace it pends every detected add (candidate changes under `$/`) with
  exclusions applied; "no adds detected" is a stderr error with exit 1. In a server workspace, or with `/noignore`,
  a path is required (exit 100).
- `/noignore` is a switch of tf.exe 18.10 (`add` only); `/lock`, `/silent`, `/type` (deprecated form of
  `/encoding`) are the others.

## Consequences for TfPortable

- compat.md §1.1 (exit-code table built from captures) is consistent with the rule above; the per-command
  "overrides" are the explicit `SetExitCode` calls listed in the command notes.
- compat.md §1.2 (streams): confirmed, "each command decides" at message level, with a uniform host.
- compat.md §20 `add/recursive-with-tfignore`: the recorded deviation (tfp exits 0) stands; the original's exit 1
  is deliberate code in `add`, not a side effect.
