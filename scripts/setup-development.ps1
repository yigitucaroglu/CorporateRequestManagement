<#
.SYNOPSIS
Development ortamını güvenli ve idempotent biçimde hazırlar.
.DESCRIPTION
SQL bağlantısını doğrular, API User Secrets değerlerini mevcut değerleri koruyarak birleştirir,
HTTPS/restore/build ve isteğe bağlı migration-seed işlemini çalıştırır. SQL bağlantısı ve
cryptographic JWT signing key Git'e yazılmaz; API User Secrets alanında saklanır.
.PARAMETER NonInteractive
Prompt kullanmaz; bağlantı environment variable üzerinden alınmalıdır.
.PARAMETER Server
SQL Server veya named instance adı.
.PARAMETER Database
Development database adı.
.PARAMETER Authentication
Windows veya SQL.
.PARAMETER SqlUser
SQL Authentication kullanıcı adı. Parola CRM_DEVELOPMENT_SQLSERVER_PASSWORD environment variable veya güvenli prompt ile alınır.
.PARAMETER ConnectionStringEnvironmentVariable
Kaynak connection string'i taşıyan environment variable adı.
.PARAMETER SkipBuild
Restore/build kapısını atlar.
.PARAMETER SkipDatabaseInitialization
Migration ve Development seed adımını atlar.
.PARAMETER SkipHttpsTrust
HTTPS certificate trust kontrolünü atlar.
.PARAMETER Force
Mevcut JWT signing key'i cryptographic random değerle yeniler.
.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\setup-development.ps1
.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\setup-development.ps1 -NonInteractive -ConnectionStringEnvironmentVariable CRM_DEVELOPMENT_SQLSERVER_CONNECTION
#>


[CmdletBinding()]
param(
    [switch]$NonInteractive,
    [string]$Server,
    [string]$Database = 'CorporateRequestManagement_Development',
    [ValidateSet('Windows','SQL')][string]$Authentication = 'Windows',
    [string]$SqlUser,
    [string]$ConnectionStringEnvironmentVariable,
    [switch]$SkipBuild,
    [switch]$SkipDatabaseInitialization,
    [switch]$SkipHttpsTrust,
    [switch]$Force
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\_common.ps1"

$root = Get-RepositoryRoot
$apiProject = Join-Path $root 'src\CorporateRequestManagement.Api\CorporateRequestManagement.Api.csproj'
Push-Location $root
try {
    Assert-Tooling
    if (-not $SkipBuild) {
        Write-Step 'Local tool ve NuGet bağımlılıkları geri yükleniyor.'
        Invoke-DotNet @('tool','restore')
        Invoke-DotNet @('restore','CorporateRequestManagement.sln')
    }
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    if (-not [string]::IsNullOrWhiteSpace($ConnectionStringEnvironmentVariable)) {
        $source = [Environment]::GetEnvironmentVariable($ConnectionStringEnvironmentVariable, 'Process')
        if ([string]::IsNullOrWhiteSpace($source)) { throw "Belirtilen connection environment variable mevcut değil." }
        $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder -ArgumentList $source
        $builder['Initial Catalog'] = $Database
    } else {
        if ([string]::IsNullOrWhiteSpace($Server)) {
            if ($NonInteractive) { throw '-Server veya -ConnectionStringEnvironmentVariable zorunludur.' }
            $Server = Read-Host 'SQL Server / instance'
        }
        $builder['Data Source'] = $Server
        $builder['Initial Catalog'] = $Database
        if ($Authentication -eq 'Windows') {
            $builder['Integrated Security'] = $true
        } else {
            if ([string]::IsNullOrWhiteSpace($SqlUser)) {
                if ($NonInteractive) { throw 'SQL Authentication için -SqlUser zorunludur.' }
                $SqlUser = Read-Host 'SQL kullanıcı adı'
            }
            $secure = $null
            $plain = $null
            try {
                $plain = [Environment]::GetEnvironmentVariable('CRM_DEVELOPMENT_SQLSERVER_PASSWORD', 'Process')
                if ([string]::IsNullOrWhiteSpace($plain)) {
                    if ($NonInteractive) { throw 'CRM_DEVELOPMENT_SQLSERVER_PASSWORD process environment variable zorunludur.' }
                    $secure = Read-Host 'SQL parolası' -AsSecureString
                    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
                    try { $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
                }
                $builder['User ID'] = $SqlUser
                $builder['Password'] = $plain
                $builder['Integrated Security'] = $false
            } finally { $plain = $null; $secure = $null }
        }
    }
    $builder['Encrypt'] = $true
    $builder['TrustServerCertificate'] = $true
    $builder['Connect Timeout'] = 15
    $builder['Application Name'] = 'CorporateRequestManagement.Development'
    $builder['MultipleActiveResultSets'] = $true

    Write-Step 'SQL Server erişimi ve minimum database oluşturma yetkisi doğrulanıyor.'
    $developmentConnectionString = $builder.ToString()
    Test-SqlConnection -ConnectionString $developmentConnectionString -RequireCreateDatabase

    $secretPath = Get-UserSecretsPath (Get-ProjectUserSecretsId $apiProject)
    $secrets = Read-SecretMap $secretPath
    $secrets['ConnectionStrings:DefaultConnection'] = $developmentConnectionString
    if ($Force -or -not $secrets.ContainsKey('Jwt:SigningKey') -or [string]::IsNullOrWhiteSpace([string]$secrets['Jwt:SigningKey'])) {
        if ($Force) { Write-WarningSafe 'JWT signing key yenileniyor; mevcut Development tokenları geçersiz olacaktır.' }
        $bytes = New-Object byte[] 64
        $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $rng.GetBytes($bytes); $secrets['Jwt:SigningKey'] = [Convert]::ToBase64String($bytes) } finally { $rng.Dispose(); [Array]::Clear($bytes, 0, $bytes.Length) }
    }
    Write-SecretMap $secretPath $secrets
    $verifiedSecrets = Confirm-DevelopmentSecretMap $secretPath $developmentConnectionString $Database $root
    Write-Success 'Connection ve JWT ayarları Git dışındaki API User Secrets alanına kaydedildi.'

    if (-not $SkipHttpsTrust) {
        & dotnet dev-certs https --check | Out-Null
        if ($LASTEXITCODE -ne 0) {
            if ($NonInteractive) { throw 'HTTPS developer certificate güvenilir değil. dotnet dev-certs https --trust çalıştırın.' }
            & dotnet dev-certs https --trust
            if ($LASTEXITCODE -ne 0) { throw 'HTTPS developer certificate trust işlemi başarısız.' }
        }
        Write-Success 'HTTPS developer certificate hazır.'
    }
    if (-not $SkipBuild) {
        Invoke-DotNet @('build','CorporateRequestManagement.sln','--configuration','Release','--no-restore')
    }
    if (-not $SkipDatabaseInitialization) {
        Write-Step 'Migration ve idempotent Development seed çalıştırılıyor.'
        $initializerEnvironment = @{
            'ASPNETCORE_ENVIRONMENT' = 'Development'
            'ConnectionStrings__DefaultConnection' = $developmentConnectionString
            'Jwt__SigningKey' = [string]$verifiedSecrets['Jwt:SigningKey']
        }
        Invoke-WithProcessEnvironment $initializerEnvironment {
            $arguments = @('run','--project','src\CorporateRequestManagement.Api','--configuration','Release','--no-build','--no-launch-profile','--','--initialize-database')
            Invoke-DotNet $arguments
        }
        $verify = [System.Data.SqlClient.SqlConnection]::new($developmentConnectionString)
        try {
            $verify.Open()
            $command = $verify.CreateCommand()
            $command.CommandText = "SELECT DB_NAME(), (SELECT COUNT(*) FROM Users WHERE Email IN ('admin@company.com','manager@company.com','employee@company.com') AND IsDeleted=0), (SELECT COUNT(*) FROM Departments WHERE Id IN ('10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000002') AND IsDeleted=0)"
            $reader = $command.ExecuteReader()
            try {
                if (-not $reader.Read() -or
                    -not [string]::Equals([string]$reader.GetString(0), $Database, [StringComparison]::Ordinal) -or
                    $reader.GetInt32(1) -ne 3 -or $reader.GetInt32(2) -ne 2) {
                    throw 'Development target database veya seed metadata doğrulaması başarısız.'
                }
            } finally { $reader.Dispose() }
        } finally { $verify.Dispose() }
        Write-Success 'Migration ve Development seed doğrulandı.'
    }
    Write-Success 'Setup tamamlandı.'
    Write-Host 'API: https://localhost:7001'
    Write-Host 'Web: https://localhost:7002'
    Write-Host 'Sonraki komut: powershell -ExecutionPolicy Bypass -File .\scripts\run-development.ps1'
    Write-Host 'Test komutu: powershell -ExecutionPolicy Bypass -File .\scripts\test.ps1'
    Write-Host 'Development demo hesapları README.md içinde belgelenmiştir.'
} finally { Pop-Location }
