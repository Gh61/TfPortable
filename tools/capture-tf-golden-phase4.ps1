<#
.SYNOPSIS
    Captures the tf.exe scenarios of the planned phase 4 of TfPortable (administrative and foreign-workspace commands).

.DESCRIPTION
    Complements capture-tf-golden-extra.ps1 and capture-tf-golden-phase3.ps1. The cases:

      property (read and set on a file of the run), permission (allow and remove for the signed-in user on
      the run folder only, then the listing), lock /workspace and undo /workspace:<other> between two
      workspaces, status /workspace, shelve /validate, checkin /shelveset, unshelve /move, destroy /preview,
      destroy /keephistory, destroy of the run folder, history and dir /deleted after a destroy,
      workspace /delete of the second workspace.

    Everything happens under $/<Project>/Main/golden4-<run> and in a second temporary workspace
    (tfp-golden-B4-<run>): no permission of an existing item is touched, nothing outside the run folder is
    destroyed, the second workspace and the shelvesets are deleted at the end. The run folder itself is
    destroyed at the end (that is the destroy capture); when destroy is refused, it is deleted with a checkin.
    Results go to <OutDir>\<command>\<case>.txt and <OutDir>\index-phase4-<run>.json.

    Requirements: the seeded test project, tf.exe signed in, a clean workspace, a work item id for the
    checkins (-WorkItem; the developer created bug #1), and for destroy and permission the project
    administrator rights of the signed-in user (a refusal is captured as a case, not a failure).

.PARAMETER Manifest
    Path to seed-manifest.json written by seed-tfvc-test-repo.ps1.

.PARAMETER OutDir
    Output folder (same as for capture-tf-golden.ps1). Default: .\tests\golden

.PARAMETER TfPath
    Full path to tf.exe. Found automatically via vswhere when omitted.

.PARAMETER WorkItem
    Id of an existing work item of the test project for the checkins (checkin policies). Default: 1

.PARAMETER Redact
    Extra strings to replace with <REDACTED>.

.PARAMETER TimeoutSeconds
    Kill tf.exe when a single call takes longer (e.g. it opened a dialog). Default: 180

.PARAMETER TfOutputEncoding
    Code page used to decode tf.exe output: ansi (default), oem or utf8.

.EXAMPLE
    .\capture-tf-golden-phase4.ps1 -Manifest D:\TfpTest\seed-manifest.json -OutDir D:\TfpGolden
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

function New-TestWorkspace {
    param([string] $Name, [string] $Root, [string] $ServerPath, [string] $LocalPath)
    New-Item -ItemType Directory -Force -Path $LocalPath | Out-Null
    Invoke-Step @('workspace', '/new', $Name, $script:C, '/location:local', '/comment:TfPortable golden capture (phase 4)', '/noprompt') -WorkingDirectory $Root | Out-Null
    $script:CreatedWorkspaces.Add($Name)
    Invoke-Step @('workfold', '/unmap', "/workspace:$Name", $script:C, '$/') -WorkingDirectory $Root -Expect @(0, 1, 100) | Out-Null
    Invoke-Step @('workfold', '/map', "/workspace:$Name", $script:C, $ServerPath, $LocalPath) -WorkingDirectory $Root | Out-Null
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
$G = "golden4-$run"
$GL = "Main\$G"
$GS = "$S/Main/$G"
$shelf = "Golden4-$run"
$shelfMove = "Golden4-$run-move"
$changesets = [ordered]@{}
$script:CreatedWorkspaces = New-Object System.Collections.Generic.List[string]
$script:CreatedFolders = New-Object System.Collections.Generic.List[string]
$rootB = "$($m.localRoot)-B4-$run"
$wsBName = "tfp-golden-B4-$run"
$WsB = Join-Path $rootB $m.project
New-Item -ItemType Directory -Force -Path $rootB | Out-Null
$script:CreatedFolders.Add($rootB)

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
$pairs.Add(@{ From = $WsB; To = '<WORKSPACE_B_ROOT>' })
$pairs.Add(@{ From = $rootB; To = '<LOCAL_ROOT_B>' })
$pairs.Add(@{ From = $wsBName; To = '<WORKSPACE_B>' })
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
$ownerSpec = if ($owner) { $owner } else { $env:USERNAME }
# /override is listed by the captured help of checkin and satisfies a work item policy of the project; /associate is not
# in that help (Microsoft docs only), so the phase 3 script captures it as a case and this script does not rely on it.
$checkinExtra = @("/override:Golden4 capture ($run)")
try {
    # --- Setup: the run folder with three files, checked in; the second workspace ----------------
    Write-Host '--- setup' -ForegroundColor Cyan
    Write-File $WsA "$GL\a.txt" @('first line')
    Write-File $WsA "$GL\b.txt" @('this file will be destroyed with /keephistory')
    Write-File $WsA "$GL\c.txt" @('this file is shelved from workspace B')
    Invoke-Step @('add', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Invoke-Step (@('checkin', "$WsA\$GL", '/recursive', "/comment:Golden4 setup ($run)", '/noprompt') + $checkinExtra) | Out-Null
    $changesets['setup'] = Get-LatestChangeset
    New-TestWorkspace -Name $wsBName -Root $rootB -ServerPath $S -LocalPath $WsB
    Invoke-Step @('get', $WsB, '/recursive', '/noprompt') -WorkingDirectory $WsB -Expect @(0, 1) | Out-Null

    # --- property ------------------------------------------------------------------------------------
    Write-Host '--- property' -ForegroundColor Cyan
    # No help capture exists for property (only the one-line listing of `tf vc help`); the switches come from the decompiled
    # option table (docs/tf-reference/property.md): the write form is /setvalues, not /setproperty (the first capture used that).
    Invoke-Case 'property' 'read' @('property', "$WsA\$GL\a.txt") | Out-Null
    Invoke-Case 'property' 'set' @('property', '/setvalues:tfp-golden=1', "$WsA\$GL\a.txt") | Out-Null
    Invoke-Case 'property' 'read-after-set' @('property', "$WsA\$GL\a.txt") | Out-Null
    Invoke-Case 'status' 'after-property' @('status', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Invoke-Step @('undo', "$WsA\$GL", '/recursive', '/noprompt') -Expect @(0, 1, 100) | Out-Null

    # --- permission (the run folder only) ---------------------------------------------------------------
    Write-Host '--- permission' -ForegroundColor Cyan
    Invoke-Case 'permission' 'folder' @('permission', $GS) | Out-Null
    Invoke-Case 'permission' 'allow-lock' @('permission', '/allow:Lock', "/user:$ownerSpec", $GS) | Out-Null
    Invoke-Case 'permission' 'folder-after-allow' @('permission', $GS) | Out-Null
    Invoke-Case 'permission' 'folder-recursive' @('permission', $GS, '/recursive') | Out-Null
    Invoke-Case 'permission' 'remove' @('permission', '/remove:Lock', "/user:$ownerSpec", $GS) | Out-Null
    Invoke-Case 'permission' 'folder-after-remove' @('permission', $GS) | Out-Null

    # --- locks and undo across workspaces -------------------------------------------------------------
    Write-Host '--- lock /workspace, undo /workspace' -ForegroundColor Cyan
    Invoke-Case 'lock' 'other-workspace' @('lock', '/lock:checkin', "/workspace:$wsBName", "$GS/a.txt") | Out-Null
    Invoke-Case 'status' 'other-workspace' @('status', "/workspace:$wsBName", $GS, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'lock' 'other-workspace-remove' @('lock', '/lock:none', "/workspace:$wsBName", "$GS/a.txt") | Out-Null
    Invoke-Step @('lock', '/lock:none', "$GS/a.txt") -WorkingDirectory $WsB -Expect @(0, 1, 100) | Out-Null

    Invoke-Step @('checkout', "$WsB\$GL\a.txt", '/noprompt') -WorkingDirectory $WsB | Out-Null
    Write-File $WsB "$GL\a.txt" @('first line', 'edited in workspace B')
    Invoke-Case 'status' 'user-all-workspaces' @('status', $GS, '/recursive', '/user:*', '/noprompt') | Out-Null
    Invoke-Case 'undo' 'other-workspace' @('undo', "/workspace:$wsBName;$ownerSpec", "$GS/a.txt", '/noprompt') | Out-Null
    Invoke-Case 'status' 'other-workspace-after-undo' @('status', "/workspace:$wsBName", $GS, '/recursive', '/noprompt') | Out-Null
    Invoke-Step @('undo', "$WsB\$GL", '/recursive', '/noprompt') -WorkingDirectory $WsB -Expect @(0, 1, 100) | Out-Null
    Invoke-Step @('get', "$WsB\$GL", '/recursive', '/overwrite', '/noprompt') -WorkingDirectory $WsB -Expect @(0, 1) | Out-Null

    # --- shelve /validate, checkin /shelveset, unshelve /move ------------------------------------------
    Write-Host '--- shelvesets' -ForegroundColor Cyan
    Invoke-Step @('checkout', "$WsB\$GL\c.txt", '/noprompt') -WorkingDirectory $WsB | Out-Null
    Write-File $WsB "$GL\c.txt" @('this file is shelved from workspace B', 'and checked in from the shelveset')
    Invoke-Case 'shelve' 'validate' @('shelve', $shelf, "$WsB\$GL", '/recursive', '/validate', "/comment:Golden4 shelveset ($run)", '/noprompt') -WorkingDirectory $WsB | Out-Null
    Invoke-Case 'shelve' 'move-from-b' @('shelve', $shelf, "$WsB\$GL", '/recursive', '/move', "/comment:Golden4 shelveset ($run)", '/noprompt') -WorkingDirectory $WsB | Out-Null
    # The shelveset form of checkin lists neither /override nor /associate in its help: with a work item policy on the
    # project this case captures the refusal, which is a result of its own.
    $fromShelveset = Invoke-Case 'checkin' 'shelveset' @('checkin', "/shelveset:$shelf;$ownerSpec", '/noprompt')
    Invoke-Case 'shelvesets' 'after-checkin-shelveset' @('shelvesets', $shelf) | Out-Null
    if ($fromShelveset.ExitCode -eq 0) {
        $changesets['checkin-shelveset'] = Get-LatestChangeset
        Invoke-Case 'changeset' 'from-shelveset' @('changeset', "$($changesets['checkin-shelveset'])", '/noprompt') | Out-Null
    }
    Invoke-Step @('get', "$WsA\$GL", '/recursive', '/noprompt') -Expect @(0, 1) | Out-Null

    Write-File $WsA "$GL\moved.txt" @('new file, shelved with /move and taken back with unshelve /move')
    Invoke-Step @('add', "$WsA\$GL\moved.txt", '/noprompt') | Out-Null
    Invoke-Step @('shelve', $shelfMove, "$WsA\$GL\moved.txt", '/move', "/comment:Golden4 move ($run)", '/noprompt') | Out-Null
    if (Test-Path -LiteralPath "$WsA\$GL\moved.txt") { Remove-Item -LiteralPath "$WsA\$GL\moved.txt" -Force }
    Invoke-Case 'unshelve' 'move' @('unshelve', '/move', $shelfMove, '/noprompt') | Out-Null
    Invoke-Case 'shelvesets' 'after-unshelve-move' @('shelvesets', $shelfMove) | Out-Null
    Invoke-Case 'status' 'after-unshelve-move' @('status', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Invoke-Step @('undo', "$WsA\$GL", '/recursive', '/noprompt') -Expect @(0, 1, 100) | Out-Null
    if (Test-Path -LiteralPath "$WsA\$GL\moved.txt") { Remove-Item -LiteralPath "$WsA\$GL\moved.txt" -Force }

    # --- destroy (the run folder only) -------------------------------------------------------------------
    Write-Host '--- destroy' -ForegroundColor Cyan
    Invoke-Case 'destroy' 'preview-file' @('destroy', '/preview', "$GS/b.txt", '/noprompt') | Out-Null
    Invoke-Case 'history' 'before-destroy' @('history', "$GS/b.txt", '/noprompt') | Out-Null
    Invoke-Case 'destroy' 'keephistory' @('destroy', '/keephistory', "$GS/b.txt", '/noprompt') | Out-Null
    Invoke-Case 'history' 'after-destroy-keephistory' @('history', "$GS/b.txt", '/noprompt') | Out-Null
    Invoke-Case 'dir' 'deleted-after-destroy' @('dir', $GS, '/deleted') | Out-Null
    Invoke-Case 'get' 'after-destroy' @('get', "$WsA\$GL", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'destroy' 'not-found' @('destroy', "$GS/no-such-$run.txt", '/noprompt') | Out-Null
    Invoke-Case 'destroy' 'folder' @('destroy', $GS, '/noprompt') | Out-Null
    $destroyed = ($script:Index | Where-Object { $_.group -eq 'destroy' -and $_.name -eq 'folder' }).exitCode -eq 0
    Invoke-Case 'history' 'after-destroy-folder' @('history', $GS, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'dir' 'after-destroy-folder' @('dir', "$S/Main", '/deleted') | Out-Null
    if ($destroyed) {
        Invoke-Case 'get' 'after-destroy-folder' @('get', "$WsA\Main", '/recursive', '/noprompt') | Out-Null
    }

    # --- workspace /delete --------------------------------------------------------------------------------
    Write-Host '--- workspace' -ForegroundColor Cyan
    Invoke-Case 'workspaces' 'two-of-owner' @('workspaces', $C, "/owner:$ownerSpec") | Out-Null
    Invoke-Case 'workspace' 'delete-local-4' @('workspace', '/delete', $wsBName, $C, '/noprompt') -WorkingDirectory $rootB | Out-Null
    $script:CreatedWorkspaces.Remove($wsBName) | Out-Null
}
catch {
    $failed = $_
}
finally {
    Write-Host ''
    Write-Host 'Cleaning up...' -ForegroundColor Cyan
    foreach ($ws in @($WsA, $WsB)) {
        if (Test-Path -LiteralPath $ws) {
            Invoke-TfRaw -Arguments @('lock', '/lock:none', "$ws\$GL", '/recursive') -WorkingDirectory $ws | Out-Null
            Invoke-TfRaw -Arguments @('undo', "$ws\$GL", '/recursive', '/noprompt') -WorkingDirectory $ws | Out-Null
        }
    }
    foreach ($name in @($shelf, $shelfMove)) {
        Invoke-TfRaw -Arguments @('shelve', '/delete', "$name;$ownerSpec") -WorkingDirectory $WsA | Out-Null
    }
    Invoke-TfRaw -Arguments @('permission', '/remove:*', "/user:$ownerSpec", $GS) -WorkingDirectory $WsA | Out-Null
    # The run folder should be destroyed by the capture; when destroy was refused, delete it with a checkin.
    $still = Invoke-TfRaw -Arguments @('dir', $GS, '/folders') -WorkingDirectory $WsA
    if ($still.ExitCode -eq 0) {
        $delete = Invoke-TfRaw -Arguments @('delete', "$WsA\$GL", '/recursive', '/noprompt') -WorkingDirectory $WsA
        if ($delete.ExitCode -eq 0) {
            Invoke-Case 'checkin' 'delete-folder-4' (@('checkin', "$WsA\$GL", '/recursive', "/comment:Golden4 cleanup ($run)", '/noprompt') + $checkinExtra) | Out-Null
            $changesets['delete-folder'] = Get-LatestChangeset
        }
        else {
            Invoke-TfRaw -Arguments @('undo', "$WsA\$GL", '/recursive', '/noprompt') -WorkingDirectory $WsA | Out-Null
        }
    }
    foreach ($name in @($script:CreatedWorkspaces)) {
        Invoke-TfRaw -Arguments @('workspace', '/delete', $name, $C, '/noprompt') -WorkingDirectory $WsA | Out-Null
    }
    foreach ($folder in @($script:CreatedFolders)) {
        if (Test-Path -LiteralPath $folder) {
            Get-ChildItem -LiteralPath $folder -Recurse -Force -File | ForEach-Object { $_.IsReadOnly = $false }
            Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue
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
        extra           = [ordered]@{ changesets = $changesets; shelveset = $shelf; goldenFolder = (Protect-Text $GS); workItem = $WorkItem; secondWorkspace = '<WORKSPACE_B>' }
        cases           = $script:Index
    }
    [System.IO.File]::WriteAllText((Join-Path $script:OutRoot "index-phase4-$run.json"), ($indexDoc | ConvertTo-Json -Depth 6), $script:Utf8NoBom)

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
