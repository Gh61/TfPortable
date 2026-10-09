<#
.SYNOPSIS
    Runs every pending golden capture in one sitting: phase 2, the extra2 gaps of phases 1 and 3, phase 3, then phase 4.

.DESCRIPTION
    Calls capture-tf-golden-phase2.ps1, capture-tf-golden-extra2.ps1, capture-tf-golden-phase3.ps1 and
    capture-tf-golden-phase4.ps1 with the same parameters and stops at the first failure. tf.exe asks for the sign-in once at most; every script checks the sign-in before it starts.
    The earlier captures (capture-tf-golden.ps1, capture-tf-golden-extra.ps1, capture-tf-help.ps1) are not repeated.

.PARAMETER Manifest
    Path to seed-manifest.json written by seed-tfvc-test-repo.ps1.

.PARAMETER OutDir
    Output folder. Default: .\tests\golden

.PARAMETER TfPath
    Full path to tf.exe. Found automatically via vswhere when omitted.

.PARAMETER WorkItem
    Id of an existing work item of the test project for checkin /associate. Default: 1

.PARAMETER SkipPhase2, SkipExtra2, SkipPhase3, SkipPhase4
    Leave a phase out, e.g. to repeat the other one after a failure.

.EXAMPLE
    .\capture-tf-golden-all.ps1 -Manifest D:\TfpTest\seed-manifest.json -OutDir .\tests\golden -WorkItem 1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Manifest,
    [string] $OutDir = '.\tests\golden',
    [string] $TfPath,
    [int] $WorkItem = 1,
    [string[]] $Redact = @(),
    [int] $TimeoutSeconds = 180,
    [ValidateSet('ansi', 'oem', 'utf8')] [string] $TfOutputEncoding = 'ansi',
    [switch] $SkipPhase2,
    [switch] $SkipExtra2,
    [switch] $SkipPhase3,
    [switch] $SkipPhase4
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$common = @{
    Manifest         = $Manifest
    OutDir           = $OutDir
    Redact           = $Redact
    TimeoutSeconds   = $TimeoutSeconds
    TfOutputEncoding = $TfOutputEncoding
}
if ($TfPath) { $common.TfPath = $TfPath }

$watch = [System.Diagnostics.Stopwatch]::StartNew()
if (-not $SkipPhase2) {
    Write-Host '=== Phase 2 captures ===' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'capture-tf-golden-phase2.ps1') @common
    Write-Host ''
}
if (-not $SkipExtra2) {
    Write-Host '=== Extra2 captures (gaps of phases 1 and 3) ===' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'capture-tf-golden-extra2.ps1') @common
    Write-Host ''
}
if (-not $SkipPhase3) {
    Write-Host '=== Phase 3 captures ===' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'capture-tf-golden-phase3.ps1') @common -WorkItem $WorkItem
}
if (-not $SkipPhase4) {
    Write-Host ''
    Write-Host '=== Phase 4 captures ===' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'capture-tf-golden-phase4.ps1') @common -WorkItem $WorkItem
}
$watch.Stop()
Write-Host ''
Write-Host "All captures done in $([int]$watch.Elapsed.TotalMinutes) min. Review the new files under $OutDir for personal data, then commit them with the index-phase*.json files." -ForegroundColor Green
