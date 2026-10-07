<#
.SYNOPSIS
    Captures stdout, stderr and exit codes of the original tf.exe as golden files for TfPortable.

.DESCRIPTION
    Runs every phase 1 command of the TfPortable specification against the test project
    created by seed-tfvc-test-repo.ps1 and stores the results in
    <OutDir>\<command>\<case>.txt plus an index.json.

    Standard input is redirected and closed for every call, the same way Claude Code
    runs commands, so tf.exe behaves non-interactively.

    Personal data is replaced with placeholders (<COLLECTION>, <WORKSPACE>, <OWNER>,
    <EMAIL>, <COMPUTER>, <USER>, <LOCAL_ROOT>, <WORKSPACE_ROOT>). Review the output
    before you commit it anyway.

    With -IncludeMutating the script also captures add, checkout, rename, delete, undo
    and pending-change output. Those cases work only inside the test workspace and are
    undone at the end; nothing is checked in.

.PARAMETER Manifest
    Path to seed-manifest.json written by seed-tfvc-test-repo.ps1.

.PARAMETER OutDir
    Output folder. Default: .\tests\golden

.PARAMETER TfPath
    Full path to tf.exe. Found automatically via vswhere when omitted.

.PARAMETER IncludeMutating
    Also capture commands that create pending changes (undone at the end).

.PARAMETER Redact
    Extra strings to replace with <REDACTED>.

.PARAMETER TimeoutSeconds
    Kill tf.exe when a single call takes longer (e.g. it opened a dialog). Default: 120

.PARAMETER TfOutputEncoding
    Code page used to decode tf.exe output: ansi (default, what tf.exe uses when its
    output is redirected), oem or utf8. Change it only if diacritics in the golden
    files come out garbled.

.PARAMETER KeepLocalizedOutput
    By default the script sets VSLANG=1033 to ask tf.exe for English output. Use this
    switch to keep the language of your Visual Studio installation.

.EXAMPLE
    .\capture-tf-golden.ps1 -Manifest C:\TfpTest\seed-manifest.json -IncludeMutating
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Manifest,
    [string] $OutDir = '.\tests\golden',
    [string] $TfPath,
    [switch] $IncludeMutating,
    [string[]] $Redact = @(),
    [int] $TimeoutSeconds = 120,
    [ValidateSet('ansi', 'oem', 'utf8')] [string] $TfOutputEncoding = 'ansi',
    [switch] $KeepLocalizedOutput
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
    $psi.StandardOutputEncoding = $script:ConsoleEncoding
    $psi.StandardErrorEncoding = $script:ConsoleEncoding
    if ($script:English) { $psi.EnvironmentVariables['VSLANG'] = '1033' }

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

function Invoke-Case {
    param(
        [Parameter(Mandatory = $true)] [string] $Group,
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [AllowEmptyCollection()] [string[]] $Arguments,
        [string] $WorkingDirectory = $script:ProjectLocal
    )
    Write-Host ("{0,-18} {1,-28}" -f $Group, $Name) -NoNewline
    $result = Invoke-TfRaw -Arguments $Arguments -WorkingDirectory $WorkingDirectory

    $commandLine = Protect-Text ('tf ' + ((@($Arguments | ForEach-Object { ConvertTo-ArgString $_ })) -join ' '))
    $content = @(
        "# case: $Group/$Name",
        "# command: $commandLine",
        "# cwd: $(Protect-Text $WorkingDirectory)",
        "# exit: $($result.ExitCode)",
        "# timed-out: $($result.TimedOut.ToString().ToLowerInvariant())",
        "# duration-ms: $($result.DurationMs)",
        "# console-encoding: $($script:ConsoleEncoding.WebName)",
        "# vslang: $(if ($script:English) { '1033' } else { '(not set)' })",
        "# tf-version: $($script:TfVersion)",
        '==== stdout ====',
        (Protect-Text $result.Stdout),
        '==== stderr ====',
        (Protect-Text $result.Stderr)
    ) -join "`r`n"

    $dir = Join-Path $script:OutRoot $Group
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    $file = Join-Path $dir "$Name.txt"
    [System.IO.File]::WriteAllText($file, $content, $script:Utf8NoBom)

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
    return $result
}

# ---------------------------------------------------------------------------
# Setup
# ---------------------------------------------------------------------------

$script:Tf = Find-Tf $TfPath
$script:TfVersion = (Get-Item -LiteralPath $script:Tf).VersionInfo.FileVersion
# Redirected tf.exe output uses the ANSI code page (e.g. windows-1250), not the console one.
$textInfo = [System.Globalization.CultureInfo]::CurrentCulture.TextInfo
$script:ConsoleEncoding = switch ($TfOutputEncoding) {
    'ansi' { [System.Text.Encoding]::GetEncoding($textInfo.ANSICodePage) }
    'oem'  { [System.Text.Encoding]::GetEncoding($textInfo.OEMCodePage) }
    'utf8' { New-Object System.Text.UTF8Encoding($false) }
}
# Ask Visual Studio tools for English output (ignored if tf.exe does not honor it).
$script:English = -not $KeepLocalizedOutput
$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$script:Index = New-Object System.Collections.Generic.List[object]

$m = Get-Content -LiteralPath $Manifest -Raw -Encoding UTF8 | ConvertFrom-Json
$script:ProjectLocal = $m.projectLocal
$S = $m.serverRoot
$C = "/collection:$($m.collection)"
$cs = $m.changesets
$label = $m.label
$czFile = "P$([char]0x0159)$([char]0x00ED)ru$([char]0x010D)ka.md"

if (-not (Test-Path -LiteralPath $script:ProjectLocal)) {
    throw "Workspace folder '$($script:ProjectLocal)' from the manifest does not exist. Run seed-tfvc-test-repo.ps1 first."
}

$script:OutRoot = [System.IO.Path]::GetFullPath($OutDir)
if (-not (Test-Path -LiteralPath $script:OutRoot)) { New-Item -ItemType Directory -Path $script:OutRoot | Out-Null }

# Sign-in check (non-interactive, like Claude Code).
$pre = Invoke-TfRaw -Arguments @('workspaces', $C) -WorkingDirectory $script:ProjectLocal
if ($pre.ExitCode -ne 0 -or ($pre.Stdout + $pre.Stderr) -match 'TF30063|TF400813') {
    throw "tf.exe is not signed in or cannot reach the collection. Run `"$($script:Tf)`" workspaces $C in a terminal, sign in, then run this script again.`n$($pre.Stdout)$($pre.Stderr)"
}

# Workspace owner from the detailed listing, for redaction.
$owner = $null
$detailed = Invoke-TfRaw -Arguments @('workspaces', $C, '/format:detailed') -WorkingDirectory $script:ProjectLocal
foreach ($block in ($detailed.Stdout -split '(?m)^=+\s*$')) {
    if ($block -match "(?m)^\s*Workspace\s*:\s*$([regex]::Escape($m.workspace))\s*$" -and $block -match '(?m)^\s*Owner\s*:\s*(.+?)\s*$') {
        $owner = $Matches[1]
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
Write-Host "Output:  $($script:OutRoot)"
Write-Host ''

# ---------------------------------------------------------------------------
# Read-only cases (clean workspace)
# ---------------------------------------------------------------------------

Invoke-Case 'general' 'no-arguments'       @() | Out-Null
Invoke-Case 'general' 'help'               @('help') | Out-Null
Invoke-Case 'general' 'help-status'        @('help', 'status') | Out-Null
Invoke-Case 'general' 'unknown-command'    @('nosuchcommand') | Out-Null
Invoke-Case 'general' 'unknown-switch'     @('status', '/nosuchswitch') | Out-Null
Invoke-Case 'general' 'vc-prefix'          @('vc', 'status', '/recursive', '/noprompt') | Out-Null
Invoke-Case 'general' 'dash-switches'      @('status', '-recursive', '-noprompt') | Out-Null
Invoke-Case 'general' 'short-switches'     @('status', '-R', '-I') | Out-Null
Invoke-Case 'general' 'outside-workspace'  @('status', '/recursive', '/noprompt') -WorkingDirectory $env:TEMP | Out-Null

Invoke-Case 'workspaces' 'list'            @('workspaces', $C) | Out-Null
Invoke-Case 'workspaces' 'detailed'        @('workspaces', $C, '/format:detailed') | Out-Null
Invoke-Case 'workspaces' 'other-computer'  @('workspaces', $C, '/computer:NoSuchComputer') | Out-Null

Invoke-Case 'workfold' 'current'           @('workfold') | Out-Null

Invoke-Case 'status' 'clean'               @('status', '/recursive', '/noprompt') | Out-Null
Invoke-Case 'status' 'clean-detailed'      @('status', '/recursive', '/format:detailed', '/noprompt') | Out-Null
Invoke-Case 'status' 'server-path'         @('status', "$S/Main", '/recursive', '/noprompt') | Out-Null

Invoke-Case 'dir' 'folder'                 @('dir', "$S/Main") | Out-Null
Invoke-Case 'dir' 'recursive'              @('dir', "$S/Main", '/recursive') | Out-Null
Invoke-Case 'dir' 'folders-only'           @('dir', "$S/Main", '/recursive', '/folders') | Out-Null
Invoke-Case 'dir' 'deleted'                @('dir', "$S/Main", '/recursive', '/deleted') | Out-Null
Invoke-Case 'dir' 'at-version'             @('dir', "$S/Main", '/recursive', "/version:C$($cs.initial)") | Out-Null
Invoke-Case 'dir' 'not-found'              @('dir', "$S/NoSuchFolder") | Out-Null

Invoke-Case 'localversions' 'recursive'    @('localversions', 'Main', '/recursive') | Out-Null
Invoke-Case 'localversions' 'detailed'     @('localversions', 'Main', '/recursive', '/format:detailed') | Out-Null

Invoke-Case 'properties' 'local-file'      @('properties', 'Main\src\Program.cs') | Out-Null
Invoke-Case 'properties' 'info-alias'      @('info', 'Main\src\Utils\TextHelper.cs') | Out-Null
Invoke-Case 'properties' 'server-version'  @('properties', "$S/Main/src/Program.cs", "/version:C$($cs.initial)") | Out-Null
Invoke-Case 'properties' 'czech-name'      @('properties', "Main\docs\$czFile") | Out-Null
Invoke-Case 'properties' 'recursive'       @('properties', 'Main\data', '/recursive') | Out-Null

Invoke-Case 'history' 'brief'              @('history', "$S/Main", '/recursive', '/noprompt') | Out-Null
Invoke-Case 'history' 'detailed-stopafter' @('history', "$S/Main", '/recursive', '/format:detailed', '/stopafter:3', '/noprompt') | Out-Null
Invoke-Case 'history' 'renamed-file'       @('history', 'Main\src\Utils\TextHelper.cs', '/noprompt') | Out-Null
Invoke-Case 'history' 'range'              @('history', "$S/Main", '/recursive', "/version:C$($cs.editProgram)~C$($cs.rename)", '/noprompt') | Out-Null
Invoke-Case 'history' 'short-version'      @('history', "$S/Main/src/Program.cs", "/v:C$($cs.fixMain)", '/noprompt') | Out-Null
Invoke-Case 'history' 'release-branch'     @('history', "$S/Release", '/recursive', '/noprompt') | Out-Null
Invoke-Case 'history' 'not-found'          @('history', "$S/NoSuchFile.cs", '/noprompt') | Out-Null
Invoke-Case 'history' 'vc-dash'            @('vc', 'history', "$S/Main/src/Program.cs", '-noprompt') | Out-Null

Invoke-Case 'changeset' 'initial'          @('changeset', "$($cs.initial)", '/noprompt') | Out-Null
Invoke-Case 'changeset' 'edit'             @('changeset', "$($cs.editProgram)", '/noprompt') | Out-Null
Invoke-Case 'changeset' 'rename'           @('changeset', "$($cs.rename)", '/noprompt') | Out-Null
Invoke-Case 'changeset' 'delete'           @('changeset', "$($cs.delete)", '/noprompt') | Out-Null
Invoke-Case 'changeset' 'branch'           @('changeset', "$($cs.branch)", '/noprompt') | Out-Null
Invoke-Case 'changeset' 'merge'            @('changeset', "$($cs.mergeToRelease)", '/noprompt') | Out-Null
Invoke-Case 'changeset' 'latest'           @('changeset', '/latest', '/noprompt') | Out-Null
Invoke-Case 'changeset' 'not-found'        @('changeset', '99999999', '/noprompt') | Out-Null
Invoke-Case 'changeset' 'vc-dash'          @('vc', 'changeset', "$($cs.fixMain)", '-noprompt') | Out-Null

Invoke-Case 'view' 'server-version'        @('view', "$S/Main/src/Program.cs", "/version:C$($cs.initial)", '/noprompt') | Out-Null
Invoke-Case 'view' 'semicolon-version'     @('view', "$S/Main/src/Program.cs;C$($cs.initial)", '/noprompt') | Out-Null
Invoke-Case 'view' 'changeset-no-c'        @('view', "$S/Main/src/Program.cs", "/version:$($cs.initial)", '/noprompt') | Out-Null
Invoke-Case 'view' 'label-version'         @('view', "$S/Main/src/Program.cs", "/version:L$label", '/noprompt') | Out-Null
Invoke-Case 'view' 'local-path'            @('view', 'Main\README.md', '/noprompt') | Out-Null
Invoke-Case 'view' 'forward-slashes'       @('view', (($m.projectLocal -replace '\\', '/') + '/Main/README.md'), '/noprompt') | Out-Null
Invoke-Case 'view' 'utf8-bom'              @('view', "$S/Main/data/utf8-bom.txt", '/noprompt') | Out-Null
Invoke-Case 'view' 'cp1250'                @('view', "$S/Main/data/cp1250.txt", '/noprompt') | Out-Null
Invoke-Case 'view' 'czech-name'            @('view', "$S/Main/docs/$czFile", '/noprompt') | Out-Null
Invoke-Case 'view' 'deleted-file'          @('view', "$S/Main/docs/obsolete.md", "/version:C$($cs.initial)", '/noprompt') | Out-Null
Invoke-Case 'view' 'not-found'             @('view', "$S/NoSuchFile.cs", '/noprompt') | Out-Null

Invoke-Case 'difference' 'server-unified'  @('difference', "$S/Main/src/Program.cs", "/version:C$($cs.initial)~C$($cs.fixMain)", '/format:unified', '/noprompt') | Out-Null
Invoke-Case 'difference' 'server-brief'    @('difference', "$S/Main/src/Program.cs", "/version:C$($cs.initial)~C$($cs.fixMain)", '/format:brief', '/noprompt') | Out-Null
Invoke-Case 'difference' 'no-local-change' @('diff', 'Main\src\Program.cs', '/format:unified', '/noprompt') | Out-Null

Invoke-Case 'labels' 'all'                 @('labels') | Out-Null
Invoke-Case 'labels' 'detailed'            @('labels', '/format:detailed', $label) | Out-Null

Invoke-Case 'branches' 'main'              @('branches', "$S/Main") | Out-Null

Invoke-Case 'merge' 'candidate'            @('merge', '/candidate', "$S/Main", "$S/Release", '/recursive') | Out-Null
Invoke-Case 'merges' 'main-to-release'     @('merges', "$S/Main", "$S/Release", '/recursive') | Out-Null

Invoke-Case 'get' 'preview'                @('get', '/recursive', '/preview', '/noprompt') | Out-Null
Invoke-Case 'get' 'up-to-date'             @('get', '/recursive', '/noprompt') | Out-Null

Invoke-Case 'resolve' 'no-conflicts'       @('resolve', '/recursive', '/noprompt') | Out-Null

# ---------------------------------------------------------------------------
# Mutating cases (pending changes only, undone at the end)
# ---------------------------------------------------------------------------

if ($IncludeMutating) {
    $scratch = Join-Path $script:ProjectLocal 'Main\_golden'
    $program = Join-Path $script:ProjectLocal 'Main\src\Program.cs'
    try {
        New-Item -ItemType Directory -Force -Path $scratch | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $scratch 'new-file.txt'), "new file`r`n", $script:Utf8NoBom)
        [System.IO.File]::WriteAllText((Join-Path $scratch 'file with spaces.txt'), "spaces`r`n", $script:Utf8NoBom)

        Invoke-Case 'status' 'detected-adds'        @('status', '/recursive', '/noprompt') | Out-Null
        Invoke-Case 'add' 'folder-recursive'        @('add', 'Main\_golden', '/recursive', '/noprompt') | Out-Null
        Invoke-Case 'add' 'already-versioned'       @('add', 'Main\src\Program.cs', '/noprompt') | Out-Null
        Invoke-Case 'add' 'ignored-file'            @('add', 'Main\src\bin\Debug\ignored.txt', '/noprompt') | Out-Null
        Invoke-Case 'add' 'not-found'               @('add', 'Main\NoSuchFile.txt', '/noprompt') | Out-Null

        Invoke-Case 'checkout' 'file'               @('checkout', 'Main\README.md') | Out-Null
        Invoke-Case 'checkout' 'edit-alias'         @('edit', 'Main\config\app.config') | Out-Null

        [System.IO.File]::AppendAllText($program, "// local edit for golden capture`r`n", $script:Utf8NoBom)
        Invoke-Case 'difference' 'local-edit'       @('diff', 'Main\src\Program.cs', '/format:unified', '/noprompt') | Out-Null

        Invoke-Case 'rename' 'file'                 @('rename', 'Main\data\sample-lf.txt', 'Main\data\sample-renamed.txt') | Out-Null
        Invoke-Case 'rename' 'not-found'            @('rename', 'Main\data\no-such.txt', 'Main\data\other.txt') | Out-Null
        Invoke-Case 'delete' 'file'                 @('delete', 'Main\assets\logo.png') | Out-Null
        Invoke-Case 'delete' 'not-found'            @('delete', 'Main\NoSuchFile.txt') | Out-Null

        Invoke-Case 'status' 'pending'              @('status', '/recursive', '/noprompt') | Out-Null
        Invoke-Case 'status' 'pending-detailed'     @('status', '/recursive', '/format:detailed', '/noprompt') | Out-Null
        Invoke-Case 'get' 'with-pending'            @('get', '/recursive', '/noprompt') | Out-Null

        Invoke-Case 'undo' 'recursive'              @('undo', 'Main', '/recursive', '/noprompt') | Out-Null
        Invoke-Case 'undo' 'nothing-to-undo'        @('undo', 'Main\README.md', '/noprompt') | Out-Null
        Invoke-Case 'status' 'after-undo'           @('status', '/recursive', '/noprompt') | Out-Null
    }
    finally {
        Write-Host ''
        Write-Host 'Cleaning up pending changes...'
        Invoke-TfRaw -Arguments @('undo', 'Main', '/recursive', '/noprompt') -WorkingDirectory $script:ProjectLocal | Out-Null
        if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
        $check = Invoke-TfRaw -Arguments @('status', '/recursive', '/noprompt') -WorkingDirectory $script:ProjectLocal
        Write-Host $check.Stdout
        Write-Host 'Check that the status above shows no pending changes.' -ForegroundColor Yellow
    }
}

# ---------------------------------------------------------------------------
# Index
# ---------------------------------------------------------------------------

$indexDoc = [ordered]@{
    capturedAt      = (Get-Date).ToString('o')
    tfVersion       = $script:TfVersion
    consoleEncoding = $script:ConsoleEncoding.WebName
    culture         = [System.Globalization.CultureInfo]::CurrentUICulture.Name
    manifest        = [ordered]@{ changesets = $cs; label = (Protect-Text $label) }
    cases           = $script:Index
}
[System.IO.File]::WriteAllText((Join-Path $script:OutRoot 'index.json'), ($indexDoc | ConvertTo-Json -Depth 6), $script:Utf8NoBom)

$timedOut = @($script:Index | Where-Object { $_.timedOut })
Write-Host ''
Write-Host "Captured $($script:Index.Count) cases into $($script:OutRoot)" -ForegroundColor Green
if ($timedOut.Count -gt 0) {
    Write-Host "$($timedOut.Count) case(s) timed out (tf.exe probably opened a dialog): $(($timedOut | ForEach-Object { $_.file }) -join ', ')" -ForegroundColor Red
}
Write-Host 'Review the files for personal data before committing them.' -ForegroundColor Yellow
