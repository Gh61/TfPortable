# tf.exe reference notes (from decompilation)

Facts about what the original tf.exe does, verified by reading its decompiled implementation. They settle the
questions that neither the golden captures nor the Microsoft docs answer (default values, which library calls a
command makes, how exit codes are chosen) without running anything against a server.

## Rules

1. **Reference only.** The decompiled sources show what tf.exe does; TfPortable is written from that knowledge,
   never from that code. No code, no structure, no resource string (message text) is copied. Decompilation for
   interoperability is what EU law allows (directive 2009/24/EC, article 6; in the Czech Republic § 66 of the
   copyright act) and what the Visual Studio licence leaves open: the notes here stay within that.
2. **Nothing decompiled is committed.** `local/` is in `.gitignore`; the decompiled sources live in
   `local/decomp/<tf version>/`. Only the notes in this folder are committed.
3. **Every note names the tf.exe version** it was read from (`18.10.12215.6` is the reference version of the
   golden captures) and the class and method where the fact was found, so that anyone with the same tf.exe can
   check it in a minute.
4. **A note states behavior in TfPortable's words**: "the default child option of `label` is Fail", "exit 1 when
   at least one item failed and at least one succeeded". It does not describe the code.
5. Library calls are named when they matter (`Workspace.CheckIn(WorkspaceCheckInParameters)` with which options):
   the client library is a public API, its names are not the secret.

## Procedure

1. On a machine with Visual Studio (Team Explorer) run, from the repository root:

   ```powershell
   .\tools\decompile-tf.ps1            # or -TfPath "C:\...\Team Explorer\TF.exe"
   ```

   It installs `ilspycmd` when needed, decompiles `TF.exe` and the `Microsoft.TeamFoundation.VersionControl.*`
   assemblies next to it into `local\decomp\<version>\`, and writes `index.md` there with the assembly versions
   and the list of command classes (`TF\Microsoft.TeamFoundation.VersionControl.CommandLine\Command<Name>.cs`).
   The exit-code and output machinery lives in `Microsoft.TeamFoundation.Client.dll`
   (`Microsoft.TeamFoundation.Client.CommandLine` and `ConsoleHost`), which the script decompiles as well.
2. Answer the questions in `questions.md`, one file per command (`checkin.md`, `label.md`, ...), using the
   template below. Either the developer answers them, or the developer gives the agent the `local/decomp/`
   folder in a session (upload or a shared drive) and the agent writes the notes for the developer's review.
3. Every answered question moves from `questions.md` into the command's note. When a note changes a TfPortable
   decision, the change goes through SPEC.md, `docs/compat.md` and "Decisions" in `docs/PROGRESS.md` as usual.

## Template of a command note

```markdown
# <command> (tf.exe <version>)

Read from: `<Assembly>/<Namespace>/<Class>.cs` (`<Method>`), <date>.

## Switches and defaults
- `/child`: default Fail (...).

## Library calls
- `VersionControlServer.CreateLabel(label, items, LabelChildOption.Fail)` ...

## Output
- stdout: ...; stderr: ...

## Exit codes
- 0 ..., 1 when ..., 100 when ...

## Consequences for TfPortable
- compat.md §16 row "...": matches / differs: ...
```
