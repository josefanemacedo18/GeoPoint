# GeoPoint - remoção (usuário atual)
param(
    [string]$RevitVersion = "2027"
)
$addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"

if (Get-Process -Name "Revit" -ErrorAction SilentlyContinue) {
    Write-Host "Feche o Revit antes de remover o GeoPoint." -ForegroundColor Yellow
    exit 1
}

Remove-Item -Path (Join-Path $addins "GeoPoint.addin") -Force -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $addins "GeoPoint") -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "GeoPoint removido. Suas configurações continuam em $env:APPDATA\GeoPoint (apague a pasta se quiser)." -ForegroundColor Green
