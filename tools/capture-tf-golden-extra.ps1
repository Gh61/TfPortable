<#
.SYNOPSIS
    Captures the remaining tf.exe scenarios (writes to the server) as golden files for TfPortable.

.DESCRIPTION
    Complements capture-tf-golden.ps1. Runs against the test project created by
    seed-tfvc-test-repo.ps1 and captures everything that changes the server or needs
    more than one workspace:

      checkin (add, edit, delete, rename, branch, merge, failures), changeset comment
      edit, label/unlabel, undelete, rollback, locks seen from a second workspace,
      get and checkin conflicts, resolve, get variants, reconcile, shelve/unshelve,
      branch and merge (with conflict), folderdiff, workfold map/unmap/cloak/decloak,
      a temporary server workspace, misc read-only commands, and a long history
      (optional padding changesets).

    Every run uses a run id (yyMMddHHmm) in names of files, labels, shelvesets and
    branches, so it can be run again. All new files live under $/<Project>/Main/golden-<run>.
    The second and the server workspace are deleted at the end.

    Results go to <OutDir>\<command>\<case>.txt and <OutDir>\index-extra-<run>.json; changeset
    numbers to extra-manifest-<run>.json next to the seed manifest.

.PARAMETER Manifest
    Path to seed-manifest.json written by seed-tfvc-test-repo.ps1.

.PARAMETER OutDir
    Output folder (same as for capture-tf-golden.ps1). Default: .\tests\golden

.PARAMETER TfPath
    Full path to tf.exe. Found automatically via vswhere when omitted.

.PARAMETER HistoryPadding
    Number of one-line changesets created at the end for long-history cases. 0 skips it.
    Default: 300 (takes several minutes).

.PARAMETER Redact
    Extra strings to replace with <REDACTED>.

.PARAMETER TimeoutSeconds
    Kill tf.exe when a single call takes longer (e.g. it opened a dialog). Default: 180

.PARAMETER TfOutputEncoding
    Code page used to decode tf.exe output: ansi (default), oem or utf8.

.PARAMETER StartAt
    Section to start with, to continue after a failed run: checkin (default, everything),
    mergeconflict, folderdiff, workfold, server, misc, padding. Sections after the start
    run in order.

.EXAMPLE
    .\capture-tf-golden-extra.ps1 -Manifest D:\TfpTest\seed-manifest.json -OutDir D:\TfpGolden

.EXAMPLE
    .\capture-tf-golden-extra.ps1 -Manifest D:\TfpTest\seed-manifest.json -OutDir D:\TfpGolden -StartAt mergeconflict
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Manifest,
    [string] $OutDir = '.\tests\golden',
    [string] $TfPath,
    [int] $HistoryPadding = 300,
    [string[]] $Redact = @(),
    [int] $TimeoutSeconds = 180,
    [ValidateSet('ansi', 'oem', 'utf8')] [string] $TfOutputEncoding = 'ansi',
    [ValidateSet('checkin', 'mergeconflict', 'folderdiff', 'workfold', 'server', 'misc', 'padding')] [string] $StartAt = 'checkin'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -eq 'Core') {
    [System.Text.Encoding]::RegisterProvider([System.Text.CodePagesEncodingProvider]::Instance)
}

# ---------------------------------------------------------------------------
# Helpers
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

function Remove-LocalFile {
    param([string] $Base, [string] $RelativePath)
    $path = Join-Path $Base $RelativePath
    if (Test-Path -LiteralPath $path) {
        Set-ItemProperty -LiteralPath $path -Name IsReadOnly -Value $false
        Remove-Item -LiteralPath $path -Force
    }
}

function Get-LatestChangeset {
    $history = Invoke-TfRaw -Arguments @('history', $script:S, '/recursive', '/stopafter:1', '/format:brief', '/noprompt') -WorkingDirectory $script:WsA
    $match = [regex]::Match($history.Stdout, '(?m)^\s*(\d+)\s')
    if (-not $match.Success) { throw "Could not read the latest changeset number.`n$($history.Stdout)$($history.Stderr)" }
    return [int]$match.Groups[1].Value
}

function Invoke-CheckinCase {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string] $Comment,
        [string] $WorkingDirectory = $script:WsA
    )
    Invoke-Case 'checkin' $Name @('checkin', $WorkingDirectory, '/recursive', "/comment:$Comment", '/noprompt') -WorkingDirectory $WorkingDirectory -Expect @(0) | Out-Null
    $number = Get-LatestChangeset
    $script:Extra.changesets[$Name] = $number
    Write-Host "    -> C$number" -ForegroundColor Green
    return $number
}

function New-TestWorkspace {
    param([string] $Name, [string] $Root, [string] $Location, [string] $ServerPath, [string] $LocalPath, [string] $CaseName)
    New-Item -ItemType Directory -Force -Path $LocalPath | Out-Null
    $arguments = @('workspace', '/new', $Name, $script:C, "/location:$Location", '/comment:TfPortable golden capture', '/noprompt')
    if ($CaseName) {
        Invoke-Case 'workspace' $CaseName $arguments -WorkingDirectory $Root -Expect @(0) | Out-Null
    }
    else {
        Invoke-Step $arguments -WorkingDirectory $Root | Out-Null
    }
    $script:CreatedWorkspaces.Add($Name)
    Invoke-Step @('workfold', '/unmap', "/workspace:$Name", $script:C, '$/') -WorkingDirectory $Root -Expect @(0, 1, 100) | Out-Null
    Invoke-Step @('workfold', '/map', "/workspace:$Name", $script:C, $ServerPath, $LocalPath) -WorkingDirectory $Root | Out-Null
}

function Test-Section {
    param([string] $Name)
    $order = @('checkin', 'mergeconflict', 'folderdiff', 'workfold', 'server', 'misc', 'padding')
    return [array]::IndexOf($order, $Name) -ge [array]::IndexOf($order, $StartAt)
}

function Initialize-WorkspaceB {
    param([string] $CaseName)
    if ($script:CreatedWorkspaces.Contains($script:WsBName)) { return }
    New-TestWorkspace -Name $script:WsBName -Root $script:RootB -Location 'local' -ServerPath $script:S -LocalPath $script:WsB -CaseName $CaseName
    Invoke-Step @('get', $script:WsB, '/recursive', '/noprompt') -WorkingDirectory $script:WsB -Expect @(0, 1) | Out-Null
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
$script:CreatedWorkspaces = New-Object System.Collections.Generic.List[string]
$script:CreatedFolders = New-Object System.Collections.Generic.List[string]

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
$G = "golden-$run"
$GL = "Main\$G"
$GS = "$S/Main/$G"
$label = "golden-$run@$S"
$shelf = "Golden-$run"
$feature = "$S/Feature-$run"

$rootB = "$($m.localRoot)-B-$run"
$wsBName = "tfp-golden-B-$run"
$WsB = Join-Path $rootB $m.project
$script:RootB = $rootB
$script:WsBName = $wsBName
$script:WsB = $WsB
$rootS = "$($m.localRoot)-S-$run"
$wsSName = "tfp-golden-S-$run"
$WsS = Join-Path $rootS $m.project
foreach ($folder in @($rootB, $rootS)) {
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $script:CreatedFolders.Add($folder)
}

$script:OutRoot = [System.IO.Path]::GetFullPath($OutDir)
if (-not (Test-Path -LiteralPath $script:OutRoot)) { New-Item -ItemType Directory -Path $script:OutRoot | Out-Null }

$script:Extra = [ordered]@{
    run           = $run
    goldenFolder  = $GS
    label         = $label
    shelveset     = $shelf
    featureBranch = $feature
    changesets    = [ordered]@{}
}

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
$pairs.Add(@{ From = $WsS; To = '<WORKSPACE_S_ROOT>' })
$pairs.Add(@{ From = $rootS; To = '<LOCAL_ROOT_S>' })
$pairs.Add(@{ From = $wsBName; To = '<WORKSPACE_B>' })
$pairs.Add(@{ From = $wsSName; To = '<WORKSPACE_S>' })
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
try {
  if ($StartAt -eq 'checkin') {
    # --- Checkins ----------------------------------------------------------
    Write-Host '--- checkin' -ForegroundColor Cyan
    Invoke-Case 'checkin' 'nothing-pending' @('checkin', $WsA, '/recursive', '/comment:Nothing to check in', '/noprompt') | Out-Null

    Write-File $WsA "$GL\a.txt" @('first line')
    Write-File $WsA "$GL\b.txt" @('this file will be deleted')
    Write-File $WsA "$GL\c.txt" @('this file will be locked and conflicted')
    Invoke-Step @('add', $GL, '/recursive', '/noprompt') -Expect @(0, 1) | Out-Null
    $csAdd = Invoke-CheckinCase 'add-files' "Golden: add files ($run)"

    Invoke-Step @('checkout', "$GL\a.txt") | Out-Null
    Write-File $WsA "$GL\a.txt" @('first line', 'second line')
    $csEdit = Invoke-CheckinCase 'edit-file' "Golden: edit a.txt ($run)"

    Invoke-Step @('delete', "$GL\b.txt") | Out-Null
    $csDelete = Invoke-CheckinCase 'delete-file' "Golden: delete b.txt ($run)"

    Invoke-Step @('rename', "$GL\a.txt", "$GL\a-renamed.txt") | Out-Null
    $csRename = Invoke-CheckinCase 'rename-file' "Golden: rename a.txt ($run)"

    Invoke-Case 'changeset' 'edit-comment' @('changeset', "/comment:Golden: edited comment ($run)", "$csAdd", '/noprompt') | Out-Null
    Invoke-Case 'changeset' 'after-comment-edit' @('changeset', "$csAdd", '/noprompt') | Out-Null

    # --- Labels ------------------------------------------------------------
    Write-Host '--- label' -ForegroundColor Cyan
    Invoke-Case 'label' 'create' @('label', $label, $GS, '/recursive', "/comment:Golden label ($run)", '/noprompt') -Expect @(0) | Out-Null
    Invoke-Case 'labels' 'golden-detailed' @('labels', '/format:detailed', $label) | Out-Null
    Invoke-Case 'view' 'golden-label' @('view', "$GS/a-renamed.txt", "/version:L$label", '/noprompt') | Out-Null
    Invoke-Case 'unlabel' 'item' @('unlabel', $label, "$GS/c.txt") | Out-Null
    Invoke-Case 'unlabel' 'not-labeled' @('unlabel', $label, "$GS/c.txt") | Out-Null
    Invoke-Case 'label' 'not-found-item' @('label', $label, "$GS/no-such.txt", '/noprompt') | Out-Null

    # --- Undelete ----------------------------------------------------------
    Write-Host '--- undelete' -ForegroundColor Cyan
    $deleted = Invoke-Case 'dir' 'golden-deleted' @('dir', $GS, '/deleted')
    $deletion = [regex]::Match($deleted.Stdout, 'b\.txt;X(\d+)')
    if ($deletion.Success) {
        Invoke-Case 'undelete' 'file' @('undelete', "$GS/b.txt;X$($deletion.Groups[1].Value)", '/noprompt') | Out-Null
        Invoke-Case 'status' 'pending-undelete' @('status', '/recursive', '/noprompt') | Out-Null
        Invoke-Step @('undo', $GL, '/recursive', '/noprompt') -Expect @(0, 1) | Out-Null
        Remove-LocalFile $WsA "$GL\b.txt"
    }
    else {
        Write-Host '  (skipped: deletion id of b.txt not found in dir /deleted output)' -ForegroundColor Yellow
    }
    Invoke-Case 'undelete' 'not-found' @('undelete', "$GS/no-such.txt", '/noprompt') | Out-Null

    # --- Rollback ----------------------------------------------------------
    Write-Host '--- rollback' -ForegroundColor Cyan
    Invoke-Case 'rollback' 'changeset' @('rollback', "/changeset:C$csEdit", '/noprompt') | Out-Null
    Invoke-Case 'status' 'pending-rollback' @('status', '/recursive', '/noprompt') | Out-Null
    Invoke-Step @('undo', $WsA, '/recursive', '/noprompt') -Expect @(0, 1) | Out-Null
    Invoke-Case 'rollback' 'to-version' @('rollback', "/toversion:C$csAdd", $GL, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'status' 'pending-rollback-to-version' @('status', '/recursive', '/noprompt') | Out-Null
    Invoke-Step @('undo', $WsA, '/recursive', '/noprompt') -Expect @(0, 1) | Out-Null
    Invoke-Step @('get', $WsA, '/recursive', '/noprompt') -Expect @(0, 1) | Out-Null

    # --- Second workspace: locks -------------------------------------------
    Write-Host '--- second workspace, locks' -ForegroundColor Cyan
    Initialize-WorkspaceB -CaseName 'new-local'

    Invoke-Case 'lock' 'checkin-lock' @('lock', '/lock:checkin', "$GL\c.txt") | Out-Null
    Invoke-Case 'properties' 'locked-file' @('properties', "$GL\c.txt") | Out-Null
    Invoke-Case 'status' 'all-users' @('status', $GS, '/recursive', '/user:*', '/noprompt') | Out-Null
    Invoke-Case 'checkout' 'locked-by-other' @('checkout', "$GL\c.txt") -WorkingDirectory $WsB | Out-Null
    Write-File $WsB "$GL\c.txt" @('changed in workspace B while locked')
    Invoke-Case 'checkin' 'blocked-by-lock' @('checkin', $WsB, '/recursive', '/comment:Should fail because of the lock', '/noprompt') -WorkingDirectory $WsB | Out-Null
    Invoke-Step @('undo', $WsB, '/recursive', '/noprompt') -WorkingDirectory $WsB -Expect @(0, 1) | Out-Null
    Invoke-Case 'lock' 'remove' @('lock', '/lock:none', "$GL\c.txt") | Out-Null
    Invoke-Case 'lock' 'checkout-lock' @('lock', '/lock:checkout', "$GL\c.txt") | Out-Null
    Invoke-Step @('lock', '/lock:none', "$GL\c.txt") -Expect @(0, 1, 100) | Out-Null
    Invoke-Step @('undo', $WsA, '/recursive', '/noprompt') -Expect @(0, 1) | Out-Null

    # --- Conflicts ---------------------------------------------------------
    Write-Host '--- conflicts' -ForegroundColor Cyan
    Invoke-Step @('checkout', "$GL\c.txt") -WorkingDirectory $WsB | Out-Null
    Write-File $WsB "$GL\c.txt" @('changed in workspace B')
    Invoke-CheckinCase 'from-second-workspace' "Golden: change c.txt in workspace B ($run)" -WorkingDirectory $WsB | Out-Null

    Invoke-Step @('checkout', "$GL\c.txt") | Out-Null
    Write-File $WsA "$GL\c.txt" @('changed in workspace A')
    Invoke-Case 'get' 'conflict' @('get', $WsA, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'status' 'with-conflict' @('status', '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'resolve' 'list' @('resolve', $WsA, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'resolve' 'list-preview' @('resolve', $WsA, '/recursive', '/preview', '/noprompt') | Out-Null
    Invoke-Case 'resolve' 'accept-theirs-preview' @('resolve', "$GL\c.txt", '/auto:AcceptTheirs', '/preview', '/noprompt') | Out-Null
    Invoke-Case 'resolve' 'accept-yours' @('resolve', "$GL\c.txt", '/auto:AcceptYours', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-resolve' @('status', '/recursive', '/noprompt') | Out-Null

    Invoke-Step @('get', $WsB, '/recursive', '/noprompt') -WorkingDirectory $WsB -Expect @(0, 1) | Out-Null
    Invoke-Step @('checkout', "$GL\c.txt") -WorkingDirectory $WsB | Out-Null
    Write-File $WsB "$GL\c.txt" @('changed in workspace B again')
    Invoke-CheckinCase 'from-second-workspace-2' "Golden: change c.txt in workspace B again ($run)" -WorkingDirectory $WsB | Out-Null
    Invoke-Case 'checkin' 'out-of-date' @('checkin', "$GL\c.txt", '/comment:Should conflict', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-checkin-conflict' @('status', '/recursive', '/noprompt') | Out-Null
    Invoke-Step @('undo', $WsA, '/recursive', '/noprompt') -Expect @(0, 1) | Out-Null
    Invoke-Step @('get', $WsA, '/recursive', '/noprompt') -Expect @(0, 1) | Out-Null

    # --- Get variants ------------------------------------------------------
    Write-Host '--- get' -ForegroundColor Cyan
    Invoke-Case 'get' 'specific-version' @('get', "$GL\c.txt", "/version:C$csAdd", '/noprompt') | Out-Null
    Invoke-Case 'localversions' 'after-old-get' @('localversions', $GL, '/recursive') | Out-Null
    Invoke-Case 'get' 'back-to-latest' @('get', "$GL\c.txt", '/noprompt') | Out-Null
    Invoke-Case 'get' 'force' @('get', $GL, '/recursive', '/force', '/noprompt') | Out-Null
    Invoke-Case 'get' 'all' @('get', $GL, '/recursive', '/all', '/noprompt') | Out-Null
    Invoke-Case 'get' 'date-version-preview' @('get', $GL, '/recursive', "/version:D$((Get-Date).AddYears(-1).ToString('yyyy-MM-dd'))", '/preview', '/noprompt') | Out-Null

    # --- Reconcile ---------------------------------------------------------
    Write-Host '--- reconcile' -ForegroundColor Cyan
    Write-File $WsA "$GL\untracked.txt" @('not under version control')
    Remove-LocalFile $WsA "$GL\c.txt"
    Invoke-Case 'status' 'candidates' @('status', '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'reconcile' 'promote-preview' @('reconcile', '/promote', $GL, '/recursive', '/preview', '/noprompt') | Out-Null
    Invoke-Case 'reconcile' 'promote' @('reconcile', '/promote', $GL, '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-promote' @('status', '/recursive', '/noprompt') | Out-Null
    Invoke-Step @('undo', $WsA, '/recursive', '/noprompt') -Expect @(0, 1) | Out-Null
    Invoke-Step @('get', $GL, '/recursive', '/force', '/noprompt') -Expect @(0, 1) | Out-Null
    Invoke-Case 'reconcile' 'clean-preview' @('reconcile', '/clean', $GL, '/recursive', '/preview', '/noprompt') | Out-Null
    Invoke-Case 'reconcile' 'clean' @('reconcile', '/clean', $GL, '/recursive', '/noprompt') | Out-Null
    Remove-LocalFile $WsA "$GL\untracked.txt"

    # --- Shelvesets --------------------------------------------------------
    Write-Host '--- shelve' -ForegroundColor Cyan
    Invoke-Step @('checkout', "$GL\a-renamed.txt") | Out-Null
    Write-File $WsA "$GL\a-renamed.txt" @('first line', 'second line', 'shelved line')
    Write-File $WsA "$GL\shelved.txt" @('new file in a shelveset')
    Invoke-Step @('add', "$GL\shelved.txt", '/noprompt') | Out-Null
    Invoke-Case 'shelve' 'create' @('shelve', $shelf, $GL, '/recursive', "/comment:Golden shelveset ($run)", '/noprompt') | Out-Null
    Invoke-Case 'shelvesets' 'list' @('shelvesets') | Out-Null
    Invoke-Case 'shelvesets' 'detailed' @('shelvesets', $shelf, '/format:detailed') | Out-Null
    Invoke-Case 'status' 'shelveset' @('status', "/shelveset:$shelf", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'difference' 'shelveset' @('difference', "/shelveset:$shelf", '/format:unified', '/noprompt') | Out-Null
    Invoke-Case 'view' 'shelveset' @('view', "/shelveset:$shelf", "$GS/a-renamed.txt", '/noprompt') | Out-Null
    Invoke-Step @('undo', $WsA, '/recursive', '/noprompt') -Expect @(0, 1) | Out-Null
    Remove-LocalFile $WsA "$GL\shelved.txt"
    Invoke-Case 'unshelve' 'basic' @('unshelve', $shelf, '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-unshelve' @('status', '/recursive', '/noprompt') | Out-Null
    Invoke-Step @('undo', $WsA, '/recursive', '/noprompt') -Expect @(0, 1) | Out-Null
    Remove-LocalFile $WsA "$GL\shelved.txt"
    Invoke-Case 'shelve' 'delete' @('shelve', '/delete', $shelf, '/noprompt') | Out-Null
    Invoke-Case 'shelvesets' 'not-found' @('shelvesets', $shelf) | Out-Null
    Invoke-Case 'unshelve' 'not-found' @('unshelve', $shelf, '/noprompt') | Out-Null

    # --- Branch and merge --------------------------------------------------
    Write-Host '--- branch, merge' -ForegroundColor Cyan
    Invoke-Case 'branch' 'create' @('branch', "$S/Main", $feature, '/noprompt') | Out-Null
    Invoke-Case 'status' 'pending-branch' @('status', '/recursive', '/noprompt') | Out-Null
    Invoke-CheckinCase 'branch' "Golden: branch Main to Feature ($run)" | Out-Null
    Invoke-Case 'branches' 'after-feature' @('branches', "$S/Main") | Out-Null

    Invoke-Case 'merge' 'candidate-before-merge' @('merge', '/candidate', "$S/Main", "$S/Release", '/recursive') | Out-Null
    Invoke-Case 'merge' 'preview' @('merge', "$S/Main", "$S/Release", '/recursive', '/preview', '/noprompt') | Out-Null
    Invoke-Case 'merge' 'all' @('merge', "$S/Main", "$S/Release", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'status' 'pending-merge' @('status', '/recursive', '/noprompt') | Out-Null
    $csMerge = Invoke-CheckinCase 'merge' "Golden: merge Main to Release ($run)"
    Invoke-Case 'changeset' 'merge-many-items' @('changeset', "$csMerge", '/noprompt') | Out-Null
    Invoke-Case 'merges' 'extended' @('merges', "$S/Main", "$S/Release", '/recursive', '/extended') | Out-Null
    Invoke-Case 'merges' 'showall' @('merges', "$S/Main", "$S/Release", '/recursive', '/showall') | Out-Null
  }

  if (Test-Section 'mergeconflict') {
    Write-Host '--- merge conflict' -ForegroundColor Cyan
    Invoke-Step @('checkout', 'Main\README.md') | Out-Null
    Write-File $WsA 'Main\README.md' @('# TfPortable test project', '', "Main change for merge conflict ($run).")
    Invoke-CheckinCase 'main-readme' "Golden: README change in Main ($run)" | Out-Null
    Invoke-Step @('checkout', 'Release\README.md') | Out-Null
    Write-File $WsA 'Release\README.md' @('# TfPortable test project', '', "Release change for merge conflict ($run).")
    Invoke-CheckinCase 'release-readme' "Golden: README change in Release ($run)" | Out-Null
    Invoke-Case 'merge' 'conflict' @('merge', "$S/Main/README.md", "$S/Release/README.md", '/noprompt') | Out-Null
    Invoke-Case 'status' 'merge-conflict' @('status', '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'resolve' 'merge-conflict-list' @('resolve', $WsA, '/recursive', '/preview', '/noprompt') | Out-Null
    Invoke-Case 'resolve' 'accept-merge' @('resolve', 'Release\README.md', '/auto:AcceptMerge', '/noprompt') | Out-Null
    Invoke-Case 'resolve' 'accept-theirs' @('resolve', 'Release\README.md', '/auto:AcceptTheirs', '/noprompt') | Out-Null
    Invoke-Case 'status' 'after-merge-resolve' @('status', '/recursive', '/noprompt') | Out-Null
    Invoke-Step @('undo', $WsA, '/recursive', '/noprompt') -Expect @(0, 1) | Out-Null
    Invoke-Step @('get', $WsA, '/recursive', '/noprompt') -Expect @(0, 1) | Out-Null
  }

  if (Test-Section 'folderdiff') {
    Invoke-Case 'folderdiff' 'main-release' @('folderdiff', "$S/Main", "$S/Release", '/recursive', '/noprompt') | Out-Null
    Invoke-Case 'difference' 'two-branches' @('difference', "$S/Main/README.md", "$S/Release/README.md", '/format:unified', '/noprompt') | Out-Null
    Invoke-Case 'difference' 'context-format' @('difference', "$S/Main/README.md", "$S/Release/README.md", '/format:context', '/noprompt') | Out-Null

  }

  if (Test-Section 'workfold') {
    # --- Workfold changes (workspace B) ------------------------------------
    Write-Host '--- workfold' -ForegroundColor Cyan
    Initialize-WorkspaceB
    Invoke-Case 'workfold' 'cloak' @('workfold', '/cloak', "$S/Release", "/workspace:$wsBName", $C) -WorkingDirectory $rootB | Out-Null
    Invoke-Case 'workfold' 'list-cloaked' @('workfold', "/workspace:$wsBName", $C) -WorkingDirectory $rootB | Out-Null
    Invoke-Case 'get' 'after-cloak' @('get', $WsB, '/recursive', '/noprompt') -WorkingDirectory $WsB | Out-Null
    Invoke-Case 'workfold' 'decloak' @('workfold', '/decloak', "$S/Release", "/workspace:$wsBName", $C) -WorkingDirectory $rootB | Out-Null
    Invoke-Case 'workfold' 'unmap' @('workfold', '/unmap', "/workspace:$wsBName", $C, $WsB) -WorkingDirectory $rootB | Out-Null
    Invoke-Case 'workfold' 'map' @('workfold', '/map', "/workspace:$wsBName", $C, $S, $WsB) -WorkingDirectory $rootB | Out-Null
    Invoke-Case 'workfold' 'map-already-mapped' @('workfold', '/map', "/workspace:$wsBName", $C, $S, $WsB) -WorkingDirectory $rootB | Out-Null

  }

  if (Test-Section 'server') {
    # --- Server workspace ----------------------------------------------------
    Write-Host '--- server workspace' -ForegroundColor Cyan
    $WsSMain = Join-Path $WsS 'Main'
    New-TestWorkspace -Name $wsSName -Root $rootS -Location 'server' -ServerPath "$S/Main" -LocalPath $WsSMain -CaseName 'new-server'
    Invoke-Case 'workspaces' 'with-server-workspace' @('workspaces', $C, '/format:detailed') | Out-Null
    Invoke-Case 'get' 'server-workspace' @('get', $WsSMain, '/recursive', '/noprompt') -WorkingDirectory $WsSMain | Out-Null
    Invoke-Case 'status' 'server-workspace' @('status', '/recursive', '/noprompt') -WorkingDirectory $WsSMain | Out-Null
    Invoke-Case 'properties' 'server-workspace' @('properties', 'README.md') -WorkingDirectory $WsSMain | Out-Null
    Invoke-Case 'checkout' 'server-workspace' @('checkout', 'README.md') -WorkingDirectory $WsSMain | Out-Null
    Invoke-Case 'status' 'server-workspace-pending' @('status', '/recursive', '/noprompt') -WorkingDirectory $WsSMain | Out-Null
    Invoke-Case 'undo' 'server-workspace' @('undo', 'README.md', '/noprompt') -WorkingDirectory $WsSMain | Out-Null
    Invoke-Case 'workspace' 'delete-server' @('workspace', '/delete', $wsSName, $C, '/noprompt') -WorkingDirectory $rootS | Out-Null
    [void]$script:CreatedWorkspaces.Remove($wsSName)

  }

  if (Test-Section 'misc') {
    # --- Misc ----------------------------------------------------------------
    Write-Host '--- misc' -ForegroundColor Cyan
    Invoke-Case 'permission' 'project' @('permission', $S, $C) | Out-Null
    Invoke-Case 'destroy' 'preview' @('destroy', "$S/Main/data/counter.txt", '/preview', '/noprompt') | Out-Null
    Invoke-Case 'annotate' 'attempt' @('annotate', "$S/Main/src/Program.cs", '/noprompt') | Out-Null
    Invoke-Case 'history' 'sort-ascending' @('history', "$S/Main", '/recursive', '/sort:ascending', '/stopafter:5', '/noprompt') | Out-Null
    Invoke-Case 'history' 'user-not-found' @('history', "$S/Main", '/recursive', '/user:NoSuchUser', '/noprompt') | Out-Null
    Invoke-Case 'history' 'date-range' @('history', "$S/Main", '/recursive', "/version:D$((Get-Date).AddDays(-1).ToString('yyyy-MM-dd'))~T", '/noprompt') | Out-Null
    Invoke-Case 'history' 'itemmode' @('history', "$S/Main/src/Utils/TextHelper.cs", '/itemmode', '/noprompt') | Out-Null
    Invoke-Case 'history' 'slotmode' @('history', "$S/Main/src/Utils/TextHelper.cs", '/slotmode', '/noprompt') | Out-Null
    Invoke-Case 'history' 'format-xml' @('history', "$S/Main", '/recursive', '/format:xml', '/stopafter:3', '/noprompt') | Out-Null
    Invoke-Case 'history' 'detailed-with-merge' @('history', "$S/Release", '/recursive', '/format:detailed', '/stopafter:3', '/noprompt') | Out-Null

  }

    # --- Long history ------------------------------------------------------
    if ((Test-Section 'padding') -and $HistoryPadding -gt 0) {
        Write-Host "--- history padding ($HistoryPadding changesets)" -ForegroundColor Cyan
        for ($i = 1; $i -le $HistoryPadding; $i++) {
            Write-Progress -Activity 'History padding' -Status "$i of $HistoryPadding" -PercentComplete ($i * 100 / $HistoryPadding)
            Invoke-TfRaw -Arguments @('checkout', 'Main\data\counter.txt') -WorkingDirectory $WsA | Out-Null
            Write-File $WsA 'Main\data\counter.txt' @("$run-$i")
            $result = Invoke-TfRaw -Arguments @('checkin', 'Main\data\counter.txt', "/comment:History padding $i of $HistoryPadding ($run)", '/noprompt') -WorkingDirectory $WsA
            if ($result.ExitCode -ne 0) { throw "Padding checkin $i failed.`n$($result.Stdout)$($result.Stderr)" }
            if ($i -eq 1) { $script:Extra.changesets['paddingFirst'] = Get-LatestChangeset }
        }
        Write-Progress -Activity 'History padding' -Completed
        $script:Extra.changesets['paddingLast'] = Get-LatestChangeset
        Invoke-Case 'history' 'long-default' @('history', "$S/Main/data/counter.txt", '/noprompt') | Out-Null
        Invoke-Case 'history' 'long-stopafter-256' @('history', "$S/Main/data/counter.txt", '/stopafter:256', '/noprompt') | Out-Null
        Invoke-Case 'history' 'long-project-recursive' @('history', $S, '/recursive', '/noprompt') | Out-Null
    }
}
catch {
    $failed = $_
}
finally {
    Write-Host ''
    Write-Host 'Cleaning up...' -ForegroundColor Cyan
    Invoke-TfRaw -Arguments @('undo', $WsA, '/recursive', '/noprompt') -WorkingDirectory $WsA | Out-Null
    foreach ($name in @($script:CreatedWorkspaces)) {
        if ($name -eq $wsBName) {
            Invoke-Case 'workspace' 'delete-local' @('workspace', '/delete', $name, $C, '/noprompt') -WorkingDirectory $rootB | Out-Null
        }
        else {
            Invoke-TfRaw -Arguments @('workspace', '/delete', $name, $C, '/noprompt') -WorkingDirectory $WsA | Out-Null
        }
    }
    foreach ($folder in @($script:CreatedFolders)) {
        if (Test-Path -LiteralPath $folder) {
            Get-ChildItem -LiteralPath $folder -Recurse -Force -File | ForEach-Object { $_.IsReadOnly = $false }
            Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    foreach ($file in @("$GL\untracked.txt", "$GL\shelved.txt")) { Remove-LocalFile $WsA $file }

    $manifestDir = Split-Path -Parent ([System.IO.Path]::GetFullPath($Manifest))
    $extraPath = Join-Path $manifestDir "extra-manifest-$run.json"
    [System.IO.File]::WriteAllText($extraPath, ($script:Extra | ConvertTo-Json -Depth 5), $script:Utf8NoBom)

    $indexDoc = [ordered]@{
        capturedAt      = (Get-Date).ToString('o')
        run             = $run
        tfVersion       = $script:TfVersion
        consoleEncoding = $script:TfEncoding.WebName
        culture         = [System.Globalization.CultureInfo]::CurrentUICulture.Name
        extra           = [ordered]@{ changesets = $script:Extra.changesets; label = (Protect-Text $label); shelveset = $shelf; featureBranch = (Protect-Text $feature) }
        cases           = $script:Index
    }
    [System.IO.File]::WriteAllText((Join-Path $script:OutRoot "index-extra-$run.json"), ($indexDoc | ConvertTo-Json -Depth 6), $script:Utf8NoBom)

    $check = Invoke-TfRaw -Arguments @('status', '/recursive', '/noprompt') -WorkingDirectory $WsA
    Write-Host $check.Stdout
    Write-Host 'Check that the status above shows no pending changes.' -ForegroundColor Yellow
    Write-Host "Changesets: $extraPath"
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
