<#
.SYNOPSIS
  Collects the objective benchmark metrics of one finished run (issue #56).

.DESCRIPTION
  Run it on the checked-out branch of a finished run (the working tree must
  match HEAD). It compares HEAD with the base branch and records:

    - M1  time to PR (needs -StartedAt and -PullRequest, otherwise the time
          of the last commit is reported as a fallback);
    - the commit list and the number of "fix:" commits (a proxy for repair
          cycles - the authoritative M4 values come from the run record);
    - M5a .editorconfig violations of all changed files per rule
          (FINALNEWLINE, ENDOFLINE, CHARSET, WHITESPACE) and the result of
          "dotnet format --verify-no-changes" on the changed files;
    - the CI verification pair (dotnet build + dotnet test) on the final state.

  The result is written to docs/benchmark/runs/<RunId>/metrics.json and a
  short summary is printed. Definitions: docs/benchmark/methodology.md.

.PARAMETER RunId
  Run identifier, for example h-autonomous, h-copilot, s-autonomous, s-copilot.

.PARAMETER StartedAt
  Moment the issue was submitted to the tool (any format [DateTimeOffset] parses).

.PARAMETER PullRequest
  Pull request number; its creation time is read with "gh pr view".

.PARAMETER EndedAt
  Pull request creation time entered by hand (any format [DateTimeOffset]
  parses). Takes precedence over "gh pr view"; use it when gh is not available.

.PARAMETER Base
  Base ref for the diff. Defaults to origin/main.

.PARAMETER SkipTests
  Skip dotnet build and dotnet test.

.PARAMETER SkipFormat
  Skip dotnet format --verify-no-changes.

.EXAMPLE
  pwsh scripts/collect-run-metrics.ps1 -RunId s-autonomous -StartedAt "2026-11-20 09:30 +01:00" -PullRequest 70
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RunId,

    [string]$StartedAt,

    [int]$PullRequest = 0,

    [string]$EndedAt,

    [string]$Base = 'origin/main',

    [switch]$SkipTests,

    [switch]$SkipFormat
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# PowerShell 7.4+ can turn a non-zero exit code of a native command into an error; the exit codes
# of dotnet format, build and test are data for this script, not failures of the script itself.
$PSNativeCommandUseErrorActionPreference = $false

$solution = 'UAI.ZeroVP.sln'
# git prints UTF-8; decode it as such so paths with diacritics (for example D:\Skola) survive.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repoRoot
if (-not (Test-Path $solution)) { throw "Run this script inside the repository ($solution not found in $repoRoot)." }

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Invoke-Git {
    param([string[]]$GitArgs)
    $output = & git -c core.quotepath=off @GitArgs
    if ($LASTEXITCODE -ne 0) { throw "git $($GitArgs -join ' ') failed with exit code $LASTEXITCODE." }
    return $output
}

# Runs a native command and returns its exit code and combined stdout/stderr text.
# Never throws on a non-zero exit code (or on text written to stderr): a failing
# dotnet format, build or test is a measurement result, not a reason to stop.
function Invoke-Native {
    param([string]$FilePath, [string[]]$Arguments)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = @(& $FilePath @Arguments 2>&1 | ForEach-Object { $_.ToString() })
        $exitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
    return [pscustomobject]@{ ExitCode = $exitCode; Output = ($lines -join "`n") }
}

# Last lines of a text, to keep the reason of a failure in metrics.json.
function Get-Tail {
    param([string]$Text, [int]$Count = 30)
    $all = @($Text -split "`r?`n" | Where-Object { $_.Trim() })
    if ($all.Count -le $Count) { return ($all -join "`n") }
    return (($all | Select-Object -Last $Count) -join "`n")
}

# Expected line ending per file, mirroring .editorconfig and .gitattributes.
function Get-ExpectedEol {
    param([string]$Path)
    $name = [System.IO.Path]::GetFileName($Path)
    $ext = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
    $lfExtensions = @('.md', '.sh', '.conf', '.env')
    $lfNames = @('Dockerfile', '.dockerignore', '.git-blame-ignore-revs')
    if ($lfExtensions -contains $ext) { return 'LF' }
    if ($lfNames -contains $name -or $name -like 'Dockerfile.*') { return 'LF' }
    return 'CRLF'
}

# Checks one file against the whitespace-level rules of .editorconfig.
function Test-EditorConfigFile {
    param([string]$Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $violations = @{ FINALNEWLINE = 0; ENDOFLINE = 0; CHARSET = 0; WHITESPACE = 0 }
    if ($bytes.Length -eq 0) { return $violations }

    # FINALNEWLINE: the last byte must be LF.
    if ($bytes[$bytes.Length - 1] -ne 10) { $violations.FINALNEWLINE = 1 }

    # CHARSET: BOM only for *.cs.
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $wantsBom = ([System.IO.Path]::GetExtension($Path).ToLowerInvariant() -eq '.cs')
    if ($hasBom -ne $wantsBom) { $violations.CHARSET = 1 }

    # ENDOFLINE: count lines whose ending does not match the expectation.
    $expected = Get-ExpectedEol -Path $Path
    $crlf = 0
    $lfOnly = 0
    $crOnly = 0
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($bytes[$i] -eq 10) {
            if ($i -gt 0 -and $bytes[$i - 1] -eq 13) { $crlf++ } else { $lfOnly++ }
        }
        elseif ($bytes[$i] -eq 13 -and ($i + 1 -ge $bytes.Length -or $bytes[$i + 1] -ne 10)) {
            $crOnly++
        }
    }
    if ($expected -eq 'CRLF') { $violations.ENDOFLINE = $lfOnly + $crOnly } else { $violations.ENDOFLINE = $crlf + $crOnly }

    # WHITESPACE: trailing spaces or tabs at the end of a line.
    $text = $utf8NoBom.GetString($bytes)
    $violations.WHITESPACE = ([regex]::Matches($text, '[ \t]+(?=\r?\n|\r|$)')).Count

    return $violations
}

# ---------------------------------------------------------------- commits ---
$mergeBase = (Invoke-Git @('merge-base', $Base, 'HEAD') | Select-Object -First 1).Trim()
$headSha = (Invoke-Git @('rev-parse', 'HEAD') | Select-Object -First 1).Trim()

$commitLines = @(Invoke-Git @('log', '--reverse', '--format=%h%x09%cI%x09%s', "$mergeBase..HEAD"))
$commits = @()
foreach ($line in $commitLines) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $parts = $line -split "`t", 3
    $commits += [pscustomobject]@{ sha = $parts[0]; committedAt = $parts[1]; subject = $parts[2] }
}
if ($commits.Count -eq 0) { throw "No commits between $Base and HEAD. Check out the branch of the run first." }
$fixCommits = @($commits | Where-Object { $_.subject -match '^fix(\(.+\))?:' })

# ------------------------------------------------------------ time to PR ---
$timing = [ordered]@{
    startedAt  = $null
    endedAt    = $null
    endedKind  = $null
    totalSeconds = $null
    pullRequestUrl = $null
}
$culture = [System.Globalization.CultureInfo]::InvariantCulture
if ($StartedAt) {
    $start = [DateTimeOffset]::Parse($StartedAt, $culture)
    $timing.startedAt = $start.ToString('o')

    $end = $null
    if ($EndedAt) {
        $end = [DateTimeOffset]::Parse($EndedAt, $culture)
        $timing.endedKind = 'pullRequestCreatedAt (entered manually with -EndedAt)'
    }
    if ($null -eq $end -and $PullRequest -gt 0) {
        if (Get-Command gh -ErrorAction SilentlyContinue) {
            $prJson = & gh pr view $PullRequest --json createdAt,url
            if ($LASTEXITCODE -eq 0) {
                $pr = $prJson | ConvertFrom-Json
                $end = [DateTimeOffset]::Parse([string]$pr.createdAt, $culture)
                $timing.endedKind = 'pullRequestCreatedAt'
                $timing.pullRequestUrl = [string]$pr.url
            }
            else { Write-Warning 'gh pr view failed; falling back to the last commit time.' }
        }
        else { Write-Warning 'gh CLI not found; falling back to the last commit time.' }
    }
    if ($null -eq $end) {
        $end = [DateTimeOffset]::Parse($commits[$commits.Count - 1].committedAt, $culture)
        $timing.endedKind = 'lastCommitTime (fallback, not the PR time)'
    }
    $timing.endedAt = $end.ToString('o')
    $timing.totalSeconds = [int][Math]::Round(($end - $start).TotalSeconds)
}
else {
    Write-Warning 'No -StartedAt given: M1 is not computed.'
}

# --------------------------------------------------- changed files + M5a ---
$changed = @(Invoke-Git @('diff', '--name-only', '--diff-filter=AMR', "$mergeBase...HEAD") | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

$fileResults = @()
$totals = @{ FINALNEWLINE = 0; ENDOFLINE = 0; CHARSET = 0; WHITESPACE = 0 }
$cleanFiles = 0
$checkedFiles = 0
$binaryExtensions = @('.png', '.jpg', '.jpeg', '.gif', '.ico', '.zip', '.dll', '.exe', '.pdf', '.mp4')
foreach ($file in $changed) {
    $ext = [System.IO.Path]::GetExtension($file).ToLowerInvariant()
    if ($binaryExtensions -contains $ext) { continue }
    if (-not (Test-Path -LiteralPath $file)) { continue }
    $checkedFiles++
    $v = Test-EditorConfigFile -Path $file
    $sum = $v.FINALNEWLINE + $v.ENDOFLINE + $v.CHARSET + $v.WHITESPACE
    if ($sum -eq 0) { $cleanFiles++ }
    foreach ($key in @('FINALNEWLINE', 'ENDOFLINE', 'CHARSET', 'WHITESPACE')) { $totals[$key] += $v[$key] }
    $fileResults += [pscustomobject]@{
        file = $file
        FINALNEWLINE = $v.FINALNEWLINE
        ENDOFLINE = $v.ENDOFLINE
        CHARSET = $v.CHARSET
        WHITESPACE = $v.WHITESPACE
    }
}

# dotnet format on the changed files under src/ and tests/.
$formatResult = [ordered]@{ ran = $false; exitCode = $null; passed = $null; diagnostics = @{} }
if (-not $SkipFormat) {
    $formatFiles = @($changed | Where-Object { ($_ -like 'src/*' -or $_ -like 'tests/*') -and (Test-Path -LiteralPath $_) })
    if ($formatFiles.Count -gt 0) {
        $formatArgs = @('format', $solution, '--verify-no-changes', '--no-restore', '--include') + $formatFiles
        $formatRun = Invoke-Native -FilePath 'dotnet' -Arguments $formatArgs
        $formatOutput = $formatRun.Output
        $formatResult.ran = $true
        $formatResult.exitCode = $formatRun.ExitCode
        $formatResult.passed = ($formatRun.ExitCode -eq 0)
        if ($formatRun.ExitCode -ne 0) { $formatResult.outputTail = Get-Tail -Text $formatOutput }
        # Count diagnostic ids only; the message text is localized and never parsed.
        foreach ($m in [regex]::Matches($formatOutput, '\): (?:error|warning) (?<id>[A-Za-z0-9_]+):')) {
            $id = $m.Groups['id'].Value
            if ($formatResult.diagnostics.ContainsKey($id)) { $formatResult.diagnostics[$id]++ } else { $formatResult.diagnostics[$id] = 1 }
        }
    }
    else { Write-Warning 'No changed files under src/ or tests/: dotnet format skipped.' }
}

# ------------------------------------------------ CI verification (final) ---
$verification = [ordered]@{ ran = $false; buildExitCode = $null; testExitCode = $null; total = $null; passed = $null; failed = $null }
if (-not $SkipTests) {
    $verification.ran = $true
    $buildRun = Invoke-Native -FilePath 'dotnet' -Arguments @('build', $solution, '--configuration', 'Release')
    $verification.buildExitCode = $buildRun.ExitCode
    if ($buildRun.ExitCode -ne 0) { $verification.buildOutputTail = Get-Tail -Text $buildRun.Output }
    if ($buildRun.ExitCode -eq 0) {
        $resultsDir = Join-Path ([System.IO.Path]::GetTempPath()) ("benchmark-" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $resultsDir | Out-Null
        $testRun = Invoke-Native -FilePath 'dotnet' -Arguments @('test', $solution, '--configuration', 'Release', '--no-build',
            '--filter', 'Category!=Performance', '--logger', 'trx;LogFileName=metrics.trx', '--results-directory', $resultsDir)
        $verification.testExitCode = $testRun.ExitCode
        if ($testRun.ExitCode -ne 0) { $verification.testOutputTail = Get-Tail -Text $testRun.Output }
        $trx = Get-ChildItem -Path $resultsDir -Filter '*.trx' -Recurse | Select-Object -First 1
        if ($null -ne $trx) {
            [xml]$trxXml = Get-Content -LiteralPath $trx.FullName -Raw
            $counters = $trxXml.TestRun.ResultSummary.Counters
            $verification.total = [int]$counters.total
            $verification.passed = [int]$counters.passed
            $verification.failed = [int]$counters.failed
        }
        Remove-Item -LiteralPath $resultsDir -Recurse -Force
    }
}

# ----------------------------------------------------------------- output ---
$result = [ordered]@{
    runId = $RunId
    collectedAt = (Get-Date).ToString('o')
    base = $Base
    mergeBase = $mergeBase
    head = $headSha
    timing = $timing
    commits = [ordered]@{
        count = $commits.Count
        fixCommitCount = $fixCommits.Count
        note = 'fix: commit count is only a proxy for repair cycles; use the run record for M4.'
        list = $commits
    }
    editorConfig = [ordered]@{
        filesChecked = $checkedFiles
        filesClean = $cleanFiles
        adherencePercent = $(if ($checkedFiles -gt 0) { [Math]::Round(100.0 * $cleanFiles / $checkedFiles, 1) } else { $null })
        violations = $totals
        files = $fileResults
        dotnetFormat = $formatResult
    }
    verification = $verification
}

$outDir = Join-Path $repoRoot (Join-Path 'docs/benchmark/runs' $RunId)
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$outFile = Join-Path $outDir 'metrics.json'
# *.json uses CRLF and a final newline (.editorconfig).
$json = ($result | ConvertTo-Json -Depth 8) -replace "\r?\n", "`r`n"
[System.IO.File]::WriteAllText($outFile, $json + "`r`n", $utf8NoBom)

Write-Host ''
Write-Host "Run $RunId  ($($commits.Count) commits, $($fixCommits.Count) fix: commits)"
if ($null -ne $timing.totalSeconds) {
    Write-Host ("M1 time to PR: {0} ({1})" -f ([TimeSpan]::FromSeconds($timing.totalSeconds).ToString()), $timing.endedKind)
}
Write-Host ("M5a editorconfig: {0}/{1} files clean; FINALNEWLINE={2} ENDOFLINE={3} CHARSET={4} WHITESPACE={5}" -f `
    $cleanFiles, $checkedFiles, $totals.FINALNEWLINE, $totals.ENDOFLINE, $totals.CHARSET, $totals.WHITESPACE)
if ($formatResult.ran) { Write-Host ("dotnet format --verify-no-changes: {0}" -f $(if ($formatResult.passed) { 'pass' } else { 'FAIL' })) }
if ($verification.ran) {
    Write-Host ("CI pair: build exit {0}, test exit {1}, tests {2} total / {3} passed / {4} failed" -f `
        $verification.buildExitCode, $verification.testExitCode, $verification.total, $verification.passed, $verification.failed)
}
Write-Host "Written: $outFile"
