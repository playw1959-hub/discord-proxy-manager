param([switch]$Fetch)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$dll = Join-Path $PSScriptRoot 'assets/version.dll'
if ($Fetch) {
    & (Join-Path $PSScriptRoot 'fetch-core.ps1')
}
if (!(Test-Path $dll)) { throw 'Missing assets/version.dll. Run .\fetch-core.ps1 first.' }
$pin = Get-Content (Join-Path $PSScriptRoot 'assets/core.json') -Raw | ConvertFrom-Json
if ((Get-FileHash $dll -Algorithm SHA256).Hash -ne $pin.sha256) { throw 'Proxy DLL hash does not match assets/core.json.' }
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$wpf = Join-Path $framework 'WPF'
New-Item -ItemType Directory -Force build | Out-Null
$refs = @('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Security.dll','Microsoft.CSharp.dll') | ForEach-Object { '/r:' + (Join-Path $framework $_) }
$refs += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/r:' + (Join-Path $wpf $_) }
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:x64 /optimize+ /utf8output /out:build\DiscordProxyManager.exe /win32manifest:src\app.manifest /win32icon:assets\app.ico /resource:assets\logo.png,AppLogo /resource:assets\version.dll,ProxyCore @refs .\src\Core.cs .\src\Program.cs
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Host 'Built build/DiscordProxyManager.exe'
