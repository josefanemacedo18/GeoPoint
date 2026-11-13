#!/usr/bin/env bash
# Compila, testa e monta o pacote de instalação do GeoPoint (Linux/macOS com .NET 10 SDK).
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION=$(grep -oP '(?<=<Version>)[^<]+' Directory.Build.props)
dotnet test tests/GeoPoint.Core.Tests/GeoPoint.Core.Tests.csproj -c Release
dotnet build src/GeoPoint.Revit/GeoPoint.Revit.csproj -c Release
OUT=src/GeoPoint.Revit/bin/Release/net10.0-windows10.0.19041.0
PKG=artifacts/GeoPoint-$VERSION
rm -rf "$PKG" && mkdir -p "$PKG/GeoPoint"
# GeoPoint + bibliotecas (PdfPig: texto de PDF; Microsoft.Windows.SDK.NET/WinRT.Runtime: OCR e páginas de PDF do Windows).
cp "$OUT"/*.dll "$PKG/GeoPoint/"
cp src/GeoPoint.Core/ThirdParty/PdfPig/LICENSE_PdfPig.txt "$PKG/GeoPoint/"
cp src/GeoPoint.Core/ThirdParty/Clipper2/LICENSE_Clipper2.txt "$PKG/GeoPoint/"
cp docs/MANUAL.md "$PKG/GeoPoint/MANUAL.md"
cp docs/MANUAL.md "$PKG/MANUAL.md"
cp src/GeoPoint.Revit/GeoPoint.addin "$PKG/"
cp tools/Instalar.bat tools/Desinstalar.bat "$PKG/"
# Os .ps1 são gravados com BOM (PowerShell 5.1 lê os acentos corretamente).
cp tools/install.ps1 tools/uninstall.ps1 "$PKG/"
sed -i 's/$/\r/' "$PKG/Instalar.bat" "$PKG/Desinstalar.bat"
mkdir -p dist
rm -f "dist/GeoPoint-$VERSION.zip"
(cd artifacts && zip -qr "../dist/GeoPoint-$VERSION.zip" "GeoPoint-$VERSION")
echo "Pacote: dist/GeoPoint-$VERSION.zip"
