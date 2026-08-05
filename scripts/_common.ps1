<#
.SYNOPSIS
Phase 8A geliştirme scriptleri için ortak, güvenli yardımcılar.
.DESCRIPTION
Repository çözümleme, User Secrets, process/port, readiness ve SQL preflight yardımcılarını sağlar.
.PARAMETER Yok
Bu dosya doğrudan çalıştırılmaz; diğer scriptler tarafından dot-source edilir.
.EXAMPLE
. "$PSScriptRoot\_common.ps1"
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RepositoryRoot {
    $root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    if (-not (Test-Path -LiteralPath (Join-Path $root 'CorporateRequestManagement.sln'))) { throw 'Repository kökü doğrulanamadı.' }
    return $root
}
function Write-Step([string]$Message) { Write-Host "[INFO] $Message" -ForegroundColor Cyan }
function Write-Success([string]$Message) { Write-Host "[OK] $Message" -ForegroundColor Green }
function Write-WarningSafe([string]$Message) { Write-Warning $Message }
function Assert-Tooling {
    if ($PSVersionTable.PSVersion.Major -lt 5) { throw 'PowerShell 5.1 veya daha yeni bir sürüm gereklidir.' }
    $sdk = & dotnet --list-sdks
    if ($LASTEXITCODE -ne 0 -or -not ($sdk | Select-String '^8\.')) { throw '.NET 8 SDK bulunamadı. Resmî .NET 8 SDK kurulmalıdır.' }
}
function Get-ProjectUserSecretsId([string]$ProjectPath) {
    [xml]$project = Get-Content -LiteralPath $ProjectPath -Raw
    $id = @($project.Project.PropertyGroup.UserSecretsId | Where-Object { $_ }) | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace([string]$id)) { throw "UserSecretsId bulunamadı: $ProjectPath" }
    return [string]$id
}
function Get-UserSecretsPath([string]$UserSecretsId) {
    if ($env:OS -eq 'Windows_NT') {
        $base = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
        return Join-Path $base "Microsoft\UserSecrets\$UserSecretsId\secrets.json"
    }
    return Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) ".microsoft/usersecrets/$UserSecretsId/secrets.json"
}
function Read-SecretMap([string]$Path) {
    $map = @{}
    if (-not (Test-Path -LiteralPath $Path)) { return $map }
    $raw = Get-Content -LiteralPath $Path -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) { return $map }
    $object = $raw | ConvertFrom-Json
    foreach ($property in $object.PSObject.Properties) { $map[$property.Name] = $property.Value }
    return $map
}
function Write-SecretMap([string]$Path, [hashtable]$Values) {
    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    $json = $Values | ConvertTo-Json -Depth 10
    [IO.File]::WriteAllText($Path, $json, (New-Object Text.UTF8Encoding($false)))
}
function Get-PortNumber([string]$Url) { return ([Uri]$Url).Port }
function Get-PortOwner([int]$Port) {
    $connection = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $connection) { return $null }
    return Get-CimInstance Win32_Process -Filter "ProcessId=$($connection.OwningProcess)" -ErrorAction SilentlyContinue
}
function Test-RepositoryProcess($Process, [string]$Root, [string]$ProjectName) {
    if ($null -eq $Process -or [string]::IsNullOrWhiteSpace([string]$Process.CommandLine)) { return $false }
    return $Process.CommandLine.IndexOf($Root, [StringComparison]::OrdinalIgnoreCase) -ge 0 -and
        $Process.CommandLine.IndexOf($ProjectName, [StringComparison]::OrdinalIgnoreCase) -ge 0
}
function Stop-VerifiedProcessTree([int]$ProcessId, [string]$Root, [string]$ProjectName) {
    $process = Get-CimInstance Win32_Process -Filter "ProcessId=$ProcessId" -ErrorAction SilentlyContinue
    if ($null -eq $process) { return }
    if (-not (Test-RepositoryProcess $process $Root $ProjectName)) { throw "PID $ProcessId repository projesi olarak doğrulanamadı; durdurulmadı." }
    & taskkill /PID $ProcessId /T /F | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "PID $ProcessId process tree durdurulamadı." }
}
function Wait-HttpReady([string]$Url, [int]$TimeoutSeconds) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5
            if ([int]$response.StatusCode -ge 200 -and [int]$response.StatusCode -lt 400) { return $true }
        } catch { Start-Sleep -Milliseconds 750; continue }
        Start-Sleep -Milliseconds 750
    }
    return $false
}
function Get-StatePath([string]$Root) { return Join-Path $Root '.local\development-processes.json' }
function Read-RuntimeState([string]$Root) {
    $path = Get-StatePath $Root
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}
function Write-RuntimeState([string]$Root, $State) {
    $path = Get-StatePath $Root
    $directory = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    [IO.File]::WriteAllText($path, ($State | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
}
function Test-SqlConnection([string]$ConnectionString, [switch]$RequireCreateDatabase) {
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder -ArgumentList $ConnectionString
    $builder['Initial Catalog'] = 'master'
    $masterConnectionString = $builder.ToString()
    $connection = [System.Data.SqlClient.SqlConnection]::new($masterConnectionString)
    try {
        $connection.Open()
        if ($RequireCreateDatabase) {
            $command = $connection.CreateCommand()
            $command.CommandText = "SELECT HAS_PERMS_BY_NAME(NULL, NULL, 'CREATE ANY DATABASE')"
            if ([int]$command.ExecuteScalar() -ne 1) { throw 'SQL login CREATE ANY DATABASE yetkisine sahip değil. Yalnız bu minimum yetkiyi sağlayın.' }
        }
    } finally { $connection.Dispose() }
}
function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet komutu başarısız oldu (exit $LASTEXITCODE)." }
}
