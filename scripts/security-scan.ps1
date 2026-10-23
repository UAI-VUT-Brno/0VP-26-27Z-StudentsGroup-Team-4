<#
.SYNOPSIS
  Lokalni bezpecnostni kontrola projektu (stejne kontroly bezi i v CI).
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/security-scan.ps1
#>
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)   # koren repozitare
$failed = $false

Write-Host '==> [1/2] Zranitelne NuGet balicky'

dotnet restore UAI.ZeroVP.sln
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Restore selhal (pravdepodobne zranitelny balicek, viz chyby NU190x vyse).' -ForegroundColor Red
    $failed = $true
}
else {
    # JSON vystup nezavisi na jazyce SDK (u tebe cesky), takze se da spolehlive parsovat.
    $json = dotnet list UAI.ZeroVP.sln package --vulnerable --include-transitive --format json --output-version 1 |
        Out-String | ConvertFrom-Json

    $findings = foreach ($project in $json.projects) {
        foreach ($fw in $project.frameworks) {
            foreach ($pkg in (@($fw.topLevelPackages) + @($fw.transitivePackages))) {
                foreach ($v in $pkg.vulnerabilities) {
                    [pscustomobject]@{
                        Project  = Split-Path $project.path -Leaf
                        Package  = $pkg.id
                        Version  = $pkg.resolvedVersion
                        Severity = $v.severity
                        Advisory = $v.advisoryurl
                    }
                }
            }
        }
    }

    if ($findings) {
        $findings | Format-Table -AutoSize | Out-String | Write-Host
        $failed = $true
    }
    else {
        Write-Host 'OK: zadne zname zranitelnosti.' -ForegroundColor Green
    }
}

Write-Host ""
Write-Host '==> [2/2] Detekce secretu (Gitleaks)'
# Ocekavame, ze na lokalu mozna gitleaks vyvojar jeste nema
if (Get-Command gitleaks -ErrorAction SilentlyContinue) {
    # Spustime gitleaks v koreni repozitare
    $gitleaksOutput = gitleaks detect --source . --no-banner --verbose 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host $gitleaksOutput
        Write-Host "Chyba: Nalezeny unikajici secrety!" -ForegroundColor Red
        $failed = $true
    } else {
        Write-Host "OK: Zadne secrety nenalezeny." -ForegroundColor Green
    }
} else {
    Write-Host "Varovani: 'gitleaks' neni nainstalovan." -ForegroundColor Yellow
    Write-Host "Pro lokalni detekci si ho stahni: https://github.com/gitleaks/gitleaks"
}
Write-Host ""

if ($failed) {
    Write-Host 'SECURITY SCAN: SELHAL' -ForegroundColor Red
    exit 1
}
Write-Host 'SECURITY SCAN: OK' -ForegroundColor Green