<#
.SYNOPSIS
  Starts N local instances of the API on consecutive ports (BasePort, BasePort+1, ...).
.DESCRIPTION
  Builds the API once, then starts every instance as its own process with:
    - its own port         (--urls http://localhost:<port>)
    - its own instance id  (Instance__Id = <IdPrefix>-<n>)
    - its own log file     (FileLogging__IncludeInstanceId = true -> zerovp-<id>-yyyyMMdd.log)
  Press Ctrl+C to stop all instances. Data is NOT shared between instances yet
  (in-memory storage per process); see the README section "Vice instanci na localhostu".
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/run-instances.ps1 -Count 3
.EXAMPLE
  pwsh scripts/run-instances.ps1 -Count 2 -BasePort 6000 -IdPrefix node -NoBuild
#>

[CmdletBinding()]
param(
    # How many instances to start.
    [ValidateRange(1, 50)]
    [int]$Count = 3,

    # Port of the first instance; the others use the following ports.
    [ValidateRange(1, 65535)]
    [int]$BasePort = 5258,

    # Instance ids are <IdPrefix>-1, <IdPrefix>-2, ...
    [ValidatePattern('^[A-Za-z0-9._-]+$')]
    [string]$IdPrefix = 'api',

    # ASPNETCORE_ENVIRONMENT of the instances (Development turns on Swagger UI).
    [string]$EnvironmentName = 'Development',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    # Skip the build and use the existing binaries.
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $root 'src/UAI.ZeroVP.API'
$project = Join-Path $projectDir 'UAI.ZeroVP.API.csproj'
$dll = Join-Path $projectDir "bin/$Configuration/net10.0/UAI.ZeroVP.API.dll"
$lastPort = $BasePort + $Count - 1

if ($lastPort -gt 65535) {
    throw "Ports $BasePort..$lastPort are out of range (max 65535). Lower -Count or -BasePort."
}

function Test-PortFree([int]$Port) {
    $listener = $null
    try {
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
        $listener.Start()
        return $true
    }
    catch {
        return $false
    }
    finally {
        if ($listener) { $listener.Stop() }
    }
}

# Fail early with a clear message instead of a crashing instance.
$busy = $BasePort..$lastPort | Where-Object { -not (Test-PortFree $_) }
if ($busy) {
    throw "Port(s) already in use: $($busy -join ', '). Choose another -BasePort."
}

if (-not $NoBuild) {
    # Build once. Several parallel "dotnet run" would race on the same obj/bin folders.
    Write-Host "==> Building ($Configuration)"
    dotnet build $project --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}

if (-not (Test-Path $dll)) {
    throw "Binaries not found: $dll. Run without -NoBuild or use the matching -Configuration."
}

$saved = @{
    ASPNETCORE_ENVIRONMENT      = $env:ASPNETCORE_ENVIRONMENT
    Instance__Id                = $env:Instance__Id
    FileLogging__IncludeInstanceId = $env:FileLogging__IncludeInstanceId
}

$processes = @()

try {
    for ($i = 1; $i -le $Count; $i++) {
        $port = $BasePort + $i - 1
        $id = "$IdPrefix-$i"

        # Child processes inherit these variables.
        $env:ASPNETCORE_ENVIRONMENT = $EnvironmentName
        $env:Instance__Id = $id
        $env:FileLogging__IncludeInstanceId = 'true'

        # --contentRoot: appsettings.json and the logs/ folder are taken from the project folder,
        # exactly like with "dotnet run".
        # Paths are quoted by hand: Start-Process does not quote array items (matters for paths with spaces).
        $arguments = @("`"$dll`"", '--urls', "http://localhost:$port", '--contentRoot', "`"$projectDir`"")
        $processes += Start-Process -FilePath 'dotnet' -ArgumentList $arguments -NoNewWindow -PassThru

        Write-Host ("==> {0,-10} http://localhost:{1}  (log: {2}/logs/zerovp-{0}-*.log)" -f $id, $port, 'src/UAI.ZeroVP.API')
    }

    Write-Host ''
    Write-Host "Started $Count instance(s). Check them with:  curl http://localhost:$BasePort/api/info"
    Write-Host 'Press Ctrl+C to stop all instances.'

    Wait-Process -Id ($processes | ForEach-Object { $_.Id })
}
finally {
    foreach ($process in $processes) {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
        }
    }

    foreach ($name in $saved.Keys) {
        Set-Item -Path "Env:$name" -Value $saved[$name] -ErrorAction SilentlyContinue
        if ($null -eq $saved[$name]) { Remove-Item -Path "Env:$name" -ErrorAction SilentlyContinue }
    }
}