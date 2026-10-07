<#
.SYNOPSIS
    Seeds an empty TFVC team project with a reproducible history for TfPortable tests.

.DESCRIPTION
    Uses the original tf.exe from Visual Studio (TfPortable cannot check in).
    Creates a local workspace, adds files with different encodings, line endings and
    names, and checks in a history with edits, a rename, a delete, a label, a branch,
    a merge and an unmerged change. Optionally adds many small changesets to test
    history truncation.

    Writes seed-manifest.json into -LocalRoot with all changeset numbers, so that
    capture-tf-golden.ps1 and integration tests can refer to them.

    Requirements:
      - Windows PowerShell 5.1 or PowerShell 7
      - Visual Studio with Team Explorer (provides tf.exe)
      - A NEW, EMPTY team project with a TFVC repository

.PARAMETER Collection
    Collection URL, e.g. https://dev.azure.com/contoso or https://tfs.contoso.local/tfs/DefaultCollection

.PARAMETER Project
    Name of the empty team project with TFVC, e.g. TfpTest

.PARAMETER LocalRoot
    Local folder for the workspace. The project is mapped to <LocalRoot>\<Project>.

.PARAMETER TfPath
    Full path to tf.exe. Found automatically via vswhere when omitted.

.PARAMETER WorkspaceName
    Name of the local workspace to create. Default: tfp-seed-<COMPUTERNAME>

.PARAMETER HistoryPadding
    Number of extra one-line changesets to create (e.g. 300 to test the 256-entry
    history limit). Each takes a few seconds. Default: 0

.PARAMETER Force
    Delete an existing workspace with the same name and an existing local folder first.

.EXAMPLE
    .\seed-tfvc-test-repo.ps1 -Collection https://dev.azure.com/contoso -Project TfpTest -LocalRoot C:\TfpTest

.EXAMPLE
    .\seed-tfvc-test-repo.ps1 -Collection https://dev.azure.com/contoso -Project TfpTest -LocalRoot C:\TfpTest -HistoryPadding 300
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Collection,
    [Parameter(Mandatory = $true)] [string] $Project,
    [Parameter(Mandatory = $true)] [string] $LocalRoot,
    [string] $TfPath,
    [string] $WorkspaceName = "tfp-seed-$env:COMPUTERNAME",
    [int] $HistoryPadding = 0,
    [switch] $Force
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

function Invoke-Tf {
    param(
        [Parameter(Mandatory = $true)] [string[]] $Arguments,
        [int[]] $AllowedExitCodes = @(0)
    )
    Write-Host "> tf $($Arguments -join ' ')" -ForegroundColor DarkGray
    $previous = $ErrorActionPreference
    $previousEncoding = [Console]::OutputEncoding
    $ErrorActionPreference = 'Continue'
    try {
        # Redirected tf.exe output uses the ANSI code page, not the console (OEM) one.
        [Console]::OutputEncoding = $script:TfEncoding
        $output = & $script:Tf @Arguments 2>&1 | ForEach-Object { "$_" }
        $code = $LASTEXITCODE
    }
    finally {
        [Console]::OutputEncoding = $previousEncoding
        $ErrorActionPreference = $previous
    }
    $text = ($output -join [Environment]::NewLine)
    if ($text) { Write-Host $text }
    if ($AllowedExitCodes -notcontains $code) {
        if ($text -match 'TF30063|TF400813|\b401\b') {
            throw "tf.exe is not signed in. Run `"$script:Tf`" workspaces /collection:$Collection , sign in in the dialog, then run this script again."
        }
        throw "tf.exe failed with exit code $code."
    }
    return $text
}

function Write-File {
    param(
        [Parameter(Mandatory = $true)] [string] $RelativePath,
        [Parameter(Mandatory = $true)] [AllowEmptyString()] [string] $Text,
        [System.Text.Encoding] $Encoding = $script:Utf8NoBom
    )
    $path = Join-Path $script:ProjectLocal $RelativePath
    $dir = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    [System.IO.File]::WriteAllText($path, $Text, $Encoding)
}

function Write-Bytes {
    param([string] $RelativePath, [byte[]] $Bytes)
    $path = Join-Path $script:ProjectLocal $RelativePath
    $dir = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    [System.IO.File]::WriteAllBytes($path, $Bytes)
}

function Submit-Changes {
    param([string] $Key, [string] $Comment)
    $text = Invoke-Tf @('checkin', $script:ProjectLocal, '/recursive', "/comment:$Comment", '/noprompt')
    $match = [regex]::Match($text, '#\s*(\d+)')
    if (-not $match.Success) {
        # Localized tf.exe: read the newest changeset from history instead.
        $history = Invoke-Tf @('history', $script:ServerRoot, '/recursive', '/stopafter:1', '/format:brief', '/noprompt')
        $match = [regex]::Match($history, '(?m)^\s*(\d+)\s')
    }
    if (-not $match.Success) { throw "Could not determine the changeset number for '$Comment'." }
    $number = [int]$match.Groups[1].Value
    $script:Changesets[$Key] = $number
    Write-Host "  changeset $number ($Key)" -ForegroundColor Green
    return $number
}

# ---------------------------------------------------------------------------
# Setup
# ---------------------------------------------------------------------------

$script:Tf = Find-Tf $TfPath
$script:TfEncoding = [System.Text.Encoding]::GetEncoding([System.Globalization.CultureInfo]::CurrentCulture.TextInfo.ANSICodePage)
# Ask Visual Studio tools for English output (ignored if tf.exe does not honor it).
$env:VSLANG = '1033'
$Collection = $Collection.TrimEnd('/')
$script:ServerRoot = '$/' + $Project
$LocalRoot = [System.IO.Path]::GetFullPath($LocalRoot)
$script:ProjectLocal = Join-Path $LocalRoot $Project
$script:Changesets = [ordered]@{}
$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$utf8Bom = New-Object System.Text.UTF8Encoding($true)
$cp1250 = [System.Text.Encoding]::GetEncoding(1250)
$crlf = "`r`n"
$lf = "`n"

# Czech text built from code points so this file stays ASCII-only.
$czSentence = "P$([char]0x0159)$([char]0x00ED)li$([char]0x0161) $([char]0x017E)lu$([char]0x0165)ou$([char]0x010D)k$([char]0x00FD) k$([char]0x016F)$([char]0x0148) $([char]0x00FA)p$([char]0x011B)l $([char]0x010F)$([char]0x00E1)belsk$([char]0x00E9) $([char]0x00F3)dy."
$czFile = "P$([char]0x0159)$([char]0x00ED)ru$([char]0x010D)ka.md"

Write-Host "tf.exe:     $script:Tf"
Write-Host "Collection: $Collection"
Write-Host "Project:    $script:ServerRoot"
Write-Host "Local:      $script:ProjectLocal"
Write-Host "Workspace:  $WorkspaceName"
Write-Host ''

# 1) Sign-in check. Without /noprompt this opens the sign-in dialog when needed.
Invoke-Tf @('workspaces', "/collection:$Collection") | Out-Null

# 2) The team project must exist, use TFVC and be empty.
try {
    $dirText = Invoke-Tf @('dir', $script:ServerRoot, "/collection:$Collection")
}
catch {
    throw "Cannot list $($script:ServerRoot). Check that the team project exists and uses TFVC. ($($_.Exception.Message))"
}
$existing = @($dirText -split "`r?`n" | Where-Object { $_ -match '^\$[^/]' -and $_ -notmatch '^\$BuildProcessTemplates\s*$' })
if ($existing.Count -gt 0) {
    throw "Team project $($script:ServerRoot) is not empty ($($existing -join ', ')). Use a new, empty project."
}

# 3) Workspace and local folder.
$workspacesText = Invoke-Tf @('workspaces', "/collection:$Collection", "/computer:$env:COMPUTERNAME")
if ($workspacesText -match "(?m)^$([regex]::Escape($WorkspaceName))\s") {
    if (-not $Force) { throw "Workspace '$WorkspaceName' already exists. Use -Force to recreate it, or choose -WorkspaceName." }
    Invoke-Tf @('workspace', '/delete', $WorkspaceName, "/collection:$Collection", '/noprompt') | Out-Null
}
# The target folder (or a parent of it) must not be mapped in another workspace,
# e.g. the one Visual Studio uses. The seed always works in its own new workspace.
$probe = $LocalRoot
while ($probe -and -not (Test-Path -LiteralPath $probe)) { $probe = Split-Path -Parent $probe }
$mappedWorkspace = $null
if ($probe) {
    Push-Location $probe
    try {
        $workfoldText = Invoke-Tf @('workfold', $script:ProjectLocal) -AllowedExitCodes @(0, 1, 100)
        if ($LASTEXITCODE -eq 0) {
            $wsMatch = [regex]::Match($workfoldText, '(?m)^[^:\r\n]+:\s*(\S.*?)\s*\(')
            $mappedWorkspace = if ($wsMatch.Success) { $wsMatch.Groups[1].Value } else { '(unknown)' }
        }
    }
    finally {
        Pop-Location
    }
}
if ($mappedWorkspace -and $mappedWorkspace -ne $WorkspaceName) {
    throw "Folder '$($script:ProjectLocal)' is already mapped in workspace '$mappedWorkspace'. The seed creates its own local workspace and needs a folder that no workspace uses. Choose another -LocalRoot (e.g. D:\TfpTest); your existing mapping stays untouched."
}

if ((Test-Path -LiteralPath $script:ProjectLocal) -and (Get-ChildItem -LiteralPath $script:ProjectLocal -Force | Select-Object -First 1)) {
    if (-not $Force) { throw "Folder '$($script:ProjectLocal)' is not empty. Use -Force to delete it, or choose another -LocalRoot." }
    Remove-Item -LiteralPath $script:ProjectLocal -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $script:ProjectLocal | Out-Null

Push-Location $LocalRoot
try {
    Invoke-Tf @('workspace', '/new', $WorkspaceName, "/collection:$Collection", '/location:local', '/comment:TfPortable test workspace', '/noprompt') | Out-Null
    try {
        # A new workspace may get a default mapping of $/ to the current folder; remove it.
        Invoke-Tf @('workfold', '/unmap', "/workspace:$WorkspaceName", "/collection:$Collection", '$/') -AllowedExitCodes @(0, 1, 100) | Out-Null
        Invoke-Tf @('workfold', '/map', "/workspace:$WorkspaceName", "/collection:$Collection", $script:ServerRoot, $script:ProjectLocal) | Out-Null
    }
    catch {
        # Do not leave a half-configured workspace behind.
        Invoke-Tf @('workspace', '/delete', $WorkspaceName, "/collection:$Collection", '/noprompt') -AllowedExitCodes @(0, 1, 100) | Out-Null
        throw
    }
}
finally {
    Pop-Location
}

# ---------------------------------------------------------------------------
# History
# ---------------------------------------------------------------------------

Push-Location $script:ProjectLocal
try {
    # CS: initial content
    Write-File 'Main\README.md' ((@(
        '# TfPortable test project',
        '',
        'Generated by seed-tfvc-test-repo.ps1. Do not edit by hand.'
    ) -join $crlf) + $crlf)
    Write-File 'Main\.tfignore' ((@('# Build output', 'bin', 'obj', '*.user') -join $crlf) + $crlf)
    Write-File 'Main\src\Program.cs' ((@(
        'using System;',
        '',
        'namespace TfpTest',
        '{',
        '    internal static class Program',
        '    {',
        '        private static void Main(string[] args)',
        '        {',
        '            Console.WriteLine("Hello");',
        '        }',
        '    }',
        '}'
    ) -join $crlf) + $crlf)
    Write-File 'Main\src\Utils\StringHelper.cs' ((@(
        'namespace TfpTest.Utils',
        '{',
        '    public static class StringHelper',
        '    {',
        '        public static string Clean(string value)',
        '        {',
        '            return value.Trim();',
        '        }',
        '    }',
        '}'
    ) -join $crlf) + $crlf)
    Write-File 'Main\src\File With Spaces.cs' ('// A file name with spaces.' + $crlf)
    Write-File 'Main\src\bin\Debug\ignored.txt' ('Ignored by .tfignore, never added.' + $crlf)
    Write-File "Main\docs\$czFile" ("# $czSentence" + $crlf)
    Write-File 'Main\docs\obsolete.md' ('This document will be deleted.' + $crlf)
    Write-File 'Main\config\app.config' ((@(
        '<?xml version="1.0" encoding="utf-8"?>',
        '<configuration>',
        '  <appSettings>',
        '    <add key="Mode" value="Test" />',
        '  </appSettings>',
        '</configuration>'
    ) -join $crlf) + $crlf)
    Write-File 'Main\data\sample-lf.txt' ((@('line 1 (LF)', 'line 2 (LF)', 'line 3 (LF)') -join $lf) + $lf)
    Write-File 'Main\data\utf8-bom.txt' ($czSentence + $crlf) $utf8Bom
    Write-File 'Main\data\cp1250.txt' ($czSentence + $crlf) $cp1250
    Write-File 'Main\data\counter.txt' ('0' + $crlf)
    Write-Bytes 'Main\assets\logo.png' ([Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=='))

    # Exit code 1 is expected: tf add reports the intentionally ignored bin folder as skipped.
    Invoke-Tf @('add', 'Main', '/recursive', '/noprompt') -AllowedExitCodes @(0, 1) | Out-Null
    Submit-Changes 'initial' 'Initial content' | Out-Null

    # CS: edit
    Invoke-Tf @('checkout', 'Main\src\Program.cs') | Out-Null
    Write-File 'Main\src\Program.cs' ((@(
        'using System;',
        '',
        'namespace TfpTest',
        '{',
        '    internal static class Program',
        '    {',
        '        private static void Main(string[] args)',
        '        {',
        '            Console.WriteLine(Greeting("world"));',
        '        }',
        '',
        '        private static string Greeting(string name)',
        '        {',
        '            return "Hello, " + name + "!";',
        '        }',
        '    }',
        '}'
    ) -join $crlf) + $crlf)
    Submit-Changes 'editProgram' 'Add greeting to Program' | Out-Null

    # CS: edit of two files, comment with a ticket id for history search tests
    Invoke-Tf @('checkout', 'Main\src\Utils\StringHelper.cs', 'Main\README.md') | Out-Null
    Write-File 'Main\src\Utils\StringHelper.cs' ((@(
        'namespace TfpTest.Utils',
        '{',
        '    public static class StringHelper',
        '    {',
        '        public static string Clean(string value)',
        '        {',
        '            return value == null ? string.Empty : value.Trim();',
        '        }',
        '    }',
        '}'
    ) -join $crlf) + $crlf)
    Write-File 'Main\README.md' ((@(
        '# TfPortable test project',
        '',
        'Generated by seed-tfvc-test-repo.ps1. Do not edit by hand.',
        '',
        '- StringHelper.Clean accepts null.'
    ) -join $crlf) + $crlf)
    Submit-Changes 'bugfix123' 'Fix Clean for null input (BUG-123)' | Out-Null

    # CS: rename
    Invoke-Tf @('rename', 'Main\src\Utils\StringHelper.cs', 'Main\src\Utils\TextHelper.cs') | Out-Null
    Submit-Changes 'rename' 'Rename StringHelper to TextHelper' | Out-Null

    # CS: delete
    Invoke-Tf @('delete', 'Main\docs\obsolete.md') | Out-Null
    $deleteCs = Submit-Changes 'delete' 'Remove obsolete document'

    # Label on Main at the delete changeset
    Invoke-Tf @('label', "v1.0@$($script:ServerRoot)", "$($script:ServerRoot)/Main", '/recursive', "/version:C$deleteCs", '/comment:First test release', '/noprompt') | Out-Null

    # CS: branch
    Invoke-Tf @('branch', "$($script:ServerRoot)/Main", "$($script:ServerRoot)/Release", '/noprompt') | Out-Null
    Submit-Changes 'branch' 'Branch Main to Release' | Out-Null

    # CS: fix in Main
    Invoke-Tf @('checkout', 'Main\src\Program.cs') | Out-Null
    Write-File 'Main\src\Program.cs' ((@(
        'using System;',
        '',
        'namespace TfpTest',
        '{',
        '    internal static class Program',
        '    {',
        '        private static void Main(string[] args)',
        '        {',
        '            Console.WriteLine(Greeting(args.Length > 0 ? args[0] : "world"));',
        '        }',
        '',
        '        private static string Greeting(string name)',
        '        {',
        '            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Name is required.", "name");',
        '            return "Hello, " + name + "!";',
        '        }',
        '    }',
        '}'
    ) -join $crlf) + $crlf)
    $fixCs = Submit-Changes 'fixMain' 'Fix null check in Main (BUG-456)'

    # CS: merge the fix into Release
    Invoke-Tf @('merge', "$($script:ServerRoot)/Main", "$($script:ServerRoot)/Release", '/recursive', "/version:C$fixCs~C$fixCs", '/noprompt') | Out-Null
    Submit-Changes 'mergeToRelease' 'Merge BUG-456 fix from Main to Release' | Out-Null

    # CS: change in Main that stays unmerged (merge candidate)
    Invoke-Tf @('checkout', 'Main\README.md') | Out-Null
    Write-File 'Main\README.md' ((@(
        '# TfPortable test project',
        '',
        'Generated by seed-tfvc-test-repo.ps1. Do not edit by hand.',
        '',
        '- StringHelper.Clean accepts null.',
        '- Feature X (not merged to Release).'
    ) -join $crlf) + $crlf)
    Submit-Changes 'unmerged' 'Feature X (not merged to Release)' | Out-Null

    # Optional: many small changesets for history truncation tests
    for ($i = 1; $i -le $HistoryPadding; $i++) {
        Invoke-Tf @('checkout', 'Main\data\counter.txt') | Out-Null
        Write-File 'Main\data\counter.txt' ("$i" + $crlf)
        $number = Submit-Changes "padding$i" "History padding $i of $HistoryPadding"
        if ($i -eq 1) { $script:Changesets['paddingFirst'] = $number }
        if ($i -eq $HistoryPadding) { $script:Changesets['paddingLast'] = $number }
        $script:Changesets.Remove("padding$i")
    }
}
finally {
    Pop-Location
}

# ---------------------------------------------------------------------------
# Manifest
# ---------------------------------------------------------------------------

$manifest = [ordered]@{
    generatedBy  = 'seed-tfvc-test-repo.ps1'
    generatedAt  = (Get-Date).ToString('o')
    collection   = $Collection
    project      = $Project
    serverRoot   = $script:ServerRoot
    localRoot    = $LocalRoot
    projectLocal = $script:ProjectLocal
    workspace    = $WorkspaceName
    label        = "v1.0@$($script:ServerRoot)"
    changesets   = $script:Changesets
    files        = [ordered]@{
        program     = "$($script:ServerRoot)/Main/src/Program.cs"
        renamedFrom = "$($script:ServerRoot)/Main/src/Utils/StringHelper.cs"
        renamedTo   = "$($script:ServerRoot)/Main/src/Utils/TextHelper.cs"
        deleted     = "$($script:ServerRoot)/Main/docs/obsolete.md"
        czechName   = "$($script:ServerRoot)/Main/docs/$czFile"
        withSpaces  = "$($script:ServerRoot)/Main/src/File With Spaces.cs"
        utf8Bom     = "$($script:ServerRoot)/Main/data/utf8-bom.txt"
        cp1250      = "$($script:ServerRoot)/Main/data/cp1250.txt"
        lineEndLf   = "$($script:ServerRoot)/Main/data/sample-lf.txt"
        binary      = "$($script:ServerRoot)/Main/assets/logo.png"
        ignored     = 'Main\src\bin\Debug\ignored.txt'
    }
}
$manifestPath = Join-Path $LocalRoot 'seed-manifest.json'
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5), $script:Utf8NoBom)

Write-Host ''
Write-Host "Done. Manifest: $manifestPath" -ForegroundColor Green
$script:Changesets.GetEnumerator() | ForEach-Object { Write-Host ("  {0,-16} C{1}" -f $_.Key, $_.Value) }
