# Command and switch reference

Source: the built-in help of tf.exe 18.10.12215.6, captured as `tests/golden/help/<command>.txt`
(`tf vc help <command>`) and `<command>-qmark.txt` (`tf <command> /?`), index `index-help.json`.
Both forms print the same text. `tf help <command>` prints only the banner in this version
(golden `general/help-status`), so SPEC.md's "take the switches from `tf help`" means these files.

For every command: the switches of the original, what TfPortable does with them, and the aliases
the help names. Global switches (`/collection`, `/workspace`, `/login`, `/noprompt`, `/format:json`,
`/verbose`, `/help`) are accepted by every command and not repeated. Behavior and exit codes are
in SPEC.md and `docs/compat.md`; JSON fields in `docs/json-schema.md`.

The executable form of this table is `src/TfPortable/Cli/CommandCatalog.cs` (milestone 2): `tfp help`,
`tfp help <command>`, `tfp help /format:json`, the switch validation (TFP0002) and the MSYS
converted-path detection all derive from it. Keep the two in sync; a switch marked ✗ below is in the
catalog as unsupported, so the parser answers TFP0006 with the reason (a typo stays TFP0002).

Legend: **✓** implemented as in tf.exe · **~** accepted, limited or no-op (see note) ·
**✗** not supported yet, TFP0006 with the reason (decision of 2026-10-10; ask for it when you need it) · **+** TfPortable extension.
The error codes a command can return are described in [errors.md](errors.md).

## Phase 1 (protection level 1: nothing changes on the server; `/lock:checkin|checkout`, the `workfold` mapping edits and `changeset /comment /notes` level 2)

<a id="cmd-status"></a>
### status (`stat` is not in the help; tf.exe accepts it)

```
tf vc status [itemspec] [/collection:url] [/login:u,[p]]
          ([/workspace:name[;owner]] | [/shelveset:name[;owner]])
          [/format:(brief|detailed|xml)] [/recursive] [/user:(*|username)] [/nodetect]
```

| Switch | TfPortable |
| --- | --- |
| `/format:brief` `/format:detailed` | ✓ |
| `/format:xml` | ✗ TFP0006 (use `/format:json`) |
| `/recursive`, `/nodetect`, `/user:`, `/shelveset:`, `/workspace:` | ✓ (`/user` prints the original's warning about local workspaces) |

<a id="cmd-add"></a>
### add

```
tf vc add [itemspec] [/lock:(none|checkin|checkout)] [/encoding:filetype]
          [/noprompt] [/recursive] [/noignore] [/login:u,[p]]
```

| Switch | TfPortable |
| --- | --- |
| `/recursive`, `/noignore`, `/encoding:` | ✓ (`/encoding`, not `/type` as the SPEC draft said) |
| `/lock:` | ✓ `none` (default), `checkin`, `checkout`: the pended item carries the lock on the server, as `lock` does, so `checkin` and `checkout` need protection level 2 (the command itself runs at level 1 since v1.1.0) (a checkout lock is not supported in local workspaces by the server, golden `lock/checkout-lock`) |

<a id="cmd-checkout"></a>
### checkout (alias `edit`)

```
tf vc checkout [/lock:(none|checkin|checkout)] [/recursive] [/encoding:encoding] itemspec [/login:u,[p]]
```

Same switch handling as `add`.

<a id="cmd-delete"></a>
### delete (`del` is not in the help; tf.exe accepts it)

```
tf vc delete [/lock:(none|checkin|checkout)] [/recursive] [/login:u,[p]] [/detect] [itemspec]
```

| Switch | TfPortable |
| --- | --- |
| `/recursive`, `/detect` | ✓ |
| `/lock:` | ~ as in `add` |

<a id="cmd-rename"></a>
### rename (alias `move`)

```
tf vc rename [/lock:(none|checkout|checkin)] [/login:u,[p]] olditem newitem
```

`/lock:` as in `add`.

<a id="cmd-undo"></a>
### undo

```
tf vc undo [/workspace:name[;owner]] [/recursive] itemspec [/noprompt] [/login:u,[p]] [/collection:url]
```

All switches ✓.

<a id="cmd-get"></a>
### get

```
tf vc get [itemspec] [/version:versionspec] [/all] [/overwrite] [/force] [/remap]
       [/recursive] [/preview] [/noautoresolve] [/noprompt] [/login:u,[p]]
```

| Switch | TfPortable |
| --- | --- |
| `/version`, `/all`, `/overwrite`, `/force`, `/remap`, `/recursive`, `/preview` | ✓ |
| `/noautoresolve` | ✓ (missing from the SPEC draft; without it the client auto-resolves trivial conflicts as tf.exe does) |

<a id="cmd-resolve"></a>
### resolve

```
tf vc resolve [itemspec] [/auto:(AutoMerge|AutoMergeForced|TakeTheirs|KeepYours|
              OverwriteLocal|DeleteConflict|KeepYoursRenameTheirs)]
              [/preview] [/recursive] [/newname:path] [/noprompt]
              [(/overridetype:type | /converttotype:type)]
              [/properties:(@valuefile|name1=value1[;...])] [/login:u,[p]]
```

| Switch | TfPortable |
| --- | --- |
| `/auto:` | ✓ with the help's names; the documentation names `AcceptMerge`, `AcceptYours`, `AcceptTheirs`, `AcceptYoursRenameTheirs` are accepted as aliases (the golden capture used them successfully: `resolve/accept-yours`, `resolve/accept-theirs`). `AutoMergeForced` ✓. Text output prints the server's resolution names (`KeepYours`, `TakeTheirs`), JSON lower-case |
| `/preview`, `/recursive`, `/newname:` | ✓ |
| `/overridetype:`, `/converttotype:`, `/properties:` | ✗ TFP0006 (encoding and property conflicts do not arise in practice; add on demand) |

<a id="cmd-reconcile"></a>
### reconcile

```
tf vc reconcile /clean [/diff] [/noprompt] [/preview] [/recursive] [/ignore] [/unmapped] [/exclude:spec1,spec2,...] [itemspec]
tf vc reconcile /promote [/adds] [/deletes] [/diff] [/noprompt] [/preview] [/recursive] [/noignore] [/exclude:spec1,spec2,...] [itemspec]
```

| Switch | TfPortable |
| --- | --- |
| `/clean`, `/promote`, `/adds`, `/deletes`, `/diff`, `/preview`, `/recursive`, `/noignore`, `/ignore`, `/unmapped`, `/exclude:` | ✓ |
| `/clean` without `/preview` | needs `/noprompt` as an explicit confirmation (TFP0405, SPEC.md) |

<a id="cmd-localversions"></a>
### localversions (`lv` is not in the help; tf.exe accepts it)

```
tf vc localversions itemspec [/recursive] [/format:brief|detailed] [/workspace:name[;owner]] [/collection:url] [/login:u,[p]]
```

All switches ✓.

<a id="cmd-workfold"></a>
### workfold

```
tf vc workfold localfolder
tf vc workfold [/workspace:name]
tf vc workfold [/collection:url] [/workspace:name] serverfolder
tf vc workfold /map serverfolder localfolder [/collection:url] [/workspace:name]
tf vc workfold /unmap (serverfolder|localfolder) [/collection:url] [/workspace:name]
tf vc workfold /cloak serverfolder [/workspace:name] [/collection:url]
tf vc workfold /decloak serverfolder [/workspace:name] [/collection:url]
```

| Form | TfPortable |
| --- | --- |
| listing (no switch, `localfolder`, `serverfolder`) | ✓ phase 1 |
| `/map serverfolder localfolder`, `/unmap (serverfolder\|localfolder)`, `/cloak serverfolder`, `/decloak serverfolder` | ✓ phase 4, protection level 2 (the mapping change through the client library, then the listing; a folder that is not a mapping is TFP0203). The help lists `/collection` for `/unmap`, but tf.exe 18.10 rejects it at run time (golden `workfold/unmap`); TfPortable accepts it |

<a id="cmd-workspaces"></a>
### workspaces

```
tf vc workspaces [/owner:name] [/computer:name] [/collection:url] [/format:(brief|detailed|xml)]
                 [/updateUserName:old] [/updateComputerName:old] [workspacename] [/login:u,[p]]
tf vc workspaces /remove:(*|ws1[,ws2,...]) /collection:(*|url) [/login:u,[p]]
```

| Switch | TfPortable |
| --- | --- |
| `/owner`, `/computer`, `/format:brief\|detailed`, `workspacename` | ✓ |
| `/format:xml` | ✗ TFP0006 (use `/format:json`) |
| `/remove:` | ✓ (local cache only, as in tf.exe) |
| `/updateUserName:`, `/updateComputerName:` | ✗ TFP0006 (server administration after an account or machine rename) |

<a id="cmd-history"></a>
### history (`hist` is not in the help; tf.exe accepts it)

```
tf vc history itemspec [/version:versionspec] [/stopafter:number] [/recursive] [/user:username]
           [/format:(brief|detailed)] [/slotmode] [/itemmode] [/noprompt] [/login:u,[p]]
           [/sort:ascending,descending] [/collection:url]
```

| Switch | TfPortable |
| --- | --- |
| all of the above | ✓ (default slot mode, docs/compat.md §9; without `/stopafter` at most 256 entries) |
| `/comment:`, `/changetype:`, `/workitem:`, `/files`, `/contains:` | + extensions (SPEC.md "Vyhledávání v historii"); tf.exe 18.10 rejects `/workitem` (golden `history/by-work-item`, exit 100) |

<a id="cmd-changeset"></a>
### changeset

```
tf vc changeset [/comment:("text"|@file)] [/notes:("Name"="Value"|@file)] [/noprompt] [/collection:url]
             [changesetnumber | /latest] [/login:u,[p]]
```

| Switch | TfPortable |
| --- | --- |
| `changesetnumber`, `/latest` | ✓ |
| `/comment:`, `/notes:"Name=Value;Name2=Value2"` (or `@file`, one `Name=Value` per line) | ✓ phase 4, protection level 2: the changeset is updated (`Changeset <n> updated.`) and printed |
| `/diff` | + extension |

<a id="cmd-view"></a>
### view

```
tf vc view [/collection:url] [/console] [/recursive] [/output:localfile]
        [/shelveset:name[;owner]] [/noprompt] itemspec [/version:versionspec] [/login:u,[p]]
```

| Switch | TfPortable |
| --- | --- |
| `/version`, `/output:`, `/shelveset:` | ✓ |
| `/console` | ~ accepted, no-op (TfPortable always writes to the console) |
| `/recursive` | ✓ (several files, each preceded by its path, as tf.exe `/console /recursive`) |

<a id="cmd-difference"></a>
### difference (alias `diff`; the help writes `diff[erence]`)

```
tf vc diff[erence] itemspec [/version:versionspec] [/encoding:filetype] [/format:format]
                [/ignorespace] [/ignoreeol] [/ignorecase] [/recursive] [/options] [/noprompt] [/login:u,[p]]
tf vc diff[erence] itemspec itemspec2 [...]
tf vc diff[erence] [/shelveset:name[;owner]] shelveset_itemspec [...]
tf vc diff[erence] /configure [/login:u,[p]]
Formats: Brief, Context, Rcs, SS, SS_SideBySide, SS_Unix, Unified, Unix, Visual
```

| Switch | TfPortable |
| --- | --- |
| one itemspec, two itemspecs, `/shelveset:` | ✓ |
| `/format:unified`, `/format:brief` | ✓ |
| other formats (`context`, `rcs`, `ss*`, `unix`, `visual`) | ✗ TFP0006 (docs/compat.md §12) |
| `/ignorespace`, `/ignoreeol`, `/ignorecase`, `/recursive` | ✓ |
| `/encoding:` | ~ accepted (forces the encoding used to read both sides) |
| `/options`, `/configure` | ✗ TFP0006 (external diff tool configuration of Visual Studio) |

<a id="cmd-dir"></a>
### dir

```
tf vc dir itemspec [/version:versionspec] [/recursive] [/folders] [/deleted] [/login:u,[p]] [/collection:url]
```

All switches ✓.

<a id="cmd-properties"></a>
### properties / info (canonical name in the help is `info`; `properties` is the deprecated alias)

```
tf vc info [/recursive] itemspec [/version:versionspec] [/workspace:name] [/login:u,[p]] [/collection:url]
```

All switches ✓. No deprecation warning for either name (docs/compat.md §1.3).

<a id="cmd-labels"></a>
### labels

```
tf vc labels [/owner:name] [/format:(brief|detailed)] [/collection:url] [labelname] [/login:u,[p]]
```

All switches ✓.

<a id="cmd-branches"></a>
### branches

```
tf vc branches itemspec [/version:versionspec] [/collection:url] [/login:u,[p]]
```

All switches ✓.

<a id="cmd-merge"></a>
### merge /candidate (phase 1 part of `merge`)

```
tf vc merge [/recursive] [/force] [/candidate] [/discard] [/version:versionspec]
         [/lock:(none|checkin|checkout)] [/preview] [/baseless] [/nosummary]
         [/noimplicitbaseless] [/conservative] [/format:(brief|detailed)]
         [/noautoresolve] [/noprompt] [/login:u,[p]] source destination
```

| Switch | TfPortable |
| --- | --- |
| `/candidate` with `/recursive`, `/version:`, `/format:` | ✓ phase 1 |
| `/force`, `/baseless`, `/discard`, `/noimplicitbaseless`, `/conservative`, `/noautoresolve`, `/recursive`, `/version:from~to` | ✓ phase 2, as pending changes (mapped to the merge options of the client library; a single `/version:C<n>` is the upper bound) |
| `/preview` | ✓ phase 2 through the server's preview (the `NoMerge` merge option): the operations a merge would pend, in the merge line shape; predicted conflicts are `Conflict (...)` lines as in the original (TFP0403, exit 1) |
| `/lock:` | ✓ as in `add` (the merged items carry the lock) |
| `/nosummary` | ~ accepted; tf.exe 18.10 prints no summary either (golden `merge/nosummary` equals `merge/all-run`) |
| `/format:brief\|detailed` | ~ accepted; the pending merge listing has one shape in tf.exe 18.10 too (golden `merge/detailed`) |

## Phase 2 (branching as pending changes; `merge`, `branch`, `undelete`, `rollback` at protection level 1 since v1.1.0, `/lock:checkin|checkout` level 2)

<a id="cmd-merges"></a>
### merges

```
tf vc merges [source] destination [/recursive] [/extended] [/format:(brief|detailed)] [/login:u,[p]] [/showall] [/collection:url]
```

`/extended` is listed by the help but rejected at run time by tf.exe 18.10 ("option not allowed",
golden `merges/extended`); TfPortable does not implement it (✗ TFP0006). `/recursive`, `/showall` ✓;
`/format:brief` is the table Changeset, Merged in Changeset, Author, Date (`*` after a partial merge),
`/format:detailed` is the list of merged items (`Merged items:`, then `$/src/a.txt;C596 -> $/rel/a.txt;C599`
per item, golden `merges/detailed`). Without a source every merge into the destination is listed.

<a id="cmd-annotate"></a>
### annotate (+ extension; tf.exe has no such command, golden `help/annotate` exit 2)

```
tfp annotate itemspec [/version:versionspec]
```

One text file. Columns Changeset, Owner, Date, Line; each line is attributed to the changeset that last
introduced it (tfp's own line diff over the file's history, renames included). A folder is TFP0002,
a binary file TFP0303, an unknown item TFP0301 (exit 1).

<a id="cmd-folderdiff"></a>
### folderdiff

```
tf vc folderdiff [sourcePath] targetPath [/recursive] [/noprompt] [/collection:url] [/filter:filter]
              [/filterLocalPathsOnly] [/login:u,[p]] [/view:same,different,sourceOnly,targetOnly]
```

| Switch | TfPortable |
| --- | --- |
| `sourcePath targetPath` (server or local, `;version` on a server path) | ✓ compared by content hash (the server's MD5, computed for local files); the relative path decides which items are compared |
| `targetPath` alone | ✓ the folder against its counterpart in the workspace: the server folder is the source and the local folder the target (golden `folderdiff/one-path-local`); a server path alone is accepted too (tf.exe 18.10 refuses it with TF206007, `docs/compat.md` §20); TFP0201 outside a workspace |
| `/recursive`, `/filter:` (`;`-separated patterns, `!` excludes), `/view:` | ✓ (default view: sourceOnly, targetOnly, different); tf.exe 18.10 ignores a `!` exclusion (golden `folderdiff/filter-exclude`), tfp honors it as the Microsoft docs describe |
| `/filterLocalPathsOnly` | ✗ TFP0006 (an edge case of `/filter`) |
| `/noprompt` | ~ accepted (tfp never opens the dialog) |

## Phase 3 (the everyday write path; protection level 2, `/delete` level 3; `unshelve` without `/move` level 1 since v1.1.0)

<a id="cmd-checkin"></a>
### checkin (alias `ci`, from the Microsoft docs)

```
tf vc checkin [/author:author name] [/comment:("comment"|@commentfile)] [/noprompt] [/notes:(...)]
           [/override:(reason|@reasonfile)] [/recursive] [/saved] [/validate] [itemspec] [/bypass] [/force]
           [/noautoresolve] [/login:u,[p]] [/new]
tf vc checkin /shelveset:shelvesetname[;shelvesetowner] [/bypass] [/noprompt] [/login:u,[p]] [/collection:url]
           [/author:author name] [/force]
```

| Switch | TfPortable |
| --- | --- |
| `/comment:` | ✓ required (tfp never opens the checkin dialog); `@file` reads it from a file |
| `/recursive`, `/validate`, `/author:`, `/noautoresolve` | ✓ (`/validate` evaluates conflicts, policies and notes and checks nothing in) |
| `/override:` | ✓ bypasses failed (TFP0407) and unknown (TFP0409) checkin policies and missing notes; the reason is recorded in the changeset |
| `/associate:id[,id]` | **+** links the work items to the changeset over the REST work item API after the checkin (the "Fixed in Changeset" link); not a switch of tf.exe 18.10 (golden `checkin/associate`, exit 100) |
| `/force` | ✓ checks in an edit whose content did not change (`AllowUnchangedContent`); without it the server commits nothing, drops the pending edit and tfp reports TFP0301 with exit 1 (golden `checkin/unchanged-edit`) |
| `/shelveset:name[;owner]` | ✓ phase 4: the shelveset is checked in as a changeset with its own comment and deleted by the server (`Changeset #<n> checked in.`, `Shelveset <name>;<owner> deleted.`); no itemspec, `/comment`, `/validate` or `/override` with it; `/associate`, `/author`, `/force`, `/noautoresolve` apply |
| `/notes:` | ✓ `"Name=Value;Name2=Value2"` or `@file` (one `Name=Value` per line): the checkin notes, evaluated against the required notes of the project and stored in the changeset (`changeset <n>` shows them under `Check-in Notes:`, JSON `notes`) |
| `/saved`, `/bypass`, `/new` | ✗ TFP0006 (a Visual Studio dialog state, the gated-build bypass, the Visual Studio dialog) |

Output: `Checking in <change>: <name>` grouped by folder, then `Changeset #<n> checked in.` Nothing pending is TFP0301 (exit 100).
Every conflict (TFP0406), failed policy or missing note (TFP0407) and policy tfp cannot evaluate (TFP0409) is an item error,
followed by a summary error with exit 100 and nothing checked in; `/validate` prints the same findings and exits 100 without
the summary, 0 when the checkin would pass. Policies: tfp evaluates "Work Items" (`/associate`) and "Changeset Comments"
(`/comment`) itself; every other policy runs inside Visual Studio only and is reported as unknown.

<a id="cmd-shelve"></a>
### shelve

```
tf vc shelve [/replace] [/comment:("comment"|@commentfile)] [shelvesetname] [/validate] [/noprompt] [/login:u,[p]] [/new]
tf vc shelve [/move] [/replace] [/comment:("comment"|@commentfile)] [/recursive] [shelvesetname] itemspec [/validate] [/noprompt]
             [/login:u,[p]] [/new]
tf vc shelve /delete shelvesetname[;owner] [/login:u,[p]] [/collection:url]
```

| Switch | TfPortable |
| --- | --- |
| `shelvesetname` | ✓ required; an owner part is accepted only when it is the authenticated user |
| `/comment:`, `/replace`, `/move`, `/recursive` | ✓ (a shelveset that exists without `/replace` is TFP0408) |
| `/delete` | ✓ at protection level 3 (prints `Shelveset <name>;<owner> deleted.`; tf.exe is silent) |
| `/validate` | ✓ phase 4: the checkin policies and notes are evaluated as `checkin /validate` does; a failure is an item error (exit 1) and the shelveset is still created (tf.exe refuses `/validate` with `/noprompt`, phase 4 capture `shelve/validate`) |
| `/new` | ✗ TFP0006 (opens the Visual Studio shelve dialog) |

<a id="cmd-shelvesets"></a>
### shelvesets

```
tf vc shelvesets [/owner:name] [/format:(brief|detailed|xml)] [/collection:url] [/login:u,[p]] shelvesetname
```

`/owner:` and `/format:brief|detailed` ✓ (the default owner is the authenticated user, `*` everyone); `/format:xml` ✗ TFP0006. A named
shelveset that does not exist is TFP0301 (exit 100, as tf.exe); an empty list without a name prints `No shelvesets found.` (exit 0).

<a id="cmd-unshelve"></a>
### unshelve

```
tf vc unshelve [/move] [shelvesetname[;username]] [itemspec] [/recursive] [/nomerge] [/noautoresolve] [/noprompt] [/login:u,[p]]
```

`shelvesetname` ✓ required (tfp never opens the dialog); `/recursive`, `/nomerge`, `/noautoresolve` ✓; `/move` ✓ phase 4, protection level 2
(the shelveset is deleted after a complete unshelve without conflicts or failures, `Shelveset <name>;<owner> deleted.`;
otherwise it is kept with a warning); without `/move` nothing changes on the server, so `unshelve` runs at level 1. Open conflicts of the merge are TFP0403 (exit 1); a local pending change in the way is TFP0401 (exit 100).

<a id="cmd-label"></a>
### label

```
tf vc label labelname[@scope] [/owner:ownername] itemspec [/version:versionspec] [/comment:("comment"|@commentfile)]
         [/child:(replace|merge)] [/recursive] [/login:u,[p]] [/collection:url]
tf vc label /delete labelname[@scope] itemspec [/login:u,[p]] [/collection:url]
```

| Switch | TfPortable |
| --- | --- |
| `labelname[@scope]` | ✓; without a scope the team project of the first item |
| `/version:`, `/comment:`, `/child:replace\|merge`, `/recursive` | ✓ (default `/child:fail`); without `/version` a local path is labelled at the workspace version, a server path at the latest |
| `/delete` | ✓ at protection level 3 (takes `labelname@scope` only) |
| `/owner:` | ✗ TFP0006 (the label belongs to the authenticated user) |

<a id="cmd-unlabel"></a>
### unlabel

```
tf vc unlabel [/collection:url] [/recursive] [/login:u,[p]] labelname itemspec
```

✓ as in tf.exe; an item that is not in the label is TFP0301 (exit 1).

<a id="cmd-lock"></a>
### lock

```
tf vc lock itemspec /lock:(none|checkout|checkin) [/workspace:workspacename] [/recursive] [/login:u,[p]] [/collection:url]
```

`/lock:` ✓ required, `/recursive` ✓, `/workspace:` ✓ (phase 4, protection level 3: a lock in another workspace of
the same owner; server paths go to the library as given, so an item that is not mapped in that workspace is the
server's error, not TFP0203).

<a id="cmd-undelete"></a>
### undelete (phase 2)

```
tf vc undelete [/noget] [/lock:(none|checkin|checkout)] [/recursive] itemspec[;deletionID] [/login:u,[p]]
```

`itemspec[;Xn]`, `/noget`, `/recursive`, `/lock:` ✓ (grouped listing like `add`; the lock as in `add`).
An item that is not deleted or does not exist is TFP0301 (exit 1, golden `undelete/not-found`).

<a id="cmd-rollback"></a>
### rollback (phase 2)

```
tf vc rollback /changeset:from~to [itemspec] [/recursive] [/lock:none|checkin|checkout] [/version:versionspec]
               [/keepmergehistory] [/noautoresolve] [/noprompt] [/login:u,[p]]
tf vc rollback /toversion:versionspec itemspec [/recursive] [/lock:...] [/version:versionspec]
               [/keepmergehistory] [/noautoresolve] [/noprompt] [/login:u,[p]]
```

Exactly one of `/changeset:` (a changeset or a range, with or without items) and `/toversion:` (items
required) ✓; `/recursive`, `/version:` (the version of the items), `/keepmergehistory`, `/noautoresolve`,
`/lock:` ✓ as in `add`. Lines `edit, rollback: $/a.txt;C85~C85 -> $/a.txt;C87`;
conflicts are TFP0403 item errors (exit 1); nothing to do prints `There is nothing to roll back.`.

<a id="cmd-branch"></a>
### branch (phase 2)

```
tf vc branch olditem newitem [/version:versionspec] [/noget] [/lock:(none|checkin|checkout)] [/noprompt]
          [/silent] [/checkin] [/comment:("text"|@file)] [/author:name] [/login:u,[p]] [/notes:(...)]
```

`olditem newitem`, `/version:` (default latest), `/noget`, `/silent`, `/lock:none` ✓ (prints the target
path and the branched tree grouped like `add`; with `/silent` the server returns no items, so nothing is
listed or downloaded); `/lock:` ✓ as in `add`; `/checkin`, `/comment:`,
`/author:`, `/notes:` ✗ TFP0006 (they check in immediately; TfPortable pends the branch and `checkin` commits it).

## Phase 4 (server administration; `destroy`, permission changes and workspace changes at protection level 3)

<a id="cmd-destroy"></a>
### destroy

```
tf vc destroy [/keephistory] itemspec1[;versionspec] [itemspec2...itemspecN] [/stopat:versionspec] [/preview]
           [/startcleanup] [/noprompt] [/silent] [/login:u,[p]] [/collection:url]
```

| Switch | TfPortable |
| --- | --- |
| `itemspec[;versionspec\|;Xdeletionid]` | ✓ one server call per item (a local path is mapped through the workspace) |
| `/preview` | ✓ lists `Would destroy: $/path` per item and records the preview; **the real destroy needs a preview with the same items and switches in the last 10 minutes** (TFP0410 otherwise; one preview covers one destroy). tf.exe prints `Destroyed:` even in a preview |
| `/keephistory`, `/stopat:` | ✓ passed to the server (`/stopat` without `/keephistory` is TFP0002) |
| `/startcleanup`, `/silent` | ✓ (`/startcleanup` is passed to the server; Azure DevOps Services accepted it, CI run 167 of 2026-10-10) |

Output: `Destroyed: $/path` per item. A missing item is TFP0301 (exit 100, as tf.exe VS403405). Protection level 3.
The library's query of the pending and shelved changes a destroy touches (`AffectedChanges`) is not used: it empties
the item list and tf.exe prints nothing of the kind either. The item spec goes to the library with full recursion
(a destroy is always recursive; the library rejects anything else with TF203086).

<a id="cmd-permission"></a>
### permission

```
tf vc permission [/allow:(* |perm1[,perm2,...]] [/deny:(* |perm1[,perm2,...])] [/remove:(* |perm1[,perm2,...])] [/inherit:yes|no]
              [/user:username1[,username2,...]] [/group:groupname1[,groupname2,...]] [/collection:url] [/recursive] itemspec [/global] [/login:u,[p]]
```

| Switch | TfPortable |
| --- | --- |
| `itemspec ...`, `/recursive` (listing) | ✓ protection level 1; the `====` block per item with `Server item: $/path (Inherit: Yes)` and, per identity, `Allow`, `Deny`, `Allow (inherited)`, `Deny (inherited)` |
| `/allow:`, `/deny:`, `/remove:` with `/user:` and/or `/group:` | ✓ protection level 3; the permission names of tf.exe (Read, PendChange, Checkin, Label, Lock, ReviseOther, UnlockOther, UndoOther, LabelOther, AdminProjectRights, CheckinOther, Merge, ManageBranch; `*` = all; an unknown name is TFP0002); the identities go to the server as given (display name, account, e-mail, `[Project]\Group`); prints `$/path: identity` per applied change as tf.exe; a change the server did not apply (an unknown identity, an inheritance change on `$/`) is TFP0501 per item (exit 1) with the library's failure text as the server message; when the server applied nothing at all the exit code is 100, as tf.exe with its empty result |
| `/inherit:yes\|no` | ✓ protection level 3 (`$/path: inherit no`) |
| `/recursive` with a change | ✓ the change is applied to every item below (the server stores ACLs per item) |
| `/global` | ✗ TFP0006 (collection-level permissions are server administration) |

<a id="cmd-workspace"></a>
### workspace

```
tf vc workspace /new [/noprompt] [/template:workspacename[;workspaceowner]] [/computer:computername] [/comment:("comment"|@commentfile)]
                [workspacename[;workspaceowner]] [/collection:url] [/permission:(Private|PublicLimited|Public)] [/location:(local|server)]
                [/filetime:(current|checkin)] [/login:u,[p]]
tf vc workspace /delete [/collection:url] workspacename[;workspaceowner] [/login:u,[p]]
tf vc workspace [/collection:url] [/comment:("comment"|@commentfile)] [/newname:workspacename] [workspacename[;workspaceowner]]
                [/newowner:ownername] [/computer:computername] [/permission:(Private|PublicLimited|Public)] [/location:(local|server)]
                [/filetime:(current|checkin)] [/login:u,[p]]
```

| Form | TfPortable |
| --- | --- |
| `/new name` | ✓ protection level 3; the name is required (tf.exe defaults to the computer name; a tool without a dialog wants it explicit); a local workspace of the authenticated user with `$/` mapped to the current folder, or the mappings of `/template:`; `/comment:`, `/computer:`, `/permission:` (default Private), `/filetime:` (default current) ✓; `/location:server` is TFP0204; another owner is TFP0002. Prints `Workspace <name>;<owner> created.` and the detailed listing |
| `/delete name[;owner]` | ✓ protection level 3; deletes the workspace on the server and from the local cache; the pending changes it had are dropped (a warning with their count); prints `Workspace <name>;<owner> deleted.` (tf.exe is silent) |
| `name /newname: /comment: /computer: /permission: /filetime:` | ✓ protection level 3 (`Workspace <name>;<owner> updated.` and the listing); `/newowner:` ✗ TFP0006 |
| `name` without switches | ✓ protection level 1: the detailed listing of `workspaces /format:detailed` |

<a id="cmd-property"></a>
### property

```
tf vc property itemspec [/version:versionspec] [/recursive] [/propertynames:name]... [/output:file]
tf vc property itemspec /setvalues:name=value[;name=value...] [/recursive] [/silent] [/lock:none]
tf vc property itemspec /deletevalues:name... [/recursive] [/silent] [/lock:none]
tf vc property itemspec /deleteall [/recursive] [/silent] [/lock:none]
```

(tf.exe 18.10 has no help text for `property`; the forms come from its decompiled option table, `docs/tf-reference/property.md`.)

| Switch | TfPortable |
| --- | --- |
| listing | ✓ protection level 1: a dashed line, `Server item: $/path`, then `name = value` per property or `The item has no properties.` (phase 4 capture `property/read`); a binary value shows as `<binary, n bytes>` |
| `/version:`, `/recursive` | ✓ |
| `/propertynames:` | ✓ repeatable or comma-separated; lists only these properties |
| `/output:` | ✓ writes the value of the single property named by `/propertynames` to the file (its bytes for a binary value, UTF-8 text otherwise) and prints `Wrote <name> of <item> to <file>.`; a missing property is a warning (exit 0); needs exactly one name and no `/recursive` |
| `/setvalues:` | ✓ protection level 1 (a pending change; since v1.1.0): pends a property change (`name=value[;name=value...]`; `;;` and `==` are literal separators; a value `@file` reads the file as a binary value); the usual grouped listing of the pended items, `status` shows the change type `property`, `undo` takes it back, `checkin` stores it |
| `/deletevalues:`, `/deleteall` | ✓ protection level 1: pend the deletion of the named properties (repeatable or comma-separated) or of every property |
| `/silent`, `/lock:` | ✓ as on `add` |
| `/setproperty` | not a switch of tf.exe 18.10 either (capture `property/set`, exit 100): TFP0002 |

The combinations tf.exe refuses are TFP0002 (exit 100): a write switch with `/output`, `/version`, `/propertynames` or
`/collection`; `/setvalues` with `/deletevalues` or `/deleteall`; `/output` with `/recursive` or without exactly one
`/propertynames`; an empty name or value in `/setvalues`. Limits: a text value cannot start with `@` (that spelling
names a file), and a property name with a comma or surrounding spaces cannot be passed to `/deletevalues` or
`/propertynames` (the values are split on commas and trimmed).

<a id="cmd-resolvepath"></a>
### resolvepath

```
tf vc resolvepath itemspec ...
```

✓ protection level 1: `<given path> -> <counterpart>` per item (a local path gives its server path, a server path its local path);
a path outside the mappings is TFP0203 (exit 1 per item).

<a id="cmd-docs"></a>
### docs (+ extension; `msdn` is its alias)

```
tfp docs [command] [/url]
```

Opens the TfPortable documentation on GitHub in the default browser (the repository, or `docs/commands.md` at the command's
anchor) and prints `Opened <url> in the default browser.`; `/url` prints the URL only. A browser that cannot start is a warning
(exit 0). tf.exe `msdn <command>` opens the Microsoft documentation of the command; TfPortable's `msdn` opens its own.

<a id="cmd-proxy"></a><a id="cmd-configure"></a><a id="cmd-certificates"></a>
## Never offered (TFP0005)

`proxy` (configures a TFVC proxy server in the user's registry), `configure` (the Source Control Settings dialog of a project),
`certificates` (client certificates for HTTPS sign-in to an on-premises TFS) and the command groups `tf git` and `tf settings`.
The error explains what the command does and why a portable, PAT-only tool has no place for it. Their help texts are in
`tests/golden/help/` for reference. From version 1.0 no command or switch of tf.exe answers TFP0003 (SPEC.md "Fáze 4").

## Aliases

Named by the help: `edit` → `checkout`, `move` → `rename`, `diff` → `difference`,
`properties` → `info`. Accepted by tf.exe but not named in the help (from the Microsoft docs):
`stat` → `status`, `del` → `delete`, `ren` → `rename`, `hist` → `history`, `lv` → `localversions`,
`co` → `checkout`, `rm` → `delete`. TfPortable accepts all of them and reports the canonical name
in `/format:json`.

## Versionspec (from the help)

```
Date/Time         D"any .NET Framework date format" or any date format of the local computer
Changeset number  Cnnnnnn
Label             Llabelname
Latest version    T
Workspace         Wworkspacename;workspaceowner
```

TfPortable adds a bare number (`1234` = `C1234`) and `L<label>@<scope>` as in the Microsoft docs;
parsing order of dates in SPEC.md ("Versionspec").
