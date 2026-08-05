<#
.SYNOPSIS
Phase 8A API/Web process tree'lerini güvenli biçimde durdurur.
.DESCRIPTION
State veya canonical portlardan process bulur; command line repository ve proje doğrulamasından
sonra yalnız ilgili process tree'yi kapatır. Global veya doğrulanmamış dotnet processlerini kapatmaz.
.PARAMETER ApiUrl
Fallback API URL'i.
.PARAMETER WebUrl
Fallback Web URL'i.
.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\stop-development.ps1
#>


[CmdletBinding()]
param([string]$ApiUrl='https://localhost:7001',[string]$WebUrl='https://localhost:7002')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\_common.ps1"
$root = Get-RepositoryRoot
$statePath = Get-StatePath $root
$state = Read-RuntimeState $root
$targets = @()
if ($null -ne $state) {
    $targets += @{ Id=[int]$state.webPid; Project='CorporateRequestManagement.Web' }
    $targets += @{ Id=[int]$state.apiPid; Project='CorporateRequestManagement.Api' }
} else {
    foreach ($item in @(@{ Url=$WebUrl; Project='CorporateRequestManagement.Web' },@{ Url=$ApiUrl; Project='CorporateRequestManagement.Api' })) {
        $owner = Get-PortOwner (Get-PortNumber $item.Url)
        if ($null -ne $owner) { $targets += @{ Id=[int]$owner.ProcessId; Project=$item.Project } }
    }
}
foreach ($target in $targets) {
    Stop-VerifiedProcessTree ([int]$target.Id) $root ([string]$target.Project)
    Write-Success "$($target.Project) process tree durduruldu."
}
Start-Sleep -Milliseconds 500
foreach ($url in @($ApiUrl,$WebUrl)) {
    $owner = Get-PortOwner (Get-PortNumber $url)
    if ($null -ne $owner) { throw "Port $((Get-PortNumber $url)) hâlâ kullanımda; doğrulanmamış process durdurulmadı." }
}
if (Test-Path -LiteralPath $statePath) { Remove-Item -LiteralPath $statePath -Force }
Write-Success 'Development processleri durduruldu ve runtime state kaldırıldı.'
