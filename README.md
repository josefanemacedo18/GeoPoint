# GeoPoint

Plugin para **Revit 2027** (uso interno) de georreferenciamento de lotes urbanos e rurais: reconhece lotes a partir das linhas
do desenho, calcula azimutes/rumos, distâncias, áreas e coordenadas (UTM, geográficas, SAD 69, Córrego Alegre, Plano Topográfico
Local, RTM/LTM), desenha lotes por azimutes ou coordenadas – inclusive lendo a certidão de inteiro teor em PDF ou imagem (OCR), com confrontantes e dados do imóvel, faz desmembramento (inclusive desenhando com a ferramenta Linha, com prévia ao vivo), remembramento e simulação de parcelamento,
calcula recuos, construções existentes e afastamentos, e gera memorial descritivo em Word, pranchas por modelo (grade de coordenadas, tabela,
coordenada de referência, espaço para a imagem de localização, carimbo e logomarca do escritório), PDF, planilha Excel e arquivos KMZ/KML/GeoJSON para Google Earth e QGIS.

- Manual do usuário: [`docs/MANUAL.md`](docs/MANUAL.md)
- Pacote de instalação: `dist/GeoPoint-<versão>.zip` (descompacte e execute `Instalar.bat`)

## Estrutura

```
src/GeoPoint.Core     Núcleo independente do Revit (.NET 10): geometria, reconhecimento de áreas (rede planar),
                      geodésia (TM/UTM de Krüger n⁶, datums, geodésicas, plano topográfico local), cálculo dos lotes,
                      operações (união, divisão por área e em leque, recuos), área edificada, quadro de áreas,
                      memorial (.docx), Excel (.xlsx), KML/KMZ, GeoJSON, leitura de planilhas de coordenadas,
                      composição das pranchas (modelos, grade, tabelas, carimbo) e leitura de certidões
                      (texto de PDF com PdfPig – Apache 2.0 –, limpeza de OCR, perímetro, confrontantes e dados do imóvel).
                      Clipper2 embutido (licença Boost) e PdfPig (Apache 2.0).
src/GeoPoint.Revit    Add-in do Revit 2027 (.NET 10, WPF): faixa de opções, comandos, janelas, leitura de linhas/CAD,
                      gravação dos dados no modelo (Extensible Storage), anotações, pranchas, OCR do Windows, desenho ao vivo,
                      exportação de PDF, cópias de segurança e parâmetros GP_*.
tests/                Testes automatizados do núcleo (xUnit), com valores de referência do PROJ/pyproj.
tools/                Scripts de build e de instalação.
docs/                 Manual.
```

## Compilar

Requer o **.NET 10 SDK**. As referências da API do Revit 2027 vêm do NuGet (`Nice3point.Revit.Api.*`, apenas para compilação).

```bash
dotnet test tests/GeoPoint.Core.Tests/GeoPoint.Core.Tests.csproj
```

```bash
tools/build.sh
```

No Windows: `powershell -ExecutionPolicy Bypass -File tools/build.ps1`. O pacote é gerado em `dist/`.

## Dados

- Os lotes, a referência geodésica e as operações ficam gravados no `.rvt` (Extensible Storage, esquema `GeoPointData`).
- Configurações do usuário, modelos de memorial e de prancha (com a logomarca embutida), cópias de segurança (compactadas) e registros: `%AppData%\GeoPoint\`.
