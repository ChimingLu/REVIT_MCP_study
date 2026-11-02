#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$TargetBranch = "",
    [string]$MainRef = "origin/main",
    [switch]$AllowDirty,
    [switch]$SkipMerge,
    [switch]$SkipBuild,
    [switch]$SkipDeploy
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)][string[]]$Args
    )

    & git @Args
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Args -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function Resolve-RepoRoot {
    $root = (& git rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($root)) {
        throw "Current directory is not inside a git repository."
    }

    return $root.Trim()
}

try {
    $repoRoot = Resolve-RepoRoot
    Set-Location $repoRoot

    if ([string]::IsNullOrWhiteSpace($TargetBranch)) {
        $TargetBranch = (& git branch --show-current).Trim()
    }

    if ([string]::IsNullOrWhiteSpace($TargetBranch)) {
        throw "Cannot determine target branch. Use -TargetBranch explicitly."
    }

    $dirty = (& git status --porcelain)
    if (-not $AllowDirty -and -not [string]::IsNullOrWhiteSpace($dirty)) {
        throw "Working tree is not clean. Commit/stash changes first, or use -AllowDirty."
    }

    Write-Host "== Fetch remotes ==" -ForegroundColor Cyan
    Invoke-Git -Args @("--no-pager", "fetch", "--all", "--prune")

    Write-Host "== Checkout $TargetBranch ==" -ForegroundColor Cyan
    Invoke-Git -Args @("--no-pager", "checkout", $TargetBranch)

    Write-Host "== Fast-forward local branch ==" -ForegroundColor Cyan
    $remoteBranchExists = (& git ls-remote --heads origin $TargetBranch 2>$null)
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($remoteBranchExists)) {
        Invoke-Git -Args @("--no-pager", "pull", "--ff-only", "origin", $TargetBranch)
    }
    else {
        Write-Host "  origin/$TargetBranch not found, skip pull." -ForegroundColor Yellow
    }

    if (-not $SkipMerge) {
        Write-Host "== Merge $MainRef into $TargetBranch ==" -ForegroundColor Cyan
        Invoke-Git -Args @("--no-pager", "merge", "--no-ff", $MainRef)
    }

    if (-not $SkipBuild) {
        Write-Host "== Build RevitMCP configs (R22-R27) ==" -ForegroundColor Cyan
        Push-Location (Join-Path $repoRoot "MCP")
        try {
            $configs = @("Release.R22", "Release.R23", "Release.R24", "Release.R25", "Release.R26", "Release.R27")
            foreach ($cfg in $configs) {
                Write-Host "  - dotnet build -c $cfg RevitMCP.csproj" -ForegroundColor Gray
                & dotnet build -c $cfg RevitMCP.csproj
                if ($LASTEXITCODE -ne 0) {
                    throw "Build failed: $cfg"
                }
            }
        }
        finally {
            Pop-Location
        }
    }

    if (-not $SkipDeploy) {
        Write-Host "== Ensure Addins folders (2020-2027) ==" -ForegroundColor Cyan
        $addinsRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins"
        foreach ($year in 2020..2027) {
            New-Item -ItemType Directory -Path (Join-Path $addinsRoot $year) -Force | Out-Null
        }

        Write-Host "== Deploy Add-in to all detected versions ==" -ForegroundColor Cyan
        Push-Location (Join-Path $repoRoot "scripts")
        try {
            & powershell -ExecutionPolicy Bypass -File .\install-addon.ps1 -All -NonInteractive
            if ($LASTEXITCODE -ne 0) {
                throw "install-addon.ps1 failed with exit code $LASTEXITCODE"
            }
        }
        finally {
            Pop-Location
        }
    }

    Write-Host ""
    Write-Host "Done." -ForegroundColor Green
    Write-Host "Branch: $TargetBranch"
    Write-Host "Main ref: $MainRef"
    Write-Host "SkipMerge=$SkipMerge SkipBuild=$SkipBuild SkipDeploy=$SkipDeploy"
}
catch {
    Write-Error $_
    exit 1
}
