# Compila, testa e monta o pacote de instalação do GeoPoint (Windows com .NET 10 SDK).
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
[xml]$props = Get-Content "Directory.Build.props"
$version = $props.Project.PropertyGroup.Version
dotnet test tests/GeoPoint.Core.Tests/GeoPoint.Core.Tests.csproj -c Release
dotnet build src/GeoPoint.Revit/GeoPoint.Revit.csproj -c Release
$out = "src/GeoPoint.Revit/bin/Release/net10.0-windows"
$pkg = "artifacts/GeoPoint-$version"
if (Test-Path $pkg) { Remove-Item $pkg -Recurse -Force }
New-Item -ItemType Directory -Force -Path "$pkg/GeoPoint" | Out-Null
Copy-Item "$out/GeoPoint.Revit.dll", "$out/GeoPoint.Core.dll" "$pkg/GeoPoint/"
Copy-Item "src/GeoPoint.Core/ThirdParty/Clipper2/LICENSE_Clipper2.txt" "$pkg/GeoPoint/"
Copy-Item "docs/MANUAL.md" "$pkg/GeoPoint/MANUAL.md"
Copy-Item "docs/MANUAL.md" "$pkg/MANUAL.md"
Copy-Item "src/GeoPoint.Revit/GeoPoint.addin", "tools/Instalar.bat", "tools/Desinstalar.bat", "tools/install.ps1", "tools/uninstall.ps1" "$pkg/"
New-Item -ItemType Directory -Force -Path "dist" | Out-Null
Compress-Archive -Path $pkg -DestinationPath "dist/GeoPoint-$version.zip" -Force
Write-Host "Pacote: dist/GeoPoint-$version.zip"
