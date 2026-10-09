<#
.SYNOPSIS
    Captures the tf.exe scenarios of phase 2 that the first golden run did not cover.

.DESCRIPTION
    Complements capture-tf-golden.ps1 for the commands implemented in phase 2 of TfPortable
    (merge, branch, rollback, undelete, merges, folderdiff). The cases answer the questions 27-33 of
    docs/tf-reference/questions.md and the deviations recorded in docs/compat.md section 15:

      branch (pending), branch /silent, branch /noget, merge /candidate, merge /preview (all, a version
      range, a single version, with a conflict), merge with a version range, merge /discard, merge /force,
      merge /baseless, merge /nosummary, merge /format:detailed, merge with nothing to merge, merge with a
      conflict and resolve of it, merges (brief, detailed, without a source, /showall), rollback of a
      changeset with and without an itemspec, of a range, /toversion with and without an itemspec, of a
      non-changeset version, /keepmergehistory, undelete of a file, by deletion id, of a file deleted twice,
      /noget, of a folder, of an item that is not deleted, folderdiff (sections, /view, /filter, local
      against server, one path, a missing folder, /filterLocalPathsOnly, same size and different content).

    Every run uses a run id (yyMMddHHmm) in names, so it can be run again. Everything lives under
    $/<Project>/Main/golden2-<run> (a source folder, a branch of it and a sibling for the baseless merge);
    the folder is deleted with a final checkin. Pending merges, rollbacks and undeletes are undone after
    their capture. Results go to <OutDir>\<command>\<case>.txt and <OutDir>\index-phase2-<run>.json.

    Requirements: the seeded test project (seed-tfvc-test-repo.ps1), tf.exe signed in and a clean workspace.
    Every checkin passes /override with a reason, so a checkin policy of the project does not stop the run.

.PARAMETER Manifest
    Path to seed-manifest.json written by seed-tfvc-test-repo.ps1 (only collection, project, serverRoot,
    localRoot, projectLocal and workspace are read).

.PARAMETER OutDir
    Output folder (same as for capture-tf-golden.ps1). Default: .\tests\golden

.PARAMETER TfPath
    Full path to tf.exe. Found automatically via vswhere when omitted.

.PARAMETER Redact
    Extra strings to replace with <REDACTED>.

.PARAMETER TimeoutSeconds
    Kill tf.exe when a single call takes longer (e.g. it opened a dialog). Default: 180

.PARAMETER TfOutputEncoding
    Code page used to decode tf.exe output: ansi (default), oem or utf8.

.EXAMPLE
    .\capture-tf-golden-phase2.ps1 -Manifest D:\TfpTest\seed-manifest.json -OutDir .\tests\golden
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Manifest,
    [string] $OutDir = '.\tests\golden',
    [string] $TfPath,
    [string[]] $Redact = @(),
    [int] $TimeoutSeconds = 180,
    [ValidateSet('ansi', 'oem', 'utf8')] [string] $TfOutputEncoding = 'ansi'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -eq 'Core') {
    [System.Text.Encoding]::RegisterProvider([System.Text.CodePagesEncodingProvider]::Instance)
}

# ---------------------------------------------------------------------------
# Helpers (the same as in capture-tf-golden-extra.ps1)
# ---------------------------------------------------------------------------

function Find-Tf {
    param([string] $Hint)
    if ($Hint) {
        if (-not (Test-Path -LiteralPath $Hint)) { throw "tf.exe not found at '$Hint'." }
        return (Resolve-Path -LiteralPath $Hint).Path
    }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $found = & $vswhere -latest -prerelease -products * -find 'Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\TF.exe' |
            Select-Object -First 1
        if ($found) { return $found }
    }
    $roots = @("$env:ProgramFiles\Microsoft Visual Studio", "${env:ProgramFiles(x86)}\Microsoft Visual Studio") |
        Where-Object { Test-Path -LiteralPath $_ }
    if ($roots) {
        $found = Get-ChildItem -LiteralPath $roots -Recurse -Filter 'TF.exe' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like '*Team Explorer*' } |
            Select-Object -First 1
        if ($found) { return $found.FullName }
    }
    throw 'tf.exe not found. Install Visual Studio with Team Explorer, or pass -TfPath.'
}

function ConvertTo-ArgString {
    param([string] $Value)
    if ($Value -eq '') { return '""' }
    if ($Value -notmatch '[\s"]') { return $Value }
    $escaped = $Value -replace '(\\*)"', '$1$1\"'
    $escaped = $escaped -replace '(\\+)$', '$1$1'
    return '"' + $escaped + '"'
}

function Invoke-TfRaw {
    param([string[]] $Arguments, [string] $WorkingDirectory)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $script:Tf
    $psi.Arguments = (@($Arguments | ForEach-Object { ConvertTo-ArgString $_ })) -join ' '
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = $script:TfEncoding
    $psi.StandardErrorEncoding = $script:TfEncoding
    $psi.EnvironmentVariables['VSLANG'] = '1033'

    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::Start($psi)
    $process.StandardInput.Close()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $timedOut = $false
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $timedOut = $true
        try { $process.Kill() } catch { }
    }
    $process.WaitForExit()
    $watch.Stop()

    return [pscustomobject]@{
        ExitCode   = $(if ($timedOut) { -1 } else { $process.ExitCode })
        TimedOut   = $timedOut
        Stdout     = $stdoutTask.Result
        Stderr     = $stderrTask.Result
        DurationMs = $watch.ElapsedMilliseconds
    }
}

function Protect-Text {
    param([AllowNull()] [string] $Text)
    if ([string]::IsNullOrEmpty($Text)) { return $Text }
    foreach ($pair in $script:Redactions) {
        $Text = [regex]::Replace($Text, [regex]::Escape($pair.From), $pair.To.Replace('$', '$$'), 'IgnoreCase')
    }
    return [regex]::Replace($Text, '[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+', '<EMAIL>')
}

function Format-CommandLine {
    param([string[]] $Arguments)
    return 'tf ' + ((@($Arguments | ForEach-Object { ConvertTo-ArgString $_ })) -join ' ')
}

# Runs tf.exe, saves the result as a golden case. With -Expect, throws on another exit code.
function Invoke-Case {
    param(
        [Parameter(Mandatory = $true)] [string] $Group,
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string[]] $Arguments,
        [string] $WorkingDirectory = $script:WsA,
        [int[]] $Expect
    )
    Write-Host ("{0,-18} {1,-30}" -f $Group, $Name) -NoNewline
    $result = Invoke-TfRaw -Arguments $Arguments -WorkingDirectory $WorkingDirectory

    $commandLine = Protect-Text (Format-CommandLine $Arguments)
    $content = @(
        "# case: $Group/$Name",
        "# command: $commandLine",
        "# cwd: $(Protect-Text $WorkingDirectory)",
        "# exit: $($result.ExitCode)",
        "# timed-out: $($result.TimedOut.ToString().ToLowerInvariant())",
        "# duration-ms: $($result.DurationMs)",
        "# console-encoding: $($script:TfEncoding.WebName)",
        "# tf-version: $($script:TfVersion)",
        "# run: $($script:Run)",
        '==== stdout ====',
        (Protect-Text $result.Stdout),
        '==== stderr ====',
        (Protect-Text $result.Stderr)
    ) -join "`r`n"

    $dir = Join-Path $script:OutRoot $Group
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    [System.IO.File]::WriteAllText((Join-Path $dir "$Name.txt"), $content, $script:Utf8NoBom)

    $script:Index.Add([ordered]@{
        group    = $Group
        name     = $Name
        command  = $commandLine
        exitCode = $result.ExitCode
        timedOut = $result.TimedOut
        file     = "$Group/$Name.txt"
    })

    $color = if ($result.TimedOut) { 'Red' } elseif ($result.ExitCode -eq 0) { 'Green' } else { 'Yellow' }
    Write-Host "exit $($result.ExitCode)" -ForegroundColor $color

    if ($Expect -and ($Expect -notcontains $result.ExitCode)) {
        throw "Case $Group/$Name ended with exit code $($result.ExitCode) (expected $($Expect -join ', ')). The script cannot continue.`n$($result.Stdout)$($result.Stderr)"
    }
    return $result
}

# Runs tf.exe as a setup step (not saved). Throws on an unexpected exit code.
function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)] [string[]] $Arguments,
        [string] $WorkingDirectory = $script:WsA,
        [int[]] $Expect = @(0)
    )
    Write-Host "  > $(Format-CommandLine $Arguments)" -ForegroundColor DarkGray
    $result = Invoke-TfRaw -Arguments $Arguments -WorkingDirectory $WorkingDirectory
    if ($Expect -notcontains $result.ExitCode) {
        throw "Setup step failed with exit code $($result.ExitCode): $(Format-CommandLine $Arguments)`n$($result.Stdout)$($result.Stderr)"
    }
    return $result
}

function Write-File {
    param(
        [Parameter(Mandatory = $true)] [string] $Base,
        [Parameter(Mandatory = $true)] [string] $RelativePath,
        [Parameter(Mandatory = $true)] [AllowEmptyString()] [AllowEmptyCollection()] [string[]] $Lines
    )
    $path = Join-Path $Base $RelativePath
    $dir = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    if (Test-Path -LiteralPath $path) { Set-ItemProperty -LiteralPath $path -Name IsReadOnly -Value $false }
    [System.IO.File]::WriteAllText($path, (($Lines -join "`r`n") + "`r`n"), $script:Utf8NoBom)
}

function Get-LatestChangeset {
    $history = Invoke-TfRaw -Arguments @('history', $script:S, '/recursive', '/stopafter:1', '/format:brief', '/noprompt') -WorkingDirectory $script:WsA
    $match = [regex]::Match($history.Stdout, '(?m)^\s*(\d+)\s')
    if (-not $match.Success) { throw "Could not read the latest changeset number.`n$($history.Stdout)$($history.Stderr)" }
    return [int]$match.Groups[1].Value
}

# The changeset number a checkin printed ("Changeset #123 checked in." in any language), else the latest one.
function Get-CheckinNumber {
    param([Parameter(Mandatory = $true)] $Result)
    $match = [regex]::Match($Result.Stdout + $Result.Stderr, '#(\d+)')
    if ($match.Success) { return [int]$match.Groups[1].Value }
    return Get-LatestChangeset
}

# Undoes every pending change under the run folder (nothing to undo is not an error).
function Undo-Run {
    Invoke-Step @('undo', "$script:WsA\$script:GL", '/recursive', '/noprompt') -Expect @(0, 1, 100) | Out-Null
}

# Checks in the run folder with a comment and an override reason; returns the changeset number.
function Submit-Run {
    param([Parameter(Mandatory = $true)] [string] $Comment, [string] $Path = "$script:WsA\$script:GL")
    $result = Invoke-Step @('checkin', $Path, '/recursive', "/comment:$Comment ($script:Run)", "/override:Golden2 capture ($script:Run)", '/noprompt')
    return Get-CheckinNumber $result
}

# ---------------------------------------------------------------------------
# Setup
# ---------------------------------------------------------------------------

$script:Tf = Find-Tf $TfPath
$script:TfVersion = (Get-Item -LiteralPath $script:Tf).VersionInfo.FileVersion
$textInfo = [System.Globalization.CultureInfo]::CurrentCulture.TextInfo
$script:TfEncoding = switch ($TfOutputEncoding) {
    'ansi' { [System.Text.Encoding]::GetEncoding($textInfo.ANSICodePage) }
    'oem'  { [System.Text.Encoding]::GetEncoding($textInfo.OEMCodePage) }
    'utf8' { New-Object System.Text.UTF8Encoding($false) }
}
$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$script:Index = New-Object System.Collections.Generic.List[object]

$m = Get-Content -LiteralPath $Manifest -Raw -Encoding UTF8 | ConvertFrom-Json
# A trailing separator in the manifest would double in every path.
$m.projectLocal = $m.projectLocal.TrimEnd('\', '/')
$m.localRoot = $m.localRoot.TrimEnd('\', '/')
$script:WsA = $m.projectLocal
$script:S = $m.serverRoot
$script:C = "/collection:$($m.collection)"
$S = $script:S
$C = $script:C
$WsA = $script:WsA

if (-not (Test-Path -LiteralPath $WsA)) {
    throw "Workspace folder '$WsA' from the manifest does not exist. Run seed-tfvc-test-repo.ps1 first."
}

$script:Run = Get-Date -Format 'yyMMddHHmm'
$run = $script:Run
$G = "golden2-$run"
$script:GL = "Main\$G"
$GL = $script:GL
$GS = "$S/Main/$G"
$SrcL = "$WsA\$GL\src"
$SrcS = "$GS/src"
$RelL = "$WsA\$GL\rel"
$RelS = "$GS/rel"
$OtherL = "$WsA\$GL\other"
$OtherS = "$GS/other"
$changesets = [ordered]@{}

$script:OutRoot = [System.IO.Path]::GetFullPath($OutDir)
if (-not (Test-Path -LiteralPath $script:OutRoot)) { New-Item -ItemType Directory -Path $script:OutRoot | Out-Null }

# Sign-in check.
$pre = Invoke-TfRaw -Arguments @('workspaces', $C) -WorkingDirectory $WsA
if ($pre.ExitCode -ne 0 -or ($pre.Stdout + $pre.Stderr) -match 'TF30063|TF400813') {
    throw "tf.exe is not signed in or cannot reach the collection. Run `"$($script:Tf)`" workspaces $C in a terminal, sign in, then run this script again.`n$($pre.Stdout)$($pre.Stderr)"
}

# The test workspace must be clean.
$clean = Invoke-TfRaw -Arguments @('status', '/recursive', '/noprompt') -WorkingDirectory $WsA
if ($clean.Stdout -match '(?m)^\$/|^\S+\s+(edit|add|delete|rename|upravit|p.idat|odstranit|p.ejmenovat)') {
    Write-Host $clean.Stdout
    throw "The test workspace '$WsA' has pending changes. Undo them first (tf undo /recursive in that folder)."
}

# Owner for redaction.
$owner = $null
$detailed = Invoke-TfRaw -Arguments @('workspaces', $C, '/format:detailed') -WorkingDirectory $WsA
foreach ($block in ($detailed.Stdout -split '(?m)^=+\s*$')) {
    if ($block -match "(?m)^[^:\r\n]+:\s*$([regex]::Escape($m.workspace))\s*$") {
        $ownerMatch = [regex]::Match($block, '(?m)^\s*(Owner|Vlastn\S*)\s*:\s*(.+?)\s*$')
        if ($ownerMatch.Success) { $owner = $ownerMatch.Groups[2].Value }
    }
}

$pairs = New-Object System.Collections.Generic.List[object]
$pairs.Add(@{ From = $m.projectLocal; To = '<WORKSPACE_ROOT>' })
$pairs.Add(@{ From = ($m.projectLocal -replace '\\', '/'); To = '<WORKSPACE_ROOT>' })
$pairs.Add(@{ From = $m.localRoot; To = '<LOCAL_ROOT>' })
$pairs.Add(@{ From = $m.collection; To = '<COLLECTION>' })
$pairs.Add(@{ From = $m.workspace; To = '<WORKSPACE>' })
if ($owner) { $pairs.Add(@{ From = $owner; To = '<OWNER>' }) }
$pairs.Add(@{ From = $env:COMPUTERNAME; To = '<COMPUTER>' })
if ($env:USERNAME.Length -ge 3) { $pairs.Add(@{ From = $env:USERNAME; To = '<USER>' }) }
foreach ($r in $Redact) { if ($r) { $pairs.Add(@{ From = $r; To = '<REDACTED>' }) } }
$script:Redactions = @($pairs | Sort-Object { $_.From.Length } -Descending)

Write-Host "tf.exe:  $($script:Tf) ($($script:TfVersion))"
Write-Host "Run id:  $run"
Write-Host "Output:  $($script:OutRoot)"
Write-Host ''

# ---------------------------------------------------------------------------
# Scenarios
# ---------------------------------------------------------------------------

$failed = $null
$x1 = 0
$noget = $null
try {
    # --- source folder -------------------------------------------------------
    Write-Host '--- setup: source folder' -ForegroundColor Cyan
    Write-File $WsA "$GL\src\a.txt" @('a line 1', 'a line 2', 'a line 3')
    Write-File $WsA "$GL\src\b.txt" @('b line 1')
    Write-File $WsA "$GL\src\sub\c.txt" @('c line 1')
    Write-File $WsA "$GL\src\same.txt" @('same content on both sides')
    Write-File $WsA "$GL\src\size.txt" @('AAAA')
    Invoke-Step @('add', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    $changesets['add'] = Submit-Run 'Golden2 add source'

    # --- branch --------------------------------------------------------------
    Write-Host '--- branch' -ForegroundColor Cyan
    Invoke-Case 'branch' 'pending' @('branch', $SrcS, $RelS, '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-branch' @('status', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    $changesets['branch'] = Submit-Run 'Golden2 branch src to rel'
    Invoke-Case 'branches' 'run-folder' @('branches', $SrcS, '/noprompt') | Out-Null

    Invoke-Case 'branch' 'silent' @('branch', $SrcS, "$GS/rel-silent", '/silent', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-branch-silent' @('status', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'dir' 'after-branch-silent' @('dir', "$WsA\$GL", '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'branch' 'noget' @('branch', $SrcS, "$GS/rel-noget", '/noget', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-branch-noget' @('status', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'branch' 'version' @('branch', $SrcS, "$GS/rel-version", "/version:C$($changesets['add'])", '/noprompt') | Out-Null
    Undo-Run
    if (Test-Path -LiteralPath "$WsA\$GL\rel-silent") { Remove-Item -LiteralPath "$WsA\$GL\rel-silent" -Recurse -Force -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath "$WsA\$GL\rel-noget") { Remove-Item -LiteralPath "$WsA\$GL\rel-noget" -Recurse -Force -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath "$WsA\$GL\rel-version") { Remove-Item -LiteralPath "$WsA\$GL\rel-version" -Recurse -Force -ErrorAction SilentlyContinue }

    # --- changes on the source: three merge candidates ----------------------
    Write-Host '--- setup: changes on the source' -ForegroundColor Cyan
    Invoke-Step @('checkout', "$SrcL\a.txt", '/noprompt') | Out-Null
    Write-File $WsA "$GL\src\a.txt" @('a line 1 edited in src', 'a line 2', 'a line 3')
    $changesets['editA'] = Submit-Run 'Golden2 edit a.txt' "$SrcL\a.txt"
    Invoke-Step @('checkout', "$SrcL\b.txt", '/noprompt') | Out-Null
    Write-File $WsA "$GL\src\b.txt" @('b line 1', 'b line 2 added in src')
    $changesets['editB'] = Submit-Run 'Golden2 edit b.txt' "$SrcL\b.txt"
    Write-File $WsA "$GL\src\d.txt" @('d is new in src')
    Invoke-Step @('add', "$SrcL\d.txt", '/noprompt') | Out-Null
    $changesets['addD'] = Submit-Run 'Golden2 add d.txt' "$SrcL\d.txt"
    $cA = $changesets['editA']
    $cB = $changesets['editB']
    $cD = $changesets['addD']

    # --- merge: candidates and previews ------------------------------------
    Write-Host '--- merge: candidates and previews' -ForegroundColor Cyan
    Invoke-Case 'merge' 'candidate-run' @('merge', $SrcS, $RelS, '/recursive', '/candidate', '/noprompt') | Out-Null
    Invoke-Case 'merge' 'candidate-detailed' @('merge', $SrcS, $RelS, '/recursive', '/candidate', '/format:detailed', '/noprompt') | Out-Null
    Invoke-Case 'merge' 'preview-run' @('merge', $SrcS, $RelS, '/recursive', '/preview', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-preview' @('status', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'merge' 'preview-version-range' @('merge', $SrcS, $RelS, '/recursive', '/preview', "/version:C$cA~C$cB", '/noprompt') | Out-Null
    Invoke-Case 'merge' 'preview-single-version' @('merge', $SrcS, $RelS, '/recursive', '/preview', "/version:C$cB", '/noprompt') | Out-Null
    Invoke-Case 'merge' 'preview-detailed' @('merge', $SrcS, $RelS, '/recursive', '/preview', '/format:detailed', '/noprompt') | Out-Null
    Invoke-Case 'merge' 'preview-nosummary' @('merge', $SrcS, $RelS, '/recursive', '/preview', '/nosummary', '/noprompt') | Out-Null

    # --- merge: a version range, discard, force --------------------------------
    Write-Host '--- merge: range, discard, force' -ForegroundColor Cyan
    Invoke-Case 'merge' 'version-range' @('merge', $SrcS, $RelS, '/recursive', "/version:C$cA~C$cA", '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-merge-range' @('status', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-merge-range-detailed' @('status', "$WsA\$GL", '/recursive', '/format:detailed', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'merge' 'discard' @('merge', $SrcS, $RelS, '/recursive', "/version:C$cB~C$cB", '/discard', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-merge-discard' @('status', "$WsA\$GL", '/recursive', '/format:detailed', '/noprompt') | Out-Null
    Invoke-Case 'merge' 'candidate-after-discard' @('merge', $SrcS, $RelS, '/recursive', '/candidate', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'merge' 'all-run' @('merge', $SrcS, $RelS, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-merge-all' @('status', "$WsA\$GL", '/recursive', '/format:detailed', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'merge' 'nosummary' @('merge', $SrcS, $RelS, '/recursive', '/nosummary', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'merge' 'detailed' @('merge', $SrcS, $RelS, '/recursive', '/format:detailed', '/noprompt') | Out-Null
    $changesets['mergeAll'] = Submit-Run 'Golden2 merge src to rel'
    Invoke-Case 'merge' 'nothing-to-merge' @('merge', $SrcS, $RelS, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'merge' 'preview-nothing' @('merge', $SrcS, $RelS, '/recursive', '/preview', '/noprompt') | Out-Null
    Invoke-Case 'merge' 'force' @('merge', $SrcS, $RelS, '/recursive', '/force', "/version:C$cA~C$cA", '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-merge-force' @('status', "$WsA\$GL", '/recursive', '/format:detailed', '/noprompt') | Out-Null
    Undo-Run

    # --- merges ------------------------------------------------------------------
    Write-Host '--- merges' -ForegroundColor Cyan
    Invoke-Case 'merges' 'run-folder' @('merges', $SrcS, $RelS, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'merges' 'detailed' @('merges', $SrcS, $RelS, '/recursive', '/format:detailed', '/noprompt') | Out-Null
    Invoke-Case 'merges' 'no-source' @('merges', $RelS, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'merges' 'showall-run' @('merges', $SrcS, $RelS, '/recursive', '/showall', '/noprompt') | Out-Null
    Invoke-Case 'merges' 'single-file' @('merges', "$SrcS/a.txt", "$RelS/a.txt", '/noprompt') | Out-Null
    Invoke-Case 'merges' 'no-history' @('merges', $RelS, $SrcS, '/recursive', '/noprompt') | Out-Null

    # --- merge: a conflict ----------------------------------------------------
    Write-Host '--- merge: conflict' -ForegroundColor Cyan
    Invoke-Step @('checkout', "$RelL\a.txt", '/noprompt') | Out-Null
    Write-File $WsA "$GL\rel\a.txt" @('a line 1 edited in rel', 'a line 2', 'a line 3')
    $changesets['editRelA'] = Submit-Run 'Golden2 edit a.txt in rel' "$RelL\a.txt"
    Invoke-Step @('checkout', "$SrcL\a.txt", '/noprompt') | Out-Null
    Write-File $WsA "$GL\src\a.txt" @('a line 1 edited in src again', 'a line 2', 'a line 3')
    $changesets['editSrcA2'] = Submit-Run 'Golden2 edit a.txt in src again' "$SrcL\a.txt"
    Invoke-Case 'merge' 'preview-conflict' @('merge', $SrcS, $RelS, '/recursive', '/preview', '/noprompt') | Out-Null
    Invoke-Case 'merge' 'conflict-run' @('merge', $SrcS, $RelS, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'resolve' 'merge-conflict-list-2' @('resolve', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-merge-conflict' @('status', "$WsA\$GL", '/recursive', '/format:detailed', '/noprompt') | Out-Null
    Invoke-Case 'resolve' 'merge-conflict-keepyours-2' @('resolve', "$WsA\$GL", '/recursive', '/auto:KeepYours', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'merge' 'conflict-noautoresolve' @('merge', $SrcS, $RelS, '/recursive', '/noautoresolve', '/noprompt') | Out-Null
    Undo-Run

    # --- merge: baseless -------------------------------------------------------
    Write-Host '--- merge: baseless' -ForegroundColor Cyan
    Write-File $WsA "$GL\other\a.txt" @('unrelated a')
    Invoke-Step @('add', "$WsA\$GL\other", '/recursive', '/noprompt') | Out-Null
    $changesets['addOther'] = Submit-Run 'Golden2 add other folder' $OtherL
    Invoke-Case 'merge' 'unrelated-without-baseless' @('merge', $SrcS, $OtherS, '/recursive', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'merge' 'baseless' @('merge', $SrcS, $OtherS, '/recursive', '/baseless', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-merge-baseless' @('status', "$WsA\$GL", '/recursive', '/format:detailed', '/noprompt') | Out-Null
    Undo-Run

    # --- rollback --------------------------------------------------------------
    Write-Host '--- rollback' -ForegroundColor Cyan
    Invoke-Case 'rollback' 'changeset-item' @('rollback', "/changeset:C$cB", $SrcL, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-rollback-changeset' @('status', "$WsA\$GL", '/recursive', '/format:detailed', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'rollback' 'range' @('rollback', "/changeset:C$cA~C$cD", $SrcL, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-rollback-range' @('status', "$WsA\$GL", '/recursive', '/format:detailed', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'rollback' 'changeset-no-item' @('rollback', "/changeset:C$cB", '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-rollback-no-item' @('status', '/recursive', '/noprompt') | Out-Null
    Invoke-Step @('undo', $WsA, '/recursive', '/noprompt') -Expect @(0, 1, 100) | Out-Null
    Invoke-Case 'rollback' 'toversion-run' @('rollback', "/toversion:C$($changesets['add'])", $SrcL, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-rollback-toversion' @('status', "$WsA\$GL", '/recursive', '/format:detailed', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'rollback' 'toversion-no-item' @('rollback', "/toversion:C$($changesets['add'])", '/noprompt') | Out-Null
    Invoke-Step @('undo', $WsA, '/recursive', '/noprompt') -Expect @(0, 1, 100) | Out-Null
    Invoke-Case 'rollback' 'toversion-latest' @('rollback', '/toversion:T', $SrcL, '/recursive', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'rollback' 'changeset-latest-spec' @('rollback', '/changeset:T', $SrcL, '/recursive', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'rollback' 'changeset-date-spec' @('rollback', '/changeset:D2020-01-01', $SrcL, '/recursive', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'rollback' 'both-switches' @('rollback', "/changeset:C$cB", "/toversion:C$cA", $SrcL, '/recursive', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'rollback' 'keepmergehistory' @('rollback', "/changeset:C$($changesets['mergeAll'])", $RelL, '/recursive', '/keepmergehistory', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-rollback-keepmergehistory' @('status', "$WsA\$GL", '/recursive', '/format:detailed', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'rollback' 'merge-changeset' @('rollback', "/changeset:C$($changesets['mergeAll'])", $RelL, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-rollback-merge-changeset' @('status', "$WsA\$GL", '/recursive', '/format:detailed', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'rollback' 'nothing' @('rollback', "/changeset:C$cB", $OtherL, '/recursive', '/noprompt') | Out-Null
    Undo-Run

    # --- undelete --------------------------------------------------------------
    Write-Host '--- undelete' -ForegroundColor Cyan
    Invoke-Case 'undelete' 'not-deleted' @('undelete', "$SrcS/b.txt", '/noprompt') | Out-Null
    Undo-Run
    Invoke-Step @('delete', "$SrcL\b.txt", '/noprompt') | Out-Null
    $changesets['deleteB'] = Submit-Run 'Golden2 delete b.txt' "$SrcL\b.txt"
    $deleted = Invoke-Case 'dir' 'deleted-run' @('dir', $SrcS, '/deleted', '/noprompt')
    $xMatch = [regex]::Match($deleted.Stdout, 'b\.txt;X(\d+)')
    $x1 = if ($xMatch.Success) { [int]$xMatch.Groups[1].Value } else { 0 }
    Invoke-Case 'undelete' 'file-run' @('undelete', "$SrcS/b.txt", '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-undelete' @('status', "$WsA\$GL", '/recursive', '/format:detailed', '/noprompt') | Out-Null
    Undo-Run
    if ($x1 -gt 0) {
        Invoke-Case 'undelete' 'deletion-id' @('undelete', "$SrcS/b.txt;X$x1", '/noprompt') | Out-Null
        Undo-Run
        Invoke-Case 'undelete' 'wrong-deletion-id' @('undelete', "$SrcS/b.txt;X$($x1 + 1000)", '/noprompt') | Out-Null
        Undo-Run
    }
    Invoke-Case 'undelete' 'noget' @('undelete', "$SrcS/b.txt", '/noget', '/noprompt') | Out-Null
    $noget = [ordered]@{ fileExists = (Test-Path -LiteralPath "$SrcL\b.txt") }
    Invoke-Case 'status' 'after-undelete-noget' @('status', "$WsA\$GL", '/recursive', '/format:detailed', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'undelete' 'local-path' @('undelete', "$SrcL\b.txt", '/noprompt') | Out-Null
    Undo-Run

    # Deleted twice: undelete and check in, delete and check in again.
    Invoke-Step @('undelete', "$SrcS/b.txt", '/noprompt') | Out-Null
    $changesets['undeleteB'] = Submit-Run 'Golden2 undelete b.txt' "$SrcL\b.txt"
    Invoke-Step @('delete', "$SrcL\b.txt", '/noprompt') | Out-Null
    $changesets['deleteB2'] = Submit-Run 'Golden2 delete b.txt again' "$SrcL\b.txt"
    Invoke-Case 'dir' 'deleted-twice' @('dir', $SrcS, '/deleted', '/noprompt') | Out-Null
    Invoke-Case 'undelete' 'deleted-twice' @('undelete', "$SrcS/b.txt", '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-undelete-twice' @('status', "$WsA\$GL", '/recursive', '/format:detailed', '/noprompt') | Out-Null
    Undo-Run
    if ($x1 -gt 0) {
        Invoke-Case 'undelete' 'deleted-twice-first-id' @('undelete', "$SrcS/b.txt;X$x1", '/noprompt') | Out-Null
        Undo-Run
    }

    # A folder.
    Invoke-Step @('delete', "$SrcL\sub", '/recursive', '/noprompt') | Out-Null
    $changesets['deleteSub'] = Submit-Run 'Golden2 delete sub folder' "$SrcL\sub"
    Invoke-Case 'undelete' 'folder-recursive' @('undelete', "$SrcS/sub", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-undelete-folder' @('status', "$WsA\$GL", '/recursive', '/format:detailed', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'undelete' 'folder-not-recursive' @('undelete', "$SrcS/sub", '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-undelete-folder-flat' @('status', "$WsA\$GL", '/recursive', '/format:detailed', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'undelete' 'missing-run' @('undelete', "$SrcS/no-such-$run.txt", '/noprompt') | Out-Null

    # --- folderdiff --------------------------------------------------------------
    Write-Host '--- folderdiff' -ForegroundColor Cyan
    # rel\same.txt has the same content as src\same.txt (a later date); rel\size.txt has the same size and other content.
    Invoke-Step @('checkout', "$RelL\same.txt", '/noprompt') | Out-Null
    Write-File $WsA "$GL\rel\same.txt" @('same content on both sides')
    Invoke-Step @('checkout', "$RelL\size.txt", '/noprompt') | Out-Null
    Write-File $WsA "$GL\rel\size.txt" @('BBBB')
    Write-File $WsA "$GL\rel\only-in-rel.txt" @('only here')
    Invoke-Step @('add', "$RelL\only-in-rel.txt", '/noprompt') | Out-Null
    $changesets['relDiff'] = Submit-Run 'Golden2 folderdiff setup' $RelL
    Invoke-Case 'folderdiff' 'run-folder' @('folderdiff', $SrcS, $RelS, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'folderdiff' 'not-recursive' @('folderdiff', $SrcS, $RelS, '/noprompt') | Out-Null
    Invoke-Case 'folderdiff' 'view-same' @('folderdiff', $SrcS, $RelS, '/recursive', '/view:same', '/noprompt') | Out-Null
    Invoke-Case 'folderdiff' 'view-list' @('folderdiff', $SrcS, $RelS, '/recursive', '/view:sourceOnly,targetOnly', '/noprompt') | Out-Null
    Invoke-Case 'folderdiff' 'view-different' @('folderdiff', $SrcS, $RelS, '/recursive', '/view:different', '/noprompt') | Out-Null
    Invoke-Case 'folderdiff' 'view-invalid' @('folderdiff', $SrcS, $RelS, '/recursive', '/view:bogus', '/noprompt') | Out-Null
    Invoke-Case 'folderdiff' 'filter' @('folderdiff', $SrcS, $RelS, '/recursive', '/filter:*.txt', '/noprompt') | Out-Null
    Invoke-Case 'folderdiff' 'filter-exclude' @('folderdiff', $SrcS, $RelS, '/recursive', '/filter:*.txt;!b.txt', '/noprompt') | Out-Null
    Invoke-Case 'folderdiff' 'filter-none-match' @('folderdiff', $SrcS, $RelS, '/recursive', '/filter:*.cs', '/noprompt') | Out-Null
    Invoke-Case 'folderdiff' 'local-server' @('folderdiff', $SrcL, $RelS, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'folderdiff' 'server-local' @('folderdiff', $SrcS, $RelL, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'folderdiff' 'local-local' @('folderdiff', $SrcL, $RelL, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'folderdiff' 'one-path-server' @('folderdiff', $SrcS, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'folderdiff' 'one-path-local' @('folderdiff', $SrcL, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'folderdiff' 'version' @('folderdiff', "$SrcS;C$($changesets['add'])", $RelS, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'folderdiff' 'missing-source' @('folderdiff', "$GS/no-such-$run", $RelS, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'folderdiff' 'missing-local' @('folderdiff', "$WsA\$GL\no-such-$run", $RelS, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'folderdiff' 'filter-local-paths-only' @('folderdiff', $SrcL, $RelS, '/recursive', '/filter:*.txt', '/filterLocalPathsOnly', '/noprompt') | Out-Null
    # A local edit not yet checked in: which side wins in a local-against-server comparison (src\sub was deleted above).
    Invoke-Step @('checkout', "$SrcL\a.txt", '/noprompt') | Out-Null
    Write-File $WsA "$GL\src\a.txt" @('a line 1 edited locally, not checked in', 'a line 2', 'a line 3')
    Invoke-Case 'folderdiff' 'local-pending-edit' @('folderdiff', $SrcL, $SrcS, '/recursive', '/noprompt') | Out-Null
    Undo-Run
}
catch {
    $failed = $_
}
finally {
    Write-Host ''
    Write-Host 'Cleaning up...' -ForegroundColor Cyan
    Invoke-TfRaw -Arguments @('undo', $WsA, '/recursive', '/noprompt') -WorkingDirectory $WsA | Out-Null
    if (Test-Path -LiteralPath "$WsA\$GL") {
        foreach ($leftover in @('rel-silent', 'rel-noget', 'rel-version')) {
            if (Test-Path -LiteralPath "$WsA\$GL\$leftover") { Remove-Item -LiteralPath "$WsA\$GL\$leftover" -Recurse -Force -ErrorAction SilentlyContinue }
        }
        $delete = Invoke-TfRaw -Arguments @('delete', "$WsA\$GL", '/recursive', '/noprompt') -WorkingDirectory $WsA
        if ($delete.ExitCode -eq 0) {
            $cleanup = Invoke-Case 'checkin' 'delete-folder-2' @('checkin', "$WsA\$GL", '/recursive', "/comment:Golden2 cleanup ($run)", "/override:Golden2 cleanup ($run)", '/noprompt')
            if ($cleanup.ExitCode -eq 0) { $changesets['delete-folder'] = Get-CheckinNumber $cleanup }
            else { Invoke-TfRaw -Arguments @('undo', "$WsA\$GL", '/recursive', '/noprompt') -WorkingDirectory $WsA | Out-Null }
        }
        else {
            Invoke-TfRaw -Arguments @('undo', "$WsA\$GL", '/recursive', '/noprompt') -WorkingDirectory $WsA | Out-Null
        }
    }
    if (Test-Path -LiteralPath "$WsA\$GL") {
        Get-ChildItem -LiteralPath "$WsA\$GL" -Recurse -Force -File | ForEach-Object { $_.IsReadOnly = $false }
        Remove-Item -LiteralPath "$WsA\$GL" -Recurse -Force -ErrorAction SilentlyContinue
    }

    $indexDoc = [ordered]@{
        capturedAt      = (Get-Date).ToString('o')
        run             = $run
        tfVersion       = $script:TfVersion
        consoleEncoding = $script:TfEncoding.WebName
        culture         = [System.Globalization.CultureInfo]::CurrentUICulture.Name
        extra           = [ordered]@{ changesets = $changesets; goldenFolder = (Protect-Text $GS); deletionIdOfB = $x1; undeleteNoget = $noget }
        cases           = $script:Index
    }
    [System.IO.File]::WriteAllText((Join-Path $script:OutRoot "index-phase2-$run.json"), ($indexDoc | ConvertTo-Json -Depth 6), $script:Utf8NoBom)

    $check = Invoke-TfRaw -Arguments @('status', '/recursive', '/noprompt') -WorkingDirectory $WsA
    Write-Host $check.Stdout
    Write-Host 'Check that the status above shows no pending changes.' -ForegroundColor Yellow
}

$timedOut = @($script:Index | Where-Object { $_.timedOut })
Write-Host ''
Write-Host "Captured $($script:Index.Count) cases into $($script:OutRoot)" -ForegroundColor Green
if ($timedOut.Count -gt 0) {
    Write-Host "$($timedOut.Count) case(s) timed out (tf.exe probably opened a dialog): $(($timedOut | ForEach-Object { $_.file }) -join ', ')" -ForegroundColor Red
}
if ($failed) {
    Write-Host 'The run stopped early:' -ForegroundColor Red
    throw $failed
}
Write-Host 'Review the files for personal data before committing them.' -ForegroundColor Yellow
