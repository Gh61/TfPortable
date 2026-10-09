<#
.SYNOPSIS
    Captures the remaining tf.exe gaps of phases 1 and 3 that no earlier run covered.

.DESCRIPTION
    Complements capture-tf-golden.ps1, capture-tf-golden-extra.ps1 and the phase scripts. The cases are the
    ones docs/compat.md and docs/tf-reference/questions.md still mark as assumed:

      add /recursive with .tfignore matches, add /noignore, add with a wildcard, checkin without /comment,
      checkin without a work item and with /override (the policy failure shape when the project has a policy),
      checkin /validate with nothing pending, checkout of an already checked-out file and of a folder,
      delete of a file with a pending edit and of a folder, rename of a folder, onto an existing name and
      into a folder, undo of one file, difference with /ignorespace, /ignorecase, /ignoreeol, a missing final
      newline, a binary file and a folder, view of a folder, a wildcard, /recursive and a binary file to
      /output, dir with a wildcard, properties of a folder and of a deleted item, history of a deleted item,
      branches of an item that is not branched, merge /candidate of unrelated paths, label of a server path
      without a version and with /version, labels /owner:* and a wildcard, unlabel of a folder, lock /lock:none
      on an unlocked item and a second lock, checkout of a file the same workspace locked, shelve without a
      comment, unshelve of one item and with ;owner, shelvesets /format:detailed, reconcile /promote with and
      without /adds and /deletes, get of an older version over a modified file (with and without /overwrite),
      workfold with a folder and a server path, and resolve of merge conflicts with /auto:AutoMerge (preview,
      non-overlapping, overlapping), AutoMergeForced, OverwriteLocal, DeleteConflict, KeepYoursRenameTheirs
      with /newname, plus OverwriteLocal on a get conflict.

    Everything lives under $/<Project>/Main/goldenx-<run> (a folder x, a branch y of it); the folder is
    deleted with a final checkin, the label and the shelveset of the run are deleted. Results go to
    <OutDir>\<command>\<case>.txt and <OutDir>\index-extra2-<run>.json.

    Requirements: the seeded test project, tf.exe signed in and a clean workspace. Checkins that have to
    succeed pass /override with a reason; the checkin cases without a comment or a work item are captured
    as they come (a project policy makes them fail, which is the shape wanted).

.PARAMETER Manifest
    Path to seed-manifest.json (only collection, project, serverRoot, localRoot, projectLocal and workspace are read).

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
    .\capture-tf-golden-extra2.ps1 -Manifest D:\TfpTest\seed-manifest.json -OutDir .\tests\golden
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

# Checks in with a comment and an override reason; returns the changeset number.
function Submit-Run {
    param([Parameter(Mandatory = $true)] [string] $Comment, [string] $Path = "$script:WsA\$script:GL")
    $result = Invoke-Step @('checkin', $Path, '/recursive', "/comment:$Comment ($script:Run)", "/override:Goldenx capture ($script:Run)", '/noprompt')
    return Get-CheckinNumber $result
}

# Writes raw bytes (binary files, files without a final newline).
function Write-Bytes {
    param([Parameter(Mandatory = $true)] [string] $Path, [Parameter(Mandatory = $true)] [byte[]] $Bytes)
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    if (Test-Path -LiteralPath $Path) { Set-ItemProperty -LiteralPath $Path -Name IsReadOnly -Value $false }
    [System.IO.File]::WriteAllBytes($Path, $Bytes)
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
$G = "goldenx-$run"
$script:GL = "Main\$G"
$GL = $script:GL
$GS = "$S/Main/$G"
$XL = "$WsA\$GL\x"
$XS = "$GS/x"
$YL = "$WsA\$GL\y"
$YS = "$GS/y"
$labelName = "goldenx-$run"
$label = "$labelName@$S"
$shelf = "Goldenx-$run"
$changesets = [ordered]@{}
$x1 = 0

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

# Owner for redaction and for the ;owner form of unshelve.
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
$binaryOut = Join-Path $env:TEMP "tfp-goldenx-blob-$run.bin"
try {
    # --- add -------------------------------------------------------------------
    Write-Host '--- add' -ForegroundColor Cyan
    Write-File $WsA "$GL\x\a.txt" @('Line one', 'line two', 'Line three')
    Write-File $WsA "$GL\x\b.txt" @('b line 1')
    Write-File $WsA "$GL\x\sub\c.txt" @('c line 1')
    Write-File $WsA "$GL\x\.tfignore" @('bin')
    Write-File $WsA "$GL\x\bin\ignored.txt" @('ignored by .tfignore')
    Write-Bytes "$XL\lf.txt" ([System.Text.Encoding]::ASCII.GetBytes("lf line 1`nlf line 2`n"))
    Write-Bytes "$XL\blob.bin" ([byte[]](0..255))
    Invoke-Case 'add' 'single-file-run' @('add', "$XL\a.txt", '/noprompt') | Out-Null
    Invoke-Case 'add' 'wildcard' @('add', "$XL\*.txt", '/noprompt') | Out-Null
    Invoke-Case 'add' 'recursive-with-tfignore' @('add', "$XL", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-add-recursive' @('status', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'add' 'ignored-noignore' @('add', "$XL\bin\ignored.txt", '/noignore', '/noprompt') | Out-Null
    Invoke-Case 'add' 'already-pending' @('add', "$XL\a.txt", '/noprompt') | Out-Null

    # --- checkin: comment and work item (the project policies decide the shape) ---
    Write-Host '--- checkin' -ForegroundColor Cyan
    $noComment = Invoke-Case 'checkin' 'no-comment' @('checkin', "$WsA\$GL", '/recursive', '/noprompt')
    if ($noComment.ExitCode -ne 0) {
        $noWorkItem = Invoke-Case 'checkin' 'no-work-item' @('checkin', "$WsA\$GL", '/recursive', "/comment:Goldenx add files ($run)", '/noprompt')
        if ($noWorkItem.ExitCode -ne 0) {
            $withOverride = Invoke-Case 'checkin' 'override-run' @('checkin', "$WsA\$GL", '/recursive', "/comment:Goldenx add files ($run)", "/override:Goldenx policy override ($run)", '/noprompt') -Expect @(0)
            $changesets['add'] = Get-CheckinNumber $withOverride
        }
        else { $changesets['add'] = Get-CheckinNumber $noWorkItem }
    }
    else { $changesets['add'] = Get-CheckinNumber $noComment }
    Invoke-Case 'changeset' 'add-run' @('changeset', "$($changesets['add'])", '/noprompt') | Out-Null
    Invoke-Case 'checkin' 'validate-nothing-pending' @('checkin', "$WsA\$GL", '/recursive', "/comment:Goldenx nothing ($run)", '/validate', '/noprompt') | Out-Null

    # A second version of a.txt for the get cases.
    Invoke-Step @('checkout', "$XL\a.txt", '/noprompt') | Out-Null
    Write-File $WsA "$GL\x\a.txt" @('Line one', 'line two', 'Line three', 'line four')
    $changesets['editA'] = Submit-Run 'Goldenx edit a.txt' "$XL\a.txt"

    # --- checkout, delete, rename, undo ----------------------------------------
    Write-Host '--- checkout, delete, rename, undo' -ForegroundColor Cyan
    Invoke-Case 'checkout' 'file-run' @('checkout', "$XL\a.txt", '/noprompt') | Out-Null
    Invoke-Case 'checkout' 'already-checked-out' @('checkout', "$XL\a.txt", '/noprompt') | Out-Null
    Invoke-Case 'delete' 'with-pending-edit' @('delete', "$XL\a.txt", '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-delete-with-pending-edit' @('status', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'undo' 'single-file' @('undo', "$XL\a.txt", '/noprompt') | Out-Null
    Invoke-Case 'checkout' 'folder-recursive' @('checkout', "$XL", '/recursive', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'delete' 'folder-recursive' @('delete', "$XL\sub", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-delete-folder' @('status', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'delete' 'folder-not-recursive' @('delete', "$XL\sub", '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'rename' 'folder' @('rename', "$XL\sub", "$XL\sub2", '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-rename-folder' @('status', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'rename' 'target-exists' @('rename', "$XL\a.txt", "$XL\b.txt", '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'rename' 'into-folder' @('rename', "$XL\a.txt", "$XL\sub\a.txt", '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-rename-into-folder' @('status', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'rename' 'case-only' @('rename', "$XL\b.txt", "$XL\B.txt", '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'workfold' 'folder-argument' @('workfold', "$XL") | Out-Null
    Invoke-Case 'workfold' 'server-path' @('workfold', $XS) | Out-Null

    # --- difference ------------------------------------------------------------
    Write-Host '--- difference' -ForegroundColor Cyan
    Invoke-Step @('checkout', "$XL\a.txt", '/noprompt') | Out-Null
    Write-File $WsA "$GL\x\a.txt" @('Line  one  ', 'LINE TWO', 'Line three', 'line four')
    Invoke-Case 'difference' 'space-and-case' @('difference', "$XL\a.txt", '/noprompt') | Out-Null
    Invoke-Case 'difference' 'ignorespace' @('difference', "$XL\a.txt", '/ignorespace', '/noprompt') | Out-Null
    Invoke-Case 'difference' 'ignorecase' @('difference', "$XL\a.txt", '/ignorecase', '/noprompt') | Out-Null
    Invoke-Case 'difference' 'ignorespace-ignorecase' @('difference', "$XL\a.txt", '/ignorespace', '/ignorecase', '/noprompt') | Out-Null
    Invoke-Case 'difference' 'brief-local' @('difference', "$XL\a.txt", '/format:brief', '/noprompt') | Out-Null
    Write-Bytes "$XL\a.txt" ([System.Text.Encoding]::ASCII.GetBytes("Line one`r`nline two`r`nLine three`r`nline four"))
    Invoke-Case 'difference' 'no-final-newline' @('difference', "$XL\a.txt", '/noprompt') | Out-Null
    Invoke-Case 'difference' 'no-final-newline-ignoreeol' @('difference', "$XL\a.txt", '/ignoreeol', '/noprompt') | Out-Null
    Invoke-Step @('checkout', "$XL\lf.txt", '/noprompt') | Out-Null
    Write-Bytes "$XL\lf.txt" ([System.Text.Encoding]::ASCII.GetBytes("lf line 1`r`nlf line 2`r`n"))
    Invoke-Case 'difference' 'eol-change' @('difference', "$XL\lf.txt", '/noprompt') | Out-Null
    Invoke-Case 'difference' 'eol-change-ignoreeol' @('difference', "$XL\lf.txt", '/ignoreeol', '/noprompt') | Out-Null
    Invoke-Case 'difference' 'eol-change-ignorespace' @('difference', "$XL\lf.txt", '/ignorespace', '/noprompt') | Out-Null
    Invoke-Step @('checkout', "$XL\blob.bin", '/noprompt') | Out-Null
    Write-Bytes "$XL\blob.bin" ([byte[]](255..0))
    Invoke-Case 'difference' 'binary' @('difference', "$XL\blob.bin", '/noprompt') | Out-Null
    Invoke-Case 'difference' 'folder-recursive' @('difference', "$XL", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'difference' 'folder-recursive-brief' @('difference', "$XL", '/recursive', '/format:brief', '/noprompt') | Out-Null
    Invoke-Case 'difference' 'two-server-folders' @('difference', "$XS;C$($changesets['add'])", "$XS;C$($changesets['editA'])", '/recursive', '/noprompt') | Out-Null
    Undo-Run

    # --- view, dir, properties, branches, merge /candidate ------------------------
    Write-Host '--- view, dir, properties' -ForegroundColor Cyan
    Invoke-Case 'view' 'folder' @('view', $XS, '/noprompt') | Out-Null
    Invoke-Case 'view' 'wildcard' @('view', "$XS/*.txt", '/noprompt') | Out-Null
    Invoke-Case 'view' 'folder-recursive' @('view', $XS, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'view' 'binary-to-output' @('view', "$XS/blob.bin", "/output:$binaryOut", '/noprompt') | Out-Null
    Invoke-Case 'view' 'binary-console' @('view', "$XS/blob.bin", '/console', '/noprompt') | Out-Null
    Invoke-Case 'view' 'lf-file' @('view', "$XS/lf.txt", '/noprompt') | Out-Null
    Invoke-Case 'dir' 'wildcard' @('dir', "$XS/*.txt", '/noprompt') | Out-Null
    Invoke-Case 'dir' 'wildcard-recursive' @('dir', "$XS/*.txt", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'properties' 'folder' @('properties', $XS, '/noprompt') | Out-Null
    Invoke-Case 'properties' 'folder-recursive' @('properties', $XS, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'properties' 'version' @('properties', "$XS/a.txt", "/version:C$($changesets['add'])", '/noprompt') | Out-Null
    Invoke-Case 'branches' 'not-branched' @('branches', "$XS/b.txt", '/noprompt') | Out-Null
    Invoke-Case 'merge' 'candidate-unrelated' @('merge', $XS, "$XS/sub", '/recursive', '/candidate', '/noprompt') | Out-Null

    # --- label, unlabel --------------------------------------------------------------
    Write-Host '--- label' -ForegroundColor Cyan
    Invoke-Case 'label' 'server-path-no-version' @('label', $label, $XS, '/recursive', "/comment:Goldenx label ($run)", '/noprompt') | Out-Null
    Invoke-Case 'labels' 'server-path-version' @('labels', $labelName, '/format:detailed') | Out-Null
    Invoke-Case 'label' 'explicit-version' @('label', $label, "$XS/a.txt", "/version:C$($changesets['add'])", '/child:replace', '/noprompt') | Out-Null
    Invoke-Case 'labels' 'after-explicit-version' @('labels', $labelName, '/format:detailed') | Out-Null
    Invoke-Case 'label' 'local-path-version' @('label', $label, "$XL\b.txt", "/version:C$($changesets['add'])", '/child:replace', '/noprompt') | Out-Null
    Invoke-Case 'labels' 'all-owners' @('labels', '/owner:*') | Out-Null
    Invoke-Case 'labels' 'wildcard' @('labels', 'goldenx*') | Out-Null
    Invoke-Case 'labels' 'owner-run' @('labels', $labelName, '/owner:*', '/format:detailed') | Out-Null
    Invoke-Case 'unlabel' 'folder-recursive' @('unlabel', $label, $XS, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'labels' 'after-unlabel-folder' @('labels', $labelName, '/format:detailed') | Out-Null
    Invoke-Case 'label' 'delete-run' @('label', '/delete', $label, '/noprompt') | Out-Null

    # --- lock ------------------------------------------------------------------------
    Write-Host '--- lock' -ForegroundColor Cyan
    Invoke-Case 'lock' 'none-on-unlocked' @('lock', '/lock:none', "$XL\b.txt") | Out-Null
    Invoke-Case 'lock' 'checkin-run' @('lock', '/lock:checkin', "$XL\b.txt") | Out-Null
    Invoke-Case 'lock' 'already-locked' @('lock', '/lock:checkin', "$XL\b.txt") | Out-Null
    Invoke-Case 'lock' 'change-level' @('lock', '/lock:checkout', "$XL\b.txt") | Out-Null
    Invoke-Case 'checkout' 'own-lock' @('checkout', "$XL\b.txt", '/noprompt') | Out-Null
    Invoke-Case 'status' 'locked-and-checked-out' @('status', "$WsA\$GL", '/recursive', '/format:detailed', '/noprompt') | Out-Null
    Invoke-Case 'lock' 'none-run' @('lock', '/lock:none', "$XL\b.txt") | Out-Null
    Undo-Run

    # --- shelve, unshelve ------------------------------------------------------------
    Write-Host '--- shelve' -ForegroundColor Cyan
    Invoke-Step @('checkout', "$XL\a.txt", '/noprompt') | Out-Null
    Write-File $WsA "$GL\x\a.txt" @('shelved a')
    Invoke-Step @('checkout', "$XL\b.txt", '/noprompt') | Out-Null
    Write-File $WsA "$GL\x\b.txt" @('shelved b')
    Invoke-Case 'shelve' 'no-comment' @('shelve', $shelf, "$XL", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'shelvesets' 'own-detailed' @('shelvesets', $shelf, '/format:detailed') | Out-Null
    Undo-Run
    Invoke-Case 'unshelve' 'single-item' @('unshelve', $shelf, "$XS/a.txt", '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-unshelve-single' @('status', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Undo-Run
    if ($owner) {
        Invoke-Case 'unshelve' 'with-owner' @('unshelve', "$shelf;$owner", '/noprompt') | Out-Null
        Undo-Run
    }
    Invoke-Case 'unshelve' 'local-itemspec' @('unshelve', $shelf, "$XL\b.txt", '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'shelve' 'delete-run' @('shelve', '/delete', $shelf, '/noprompt') | Out-Null

    # --- reconcile -------------------------------------------------------------------
    Write-Host '--- reconcile' -ForegroundColor Cyan
    Write-File $WsA "$GL\x\detected.txt" @('not added through tf')
    Set-ItemProperty -LiteralPath "$XL\b.txt" -Name IsReadOnly -Value $false
    Remove-Item -LiteralPath "$XL\b.txt" -Force
    Invoke-Case 'status' 'candidates-run' @('status', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'reconcile' 'promote-preview-run' @('reconcile', '/promote', "$XL", '/recursive', '/preview', '/noprompt') | Out-Null
    Invoke-Case 'reconcile' 'promote-adds' @('reconcile', '/promote', '/adds', "$XL", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-promote-adds' @('status', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'reconcile' 'promote-deletes' @('reconcile', '/promote', '/deletes', "$XL", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-promote-deletes' @('status', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Case 'reconcile' 'promote-run' @('reconcile', '/promote', "$XL", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-promote-run' @('status', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Step @('get', "$XL\b.txt", '/force', '/noprompt') -Expect @(0, 1) | Out-Null
    if (Test-Path -LiteralPath "$XL\detected.txt") { Remove-Item -LiteralPath "$XL\detected.txt" -Force }

    # --- get: an older version over a modified file -----------------------------------
    Write-Host '--- get' -ForegroundColor Cyan
    Set-ItemProperty -LiteralPath "$XL\a.txt" -Name IsReadOnly -Value $false
    Write-File $WsA "$GL\x\a.txt" @('modified without checkout')
    Invoke-Case 'get' 'older-over-modified' @('get', "$XL\a.txt", "/version:C$($changesets['add'])", '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-get-older-over-modified' @('status', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'resolve' 'list-after-get' @('resolve', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'resolve' 'overwritelocal' @('resolve', "$XL\a.txt", '/auto:OverwriteLocal', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-overwritelocal' @('status', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Undo-Run
    Write-File $WsA "$GL\x\a.txt" @('modified without checkout again')
    Invoke-Case 'get' 'older-over-modified-overwrite' @('get', "$XL\a.txt", "/version:C$($changesets['add'])", '/overwrite', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Step @('get', "$XL", '/recursive', '/force', '/noprompt') -Expect @(0, 1) | Out-Null
    Invoke-Case 'get' 'remap' @('get', "$XL", '/recursive', '/remap', '/noprompt') | Out-Null

    # --- history, properties of a deleted item -----------------------------------------
    Write-Host '--- deleted item' -ForegroundColor Cyan
    Invoke-Step @('delete', "$XL\b.txt", '/noprompt') | Out-Null
    $changesets['deleteB'] = Submit-Run 'Goldenx delete b.txt' "$XL\b.txt"
    $deletedDir = Invoke-Case 'dir' 'deleted-x' @('dir', $XS, '/deleted', '/noprompt')
    $xMatch = [regex]::Match($deletedDir.Stdout, 'b\.txt;X(\d+)')
    if ($xMatch.Success) { $x1 = [int]$xMatch.Groups[1].Value }
    Invoke-Case 'history' 'deleted-item' @('history', "$XS/b.txt", '/noprompt') | Out-Null
    if ($x1 -gt 0) {
        Invoke-Case 'history' 'deleted-item-id' @('history', "$XS/b.txt;X$x1", '/noprompt') | Out-Null
        Invoke-Case 'properties' 'deleted-item-id' @('properties', "$XS/b.txt;X$x1", '/noprompt') | Out-Null
        Invoke-Case 'view' 'deleted-item-id' @('view', "$XS/b.txt;X$x1", '/noprompt') | Out-Null
    }
    Invoke-Case 'properties' 'deleted-item' @('properties', "$XS/b.txt", '/noprompt') | Out-Null
    Invoke-Case 'get' 'deleted-item-old-version' @('get', "$XS/b.txt", "/version:C$($changesets['add'])", '/noprompt') | Out-Null
    Undo-Run

    # --- resolve: merge conflicts ----------------------------------------------------
    Write-Host '--- resolve' -ForegroundColor Cyan
    Invoke-Step @('branch', $XS, $YS, '/noprompt') | Out-Null
    $changesets['branch'] = Submit-Run 'Goldenx branch x to y'
    Invoke-Step @('checkout', "$XL\a.txt", '/noprompt') | Out-Null
    Write-File $WsA "$GL\x\a.txt" @('Line one changed in x', 'line two', 'Line three', 'line four')
    $changesets['editXA'] = Submit-Run 'Goldenx edit a.txt in x' "$XL\a.txt"
    Invoke-Step @('checkout', "$YL\a.txt", '/noprompt') | Out-Null
    Write-File $WsA "$GL\y\a.txt" @('Line one', 'line two', 'Line three', 'line four changed in y')
    $changesets['editYA'] = Submit-Run 'Goldenx edit a.txt in y' "$YL\a.txt"

    Invoke-Step @('merge', $XS, $YS, '/recursive', '/noprompt') -Expect @(0, 1) | Out-Null
    Invoke-Case 'resolve' 'automerge-preview' @('resolve', "$WsA\$GL", '/recursive', '/auto:AutoMerge', '/preview', '/noprompt') | Out-Null
    Invoke-Case 'resolve' 'automerge-nonoverlapping' @('resolve', "$WsA\$GL", '/recursive', '/auto:AutoMerge', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-automerge' @('status', "$WsA\$GL", '/recursive', '/format:detailed', '/noprompt') | Out-Null
    Invoke-Case 'difference' 'after-automerge' @('difference', "$YL\a.txt", '/noprompt') | Out-Null
    Undo-Run
    Invoke-Step @('merge', $XS, $YS, '/recursive', '/noprompt') -Expect @(0, 1) | Out-Null
    Invoke-Case 'resolve' 'automerge-forced' @('resolve', "$WsA\$GL", '/recursive', '/auto:AutoMergeForced', '/noprompt') | Out-Null
    Undo-Run
    Invoke-Step @('merge', $XS, $YS, '/recursive', '/noprompt') -Expect @(0, 1) | Out-Null
    Invoke-Case 'resolve' 'overwritelocal-on-merge' @('resolve', "$WsA\$GL", '/recursive', '/auto:OverwriteLocal', '/noprompt') | Out-Null
    Invoke-Case 'resolve' 'deleteconflict-on-merge' @('resolve', "$WsA\$GL", '/recursive', '/auto:DeleteConflict', '/noprompt') | Out-Null
    Invoke-Case 'resolve' 'keepyours-rename-theirs' @('resolve', "$YL\a.txt", '/auto:KeepYoursRenameTheirs', "/newname:$YL\a-theirs.txt", '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-keepyours-rename-theirs' @('status', "$WsA\$GL", '/recursive', '/format:detailed', '/noprompt') | Out-Null
    Undo-Run
    if (Test-Path -LiteralPath "$YL\a-theirs.txt") { Remove-Item -LiteralPath "$YL\a-theirs.txt" -Force }
    Invoke-Step @('merge', $XS, $YS, '/recursive', '/noprompt') -Expect @(0, 1) | Out-Null
    Invoke-Case 'resolve' 'alias-acceptmerge' @('resolve', "$WsA\$GL", '/recursive', '/auto:AcceptMerge', '/noprompt') | Out-Null
    Undo-Run

    # An overlapping edit: AutoMerge cannot resolve it.
    Invoke-Step @('checkout', "$YL\a.txt", '/noprompt') | Out-Null
    Write-File $WsA "$GL\y\a.txt" @('Line one changed in y', 'line two', 'Line three', 'line four changed in y')
    $changesets['editYA2'] = Submit-Run 'Goldenx edit a.txt in y again' "$YL\a.txt"
    Invoke-Step @('merge', $XS, $YS, '/recursive', '/noprompt') -Expect @(0, 1) | Out-Null
    Invoke-Case 'resolve' 'automerge-overlapping' @('resolve', "$WsA\$GL", '/recursive', '/auto:AutoMerge', '/noprompt') | Out-Null
    Invoke-Case 'resolve' 'list-overlapping' @('resolve', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'resolve' 'keepyours-run' @('resolve', "$WsA\$GL", '/recursive', '/auto:KeepYours', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-keepyours' @('status', "$WsA\$GL", '/recursive', '/format:detailed', '/noprompt') | Out-Null
    Undo-Run
}
catch {
    $failed = $_
}
finally {
    Write-Host ''
    Write-Host 'Cleaning up...' -ForegroundColor Cyan
    Invoke-TfRaw -Arguments @('lock', '/lock:none', "$WsA\$GL", '/recursive') -WorkingDirectory $WsA | Out-Null
    Invoke-TfRaw -Arguments @('undo', "$WsA\$GL", '/recursive', '/noprompt') -WorkingDirectory $WsA | Out-Null
    Invoke-TfRaw -Arguments @('shelve', '/delete', $shelf, '/noprompt') -WorkingDirectory $WsA | Out-Null
    Invoke-TfRaw -Arguments @('label', '/delete', $label, '/noprompt') -WorkingDirectory $WsA | Out-Null
    if (Test-Path -LiteralPath "$WsA\$GL") {
        $delete = Invoke-TfRaw -Arguments @('delete', "$WsA\$GL", '/recursive', '/noprompt') -WorkingDirectory $WsA
        if ($delete.ExitCode -eq 0) {
            $cleanup = Invoke-Case 'checkin' 'delete-folder-x' @('checkin', "$WsA\$GL", '/recursive', "/comment:Goldenx cleanup ($run)", "/override:Goldenx cleanup ($run)", '/noprompt')
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
    if (Test-Path -LiteralPath $binaryOut) { Remove-Item -LiteralPath $binaryOut -Force -ErrorAction SilentlyContinue }

    $indexDoc = [ordered]@{
        capturedAt      = (Get-Date).ToString('o')
        run             = $run
        tfVersion       = $script:TfVersion
        consoleEncoding = $script:TfEncoding.WebName
        culture         = [System.Globalization.CultureInfo]::CurrentUICulture.Name
        extra           = [ordered]@{ changesets = $changesets; goldenFolder = (Protect-Text $GS); label = (Protect-Text $label); shelveset = $shelf; deletionIdOfB = $x1 }
        cases           = $script:Index
    }
    [System.IO.File]::WriteAllText((Join-Path $script:OutRoot "index-extra2-$run.json"), ($indexDoc | ConvertTo-Json -Depth 6), $script:Utf8NoBom)

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
