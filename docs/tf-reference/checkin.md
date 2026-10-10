# checkin (tf.exe 18.10.12215.6)

Read from: `TF/Microsoft.TeamFoundation.VersionControl.CommandLine/CommandCheckin.cs` (`Run`,
`CheckInFromWorkspace`, `CheckInFromShelveset`, `EvaluateCheckin`, `HandlePolicyOverride`), 2026-10-10.

## Switches and defaults

- Declared options: `/all`, `/author`, `/bypass`, `/collection`, `/comment`, `/date`, `/force`, `/new`, `/notes`,
  `/override`, `/recursive`, `/reset`, `/saved`, `/server`, `/shelveset`, `/validate`, `/noautoresolve`,
  `/noconflictscheckforgated`. **`/associate` and `/resolve` are not options of tf.exe 18.10** (question 6):
  they are refused as unrecognized, exit 100. Work items reach a checkin only through the dialog or through the
  saved checkin metadata (`/saved`), as `WorkItemCheckinInfo` built with the work item object model.
- `/comment` and `/override` accept `@file`; `/override` with an empty reason is an argument error (exit 100).
- `/date` is refused ("option not allowed") outside Microsoft's test environment.
- `/validate` and `/all` cannot be combined; `/bypass` needs `/noprompt`.
- Without an item spec and without `/all`: all pending changes of the current workspace; none → stderr warning
  "no pending changes", exit 1. With item specs: the matching pending changes; none → exit 100 (and a stderr
  warning per spec that matched nothing).

## Evaluation (question 4)

- In `/noprompt` mode (the only mode an agent sees) the command calls
  `Workspace.EvaluateCheckin2(options, allChanges, checkinChanges, comment, notes, workItems)` **before every
  checkin**, with `CheckinEvaluationOptions.Policies | Notes | AddMissingFieldValues`; `/validate` adds
  `Conflicts`. So tf.exe evaluates policies and notes always (loading the policy assemblies from the Visual
  Studio installation), and conflicts only with `/validate`; a plain checkin leaves conflicts to the server
  call, which answers with a `CheckinException` (exit 100).
- Note failures are printed as stderr warnings and block the checkin; a policy evaluation exception (a policy
  that could not be loaded) is a stderr warning and blocks too; conflicts print as stderr warnings (resolvable)
  or errors.
- `/validate`: after the evaluation prints "validation succeeded" (stdout, exit 0) or "validation failed"
  (stderr, exit 100) and stops. Nothing is sent.

## `/override` (question 5)

- Policy failures are always printed on stderr ("Policy violations:" plus one indented line each).
- With failures and a reason: `PolicyOverrideInfo(reason, failures)` goes into the checkin parameters, a
  stdout info line says the failures were overridden, and the checkin proceeds.
- With failures and no reason: the checkin is blocked, exit 100.
- **Without failures the reason is dropped silently**: no `PolicyOverrideInfo` is sent, nothing is printed,
  tf.exe does not refuse `/override`.

## Library calls

- `Workspace.CheckIn(WorkspaceCheckInParameters)` with `Author` (`/author`, `ValidateAuthor = true`),
  `CheckinNotes`, `AssociatedWorkItems`, `PolicyOverride`, `QueueBuildForGatedCheckIn = /noprompt`,
  `OverrideGatedCheckIn = /noprompt && /bypass`, `AllowUnchangedContent = /force`, `NoAutoResolve`,
  `NoConflictsCheckForGatedCheckin`; `StripUnsupportedCheckinOptions` first. Pending changes are printed as they
  are checked in through the library's `BeforeCheckinPendingChange` event (grouped by folder, stdout).
- A `CheckinException` (conflicts, out of date, locks) is exit 100 with the message on stderr. A gated checkin
  exception is handled by the gated-checkin handler (shelve and queue a build; "queued" is a stderr warning with
  exit 1).

## Result (question 7)

- Changeset number > 0: blank line, "Changeset N checked in" on stdout, exit 0 (or 1 when an item error
  preceded it).
- Changeset number 0 (the server found nothing to commit, for example an unchanged edit without `/force`):
  stderr warning "no changes left to check in", exit 1.
- Any failure without a changeset: the exception message on stderr, exit 100, followed by "no files checked
  in" on stderr unless the exception was a `CheckinException` (which already said what happened).

## Shelveset form (question 8)

- `checkin /shelveset:name[;owner]` refuses item specs and `/all`, `/comment`, `/date`, `/new`, `/notes`,
  `/override`, `/recursive`, `/saved`, `/validate`, `/noconflictscheckforgated` (exit 100) and requires
  `/collection`.
- Library call: `VersionControlServer.CheckInShelveset(ShelvesetCheckInParameters)` with `Author`,
  `ValidateAuthor = true`, `DeleteShelveset = true`, `QueueBuildForGatedCheckIn = /noprompt`,
  `OverrideGatedCheckIn = /bypass`, `AllowUnchangedContent = /force`. No evaluation, no override: the shelveset's
  own comment, notes and policy override are used as stored. Result handling as above.

## Consequences for TfPortable

- compat.md §16/§20 (`checkin /associate` is an extension): confirmed.
- compat.md §16 (tfp evaluates conflicts and notes itself, policies natively): tf.exe evaluates policies and
  notes before every checkin and conflicts only with `/validate`; tfp's extra conflict check is a deliberate
  deviation already recorded (TFP0406 before the server call).
- Unchanged edit → exit 1 (compat.md §20): confirmed, it is the "changeset 0" path.
- `/override` with nothing failing: tf.exe ignores the reason silently; tfp does the same (no override info
  without failures) — no change needed, the behavior is now documented rather than assumed.
