<#
.SYNOPSIS
    Captures the built-in help of the original tf.exe (switch lists and aliases) as golden files.

.DESCRIPTION
    For every command that TfPortable implements, recognizes or refuses, runs
    `tf vc help <command>` and `tf <command> /?` and stores stdout, stderr and the exit code in
    <OutDir>\help\<command>.txt and <OutDir>\help\<command>-qmark.txt, plus index-help.json.
    Also captures `tf vc help` (the command list).

    Help is printed locally; no sign-in and no workspace are needed. The files contain no
    personal data, but the computer and user names are replaced anyway, like in the other
    capture scripts. Output is decoded from the ANSI code page (what tf.exe uses when its output
    is redirected) and stored as UTF-8 without BOM.

    Why: `tf help <command>` in Visual Studio 18 prints only the banner (see golden
    general/help-status), so the switch lists and aliases that SPEC.md wants to take from the
    original's help were missing.

.PARAMETER OutDir
    Output folder. Default: .\tests\golden relative to the current directory, so run the script
    from the repository root (or pass the full path), otherwise the files land elsewhere.

.PARAMETER TfPath
    Full path to tf.exe. Found automatically via vswhere when omitted.

.PARAMETER TimeoutSeconds
    Kill tf.exe when a single call takes longer. Default: 60

.EXAMPLE
    .\tools\capture-tf-help.ps1
#>
[CmdletBinding()]
param(
    [string] $OutDir = '.\tests\golden',
    [string] $TfPath,
    [int] $TimeoutSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -eq 'Core') {
    [System.Text.Encoding]::RegisterProvider([System.Text.CodePagesEncodingProvider]::Instance)
}

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

function Invoke-TfRaw {
    param([string[]] $Arguments)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $script:Tf
    $psi.Arguments = ($Arguments -join ' ')
    $psi.WorkingDirectory = $env:TEMP
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = $script:TfEncoding
    $psi.StandardErrorEncoding = $script:TfEncoding

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
        $Text = [regex]::Replace($Text, [regex]::Escape($pair.From), $pair.To, 'IgnoreCase')
    }
    return [regex]::Replace($Text, '[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+', '<EMAIL>')
}

function Invoke-Case {
    param([string] $Name, [string[]] $Arguments)
    Write-Host ("{0,-30}" -f $Name) -NoNewline
    $result = Invoke-TfRaw -Arguments $Arguments
    $commandLine = 'tf ' + ($Arguments -join ' ')
    $content = @(
        "# case: help/$Name",
        "# command: $commandLine",
        "# cwd: %TEMP%",
        "# exit: $($result.ExitCode)",
        "# timed-out: $($result.TimedOut.ToString().ToLowerInvariant())",
        "# duration-ms: $($result.DurationMs)",
        "# console-encoding: $($script:TfEncoding.WebName)",
        "# tf-version: $($script:TfVersion)",
        '==== stdout ====',
        (Protect-Text $result.Stdout),
        '==== stderr ====',
        (Protect-Text $result.Stderr)
    ) -join "`r`n"
    [System.IO.File]::WriteAllText((Join-Path $script:HelpDir "$Name.txt"), $content, $script:Utf8NoBom)
    $script:Index.Add([ordered]@{
        group = 'help'; name = $Name; command = $commandLine
        exitCode = $result.ExitCode; timedOut = $result.TimedOut; file = "help/$Name.txt"
    })
    $color = if ($result.TimedOut) { 'Red' } elseif ($result.ExitCode -eq 0) { 'Green' } else { 'Yellow' }
    Write-Host "exit $($result.ExitCode)" -ForegroundColor $color
}

$script:Tf = Find-Tf $TfPath
$script:TfVersion = (Get-Item -LiteralPath $script:Tf).VersionInfo.FileVersion
$script:TfEncoding = [System.Text.Encoding]::GetEncoding([System.Globalization.CultureInfo]::CurrentCulture.TextInfo.ANSICodePage)
$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$script:Index = New-Object System.Collections.Generic.List[object]
$script:HelpDir = Join-Path ([System.IO.Path]::GetFullPath($OutDir)) 'help'
if (-not (Test-Path -LiteralPath $script:HelpDir)) { New-Item -ItemType Directory -Path $script:HelpDir | Out-Null }

$pairs = New-Object System.Collections.Generic.List[object]
$pairs.Add(@{ From = $env:COMPUTERNAME; To = '<COMPUTER>' })
if ($env:USERNAME.Length -ge 3) { $pairs.Add(@{ From = $env:USERNAME; To = '<USER>' }) }
$script:Redactions = @($pairs | Sort-Object { $_.From.Length } -Descending)

Write-Host "tf.exe:  $($script:Tf) ($($script:TfVersion))"
Write-Host "Output:  $($script:HelpDir)"
Write-Host ''

# Phase 1, phase 2, out-of-scope and alias names. Aliases are included to see whether
# tf.exe prints the canonical name in their help.
$commands = @(
    'status', 'add', 'checkout', 'edit', 'delete', 'rename', 'undo', 'get', 'resolve', 'reconcile',
    'localversions', 'workfold', 'workspaces', 'history', 'changeset', 'view', 'difference', 'diff',
    'dir', 'properties', 'info', 'labels', 'branches', 'merge', 'merges',
    'annotate', 'folderdiff', 'shelvesets', 'unshelve', 'undelete', 'rollback', 'branch',
    'checkin', 'shelve', 'lock', 'label', 'unlabel', 'destroy', 'permission', 'workspace',
    'configure', 'proxy', 'help'
)

Invoke-Case -Name 'vc-help' -Arguments @('vc', 'help')
Invoke-Case -Name 'root-qmark' -Arguments @('/?')
foreach ($c in $commands) {
    Invoke-Case -Name $c -Arguments @('vc', 'help', $c)
    Invoke-Case -Name "$c-qmark" -Arguments @($c, '/?')
}

$indexDoc = [ordered]@{
    capturedAt      = (Get-Date).ToString('o')
    tfVersion       = $script:TfVersion
    consoleEncoding = $script:TfEncoding.WebName
    culture         = [System.Globalization.CultureInfo]::CurrentCulture.Name
    cases           = $script:Index
}
[System.IO.File]::WriteAllText((Join-Path ([System.IO.Path]::GetFullPath($OutDir)) 'index-help.json'), ($indexDoc | ConvertTo-Json -Depth 6), $script:Utf8NoBom)
Write-Host ''
Write-Host "Done: $($script:Index.Count) cases. Review tests\golden\help before committing."
