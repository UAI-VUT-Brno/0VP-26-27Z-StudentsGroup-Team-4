<#
.SYNOPSIS
  Copies skills/<name>/ to .claude/skills/<name>/ so Claude Code discovers the skill.
  The source of truth stays in skills/ - never edit the copy by hand.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/sync-claude-skills.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/sync-claude-skills.ps1 -Check
#>

param(
    [string]$Skill = 'issue-to-pr',
    # -Check changes nothing; it exits with 1 when the copy is missing or out of date.
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot (Join-Path 'skills' $Skill)
$target = Join-Path $repoRoot (Join-Path '.claude/skills' $Skill)

if (-not (Test-Path $source -PathType Container)) {
    throw "Source skill not found: $source"
}

# One line per file: "<relative path> <sha256>", sorted, so two trees can be compared as text.
function Get-TreeSignature([string]$root) {
    if (-not (Test-Path $root -PathType Container)) { return '' }
    $rootFull = (Resolve-Path $root).Path
    $lines = Get-ChildItem -Path $rootFull -Recurse -File -Force |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($rootFull.Length).TrimStart('\', '/').Replace('\', '/')
            '{0} {1}' -f $relative, (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash
        }
    return (@($lines) -join "`n")
}

if ($Check) {
    if ((Get-TreeSignature $source) -ne (Get-TreeSignature $target)) {
        Write-Host "Out of sync: $target. Run scripts/sync-claude-skills.ps1" -ForegroundColor Red
        exit 1
    }
    Write-Host "In sync: $target"
    exit 0
}

if (Test-Path $target) { Remove-Item -Path $target -Recurse -Force }
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force
Write-Host "Synced $source -> $target"