# shelve, shelvesets, unshelve (tf.exe 18.10.12215.6)

Read from: `TF/Microsoft.TeamFoundation.VersionControl.CommandLine/CommandShelve.cs` (`Shelve`, `DeleteShelveset`),
`CommandShelvesets.cs` (`Run`), `CommandUnshelve.cs` (`Run`);
`Microsoft.TeamFoundation.VersionControl.Client/.../Client.cs` (`Unshelve`), `VersionControlException.cs`
(server fault mapping); 2026-10-10.

## shelve

- Options: `/comment`, `/move`, `/replace`, `/delete`, `/recursive`, `/saved`, `/validate`, `/new`;
  `/collection` and `/server` are declared but refused for shelving ("option not allowed", exit 100), accepted for
  `/delete`.
- The first free argument is the shelveset name (`name[;owner]`, owner defaults to the current user). With
  `/noprompt` the name is required (exit 100); interactively it defaults to the workspace name.
- Changes: with item specs the matching pending changes (none → stderr error "no changes shelved", exit 100);
  without, all pending changes of the current workspace (none → stderr warning, exit 1).
- Library call: `Workspace.Shelve(shelveset, changes, options)` with `ShelvingOptions.Move` for `/move` and
  `ShelvingOptions.Replace` for `/replace`; `shelveset.Comment` from `/comment`; `/saved` loads the last saved
  checkin metadata (comment is kept when given). Shelved changes print through the library's
  `BeforeShelvePendingChange` event (stdout, grouped by folder).
- Result line (question 10): always the same "shelveset created" line with the name, also with `/replace`;
  tf.exe has no "replaced" wording.
- Existing name without `/replace` (question 9): the server fault `ShelvesetExistsException` is mapped by the
  library to `ShelvesetExistsException` and propagates out of the command: the server's message on stderr,
  exit 100.
- `/validate` (question 13): only meaningful with the dialog; **`/validate` with `/noprompt` is an invalid
  option combination** (exit 100). In `/noprompt` mode nothing is evaluated before shelving.
- `/delete`: exactly one argument `name[;owner]`; refuses `/comment`, `/move`, `/replace`, `/recursive`,
  `/saved`, `/validate`, `/new`; asks a yes/no question unless `/noprompt` (a "no" is exit 100);
  `VersionControlServer.DeleteShelveset(name, owner)`.

## shelvesets (question 12)

- Options: `/collection`, `/owner`, `/format:brief|detailed|xml`, `/server`. At most one free argument
  `name[;owner]`; `*` as the name means any name.
- Owner: `/owner` wins over `name;owner`; no owner → `.` (the authenticated user); `/owner:*` → every owner.
- `VersionControlServer.QueryShelvesets(name, owner)`, sorted by name.
- Empty result: without a name → stdout line "no shelvesets found matching `*;owner`", exit 1; with a name →
  the same text as an `ArgumentException`, so stderr and exit 100.
- Brief: columns Shelveset, Owner, Comment (owner display name; the comment column padded to the console
  width). Detailed: `=` line, the four fields, the comment indented, check-in notes, work items, the policy
  override reason.

## unshelve (question 11)

- Options: `/move`, `/recursive`, `/nomerge`, `/noautoresolve`. First free argument `name[;owner]` (required with
  `/noprompt`), the rest are item specs; `/move` with item specs is an error. `/recursive` is allowed only with
  `/noprompt`.
- Library call (server 2012 or newer, without `/nomerge`):
  `Workspace.Unshelve(name, owner, ItemSpec.FromStrings(paths, recursion), null, null, null, merge: true,
  noAutoResolve, out changes, out conflicts)`. With `/nomerge` (or an old server) the three-argument overload,
  which does not merge.
- Inside the library: server failures are reported through `NonFatalError` (stderr, exit 1 per item); when there
  are failures **and no operation at all** it throws `UnshelveException` "no changes unshelved" (exit 100). A
  local pending change on the same item is therefore a per-item failure (exit 1) when other items went through,
  and `UnshelveException` (exit 100) when nothing could be unshelved.
- Merge conflicts come back in `conflicts` (a `Conflict[]`); in a local workspace the library first tries
  auto-resolution (unless `/noautoresolve`), then raises `UnshelveConflict` for every unresolved one, which
  tf.exe prints as a stderr warning ("Conflict: ..."). The conflict event itself does not change the exit code;
  the get operations behind the unshelve do (a writable file in the way is a warning with exit 1).
- `/move`: the shelveset is deleted only when the exit code is still 0 and no change was excluded
  (`shelveset.ChangesExcluded`); otherwise a stdout line says it cannot be deleted after a partial unshelve.
- Interactively the resolve dialog opens for conflicts; with `/noprompt` nothing more happens.

## Consequences for TfPortable

- compat.md §16/§20: `shelve /replace` says "replaced" (deviation, kept); `shelve /validate` with `/noprompt`
  is refused by tf.exe, tfp combines them (deviation, kept); TFP0408 exit 100 for an existing name matches the
  original's exit code; TFP0301 exit 100 for a missing named shelveset and exit 1 for an empty list match.
- `unshelve`: TFP0403 (exit 1) for open conflicts and TFP0401 (exit 100) for `UnshelveException` match the
  library's behavior; the original exits 1 for a partial unshelve with per-item failures, which tfp reports as
  item errors as well.
