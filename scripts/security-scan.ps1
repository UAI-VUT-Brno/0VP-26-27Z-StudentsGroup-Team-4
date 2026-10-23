<#
.SYNOPSIS
  Lokalni bezpecnostni kontrola projektu (stejne kontroly bezi i v CI).
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/security-scan.ps1
#>

param(
    # V CI nastavujeme: chybejici gitleaks je chyba, ne jen varovani.
    [switch]$RequireGitleaks
)

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
$skipped = $false

if (Get-Command gitleaks -ErrorAction SilentlyContinue) {
    # gitleaks loguje na stderr; ve Windows PowerShellu 5.1 by to s 'Stop' vyhodilo vyjimku.
    $prevPref = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $gitleaksOutput = gitleaks detect --source . --no-banner --verbose 2>&1
    $gitleaksExit = $LASTEXITCODE
    $ErrorActionPreference = $prevPref

    if ($gitleaksExit -ne 0) {
        $gitleaksOutput | ForEach-Object { Write-Host $_ }
        Write-Host 'Chyba: nalezeny unikajici secrety!' -ForegroundColor Red
        $failed = $true
    }
    else {
        Write-Host 'OK: zadne secrety nenalezeny.' -ForegroundColor Green
    }
}
elseif ($RequireGitleaks) {
    Write-Host "Chyba: 'gitleaks' neni nainstalovan a je vyzadovan (-RequireGitleaks)." -ForegroundColor Red
    $failed = $true
}
else {
    Write-Host "Varovani: 'gitleaks' neni nainstalovan, kontrola secretu PRESKOCENA." -ForegroundColor Yellow
    Write-Host '  Instalace: winget install gitleaks'
    $skipped = $true
}
Write-Host ''

if ($failed) {
    Write-Host 'SECURITY SCAN: SELHAL' -ForegroundColor Red
    exit 1
}
if ($skipped) {
    Write-Host 'SECURITY SCAN: OK (ale kontrola secretu byla preskocena)' -ForegroundColor Yellow
    exit 0
}
Write-Host 'SECURITY SCAN: OK' -ForegroundColor Green