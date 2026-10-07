# Command and switch reference

Source: the built-in help of tf.exe 18.10.12215.6, captured as `tests/golden/help/<command>.txt`
(`tf vc help <command>`) and `<command>-qmark.txt` (`tf <command> /?`), index `index-help.json`.
Both forms print the same text. `tf help <command>` prints only the banner in this version
(golden `general/help-status`), so SPEC.md's "take the switches from `tf help`" means these files.

For every command: the switches of the original, what TfPortable does with them, and the aliases
the help names. Global switches (`/collection`, `/workspace`, `/login`, `/noprompt`, `/format:json`,
`/verbose`, `/help`) are accepted by every command and not repeated. Behavior and exit codes are
in SPEC.md and `docs/compat.md`; JSON fields in `docs/json-schema.md`.

Legend: **✓** implemented as in tf.exe · **~** accepted, limited or no-op (see note) ·
**✗** rejected with TFP0002 (not needed, or would write to the server) · **+** TfPortable extension.

## Phase 1

### status (`stat` is not in the help; tf.exe accepts it)

```
tf vc status [itemspec] [/collection:url] [/login:u,[p]]
          ([/workspace:name[;owner]] | [/shelveset:name[;owner]])
          [/format:(brief|detailed|xml)] [/recursive] [/user:(*|username)] [/nodetect]
```

| Switch | TfPortable |
| --- | --- |
| `/format:brief` `/format:detailed` | ✓ |
| `/format:xml` | ✗ (use `/format:json`) |
| `/recursive`, `/nodetect`, `/user:`, `/shelveset:`, `/workspace:` | ✓ (`/user` prints the original's warning about local workspaces) |

### add

```
tf vc add [itemspec] [/lock:(none|checkin|checkout)] [/encoding:filetype]
          [/noprompt] [/recursive] [/noignore] [/login:u,[p]]
```

| Switch | TfPortable |
| --- | --- |
| `/recursive`, `/noignore`, `/encoding:` | ✓ (`/encoding`, not `/type` as the SPEC draft said) |
| `/lock:` | ~ `none` accepted; `checkin`/`checkout` rejected (locks write to the server; checkout locks are not supported in local workspaces anyway, golden `lock/checkout-lock`) |

### checkout (alias `edit`)

```
tf vc checkout [/lock:(none|checkin|checkout)] [/recursive] [/encoding:encoding] itemspec [/login:u,[p]]
```

Same switch handling as `add`.

### delete (`del` is not in the help; tf.exe accepts it)

```
tf vc delete [/lock:(none|checkin|checkout)] [/recursive] [/login:u,[p]] [/detect] [itemspec]
```

| Switch | TfPortable |
| --- | --- |
| `/recursive`, `/detect` | ✓ |
| `/lock:` | ~ as in `add` |

### rename (alias `move`)

```
tf vc rename [/lock:(none|checkout|checkin)] [/login:u,[p]] olditem newitem
```

`/lock:` as in `add`.

### undo

```
tf vc undo [/workspace:name[;owner]] [/recursive] itemspec [/noprompt] [/login:u,[p]] [/collection:url]
```

All switches ✓.

### get

```
tf vc get [itemspec] [/version:versionspec] [/all] [/overwrite] [/force] [/remap]
       [/recursive] [/preview] [/noautoresolve] [/noprompt] [/login:u,[p]]
```

| Switch | TfPortable |
| --- | --- |
| `/version`, `/all`, `/overwrite`, `/force`, `/remap`, `/recursive`, `/preview` | ✓ |
| `/noautoresolve` | ✓ (missing from the SPEC draft; without it the client auto-resolves trivial conflicts as tf.exe does) |

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
| `/overridetype:`, `/converttotype:`, `/properties:` | ✗ (encoding and property conflicts; add on demand) |

### reconcile

```
tf vc reconcile /clean [/diff] [/noprompt] [/preview] [/recursive] [/ignore] [/unmapped] [/exclude:spec1,spec2,...] [itemspec]
tf vc reconcile /promote [/adds] [/deletes] [/diff] [/noprompt] [/preview] [/recursive] [/noignore] [/exclude:spec1,spec2,...] [itemspec]
```

| Switch | TfPortable |
| --- | --- |
| `/clean`, `/promote`, `/adds`, `/deletes`, `/diff`, `/preview`, `/recursive`, `/noignore`, `/ignore`, `/unmapped`, `/exclude:` | ✓ |
| `/clean` without `/preview` | needs `/noprompt` as an explicit confirmation (TFP0405, SPEC.md) |

### localversions (`lv` is not in the help; tf.exe accepts it)

```
tf vc localversions itemspec [/recursive] [/format:brief|detailed] [/workspace:name[;owner]] [/collection:url] [/login:u,[p]]
```

All switches ✓.

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
| `/map`, `/unmap`, `/cloak`, `/decloak` | phase 2 (the help lists `/collection` for `/unmap`, but tf.exe 18.10 rejects it at run time, golden `workfold/unmap`; TfPortable accepts it) |

### workspaces

```
tf vc workspaces [/owner:name] [/computer:name] [/collection:url] [/format:(brief|detailed|xml)]
                 [/updateUserName:old] [/updateComputerName:old] [workspacename] [/login:u,[p]]
tf vc workspaces /remove:(*|ws1[,ws2,...]) /collection:(*|url) [/login:u,[p]]
```

| Switch | TfPortable |
| --- | --- |
| `/owner`, `/computer`, `/format:brief\|detailed`, `workspacename` | ✓ |
| `/format:xml` | ✗ (use `/format:json`) |
| `/remove:` | ✓ (local cache only, as in tf.exe) |
| `/updateUserName:`, `/updateComputerName:` | ✗ (server writes) |

### history (`hist` is not in the help; tf.exe accepts it)

```
tf vc history itemspec [/version:versionspec] [/stopafter:number] [/recursive] [/user:username]
           [/format:(brief|detailed)] [/slotmode] [/itemmode] [/noprompt] [/login:u,[p]]
           [/sort:ascending,descending] [/collection:url]
```

| Switch | TfPortable |
| --- | --- |
| all of the above | ✓ (default slot mode, docs/compat.md §9; without `/stopafter` at most 256 entries) |
| `/comment:`, `/changetype:`, `/workitem:`, `/files`, `/contains:` | + extensions (SPEC.md "Vyhledávání v historii") |

### changeset

```
tf vc changeset [/comment:("text"|@file)] [/notes:("Name"="Value"|@file)] [/noprompt] [/collection:url]
             [changesetnumber | /latest] [/login:u,[p]]
```

| Switch | TfPortable |
| --- | --- |
| `changesetnumber`, `/latest` | ✓ |
| `/comment:`, `/notes:` | ✗ TFP0003 (they edit the changeset on the server) |
| `/diff` | + extension |

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
| other formats (`context`, `rcs`, `ss*`, `unix`, `visual`) | ✗ TFP0002 (docs/compat.md §12) |
| `/ignorespace`, `/ignoreeol`, `/ignorecase`, `/recursive` | ✓ |
| `/encoding:` | ~ accepted (forces the encoding used to read both sides) |
| `/options`, `/configure` | ✗ (external diff tool configuration) |

### dir

```
tf vc dir itemspec [/version:versionspec] [/recursive] [/folders] [/deleted] [/login:u,[p]] [/collection:url]
```

All switches ✓.

### properties / info (canonical name in the help is `info`; `properties` is the deprecated alias)

```
tf vc info [/recursive] itemspec [/version:versionspec] [/workspace:name] [/login:u,[p]] [/collection:url]
```

All switches ✓. No deprecation warning for either name (docs/compat.md §1.3).

### labels

```
tf vc labels [/owner:name] [/format:(brief|detailed)] [/collection:url] [labelname] [/login:u,[p]]
```

All switches ✓.

### branches

```
tf vc branches itemspec [/version:versionspec] [/collection:url] [/login:u,[p]]
```

All switches ✓.

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
| `/preview`, `/baseless`, `/noimplicitbaseless`, `/conservative`, `/noautoresolve`, `/nosummary`, `/force`, `/discard`, `/lock:` | phase 2 (pending changes only; `/lock:checkin|checkout` ✗) |

## Phase 2

### merges

```
tf vc merges [source] destination [/recursive] [/extended] [/format:(brief|detailed)] [/login:u,[p]] [/showall] [/collection:url]
```

`/extended` is listed by the help but rejected at run time by tf.exe 18.10 ("option not allowed",
golden `merges/extended`); TfPortable does not implement it. Others ✓ when implemented.

### annotate (+ extension; tf.exe has no such command, golden `help/annotate` exit 2)

Planned: `tfp annotate itemspec [/version:versionspec]`.

### folderdiff

```
tf vc folderdiff [sourcePath] targetPath [/recursive] [/noprompt] [/collection:url] [/filter:filter]
              [/filterLocalPathsOnly] [/login:u,[p]] [/view:same,different,sourceOnly,targetOnly]
```

### shelvesets

```
tf vc shelvesets [/owner:name] [/format:(brief|detailed|xml)] [/collection:url] [/login:u,[p]] shelvesetname
```

`/format:xml` ✗.

### unshelve

```
tf vc unshelve [/move] [shelvesetname[;username]] [itemspec] [/recursive] [/nomerge] [/noautoresolve] [/noprompt] [/login:u,[p]]
```

`/move` ✗ (deletes the shelveset on the server, SPEC.md).

### undelete

```
tf vc undelete [/noget] [/lock:(none|checkin|checkout)] [/recursive] itemspec[;deletionID] [/login:u,[p]]
```

### rollback

```
tf vc rollback /changeset:from~to [itemspec] [/recursive] [/lock:none|checkin|checkout] [/version:versionspec]
               [/keepmergehistory] [/noautoresolve] [/noprompt] [/login:u,[p]]
tf vc rollback /toversion:versionspec itemspec [/recursive] [/lock:...] [/version:versionspec]
               [/keepmergehistory] [/noautoresolve] [/noprompt] [/login:u,[p]]
```

### branch

```
tf vc branch olditem newitem [/version:versionspec] [/noget] [/lock:(none|checkin|checkout)] [/noprompt]
          [/silent] [/checkin] [/comment:("text"|@file)] [/author:name] [/login:u,[p]] [/notes:(...)]
```

`/checkin`, `/comment:`, `/author:`, `/notes:` ✗ (they check in immediately).

## Out of scope (recognized, TFP0003)

`checkin`, `shelve` (incl. `/delete`), `lock`, `label`, `unlabel`, `destroy`, `permission`,
`workspace` (`/new`, `/delete`, edit), `configure`, `proxy` (`tf settings proxy`), `certificates`,
`msdn`, `property`, `resolvePath`. Their help texts are in `tests/golden/help/` for reference.

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
