<#
.SYNOPSIS
    Runs the browser end-to-end suites against a running instance of the website.
.DESCRIPTION
    The suites CREATE DATA (accounts, orders, reviews, chats, quotes, a test category / product):
    point them at a development or test database, never at production.
    The admin password is read from the Web project's user-secrets (Seed:AdminPassword) unless ADMIN_PW is set.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tests\e2e\run-all.ps1
    powershell -ExecutionPolicy Bypass -File tests\e2e\run-all.ps1 -BaseUrl https://localhost:7443 -Database FS_Test
#>
param(
    [string]$BaseUrl = "https://localhost:7160",
    [string]$Database = "FurnitureStoreDb",
    [string]$EmailDirectory = (Join-Path $PSScriptRoot "..\..\src\FurnitureStore.Web\App_Data\emails"),
    [string[]]$Suites = @("dod-e2e.js", "coupon-e2e.js", "qr-e2e.js", "dining-e2e.js", "cart-select-e2e.js", "forms-resubmit.js", "chat-e2e.js", "chat-offline-e2e.js", "ai-e2e.js", "local-ai-e2e.js", "quote-e2e.js", "account-forms.js", "audit.js")
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
# "powershell -File run-all.ps1 -Suites a.js,b.js" passes one string: split it.
$Suites = @($Suites | ForEach-Object { $_ -split "," } | Where-Object { $_ })

if (-not (Test-Path (Join-Path $PSScriptRoot "node_modules"))) {
    npm install --no-audit --no-fund | Out-Null
}

if (-not $env:ADMIN_PW) {
    $secrets = dotnet user-secrets list --project (Join-Path $PSScriptRoot "..\..\src\FurnitureStore.Web")
    $env:ADMIN_PW = ($secrets | Where-Object { $_ -like "Seed:AdminPassword = *" }) -replace "^Seed:AdminPassword = ", ""
}
if (-not $env:ADMIN_PW) {
    throw "Admin password not found: set ADMIN_PW or the Seed:AdminPassword user-secret."
}

$env:BASE = $BaseUrl
$env:DB = $Database
$env:EMAIL_DIR = (Resolve-Path $EmailDirectory -ErrorAction SilentlyContinue)

$failed = @()
foreach ($suite in $Suites) {
    Write-Host "===== $suite" -ForegroundColor Cyan
    node $suite
    if ($LASTEXITCODE -ne 0) { $failed += $suite }
}

if ($failed.Count -gt 0) {
    Write-Host "FAILED: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "All suites passed." -ForegroundColor Green
