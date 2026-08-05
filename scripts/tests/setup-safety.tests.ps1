[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\..\_common.ps1"

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("crm-setup-safety-{0:N}" -f [Guid]::NewGuid())
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    $secretPath = Join-Path $temporaryRoot 'secrets.json'
    $expectedConnection = 'Server=example.invalid;Database=GuardedDatabase;Integrated Security=true;Encrypt=true'
    Write-SecretMap $secretPath @{
        'ConnectionStrings:DefaultConnection' = $expectedConnection
        'Jwt:SigningKey' = 'focused-test-signing-key-value'
    }
    $verified = Confirm-DevelopmentSecretMap $secretPath $expectedConnection 'GuardedDatabase' (Get-RepositoryRoot)
    Assert-True ($verified.ContainsKey('Jwt:SigningKey')) 'Secret read-back validation failed.'

    $wrongTargetFailed = $false
    try { Confirm-DevelopmentSecretMap $secretPath $expectedConnection 'WrongDatabase' (Get-RepositoryRoot) | Out-Null } catch { $wrongTargetFailed = $true }
    Assert-True $wrongTargetFailed 'Wrong target database was not rejected during read-back.'

    $environmentNames = @('ASPNETCORE_ENVIRONMENT', 'ConnectionStrings__DefaultConnection', 'Jwt__SigningKey')
    $original = @{}
    foreach ($environmentName in $environmentNames) {
        $original[$environmentName] = [Environment]::GetEnvironmentVariable($environmentName, 'Process')
        [Environment]::SetEnvironmentVariable($environmentName, "before-$environmentName", 'Process')
    }
    try {
        $temporaryEnvironment = @{
            'ASPNETCORE_ENVIRONMENT' = 'Development'
            'ConnectionStrings__DefaultConnection' = 'temporary-connection-value'
            'Jwt__SigningKey' = 'temporary-signing-value'
        }
        Invoke-WithProcessEnvironment $temporaryEnvironment {
            foreach ($environmentName in $temporaryEnvironment.Keys) {
                Assert-True ([Environment]::GetEnvironmentVariable($environmentName, 'Process') -eq $temporaryEnvironment[$environmentName]) 'Temporary environment was not applied.'
            }
        }
        foreach ($environmentName in $environmentNames) {
            Assert-True ([Environment]::GetEnvironmentVariable($environmentName, 'Process') -eq "before-$environmentName") 'Environment was not restored after success.'
        }
        try { Invoke-WithProcessEnvironment $temporaryEnvironment { throw 'expected' } } catch { }
        foreach ($environmentName in $environmentNames) {
            Assert-True ([Environment]::GetEnvironmentVariable($environmentName, 'Process') -eq "before-$environmentName") 'Environment was not restored after failure.'
        }
    } finally {
        foreach ($environmentName in $environmentNames) { [Environment]::SetEnvironmentVariable($environmentName, $original[$environmentName], 'Process') }
    }

    if ($env:OS -eq 'Windows_NT') {
        $mismatchAppData = Join-Path $temporaryRoot 'mismatched-appdata'
        $encodedScript = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes(@"
`$env:APPDATA = '$($mismatchAppData.Replace("'", "''"))'
. '$((Join-Path $PSScriptRoot '..\_common.ps1').Replace("'", "''"))'
Get-UserSecretsPath 'safety-test' | Out-Null
"@))
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedScript 2>$null
            $mismatchExitCode = $LASTEXITCODE
        } finally { $ErrorActionPreference = $previousErrorActionPreference }
        Assert-True ($mismatchExitCode -ne 0) 'APPDATA mismatch fail-fast guard did not trigger.'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $mismatchAppData 'Microsoft\UserSecrets\safety-test\secrets.json'))) 'Mismatch test created a secret file.'
    }

    Write-Success 'Setup safety focused tests passed.'
} finally {
    if ((Get-NormalizedPath $temporaryRoot).StartsWith((Get-NormalizedPath ([IO.Path]::GetTempPath())), [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $temporaryRoot) -like 'crm-setup-safety-*') {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
