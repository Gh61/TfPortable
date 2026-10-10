# folderdiff (tf.exe 18.10.12215.6)

Read from: `TF/Microsoft.TeamFoundation.VersionControl.CommandLine/CommandFolderDiff.cs` (`Run`,
`BuildFolderDifferenceObject`, `ParseViewOptions`, `PrintFolderDifferenceInfo`);
`Microsoft.TeamFoundation.VersionControl.Controls/.../FolderDiffSourceElement.cs` (`Equals`),
`FolderDiffSourceElementItem.cs`, `FolderDiffSourceElementPath.cs`, `FolderDiffSourceElementExtendedItem.cs`;
2026-10-10.

## Switches and defaults

- Options: `/collection`, `/filter`, `/filterlocalpathsonly`, `/noprompt`, `/recursive`, `/server`,
  `/view:different|same|sourceOnly|targetOnly` (several values allowed; an unknown one is an argument error).
- **Default recursion is one level**; `/recursive` makes it full.
- Forms: two paths (any mix of server and local; a server path takes `;version`, default Latest); one server
  path with a version range `$/path;C1~C2` (the two versions are compared; a single version is an error asking
  for a second path); one local path (compared with its server counterpart through the workspace).
- Without `/noprompt` the dialog opens and nothing is printed; the console output exists only in `/noprompt`
  mode.

## Comparison (question 32)

- Two elements are equal when their **content lengths are equal and their MD5 hashes are equal and non-empty**.
  A server item's hash is the server's `HashValue`; a local file's hash is computed by reading the file; an
  item without a stored hash is downloaded and hashed. So the comparison is by content (size plus hash), not by
  date.
- `/filter` is a `;`-separated pattern list stored as the filter history; `/filterlocalpathsonly` applies the
  filter to the local side only (`FolderDiffOptions.FilterLocalPathsOnly`).
- Elements with errors (for example unreadable files) are listed in a section of their own.

## Output

- Sections, each with a `=` ruler, a heading line and a blank line (headings are stdout info lines, the ruler
  width is the longest heading or summary line, capped at the console width): "only in <path1>" (path per
  line), "only in <path2>", "different" (`path1 - ` then a tab-indented `path2`, blank line after each pair),
  "same" (same shape), "errors"; which sections appear follows `/view` (default: different, source only,
  target only). Then the summary line with folders compared, files compared, files only in source, only in
  target, different, and error count, between two rulers.

## Consequences for TfPortable

- compat.md §15: hash comparison confirmed (tf.exe also compares the length first; a hash alone would do).
  tfp's one-level default matches. `/filterLocalPathsOnly` not supported in tfp: deviation stands.
