# GeoPoint

Plugin para **Revit 2027** (uso interno) de georreferenciamento de lotes urbanos e rurais: reconhece lotes a partir das linhas
do desenho, calcula azimutes/rumos, distâncias, áreas e coordenadas (UTM, geográficas, SAD 69, Córrego Alegre, Plano Topográfico
Local, RTM/LTM), faz desmembramento, remembramento e simulação de parcelamento, e gera memorial descritivo em Word, planilha
Excel e arquivos KMZ/KML/GeoJSON para Google Earth e QGIS.

- Manual do usuário: [`docs/MANUAL.md`](docs/MANUAL.md)
- Pacote de instalação: `dist/GeoPoint-<versão>.zip` (descompacte e execute `Instalar.bat`)

## Estrutura

```
src/GeoPoint.Core     Núcleo independente do Revit (.NET 10): geometria, reconhecimento de áreas (rede planar),
                      geodésia (TM/UTM de Krüger n⁶, datums, geodésicas, plano topográfico local), cálculo dos lotes,
                      operações (união, divisão por área, recuos), quadro de áreas, memorial (.docx), Excel (.xlsx),
                      KML/KMZ e GeoJSON. Sem dependências externas (Clipper2 embutido, licença Boost).
src/GeoPoint.Revit    Add-in do Revit 2027 (.NET 10, WPF): faixa de opções, comandos, janelas, leitura de linhas/CAD,
                      gravação dos dados no modelo (Extensible Storage), anotações, Property Lines e parâmetros.
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
- Configurações do usuário, modelo do memorial e registros: `%AppData%\GeoPoint\`.
