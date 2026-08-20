<#
.SYNOPSIS
    AgilePlus DAG-driven dispatch loop for Compound-Spheres-3D.

.DESCRIPTION
    Reads work packages from .agileplus/compound-spheres.db, claims the next
    ready package, and dispatches it to the agent runner. Operates the same
    next-ready -> claim -> dispatch loop as WorldSphereMod's agileplus-dispatch.

.NOTES
    DB:  .agileplus/compound-spheres.db
    Build: dotnet build CompoundMeshes.sln -c Release
    Tests: dotnet test CompoundMeshes.Tests
#>
param(
    [string]$DbPath = ".agileplus/compound-spheres.db",
    [string]$BuildCmd = "dotnet build CompoundMeshes.sln -c Release",
    [string]$TestCmd  = "dotnet test Tests/CompoundMeshes.Tests/",
    [int]$MaxRetries  = 3
)

$ErrorActionPreference = "Stop"

# Ensure DB exists
if (-not (Test-Path $DbPath)) {
    Write-Host "[dispatch] DB not found at $DbPath — creating initial schema"
    $dir = Split-Path $DbPath -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
}

Write-Host "[dispatch] AgilePlus dispatch loop starting (db=$DbPath)"
Write-Host "[dispatch] Build: $BuildCmd"
Write-Host "[dispatch] Test:  $TestCmd"
Write-Host "[dispatch] Max retries: $MaxRetries"

# Main loop placeholder — actual implementation uses sqlite3 CLI or .NET sqlite package
# to query the work_packages table, claim next ready package, dispatch to agent runner,
# and update status on completion/failure.
Write-Host "[dispatch] TODO: implement sqlite3 query loop against $DbPath"
Write-Host "[dispatch] Work packages should be defined in AGENTS.md L30 definitions"
