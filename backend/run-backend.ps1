# Run all backend microservices
Write-Host "Starting all backend services..." -ForegroundColor Cyan

$services = @(
    "Services/Identity.API",
    "Services/Smapi.API",
    "Services/Reporting.API",
    "ApiGateways/Gateway.API"
)

foreach ($service in $services) {
    $serviceName = ($service -split '/')[-1].ToLower()
    Write-Host "Launching $service..." -ForegroundColor Yellow
    $logDir = Join-Path $PSScriptRoot "..\logs"
    if (!(Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }
    $outLogPath = Join-Path $logDir "$serviceName.out.log"
    $errLogPath = Join-Path $logDir "$serviceName.err.log"
    
    # We use Start-Process with redirection to capture output.
    Start-Process dotnet -ArgumentList "run --project $service" -WorkingDirectory $PSScriptRoot -RedirectStandardOutput $outLogPath -RedirectStandardError $errLogPath -WindowStyle Hidden
}

Write-Host "All services launched in separate windows!" -ForegroundColor Green
