$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
$f=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$w=Join-Path $f 'WPF'
$refs=@('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Security.dll','Microsoft.CSharp.dll') | ForEach-Object { '/r:'+(Join-Path $f $_) }
$refs+=@('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/r:'+(Join-Path $w $_) }
& (Join-Path $f 'csc.exe') /nologo /target:exe /platform:x64 /utf8output /main:Tests /out:build\Tests.exe /resource:assets\logo.png,AppLogo /resource:assets\version.dll,ProxyCore @refs .\src\Core.cs .\src\Program.cs .\tests\Tests.cs
if($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& .\build\Tests.exe
if($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
