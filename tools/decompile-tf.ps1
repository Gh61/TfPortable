<#
.SYNOPSIS
    Decompiles tf.exe and the assemblies that implement its commands into a local, git-ignored folder.

.DESCRIPTION
    Finds TF.exe (vswhere, or -TfPath), installs the ILSpy command line tool when it is missing (`dotnet tool
    install -g ilspycmd`), decompiles TF.exe and every Microsoft.TeamFoundation.VersionControl.* assembly next to it
    into local\decomp\<tf version>\<assembly>\ and writes local\decomp\<tf version>\index.md with the assembly list,
    the versions and the classes whose name ends with "Command". Nothing under local\ is committed (.gitignore).
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
$version = (Get-Item -LiteralPath $tf).VersionInfo.FileVersion
$folder = Split-Path -Parent $tf
$target = Join-Path $OutDir $version
New-Item -ItemType Directory -Force -Path $target | Out-Null

$ilspy = Get-Command ilspycmd -ErrorAction SilentlyContinue
if (-not $ilspy) {
    Write-Host 'Installing ilspycmd (dotnet tool install -g ilspycmd)...' -ForegroundColor Cyan
    dotnet tool install -g ilspycmd | Out-Host
    $ilspy = Get-Command ilspycmd -ErrorAction Stop
}

# TF.exe plus the version control assemblies next to it; the client library of the NuGet package is the same code
# the agent already reads, so it is skipped unless it lives only here.
$assemblies = @(Get-Item -LiteralPath $tf) + @(Get-ChildItem -LiteralPath $folder -Filter 'Microsoft.TeamFoundation.VersionControl.*.dll')
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
$commands = Get-ChildItem -LiteralPath $target -Recurse -Filter '*Command.cs' | Sort-Object Name
foreach ($file in $commands) {
    $index.Add("- ``$($file.FullName.Substring($target.Length + 1))``")
}
[System.IO.File]::WriteAllText((Join-Path $target 'index.md'), (($index -join "`r`n") + "`r`n"), (New-Object System.Text.UTF8Encoding($false)))

Write-Host ''
Write-Host "Done: $target ($($commands.Count) command classes listed in index.md)." -ForegroundColor Green
Write-Host 'Next: answer the questions in docs/tf-reference/questions.md into docs/tf-reference/<command>.md, or hand the folder to the agent.' -ForegroundColor Yellow
