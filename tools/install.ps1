# GeoPoint - instalação para o Revit 2027 (usuário atual)
param(
    [string]$RevitVersion = "2027"
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
$target = Join-Path $addins "GeoPoint"

if (Get-Process -Name "Revit" -ErrorAction SilentlyContinue) {
    Write-Host "Feche o Revit antes de instalar o GeoPoint." -ForegroundColor Yellow
    exit 1
}

$src = Join-Path $here "GeoPoint"
if (-not (Test-Path (Join-Path $src "GeoPoint.Revit.dll"))) {
    Write-Host "Arquivos do plugin não encontrados em $src" -ForegroundColor Red
    exit 1
}

New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -Path (Join-Path $src "*") -Destination $target -Recurse -Force
Copy-Item -Path (Join-Path $here "GeoPoint.addin") -Destination $addins -Force

# Remove o bloqueio de arquivos baixados da internet.
Get-ChildItem -Path $target -Recurse | Unblock-File -ErrorAction SilentlyContinue
Unblock-File -Path (Join-Path $addins "GeoPoint.addin") -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "GeoPoint instalado em $target" -ForegroundColor Green
Write-Host "Abra o Revit ${RevitVersion}: a aba 'GeoPoint' aparece na faixa de opções."
Write-Host "Na primeira vez, escolha 'Sempre carregar' quando o Revit perguntar sobre o suplemento."
