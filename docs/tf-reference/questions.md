# Questions for the decompiled tf.exe

What TfPortable assumed without a capture or a documented answer. Each question names where the assumption
lives. Move an answered question into `docs/tf-reference/<command>.md` (template in README.md) and tell the
agent; it adjusts the code and `docs/compat.md`.

Answered on 2026-10-10 from tf.exe 18.10.12215.6 (the numbers are kept so that older notes still resolve):

| Questions | Note |
| --- | --- |
| 1, 2, 3 (exit codes, streams, `.tfignore`) | `general.md` |
| 4–8 (`checkin`) | `checkin.md` |
| 9–13 (`shelve`, `shelvesets`, `unshelve`) | `shelve.md` |
| 14–18, 34 (`label`, `unlabel`, `labels`) | `label.md` |
| 19, 20 (`lock`) | `lock.md` |
| 21 (`destroy`) | `destroy.md` |
| 22, 23 (`permission`) | `permission.md` |
| 24 (`workspace /new`) | `workspace.md` |
| 25 (`undo /workspace`, client side) | `undo.md` |
| 26 (`property`) | `property.md` |
| 27, 28 (`merge`) | `merge.md` |
| 29 (`branch`) | `branch.md` |
| 30, 33 (`rollback`) | `rollback.md` |
| 31 (`undelete`) | `undelete.md` |
| 32 (`folderdiff`) | `folderdiff.md` |

## Still open (not answerable from the client code)

25a. `undo /workspace:<other>` without the "undo other users' changes" permission: the server's TFxxxxx message.
    tf.exe makes no client-side check (`undo.md`); only a capture with a second account can show the text.

35. `permission` change that the server applies nowhere (every part refused): tf.exe exits 100 because the
    result list is empty (`permission.md`). Decided on 2026-10-10: tfp exits 100 too when nothing was applied,
    a partial result stays exit 1 through the item errors. Still untested in CI until the CI user may change
    permissions (`docs/DEVELOPER-TODO.md` §7).

36. `destroy /startcleanup` on Azure DevOps Services: tf.exe only forwards the flag (`destroy.md`). The destroy
    integration test passes it since 2026-10-10; the CI result says whether the service accepts it (recorded in
    `docs/compat.md` §22 once known).

## New facts that need a decision (see PROGRESS.md "Needs the user")

- `property` has a write form in tf.exe 18.10 (`/setvalues`, `/deletevalues`, `/deleteall`, pending property
  changes) and read-side switches `/propertynames` and `/output`; SPEC.md and compat.md §21 said there was none.
- `add /noignore` exists in tf.exe 18.10 (tfp has it in the catalog already).
