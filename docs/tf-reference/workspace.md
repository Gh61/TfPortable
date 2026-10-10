# workspace (tf.exe 18.10.12215.6)

Read from: `TF/Microsoft.TeamFoundation.VersionControl.CommandLine/CommandWorkspace.cs` (`CreateWorkspace`,
`DeleteWorkspace`, `EditWorkspace`, `InternalEditWorkspace`, `PermissionProfileFromString`);
`Microsoft.TeamFoundation.VersionControl.Controls/.../ClientHelper.cs` (`DefaultWorkspaceName`);
`Microsoft.TeamFoundation.VersionControl.Client/.../Client.cs` (`CreateWorkspace`),
`WorkspacePermissionProfile.cs`; 2026-10-10.

## `/new` defaults (question 24)

- Options: `/collection`, `/comment`, `/computer`, `/delete`, `/filetime:current|checkin`,
  `/location:local|server`, `/new`, `/newname`, `/newowner`, `/permission`, `/server`, `/template`.
- Name: the free argument `name[;owner]`; without it **the computer name** (`Environment.MachineName`) and the
  authenticated user.
- Mappings: the folders of `/template:name[;owner]` (read from the server), otherwise **one mapping `$/` → the
  current directory**.
- Comment: `/comment`; otherwise the template's comment; otherwise, for a defaulted name, the machine's
  LanmanServer "srvcomment" registry value when present; otherwise none.
- Computer: `/computer` or the local workstation name.
- Permission profile: only with `/permission:Private|PublicLimited|Public` (the three built-in profiles, matched
  case-insensitively); the server must support workspace permissions, otherwise exit 100. Default null (the
  server's default, Private).
- Location: `/location:local|server`; **without it the library uses the server's `DefaultWorkspaceLocation`
  setting** (`ServerSettings`), not a hard-coded value.
- `/filetime:checkin` sets `WorkspaceOptions.SetFileTimeToCheckin` (server 2012 or newer).
- Library call: `VersionControlServer.CreateWorkspace(CreateWorkspaceParameters)` with `OwnerName`,
  `OwnerDisplayName`, `Comment`, `Folders`, `Computer`, `PermissionProfile`, `Location`, `WorkspaceOptions`,
  `RemoveUnparentedCloaks = false`. Interactively a get-latest follows when the dialog asked for it; with
  `/noprompt` nothing is downloaded.

## `/delete`

- Exactly one argument `name[;owner]`. Several local workspaces with the same name (and no `/collection`, and
  the current directory unmapped) are listed on stderr with their collections, exit 100.
- Interactively asks "delete workspace with N pending changes?"; a "no" is exit 100. With `/noprompt` no
  question. `VersionControlServer.DeleteWorkspace(name, owner)`; no output on success.

## Edit form

- `/template` is refused; a workspace that does not exist is an argument error pointing at `/new`.
- With `/noprompt` at least one of `/newname`, `/newowner`, `/permission`, `/computer`, `/comment`,
  `/filetime`, `/location` is required (exit 100). Unspecified fields keep their current values.
- `Workspace.Update(UpdateWorkspaceParameters)`; a location change is applied by the library when it differs.

## Consequences for TfPortable

- compat.md §21: `$/` → current folder default and the name requirement (tfp deviation) stand. The location
  default of tf.exe is the server setting; tfp passes `WorkspaceLocation.Local` explicitly (`TfvcClient`), so
  the local-only design does not depend on the server's default.
