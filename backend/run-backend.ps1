# Run all backend microservices
Write-Host "Starting all backend services..." -ForegroundColor Cyan

$services = @(
    "Services/Identity.API",
    "Services/Instagram.API",
    "Services/Reporting.API",
    "ApiGateways/Gateway.API"
)

foreach ($service in $services) {
    Write-Host "Launching $service..." -ForegroundColor Yellow
    Start-Process dotnet -ArgumentList "run --project $service" -WorkingDirectory $PSScriptRoot
}

Write-Host "All services launched in separate windows!" -ForegroundColor Green
