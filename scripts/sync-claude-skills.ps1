<#
.SYNOPSIS
  Copies skills/<name>/ to .claude/skills/<name>/ so Claude Code discovers the skill,
  and registers the skill's subagents (skills/<name>/agents/*.md) under .claude/agents/.
  The source of truth stays in skills/ - never edit the copies by hand.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/sync-claude-skills.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/sync-claude-skills.ps1 -Check
#>

param(
    [string]$Skill = 'issue-to-pr',
    # -Check changes nothing; it exits with 1 when a copy is missing or out of date.
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot (Join-Path 'skills' $Skill)
$target = Join-Path $repoRoot (Join-Path '.claude/skills' $Skill)
$agentsSource = Join-Path $source 'agents'
$agentsTarget = Join-Path $repoRoot '.claude/agents'

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

# SHA256 of one file, or '' when the file does not exist.
function Get-FileSignature([string]$path) {
    if (-not (Test-Path $path -PathType Leaf)) { return '' }
    return (Get-FileHash -Path $path -Algorithm SHA256).Hash
}

# Source files of skills/<name>/agents/*.md.
function Get-AgentSourceFiles {
    if (-not (Test-Path $agentsSource -PathType Container)) { return @() }
    return @(Get-ChildItem -Path $agentsSource -Filter '*.md' -File)
}

if ($Check) {
    $ok = $true

    # 1) The whole skill directory must match its generated copy.
    if ((Get-TreeSignature $source) -ne (Get-TreeSignature $target)) {
        Write-Host "Out of sync: $target" -ForegroundColor Red
        $ok = $false
    }

    # 2) Every subagent must be registered under .claude/agents/ with identical content.
    foreach ($file in (Get-AgentSourceFiles)) {
        $registered = Join-Path $agentsTarget $file.Name
        if ((Get-FileSignature $file.FullName) -ne (Get-FileSignature $registered)) {
            Write-Host "Out of sync: $registered" -ForegroundColor Red
            $ok = $false
        }
    }

    if (-not $ok) {
        Write-Host "Run: scripts/sync-claude-skills.ps1" -ForegroundColor Red
        exit 1
    }
    Write-Host "In sync: $target and $agentsTarget"
    exit 0
}

# Skill copy: recreated from scratch so files deleted in skills/ disappear too.
if (Test-Path $target) { Remove-Item -Path $target -Recurse -Force }
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force
Write-Host "Synced $source -> $target"

# Subagent registration: .claude/agents/ is shared, so only the files that exist in
# skills/<name>/agents/ are copied (other agents in that directory are left alone).
$agentFiles = Get-AgentSourceFiles
if ($agentFiles.Count -gt 0) {
    New-Item -ItemType Directory -Path $agentsTarget -Force | Out-Null
    foreach ($file in $agentFiles) {
        Copy-Item -Path $file.FullName -Destination $agentsTarget -Force
        Write-Host "Registered agent: $(Join-Path $agentsTarget $file.Name)"
    }
}
