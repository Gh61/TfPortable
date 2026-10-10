<#
.SYNOPSIS
    Decompiles tf.exe and the assemblies that implement its commands into a local, git-ignored folder.

.DESCRIPTION
    Finds TF.exe (vswhere, or -TfPath), installs the ILSpy command line tool when it is missing (`dotnet tool
    install -g ilspycmd`), decompiles TF.exe and every Microsoft.TeamFoundation.VersionControl.* assembly next to it
    into local\decomp\<tf version>\<assembly>\ and writes local\decomp\<tf version>\index.md with the assembly list,
    the versions and the command classes (tf.exe names them Command<Name>.cs under
    TF\Microsoft.TeamFoundation.VersionControl.CommandLine\). Nothing under local\ is committed (.gitignore).
    The rules for using the output are in docs/tf-reference/README.md.

.PARAMETER TfPath
    Full path to TF.exe. Found automatically via vswhere when omitted.

.PARAMETER OutDir
    Root of the decompiled sources. Default: <repo>\local\decomp

.EXAMPLE
    .\tools\decompile-tf.ps1
#>
[CmdletBinding()]
param(
    [string] $TfPath,
    [string] $OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'local\decomp')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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
    throw 'tf.exe not found. Install Visual Studio with Team Explorer, or pass -TfPath.'
}

$tf = Find-Tf $TfPath
# FileVersion of tf.exe 18 reads "18.10.12215.6 built by: releases/dev18/18.10 (...)", which is not a folder name.
$versionInfo = (Get-Item -LiteralPath $tf).VersionInfo
$version = '{0}.{1}.{2}.{3}' -f $versionInfo.FileMajorPart, $versionInfo.FileMinorPart, $versionInfo.FileBuildPart, $versionInfo.FilePrivatePart
$folder = Split-Path -Parent $tf
$target = Join-Path $OutDir $version
New-Item -ItemType Directory -Force -Path $target | Out-Null

$ilspy = Get-Command ilspycmd -ErrorAction SilentlyContinue
if (-not $ilspy) {
    Write-Host 'Installing ilspycmd (dotnet tool install -g ilspycmd)...' -ForegroundColor Cyan
    dotnet tool install -g ilspycmd | Out-Host
    $ilspy = Get-Command ilspycmd -ErrorAction Stop
}

# TF.exe, the version control assemblies next to it, and Microsoft.TeamFoundation.Client.dll, which holds the
# command-line base classes (Command, CommandLine, ExitCode, Options, ConsoleHost) that decide exit codes and streams.
$assemblies = @(Get-Item -LiteralPath $tf) + @(Get-ChildItem -LiteralPath $folder -Filter 'Microsoft.TeamFoundation.VersionControl.*.dll')
$clientDll = Join-Path $folder 'Microsoft.TeamFoundation.Client.dll'
if (Test-Path -LiteralPath $clientDll) { $assemblies += Get-Item -LiteralPath $clientDll }
$index = New-Object System.Collections.Generic.List[string]
$index.Add("# Decompiled tf.exe $version")
$index.Add('')
$index.Add("Source folder: ``$folder``  ")
$index.Add("Decompiled on $(Get-Date -Format 'yyyy-MM-dd') with $($ilspy.Source). Reference only, never committed (docs/tf-reference/README.md).")
$index.Add('')
$index.Add('| Assembly | File version | Output |')
$index.Add('| --- | --- | --- |')
foreach ($assembly in $assemblies) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($assembly.Name)
    $out = Join-Path $target $name
    Write-Host "Decompiling $($assembly.Name)..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    & $ilspy.Source -p -o $out $assembly.FullName | Out-Host
    $index.Add("| $($assembly.Name) | $($assembly.VersionInfo.FileVersion) | ``$name\`` |")
}

$index.Add('')
$index.Add('## Command classes')
$index.Add('')
# tf.exe names its command classes Command<Name>.cs (plus the GitCommand*/SettingsCommand* and the base classes);
# only TF.exe's own folder is indexed, the UI assemblies contain thousands of unrelated *Command classes.
$tfFolder = Join-Path $target ([System.IO.Path]::GetFileNameWithoutExtension((Get-Item -LiteralPath $tf).Name))
$commands = Get-ChildItem -LiteralPath $tfFolder -Recurse -Include 'Command*.cs', '*Command.cs' | Sort-Object Name
foreach ($file in $commands) {
    $index.Add("- ``$($file.FullName.Substring($target.Length + 1))``")
}
[System.IO.File]::WriteAllText((Join-Path $target 'index.md'), (($index -join "`r`n") + "`r`n"), (New-Object System.Text.UTF8Encoding($false)))

Write-Host ''
Write-Host "Done: $target ($($commands.Count) command classes listed in index.md)." -ForegroundColor Green
Write-Host 'Next: answer the questions in docs/tf-reference/questions.md into docs/tf-reference/<command>.md, or hand the folder to the agent.' -ForegroundColor Yellow
