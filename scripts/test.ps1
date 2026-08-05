<#
.SYNOPSIS
Unit, gerçek SQL Server integration ve migration drift kapılarını çalıştırır.
.DESCRIPTION
Test bağlantısını process-scope'ta güvenli çözer; restore/build/test/drift sonuçlarından herhangi
biri başarısızsa non-zero çıkar. Integration testleri service-based gerçek SQL Server bağlantısı
ve CREATE DATABASE yetkisi gerektirir.
.PARAMETER UnitOnly
Yalnız unit testleri çalıştırır.
.PARAMETER IntegrationOnly
Yalnız integration testlerini çalıştırır.
.PARAMETER SkipRestore
Tool ve solution restore adımlarını atlar.
.PARAMETER SkipBuild
Release build adımını atlar.
.PARAMETER Configuration
Build/test configuration.
.PARAMETER TestConnectionEnvironmentVariable
Test SQL connection string kaynağı environment variable adı.
.PARAMETER VerboseTests
VSTest normal verbosity kullanır; aksi halde minimal kullanır.
.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\test.ps1
#>


[CmdletBinding()]
param(
    [switch]$UnitOnly,
    [switch]$IntegrationOnly,
    [switch]$SkipRestore,
    [switch]$SkipBuild,
    [ValidateSet('Debug','Release')][string]$Configuration='Release',
    [string]$TestConnectionEnvironmentVariable,
    [switch]$VerboseTests
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\_common.ps1"
if ($UnitOnly -and $IntegrationOnly) { throw '-UnitOnly ve -IntegrationOnly birlikte kullanılamaz.' }
$root = Get-RepositoryRoot
$startedAt = [DateTimeOffset]::UtcNow
$previousTestConnection = [Environment]::GetEnvironmentVariable('CRM_TEST_SQLSERVER_CONNECTION','Process')
Push-Location $root
try {
    Assert-Tooling
    $runUnit = -not $IntegrationOnly
    $runIntegration = -not $UnitOnly
    if ($runIntegration) {
        $connection = $null
        if (-not [string]::IsNullOrWhiteSpace($TestConnectionEnvironmentVariable)) {
            $connection = [Environment]::GetEnvironmentVariable($TestConnectionEnvironmentVariable,'Process')
        }
        if ([string]::IsNullOrWhiteSpace($connection)) { $connection = $previousTestConnection }
        if ([string]::IsNullOrWhiteSpace($connection)) {
            $apiProject = Join-Path $root 'src\CorporateRequestManagement.Api\CorporateRequestManagement.Api.csproj'
            $secretPath = Get-UserSecretsPath (Get-ProjectUserSecretsId $apiProject)
            $secrets = Read-SecretMap $secretPath
            if ($secrets.ContainsKey('ConnectionStrings:DefaultConnection')) { $connection = [string]$secrets['ConnectionStrings:DefaultConnection'] }
        }
        if ([string]::IsNullOrWhiteSpace($connection)) { throw 'Integration SQL bağlantısı bulunamadı. Setup çalıştırın veya CRM_TEST_SQLSERVER_CONNECTION sağlayın.' }
        $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder -ArgumentList $connection
        if ([string]$builder['Data Source'] -match '\(localdb\)') { throw 'Integration testleri LocalDB kullanamaz; service-based SQL Server sağlayın.' }
        $builder['Initial Catalog'] = 'master'
        $builder['Application Name'] = 'CorporateRequestManagement.IntegrationTests'
        $testConnectionString = $builder.ToString()
        Test-SqlConnection -ConnectionString $testConnectionString -RequireCreateDatabase
        [Environment]::SetEnvironmentVariable('CRM_TEST_SQLSERVER_CONNECTION',$testConnectionString,'Process')
        Write-Success 'Gerçek SQL Server test bağlantısı ve CREATE DATABASE yetkisi doğrulandı; secret raporlanmadı.'
    }
    if (-not $SkipRestore) {
        Invoke-DotNet @('tool','restore')
        Invoke-DotNet @('restore','CorporateRequestManagement.sln')
    }
    if (-not $SkipBuild) { Invoke-DotNet @('build','CorporateRequestManagement.sln','--configuration',$Configuration,'--no-restore') }
    $verbosity = if ($VerboseTests) { 'normal' } else { 'minimal' }
    if ($runUnit) {
        Write-Step 'Unit testleri çalıştırılıyor.'
        Invoke-DotNet @('test','tests\CorporateRequestManagement.UnitTests','--configuration',$Configuration,'--no-build','--logger',"console;verbosity=$verbosity")
    }
    if ($runIntegration) {
        Write-Step 'Integration testleri çalıştırılıyor; gerçek SQL Server paketi 5-7 dakika sürebilir.'
        Invoke-DotNet @('test','tests\CorporateRequestManagement.IntegrationTests','--configuration',$Configuration,'--no-build','--logger',"console;verbosity=$verbosity")
    }
    Write-Step 'Migration drift kontrol ediliyor.'
    Invoke-DotNet @('tool','run','dotnet-ef','migrations','has-pending-model-changes','--project','src\CorporateRequestManagement.Infrastructure','--startup-project','src\CorporateRequestManagement.Infrastructure','--context','ApplicationDbContext','--configuration',$Configuration)
    $duration = [DateTimeOffset]::UtcNow - $startedAt
    Write-Success ("Bütün seçili test ve drift kapıları geçti. Toplam süre: {0:hh\:mm\:ss}" -f $duration)
} finally {
    [Environment]::SetEnvironmentVariable('CRM_TEST_SQLSERVER_CONNECTION',$previousTestConnection,'Process')
    Pop-Location
}
