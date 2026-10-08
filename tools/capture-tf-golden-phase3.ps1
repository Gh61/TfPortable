<#
.SYNOPSIS
    Captures the tf.exe scenarios of phase 3 that the earlier runs did not cover.

.DESCRIPTION
    Complements capture-tf-golden-extra.ps1 for the commands implemented in phase 3 of TfPortable
    (checkin, shelve, shelvesets, unshelve, label, unlabel, lock). The cases:

      checkin /validate, checkin /associate:<work item>, checkin with a comment file,
      checkin of an unchanged edit, shelve /replace, shelve /move, shelve of an existing name,
      shelve /delete of a missing shelveset, unshelve with a local change in the way,
      unshelve /nomerge, shelvesets /owner:*, label without a scope, label /child:replace,
      label /delete, label /delete of a missing label, lock of an unknown item.

    Every run uses a run id (yyMMddHHmm) in names of files, labels and shelvesets, so it can be
    run again. New files live under $/<Project>/Main/golden3-<run>; the folder is deleted with a
    final checkin. Results go to <OutDir>\<command>\<case>.txt and <OutDir>\index-phase3-<run>.json.

    Requirements: the seeded test project (seed-tfvc-test-repo.ps1), tf.exe signed in, a clean
    workspace, and a work item with the id given by -WorkItem (the developer created bug #1).

.PARAMETER Manifest
    Path to seed-manifest.json written by seed-tfvc-test-repo.ps1.

.PARAMETER OutDir
    Output folder (same as for capture-tf-golden.ps1). Default: .\tests\golden

.PARAMETER TfPath
    Full path to tf.exe. Found automatically via vswhere when omitted.

.PARAMETER WorkItem
    Id of an existing work item of the test project for checkin /associate. Default: 1

.PARAMETER Redact
    Extra strings to replace with <REDACTED>.

.PARAMETER TimeoutSeconds
    Kill tf.exe when a single call takes longer (e.g. it opened a dialog). Default: 180

.PARAMETER TfOutputEncoding
    Code page used to decode tf.exe output: ansi (default), oem or utf8.

.EXAMPLE
    .\capture-tf-golden-phase3.ps1 -Manifest D:\TfpTest\seed-manifest.json -OutDir D:\TfpGolden
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Manifest,
    [string] $OutDir = '.\tests\golden',
    [string] $TfPath,
    [int] $WorkItem = 1,
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
$G = "golden3-$run"
$GL = "Main\$G"
$GS = "$S/Main/$G"
$labelName = "golden3-$run"
$label = "$labelName@$S"
$shelf = "Golden3-$run"
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
$commentFile = Join-Path $env:TEMP "tfp-golden3-comment-$run.txt"
try {
    # --- checkin -------------------------------------------------------------
    Write-Host '--- checkin' -ForegroundColor Cyan
    Write-File $WsA "$GL\a.txt" @('first line')
    Write-File $WsA "$GL\b.txt" @('second file')
    Invoke-Step @('add', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null

    Invoke-Case 'checkin' 'validate' @('checkin', "$WsA\$GL", '/recursive', "/comment:Golden3 validate ($run)", '/validate', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-validate' @('status', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null

    Invoke-Case 'checkin' 'associate' @('checkin', "$WsA\$GL", '/recursive', "/comment:Golden3 add files ($run)", "/associate:$WorkItem", '/noprompt') -Expect @(0) | Out-Null
    $changesets['associate'] = Get-LatestChangeset
    Invoke-Case 'changeset' 'with-work-item' @('changeset', "$($changesets['associate'])", '/noprompt') | Out-Null
    Invoke-Case 'history' 'by-work-item' @('history', $GS, '/recursive', "/workitem:$WorkItem", '/noprompt') | Out-Null

    [System.IO.File]::WriteAllText($commentFile, "Golden3 comment from a file ($run)`r`nsecond line of the comment`r`n", $script:Utf8NoBom)
    Invoke-Step @('checkout', "$WsA\$GL\a.txt", '/noprompt') | Out-Null
    Write-File $WsA "$GL\a.txt" @('first line', 'edited line')
    Invoke-Case 'checkin' 'comment-file' @('checkin', "$WsA\$GL\a.txt", "/comment:@$commentFile", '/noprompt') -Expect @(0) | Out-Null
    $changesets['comment-file'] = Get-LatestChangeset
    Invoke-Case 'changeset' 'comment-file' @('changeset', "$($changesets['comment-file'])", '/noprompt') | Out-Null

    # An edit whose content did not change.
    Invoke-Step @('checkout', "$WsA\$GL\b.txt", '/noprompt') | Out-Null
    Invoke-Case 'checkin' 'unchanged-edit' @('checkin', "$WsA\$GL\b.txt", "/comment:Golden3 unchanged edit ($run)", '/noprompt') | Out-Null
    Invoke-Step @('undo', "$WsA\$GL\b.txt", '/noprompt') -Expect @(0, 1, 100) | Out-Null
    Invoke-Case 'checkin' 'unchanged-edit-force' @('checkin', "$WsA\$GL\b.txt", "/comment:Golden3 unchanged edit ($run)", '/force', '/noprompt') | Out-Null
    Invoke-Step @('undo', "$WsA\$GL\b.txt", '/noprompt') -Expect @(0, 1, 100) | Out-Null

    # --- shelve ------------------------------------------------------------------
    Write-Host '--- shelve' -ForegroundColor Cyan
    Write-File $WsA "$GL\shelved.txt" @('new file in a shelveset')
    Invoke-Step @('add', "$WsA\$GL\shelved.txt", '/noprompt') | Out-Null
    Invoke-Case 'shelve' 'create-3' @('shelve', $shelf, "$WsA\$GL", '/recursive', "/comment:Golden3 shelveset ($run)", '/noprompt') -Expect @(0) | Out-Null
    Invoke-Case 'shelve' 'exists' @('shelve', $shelf, "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'shelve' 'replace' @('shelve', $shelf, "$WsA\$GL", '/recursive', '/replace', "/comment:Golden3 replaced ($run)", '/noprompt') | Out-Null
    Invoke-Case 'shelvesets' 'all-owners' @('shelvesets', '/owner:*') | Out-Null
    Invoke-Case 'shelve' 'move' @('shelve', $shelf, "$WsA\$GL", '/recursive', '/replace', '/move', "/comment:Golden3 moved ($run)", '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-shelve-move' @('status', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null

    # A local change in the way of the unshelve.
    Write-File $WsA "$GL\shelved.txt" @('local file with the same name, not added')
    Invoke-Case 'unshelve' 'local-file-in-the-way' @('unshelve', $shelf, '/noprompt') | Out-Null
    Invoke-Step @('undo', "$WsA\$GL", '/recursive', '/noprompt') -Expect @(0, 1, 100) | Out-Null
    if (Test-Path -LiteralPath "$WsA\$GL\shelved.txt") { Remove-Item -LiteralPath "$WsA\$GL\shelved.txt" -Force }
    Invoke-Case 'unshelve' 'nomerge' @('unshelve', $shelf, '/nomerge', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-unshelve-3' @('status', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Invoke-Step @('undo', "$WsA\$GL", '/recursive', '/noprompt') -Expect @(0, 1, 100) | Out-Null
    if (Test-Path -LiteralPath "$WsA\$GL\shelved.txt") { Remove-Item -LiteralPath "$WsA\$GL\shelved.txt" -Force }
    Invoke-Case 'shelve' 'delete-3' @('shelve', '/delete', $shelf, '/noprompt') | Out-Null
    Invoke-Case 'shelve' 'delete-missing' @('shelve', '/delete', $shelf, '/noprompt') | Out-Null
    Invoke-Case 'shelvesets' 'after-delete' @('shelvesets', $shelf) | Out-Null

    # --- label -------------------------------------------------------------------
    Write-Host '--- label' -ForegroundColor Cyan
    Invoke-Case 'label' 'no-scope' @('label', $labelName, "$WsA\$GL", '/recursive', "/comment:Golden3 label ($run)", '/noprompt') | Out-Null
    Invoke-Case 'labels' 'no-scope' @('labels', $labelName, '/format:detailed') | Out-Null
    Invoke-Case 'label' 'child-replace' @('label', $label, "$WsA\$GL\a.txt", '/child:replace', '/noprompt') | Out-Null
    Invoke-Case 'label' 'child-merge' @('label', $label, "$WsA\$GL\b.txt", '/child:merge', '/noprompt') | Out-Null
    Invoke-Case 'label' 'existing-item-default-child' @('label', $label, "$WsA\$GL\b.txt", '/noprompt') | Out-Null
    Invoke-Case 'labels' 'after-child' @('labels', $labelName, '/format:detailed') | Out-Null
    Invoke-Case 'label' 'delete' @('label', '/delete', $label, '/noprompt') | Out-Null
    Invoke-Case 'label' 'delete-missing' @('label', '/delete', $label, '/noprompt') | Out-Null
    Invoke-Case 'labels' 'after-delete' @('labels', $labelName) | Out-Null

    # --- lock --------------------------------------------------------------------
    Write-Host '--- lock' -ForegroundColor Cyan
    Invoke-Case 'lock' 'unknown-item' @('lock', '/lock:checkin', "$WsA\$GL\no-such-$run.txt") | Out-Null
    Invoke-Case 'lock' 'no-level' @('lock', "$WsA\$GL\a.txt") | Out-Null
    Invoke-Case 'lock' 'folder-recursive' @('lock', '/lock:checkout', "$WsA\$GL", '/recursive') | Out-Null
    Invoke-Case 'status' 'locked-folder' @('status', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'lock' 'folder-remove' @('lock', '/lock:none', "$WsA\$GL", '/recursive') | Out-Null
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
            Invoke-Case 'checkin' 'delete-folder' @('checkin', "$WsA\$GL", '/recursive', "/comment:Golden3 cleanup ($run)", '/noprompt') | Out-Null
            $changesets['delete-folder'] = Get-LatestChangeset
        }
        else {
            Invoke-TfRaw -Arguments @('undo', "$WsA\$GL", '/recursive', '/noprompt') -WorkingDirectory $WsA | Out-Null
        }
    }
    if (Test-Path -LiteralPath "$WsA\$GL") {
        Get-ChildItem -LiteralPath "$WsA\$GL" -Recurse -Force -File | ForEach-Object { $_.IsReadOnly = $false }
        Remove-Item -LiteralPath "$WsA\$GL" -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $commentFile) { Remove-Item -LiteralPath $commentFile -Force -ErrorAction SilentlyContinue }

    $indexDoc = [ordered]@{
        capturedAt      = (Get-Date).ToString('o')
        run             = $run
        tfVersion       = $script:TfVersion
        consoleEncoding = $script:TfEncoding.WebName
        culture         = [System.Globalization.CultureInfo]::CurrentUICulture.Name
        extra           = [ordered]@{ changesets = $changesets; label = (Protect-Text $label); shelveset = $shelf; goldenFolder = (Protect-Text $GS); workItem = $WorkItem }
        cases           = $script:Index
    }
    [System.IO.File]::WriteAllText((Join-Path $script:OutRoot "index-phase3-$run.json"), ($indexDoc | ConvertTo-Json -Depth 6), $script:Utf8NoBom)

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
