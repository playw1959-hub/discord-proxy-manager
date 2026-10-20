$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
New-Item -ItemType Directory -Force assets | Out-Null
$url = 'https://github.com/aiqinxuancai/discord-proxy/releases/download/v0.0.13/x64.zip'
$zip = Join-Path $PSScriptRoot 'assets/x64.zip'
Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $zip
$stage = Join-Path $PSScriptRoot 'assets/upstream-v0.0.13'
Expand-Archive -LiteralPath $zip -DestinationPath $stage -Force
$files = @(Get-ChildItem $stage -Filter version.dll -Recurse)
if ($files.Count -ne 1) { throw 'Expected exactly one version.dll.' }
$dll = Join-Path $PSScriptRoot 'assets/version.dll'
Copy-Item -LiteralPath $files[0].FullName -Destination $dll -Force
$hash = (Get-FileHash $dll -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = Join-Path $PSScriptRoot 'assets/core.json'
if (Test-Path $manifest) {
    $pinned = Get-Content $manifest -Raw | ConvertFrom-Json
    if ($pinned.sha256 -ne $hash) { throw 'Downloaded DLL differs from pinned hash. Inspect before changing the pin.' }
} else {
    @{version='v0.0.13'; architecture='x64'; url=$url; sha256=$hash} | ConvertTo-Json | Set-Content $manifest -Encoding UTF8
}
Write-Host "Proxy core: v0.0.13 x64 SHA256 $hash"
