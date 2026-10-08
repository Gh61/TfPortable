# Questions for the decompiled tf.exe

What TfPortable assumed without a capture or a documented answer. Each question names where the assumption
lives. Move an answered question into `docs/tf-reference/<command>.md` (template in README.md) and tell the
agent; it adjusts the code and `docs/compat.md`.

## General

1. How does tf.exe map an exception of the client library to the exit codes 1 and 100? Is there one rule
   (per-item failure = 1, fatal = 100) or a table per command? (`docs/compat.md` §1.1 is built from captures only.)
2. Which messages go to stderr and which to stdout: is it "errors and warnings to stderr, listings to stdout" or
   does each command decide? (§1.2)
3. Does tf.exe read `.tfignore` itself for `add /recursive`, or only through the library's `PendAdd(... applyIgnore)`?

## checkin

4. Which `CheckinEvaluationOptions` does `checkin` pass to the evaluation, and does it evaluate at all without
   `/validate`? (tfp: Conflicts and Notes, never Policies.)
5. What exactly does `/override:<reason>` send: a `PolicyOverrideInfo` with the failures of the evaluation, or
   with an empty list when there was no failure? Does tf.exe refuse `/override` when no policy failed?
6. `/associate:<id>` and `/resolve:<id>`: are they switches of tf.exe 18.10 (the captured help does not list
   them) and how do they reach the server (`WorkItemCheckinInfo` with `Associate`/`Resolve`)?
7. What does tf.exe do when `CheckIn` returns 0 (unchanged content without `/force`): which message, which exit?
8. The shelveset form (`checkin /shelveset:name;owner`): which library call, and does it honor `/override`?

## shelve, shelvesets, unshelve

9. `shelve` without `/replace` on an existing name: which exception, which exit code, which message? (tfp: TFP0408, exit 100.)
10. Which text does `shelve /replace` print: "created" or "replaced"? (tfp prints "replaced" when the name existed.)
11. `unshelve` without `/nomerge`: which `Unshelve` overload and which value of `merge`? With a local pending
    change on the same item, is it a conflict (`Conflict[]`) or an `UnshelveException`? (tfp: TFP0403 / TFP0401.)
12. `shelvesets` without a name: the owner filter is the current user or everyone? Exit code when a named
    shelveset does not exist (capture says 100) versus an empty list without a name?
13. `shelve /validate`: which evaluation does it run?

## label, unlabel

14. Default scope of `label name` without `@scope`: the team project of the first item, `$/`, or something else?
15. Default version of a labelled item: the workspace version for local paths and latest for server paths, or
    always the workspace version? (tfp: workspace version for local paths, latest for server paths.)
16. Default `/child`: Fail, Replace or Merge? (tfp: Fail.)
17. `unlabel`: which `VersionSpec` is passed to `UnlabelItem` (tfp: the label's own `LabelVersionSpec`)?
18. `label /delete name@scope`: `DeleteLabel` only, or a query first?

## lock

19. `lock /lock:none` on an item without a lock: exit 0 or an error? Does tf.exe list the item?
20. `lock /workspace:<other>`: which call (`Workspace.SetLock` of the other workspace through `GetWorkspace`)?

## protection-relevant commands (phase 4 candidates)

21. `destroy`: default of `/keephistory`, meaning of `/stopat`, what `/preview` prints, exit code when the item
    does not exist, whether `/startcleanup` is accepted by Azure DevOps Services.
22. `permission` listing: exact structure of the `====` blocks (identity, Allow, Deny, inherited) and the order of
    the permission names.
23. `permission /allow /deny /remove`: which library call (`SecurityService` or `VersionControlServer.SetPermissions`)
    and how `/user` and `/group` resolve an identity (display name, unique name, both).
24. `workspace /new` defaults: location (local or server) when `/location` is missing, permission profile, the
    default mapping (`$/` to the current folder or none).
25. `undo /workspace:<other>`: which permission the server checks and which message comes back when it is missing.
26. `property`: the exact switch forms of tf.exe 18.10 (`/setproperty`, `/deleteproperty`, reading) and whether
    the command exists at all in this version (the help capture is empty).
