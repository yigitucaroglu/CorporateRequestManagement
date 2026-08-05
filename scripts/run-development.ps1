<#
.SYNOPSIS
API ve Web uygulamalarını güvenli sırayla başlatır.
.DESCRIPTION
Secret ve port preflight yapar, API readiness sonrasında Web'i başlatır ve Git-ignored runtime state yazar.
.PARAMETER Configuration
Debug veya Release build configuration.
.PARAMETER NoBrowser
Web adresini otomatik açmaz.
.PARAMETER ForceRestart
Yalnız doğrulanan eski repository process tree'lerini durdurur.
.PARAMETER ApiUrl
API HTTPS URL'i.
.PARAMETER WebUrl
Web HTTPS URL'i.
.PARAMETER StartupTimeoutSeconds
Her uygulama için readiness timeout.
.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\run-development.ps1 -NoBrowser
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [switch]$NoBrowser,
    [switch]$ForceRestart,
    [string]$ApiUrl = 'https://localhost:7001',
    [string]$WebUrl = 'https://localhost:7002',
    [ValidateRange(5,300)][int]$StartupTimeoutSeconds = 60
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\_common.ps1"

$root = Get-RepositoryRoot
$apiProject = Join-Path $root 'src\CorporateRequestManagement.Api\CorporateRequestManagement.Api.csproj'
$webProject = Join-Path $root 'src\CorporateRequestManagement.Web\CorporateRequestManagement.Web.csproj'
$started = @()
Push-Location $root
try {
    Assert-Tooling
    $secretPath = Get-UserSecretsPath (Get-ProjectUserSecretsId $apiProject)
    $secrets = Read-SecretMap $secretPath
    if (-not $secrets.ContainsKey('ConnectionStrings:DefaultConnection') -or -not $secrets.ContainsKey('Jwt:SigningKey')) {
        throw 'API User Secrets eksik. Önce .\scripts\setup-development.ps1 çalıştırın.'
    }
    if ([string]::IsNullOrWhiteSpace($ApiUrl) -or ([Uri]$ApiUrl).Scheme -ne 'https') { throw 'ApiUrl geçerli bir HTTPS URL olmalıdır.' }
    if ([string]::IsNullOrWhiteSpace($WebUrl) -or ([Uri]$WebUrl).Scheme -ne 'https') { throw 'WebUrl geçerli bir HTTPS URL olmalıdır.' }

    foreach ($item in @(@{ Url=$ApiUrl; Project='CorporateRequestManagement.Api' }, @{ Url=$WebUrl; Project='CorporateRequestManagement.Web' })) {
        $owner = Get-PortOwner (Get-PortNumber $item.Url)
        if ($null -eq $owner) { continue }
        if (-not (Test-RepositoryProcess $owner $root $item.Project)) {
            throw "Port $((Get-PortNumber $item.Url)) başka bir process tarafından kullanılıyor (PID $($owner.ProcessId), $($owner.Name)); process durdurulmadı."
        }
        if (-not $ForceRestart) { throw "Port $((Get-PortNumber $item.Url)) eski repository process'i tarafından kullanılıyor (PID $($owner.ProcessId)). -ForceRestart kullanın veya stop-development.ps1 çalıştırın." }
        Stop-VerifiedProcessTree ([int]$owner.ProcessId) $root $item.Project
    }

    $previousEnvironment = [Environment]::GetEnvironmentVariable('ASPNETCORE_ENVIRONMENT','Process')
    $previousBackend = [Environment]::GetEnvironmentVariable('BackendApi__BaseUrl','Process')
    try {
        [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT','Development','Process')
        Write-Step 'API başlatılıyor.'
        $api = Start-Process -FilePath 'dotnet' -ArgumentList @('run','--project',$apiProject,'--configuration',$Configuration,'--no-launch-profile','--urls',$ApiUrl) -WorkingDirectory $root -WindowStyle Normal -PassThru
        $started += @{ Id=$api.Id; Project='CorporateRequestManagement.Api' }
        if (-not (Wait-HttpReady "$ApiUrl/swagger/index.html" $StartupTimeoutSeconds)) { throw 'API readiness zaman aşımına uğradı; Web başlatılmadı.' }
        Write-Success "API ready (PID $($api.Id))."

        [Environment]::SetEnvironmentVariable('BackendApi__BaseUrl',$ApiUrl,'Process')
        Write-Step 'Web başlatılıyor.'
        $web = Start-Process -FilePath 'dotnet' -ArgumentList @('run','--project',$webProject,'--configuration',$Configuration,'--no-launch-profile','--urls',$WebUrl) -WorkingDirectory $root -WindowStyle Normal -PassThru
        $started += @{ Id=$web.Id; Project='CorporateRequestManagement.Web' }
        if (-not (Wait-HttpReady "$WebUrl/account/login" $StartupTimeoutSeconds)) { throw 'Web readiness zaman aşımına uğradı.' }
        Write-Success "Web ready (PID $($web.Id))."
    } finally {
        [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT',$previousEnvironment,'Process')
        [Environment]::SetEnvironmentVariable('BackendApi__BaseUrl',$previousBackend,'Process')
    }
    Write-RuntimeState $root ([ordered]@{ repositoryRoot=$root; apiPid=$api.Id; webPid=$web.Id; apiUrl=$ApiUrl; webUrl=$WebUrl; startedAt=[DateTimeOffset]::UtcNow.ToString('O'); configuration=$Configuration })
    Write-Host "Swagger: $ApiUrl/swagger"
    Write-Host "Web: $WebUrl"
    Write-Host 'Durdurma: powershell -ExecutionPolicy Bypass -File .\scripts\stop-development.ps1'
    if (-not $NoBrowser) { Start-Process $WebUrl | Out-Null }
} catch {
    for ($index = $started.Count - 1; $index -ge 0; $index--) {
        $item = $started[$index]
        try { Stop-VerifiedProcessTree ([int]$item.Id) $root ([string]$item.Project) } catch { Write-WarningSafe $_.Exception.Message }
    }
    throw
} finally { Pop-Location }
