# TfPortable

**TfPortable** (`tfp.exe`) is a portable replacement for the TFVC part of Microsoft's `tf.exe`.
One file, no Visual Studio, no dialogs. It signs in only with a Personal Access Token stored in
the Windows Credential Manager, keeps the command names and switches of `tf.exe`, and adds
machine-readable output so that AI coding agents (Claude Code and others) can drive it safely.

> Status: pre-alpha. The specification is finished (`docs/SPEC.md`, in Czech), the behavior of
> the original is captured in `tests/golden/`, the code is being built milestone by milestone
> (`docs/PROGRESS.md`). Nothing is released yet.

## Why

- `tf.exe` needs Visual Studio or the Team Explorer installer and interactive sign-in. Token
  caches expire, MFA prompts appear in the middle of scripts, and GUI dialogs block agents.
- TfPortable uses a PAT from the Credential Manager, never waits for input when it is not run
  by a human in a terminal, and explains every error with a stable `TFPxxxx` code, the original
  `TFxxxxx` code when there is one, and what to do next.
- `/format:json` returns one JSON document per command (`docs/json-schema.md`).

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

Same commands and switches as `tf.exe`, for example:

```
tfp status /recursive
tfp history $/Project/Main /recursive /stopafter:20
tfp changeset 1234 /diff
tfp diff Main\src\Program.cs
tfp get /preview
```

From Git Bash (and therefore from Claude Code on Windows) write switches with a dash so that
MSYS does not turn `/recursive` into a path: `tfp status -recursive -format:json`.

Supported in phase 1: `login`, `logout`, `whoami`, `status`, `add`, `checkout`/`edit`, `delete`,
`rename`/`move`, `undo`, `get`, `resolve`, `reconcile`, `localversions`, `workfold`,
`workspaces`, `history`, `changeset`, `view`, `difference`/`diff`, `dir`, `properties`/`info`,
`labels`, `branches`, `merge /candidate`. Commands that write to the server immediately
(`checkin`, `shelve`, `lock`, `label`, `destroy`, `permission`, `workspace /new`) are recognized
and refused with a clear message; use Visual Studio or the original `tf.exe` for them. Only local
workspaces are supported. Differences from `tf.exe` are listed in `docs/compat.md`.

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
