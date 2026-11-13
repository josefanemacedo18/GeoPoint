# GeoPoint – Manual de uso (Revit 2027)

O GeoPoint transforma as linhas que você desenha no Revit em **lotes georreferenciados**: calcula azimutes, rumos,
distâncias, áreas, coordenadas (UTM, geográficas e outros sistemas), faz desmembramento, remembramento e simulação de
parcelamento, e gera **memorial descritivo em Word**, planilha Excel e arquivos para **Google Earth (KMZ/KML)** e **QGIS (GeoJSON)**.

Tudo fica gravado dentro do próprio arquivo `.rvt`. O **Desfazer (Ctrl+Z)** do Revit funciona normalmente com os comandos do GeoPoint.

---

## 1. Instalação

1. Feche o Revit.
2. Descompacte o arquivo `GeoPoint-x.y.z.zip`.
3. Clique duas vezes em **`Instalar.bat`**.
   Ele copia os arquivos para `%AppData%\Autodesk\Revit\Addins\2027\`.
4. Abra o Revit 2027. Na primeira vez o Revit pergunta se deve carregar o suplemento: escolha **Sempre carregar**.
5. A aba **GeoPoint** aparece na faixa de opções.

Para remover: feche o Revit e execute **`Desinstalar.bat`**.

> Configurações, modelos personalizados e registros de erro ficam em `%AppData%\GeoPoint\`.

---

## Novidades da versão 1.6

- **Certidão em PDF ou imagem no Desenhar Perímetro**: botão *Importar certidão ou memorial*. O GeoPoint lê o texto (PDF digital: o
  texto do próprio PDF, exato; PDF escaneado, foto ou imagem: reconhecimento de texto – OCR – do próprio Windows, sem enviar o documento
  para fora do computador) e encontra o **perímetro** (coordenadas, azimutes e distâncias, tabela de vértices ou só as medidas de frente,
  fundos e laterais), o **confrontante de cada divisa** e os **dados do imóvel** (matrícula, proprietário atual, CPF/CNPJ, lote, quadra,
  endereço, bairro, CEP, município/UF, comarca, cartório, área e perímetro). Tudo aparece para conferir: as páginas do documento ao lado,
  com **os números usados no desenho destacados em amarelo**, e a área e o perímetro do desenho comparados com os da certidão. Ao
  desenhar, os **Dados do Imóvel** são preenchidos e o **lote é cadastrado** já com os confrontantes.
- **Pranchas – coordenadas**: na janela de Pranchas você escolhe as coordenadas da prancha (grade, tabela, chamadas e carimbo):
  **Geográficas (latitude/longitude) – SIRGAS 2000** (padrão), UTM – SIRGAS 2000 ou o primeiro sistema do memorial.
- **Grade limpa**: as linhas da grade de coordenadas não passam mais por cima da tabela, do norte, das chamadas de coordenada e dos
  textos do desenho.
- **Imagem de localização manual**: o GeoPoint não baixa mais imagem de satélite. O espaço continua na prancha, **sem título**, para você
  colocar a imagem (escolhida na janela de Pranchas, ajustada ao espaço, ou inserida no Revit). A chave do Google guardada antes é apagada.

## Ajustes da versão 1.5.5

- **Croqui de satélite legível**: o GeoPoint usa o zoom mais detalhado que **existe** na região (onde a Esri não tem o zoom 18/19,
  ele desce sozinho até o zoom com foto, em vez de pôr a imagem cinza "Map data not yet available" na prancha) e nunca amplia a foto
  mais de 2× – quando o zoom disponível é baixo, o croqui mostra mais vizinhança e o lote ganha um **anel laranja** em volta.
- **Croqui desenhado** (sem imagem): lotes do projeto em volta, o lote da prancha em laranja, as ruas com o nome, o norte e a escala
  gráfica. Entra sozinho quando não há internet, quando a região não tem imagem ou quando o projeto não tem referência – e pode ser
  escolhido como fornecedor (*Desenho do projeto*), sem depender de internet nem de licença.
- **Conferência dos textos no Revit**: depois de anotar (vista ou prancha), o GeoPoint mede o tamanho real de cada texto e avisa se
  algum ficou maior que o previsto (ex.: fonte mais larga que o Arial) e pode encostar em outro.
- Retirado o roteiro de teste do pacote.

## Novidades da versão 1.5.4

- **Desenhar Perímetro** (antes *Desenhar por Azimutes*): a mesma ferramenta agora desenha o lote **por azimutes e distâncias** ou
  **pelas coordenadas dos vértices** – UTM (E, N), latitude/longitude (graus decimais ou °'") ou coordenadas locais (X, Y de estação
  total). Cole o texto de um memorial, do SIGEF ou as linhas de uma planilha: o GeoPoint encontra os vértices, o sistema e o datum,
  desenha a prévia e **confere as coordenadas com os azimutes e as distâncias escritos no texto** (trechos que não batem ficam em
  vermelho). O lote pode ser desenhado **na posição exata das coordenadas** (projeto com referência) ou a partir de um clique – e, num
  projeto ainda sem referência, o primeiro vértice pode virar o **ponto de referência do projeto**.

## Ajustes da versão 1.5.3

- **Espaço para a logomarca do escritório** em todas as pranchas: bloco entre o croqui e o título (retrato e paisagem, A4 a A1).
  Escolha a imagem em *Pranchas › Modelos… › Escritório › Logomarca* (PNG, JPG, GIF ou BMP até 3 MB). A imagem fica **guardada
  dentro do modelo de prancha** – vai junto quando você exporta o modelo para outro computador – e aparece com a proporção certa,
  centralizada; com o *Nome* do escritório preenchido, a logomarca fica à esquerda e o nome ao lado. Sem logomarca o espaço fica em
  branco na prancha (para colar a imagem depois, se preferir). Modelos gravados em versões anteriores: clique em *Reorganizar blocos*
  para abrir o espaço.
- **Nomes mais claros**: *Área Edificável* agora é **Recuos** (onde se pode construir pelos recuos) e *Área Edificada* agora é
  **Construção Existente** (edificações que já existem). Os documentos continuam com os termos técnicos (área edificável / área edificada).
- **Chave do Google protegida**: fica no *Gerenciador de Credenciais do Windows*, não mais em texto no arquivo de configurações
  (a chave antiga é levada para lá sozinha na primeira vez).
- **Importar Pontos com datum**: se a planilha disser o datum (ex.: `Datum: SAD 69`, `Córrego Alegre`, `SIRGAS 2000`, `WGS 84` ou o
  código EPSG), os pontos são convertidos para SIRGAS 2000; sem datum escrito vale SIRGAS 2000 e o GeoPoint avisa.
- **Conferência antes de emitir** com a opção *Selecionar no desenho os lotes com pendência*.
- **Cópias de segurança compactadas** (arquivos menores) e sem repetição: uma cópia igual à anterior não é gravada de novo. Também antes
  de *Hachuras* e do *Memorial* (quando os dados do imóvel mudam).
- **Desmembramento com a ferramenta Linha**: fechar o projeto encerra a janela de prévia; *Concluir* pede para voltar ao projeto
  certo se outro estiver ativo.
- Rótulo do lote igual na vista, na prancha e nas prévias; prévia do *Anotar Lotes* com exatamente as mesmas regras da anotação;
  KML/GeoJSON com o proprietário e a matrícula efetivos (lote do desmembramento sem matrícula própria não herda a do imóvel).
- **Glossário** no fim deste manual.

## Ajustes da versão 1.5.2

- **Rótulo do lote sempre dentro do lote**: o nome, o proprietário e os demais dados são quebrados em linhas pela largura do lote e o
  texto é reduzido se preciso (até 60%); valor e unidade nunca se separam ("ÁREA = 320,00 m²"). Na mesma prancha todos os rótulos
  ficam com o mesmo tamanho. Só em lotes muito pequenos ficam apenas nome e área.
- **Divisória na mudança de confrontante**: linha contínua e forte (estilo *GeoPoint - Divisão de confrontante*) no ponto em que o
  confrontante muda, atravessando a faixa da distância e do nome, separando um trecho do outro. O nome do ponto fica dentro do lote.
- **Prancha geral com os pontos dos vértices**: só o ponto (sem P-01 e sem coordenadas).
- **Remembramento**: o memorial é só do lote unido – sem histórico dos lotes que foram unidos e sem matrícula.

## Ajustes da versão 1.5.1 (revisão dos projetos de teste)

- **Confrontante pelo proprietário**: quando o confrontante é um lote do projeto, aparece só o nome do proprietário (sem número de
  lote e quadra). Sem proprietário informado, aparece o nome do lote e a conferência avisa. Outras formas em *Configurações ›
  Vértices e formatos › Confrontante* (lote e quadra; lote e quadra com o proprietário).
- **Dois confrontantes na mesma linha**: o ponto onde o confrontante muda é um vértice, com a **distância e o azimute de cada trecho**
  (desenho, tabela e memorial) e a marca de *mudança de confrontação*. O remembramento não apaga mais esses pontos (só junta trechos
  alinhados do **mesmo** confrontante).
- **Confrontante repetido**: quando o mesmo confrontante segue por vários lotes na mesma linha (ex.: fundos ou rua), o nome aparece
  **uma vez só**, no meio do conjunto.
- **Prancha geral** (vários lotes): sem coordenadas de referência – ela mostra a visão do conjunto. As coordenadas ficam nas pranchas de cada lote.
- **Convenções** iguais ao desenho: cada lote com a sua cor (até 6 lotes; acima disso uma entrada geral), o remanescente com a hachura
  cruzada na cor dele, e só os símbolos que aparecem na prancha (vértices, coordenada de referência, mudança de confrontação, meio-fio…).
- **Etiqueta das divisas nas pranchas**: `distância / AZ= azimute` (o memorial continua por extenso).
- **Matrícula no desmembramento**: só o **lote remanescente** fica com a matrícula do lote original; os demais lotes resultantes ficam
  sem matrícula (não herdam a do imóvel). Se você digitar a matrícula de um lote resultante, ela é usada.
- **CPF/CNPJ**: um lote com outro proprietário não herda o CPF do imóvel; a conferência avisa quem está sem CPF. No carimbo, vários
  proprietários aparecem como "Nome (CPF)".
- **Memorial de cada lote**: um por lote resultante, **inclusive o remanescente**, falando **só do próprio lote** (sem origem, sem os
  demais lotes). O subtítulo traz o procedimento (*Desmembramento*, *Desmembramento – lote remanescente*, *Remembramento*).

---

## Novidades da versão 1.5

- **Prancha nova**, no modelo da sua folha "Situação atual – Folha 02": mapa com **grade de coordenadas** atrás do desenho e os
  valores nas bordas, **norte**, **tabela de coordenadas** num canto livre do mapa, **coordenada do ponto de referência de cada lote**
  no mapa, e o carimbo com *Identificação e propriedade*, *Registro e localização*, *Quadro de áreas e perímetros*, *Quadro de
  assinaturas*, *Convenções topográficas*, *Orientação* (com escala gráfica), *Croqui de localização* e título com a **folha**.
- **Biblioteca de modelos de prancha** (vários modelos, ex.: um por escritório), com prévia, estilos de texto e histórico de versões.
- **Croqui de satélite automático** com o lote desenhado por cima (Esri, Google ou servidor próprio).
- **Área Edificada** (hoje *Construção Existente*): edificações desenhadas com linhas, taxa de ocupação e **distância até cada divisa** (desenho, memorial e pranchas).
- **Desmembramento desenhando com a ferramenta Linha do Revit**, com prévia que se atualiza a cada linha, e **corte em leque**.
- **Prévias** nas pranchas, no Anotar Lotes, nas Vias, no Desenhar por Azimutes (hoje *Desenhar Perímetro*) e nos modelos de prancha.
- **Importar Pontos** de planilha (CSV/TXT) ou coordenadas coladas do Excel/SIGEF; **Exportar PDF**; **cópias de segurança**;
  **conferência antes de emitir**; **arquivo de diagnóstico**; **projeto de exemplo**.
- Correções: textos das divisas organizados como no seu modelo (confrontante em cima, `53,55 m   AZ= 60°11'09"` embaixo),
  rótulo do lote com proprietário, matrícula, área e perímetro (sem CPF), textos sempre alinhados à vista, reanotação ao mudar a
  escala, larguras reais do Arial (fim dos textos encavalados), remembramento sem os pontos dos lotes antigos, numeração de
  vértices por lote como padrão, vias com pista de 7 m + calçada de 2 m e raio de esquina de 2 m, memorial sem "notas técnicas",
  dados do imóvel num lugar só e campos das janelas sem textos cortados.
- **Retirados** (o Revit ou outra ferramenta já faz): *Elementos Nativos* (os parâmetros `GP_*` agora vão nas **Hachuras**),
  *Quadro de Áreas* (a verificação urbanística fica em *Verificar Projeto*; o quadro aparece na prancha e no memorial) e
  *Área sobre Toposolid*.

---

## 2. Fluxo de trabalho recomendado

1. **Desenhe as divisas** com linhas nativas do Revit (linhas de modelo ou de detalhe) usando snap nos encontros – ou
   **Desenhar Perímetro** (azimutes e distâncias ou coordenadas, digitados ou colados de um memorial/SIGEF/planilha), ou
   **Importar Pontos** (KML/KMZ, planilha ou coordenadas coladas) e ligue os pontos, ou **Gerar Lote** pelo formato.
   Também é possível usar linhas de um **CAD vinculado/importado** (DWG/DXF) ou um **PDF** importado como base para traçar.
2. **Reconhecer Lotes** – selecione as linhas e clique dentro de cada lote (ou crie todos de uma vez).
3. **Dados do Imóvel** – proprietário, CPF/CNPJ, matrícula, município, UF, **CEP**, cartório, RRT/ART e perfil urbanístico.
   É o **único lugar** desses dados: no *Gerenciar Lotes* os campos vazios usam os dados do imóvel (aparecem em cinza);
   preencha no lote só o que for diferente (ex.: o proprietário de um lote do desmembramento).
4. **Gerenciar Lotes** – nome/número, quadra, uso, **confrontantes** e **testadas**.
5. **Definir Referência** – coordenada de **um** vértice (Google Maps/Earth, QGIS, GPS ou UTM).
6. **Vias**, **Construção Existente**, **Anotar Lotes**, **Hachuras**.
7. **Desmembramento** / **Remembramento** – com prévia; o original fica arquivado no histórico.
8. **Memorial Descritivo**, **Pranchas**, **Exportar PDF**, **Anuência de Confrontantes**, **Planilha Excel**, **Google Earth / QGIS**.

Antes de emitir memorial e pranchas o GeoPoint faz a **conferência**: proprietário, matrícula, município, responsável técnico e
RRT/ART preenchidos, área e perímetro medidos × os da matrícula (diferença acima de 5%) e divisas sem confrontante.
Você pode corrigir, **selecionar no desenho os lotes com pendência** ou gerar mesmo assim.

### Fases do projeto
Use **Fases** (painel Lotes) para separar as etapas, ex.: *Fase 0 – Situação atual* → *Fase 1 – Remembramento* → *Fase 2 – Desmembramento*.
**Você define as fases**: o projeto pode ter uma fase só; na tabela edite o **número**, o **nome**, o **tipo** (Regularização,
Desmembramento, Remembramento, Loteamento, Usucapião…) e a **descrição** de cada fase – inclusive da primeira.
- **Nova fase**: escolha o nome, o tipo e de qual fase ela parte, e marque **quais lotes ativos entram** (cópias independentes,
  com confrontantes, testadas e **edificações**) – ou *(nenhuma – fase vazia)*. A fase de partida não muda.
- **Excluir fase**: qualquer fase pode ser excluída, desde que não seja a única e nenhuma outra fase parta dela.
- Cada fase tem seus **lotes, operações, edificações, numeração de vértices**, memoriais e pranchas.
- Cada fase tem uma **vista de trabalho** ("F1 – Remembramento"), cópia da planta. **Abrir a vista de uma fase torna essa fase a ativa.**

### Numeração dos vértices
Padrão: **por lote** – cada lote tem os seus vértices P-01, P-02… (com vários lotes na mesma vista, cada lote mostra os seus
números dentro dele). Também é possível numerar por **projeto** (cada ponto um número único; num desmembramento os vértices do
original mantêm o número) ou por **quadra** – em *Configurações › Vértices e formatos* ou na janela do memorial.

---

## 3. Comandos

### Painel Lotes

**Reconhecer Lotes**
- Selecione as linhas antes do comando ou selecione depois (janela/cerca e *Concluir*).
- O GeoPoint une as extremidades que se tocam (tolerância de 1 mm), **quebra as linhas em todos os encontros** (inclusive
  em "T") e encontra todas as áreas fechadas. O diagnóstico mostra falhas de conexão, linhas soltas, sobrepostas e cruzamentos
  sem vértice; **Mostrar no Revit** seleciona as linhas do problema.
- Modos: **clicar dentro de cada lote** ou **criar todos**. Para CAD com vários layers, escolha os layers das divisas.

**Gerar Lote** – gera o lote pelo formato, digitando a medida de cada lado: quadrado, retângulo (vários lado a lado), triângulo,
trapézios, quadrilátero pelos 4 lados + ângulo ou diagonal, lote em L, esquina com chanfro ou curva e polígono por lados e ângulos.
Prévia com medidas, área e perímetro; desenha linhas nativas, cadastra e anota.

**Gerenciar Lotes**
- Dados do lote: os campos **Proprietário, CPF/CNPJ, Endereço e Matrícula** vazios usam os *Dados do Imóvel* (mostrados em cinza).
- **Sentido** da descrição e **vértice inicial**.
- Tabela de divisas: **tipo** do confrontante (*Texto / automático*, *Lote do projeto* ou *Via*), **lado** (automático pela testada
  ou fixo) e **testada**. Lote do projeto e via acompanham desmembramentos e remembramentos.
- **Dividir divisa…** – vários confrontantes numa divisa (ex.: `8; 4`); **Juntar trecho** desfaz.

**Atualizar Lotes** – depois de editar as linhas, recalcula os lotes (confrontantes e testadas mantidos).

**Fases** – ver acima.

**Dados do Imóvel** – cabeçalho do memorial e do carimbo, **situação** do processo e **perfil urbanístico**.

### Painel Georreferência

**Definir Referência**
1. Clique no vértice cuja coordenada você conhece (snap de extremidade/interseção).
2. Informe a coordenada: latitude/longitude (cole do Google Maps/Earth, QGIS, GPS – aceita graus decimais ou °'"), UTM
   (fuso, hemisfério, E e N) ou um marcador de KML/KMZ; datum WGS 84/SIRGAS 2000, SAD 69 ou Córrego Alegre; altitude; origem.
3. Orientação: **o topo da tela é o norte** (padrão, como você desenhou), norte verdadeiro gravado no Revit, azimute de uma linha
   ou dois pontos com coordenadas conhecidas.

> **Precisão:** coordenadas do Google Maps/Earth e de GPS de navegação têm erro de alguns metros. Para cartório ou SIGEF/INCRA a
> coordenada de partida precisa vir de levantamento geodésico feito por profissional habilitado.

**Importar Pontos** – escolha a origem:
- **KML/KMZ** do Google Earth ou do QGIS (marcadores);
- **planilha CSV ou TXT** – uma linha por ponto: nome e as duas coordenadas, separados por `;`, tabulação ou vírgula.
  Aceita **UTM** (`P-01; 235.142,50; 9.299.292,99`, E e N em qualquer ordem) e **latitude/longitude** em graus decimais
  (`-6,333656; -47,394191`) ou graus/minutos/segundos (`6°20'01,1641" S; 47°23'39,0858" O`). Cabeçalho é ignorado;
- **coordenadas coladas** – copie as linhas no Excel, no SIGEF ou num memorial e escolha *Colar*.
UTM usa o fuso da referência do projeto (sem referência: o fuso das Configurações, ou 23 S – o GeoPoint avisa).
**Datum**: escreva-o em qualquer linha de título da planilha (`Datum: SAD 69`, `Córrego Alegre`, `SIRGAS 2000`, `WGS 84` ou o código
EPSG, ex.: `EPSG:29193`) e os pontos são convertidos para SIRGAS 2000. Sem datum escrito, vale SIRGAS 2000 e o GeoPoint avisa.
Marque os pontos, escolha a posição (**clicar onde fica um dos pontos**, que vira a referência, ou usar a referência já definida) e,
se quiser, **Ligar os pontos com linhas** (polígono fechado na ordem da lista) – depois é só *Reconhecer Lotes*.
Cada ponto aparece como cruz laranja com o nome; desenhe as divisas com o snap de interseção. *Verificar Projeto* e a planilha
mostram a distância de cada ponto ao vértice mais próximo.

**Desenhar Perímetro** – desenha o contorno de um lote por azimutes e distâncias ou por coordenadas – digitados, colados ou lidos de
uma **certidão/memorial em PDF ou imagem** (*Importar certidão ou memorial*, ver abaixo):

*Azimutes e distâncias*
- A janela abre com um **modelo de texto pronto** (*"Inicia-se no vértice P-01; deste, segue com azimute de 90°00'00" e distância de
  30,00 m até o vértice P-02; …"*): troque os valores ou cole o texto de um memorial antigo.
- O texto é **interpretado enquanto você digita**: azimutes (ou rumos) e distâncias aparecem na tabela e o **desenho aparece na prévia**,
  com o **erro de fechamento** em vermelho e a precisão (1:x). Distribuição do erro (Bowditch) opcional.
- Entrada em **azimute**; a coluna *No formato do projeto* mostra cada trecho como sairá (azimute ou rumo, casas decimais configuradas).
- Arcos: informe o raio (a distância é a corda).
- Se o texto também trouxer as **coordenadas dos vértices**, aparece o aviso *Desenhar pelas coordenadas* (mais preciso, com conferência).
  Com só a coordenada do primeiro vértice, dá para **começar nela** (projeto com referência).

*Coordenadas*
- Cole ou digite: **memorial** (*"vértice P-01, de coordenadas N 9.299.292,99 m e E 235.142,50 m"*, também `E=… N=…`, `X/Y`,
  `Este/Norte`), **SIGEF** (*"Longitude: -47°23'39,086", Latitude: -6°20'01,164""*, com S/W/O) ou **linhas de planilha**
  (`P-01; 235142,50; 9299292,99`, com tabulação, ponto e vírgula ou espaços; cabeçalho `N;E` ou `Longitude;Latitude` inverte as colunas).
- O GeoPoint reconhece o **sistema** – **UTM**, **geográficas** ou **locais** (X para leste e Y para norte, ex.: estação total com
  1000/1000) – e o **datum** escrito no texto (SIRGAS 2000, WGS 84, SAD 69, Córrego Alegre ou EPSG). Confira e troque se preciso;
  o **fuso** vem da referência do projeto (ou das Configurações).
- Tabela de vértices: nome, coordenadas, **raio até o próximo** (arcos) e lado do arco, o **azimute e a distância até o próximo** no
  formato do projeto e a **conferência com o texto**: cada trecho é comparado com o azimute e a distância escritos no memorial
  (✓ confere, dizendo se bateu com o azimute **plano UTM** ou o **verdadeiro**; ⚠ com a diferença, em vermelho na tabela e na prévia).
  A tolerância é de 30" (ou o ângulo de 2 cm nos trechos curtos) e 5 cm.
- *Fechar o polígono* liga o último vértice ao primeiro; o vértice inicial repetido no fim do texto é ignorado. *Inverter sentido*
  percorre os vértices ao contrário.
- O desenho fica no plano topográfico local do projeto (distâncias horizontais reais), como o resto do GeoPoint.

*Posição do desenho*
- **Na posição das coordenadas** – com a referência do projeto definida, o lote cai exatamente onde as coordenadas indicam, junto dos
  outros lotes georreferenciados;
- **Clicar onde fica o primeiro vértice** – e, se o projeto ainda não tem referência, *Usar o primeiro vértice como ponto de
  referência do projeto* (o topo da vista é o norte): o projeto fica georreferenciado por essas coordenadas;
- **Começar no ponto de referência do projeto** (aba de azimutes).
As linhas criadas ficam selecionadas, prontas para *Reconhecer Lotes* (ou o lote já sai cadastrado, ver abaixo). Tudo é desfeito com um
*Desfazer*.

*Importar certidão ou memorial (PDF ou imagem)*
- **PDF digital** (gerado pelo cartório ou pelo sistema): o texto é lido do próprio PDF – os números saem exatos. **PDF escaneado, foto
  do celular ou imagem** (JPG, PNG, TIF): o texto é reconhecido pelo **OCR do Windows**, no próprio computador (nada é enviado pela
  internet). Precisa do idioma *Português (Brasil)* instalado no Windows (Configurações › Hora e idioma › Idioma); sem ele, o GeoPoint avisa.
- O GeoPoint encontra o perímetro e põe na aba certa: **coordenadas** (memorial, SIGEF ou tabela de vértices com azimute, distância, E e
  N – como a tabela das pranchas do GeoPoint), **azimutes/rumos e distâncias**, ou **só as medidas dos lados** ("12,00 m de frente para a
  Rua X, 30,00 m pelo lado direito…"): neste caso o desenho supõe as laterais perpendiculares à frente e avisa para conferir os ângulos.
  O **fuso UTM** e o **datum** escritos na certidão também são usados.
- Erros comuns de OCR são corrigidos nos números (O no lugar de 0, l no lugar de 1, espaços dentro do número e do ângulo, grau lido
  como "o").
- **Aba Documento** (ao lado da prévia): as páginas, com os números que entraram no desenho **destacados em amarelo**; zoom (− / + /
  Largura) e *Abrir o arquivo*. Arraste a divisória para alargar o painel.
- **Aba Dados da certidão**: matrícula, proprietário **atual** (o último adquirente dos registros), CPF/CNPJ, lote, quadra, endereço,
  bairro, CEP, município, UF, comarca, cartório, área e perímetro – corrija o que precisar e desmarque o que não quiser usar; e o
  **confrontante de cada divisa** (pelo proprietário quando a certidão diz "de propriedade de…"; ruas marcadas como testada).
- **Conferência**: além de conferir as coordenadas com os azimutes e distâncias do texto, o resultado mostra a **área e o perímetro do
  desenho × os da certidão** – diferença acima de 1% indica número lido errado.
- Ao **Desenhar**: *Preencher os Dados do Imóvel* (só os campos vazios, ou todos com *Substituir*) e *Cadastrar o lote ao desenhar* – o
  lote já sai pronto (lote, quadra, matrícula, área e perímetro do título e o confrontante de cada divisa), sem *Reconhecer Lotes*.
- O documento **não é guardado** no projeto (tem CPF): só os dados que você confirmou.

### Painel Operações

**Desmembramento**
1. Escolha o **lote a desmembrar** e o modo:
   - **Automático** – frente, início/fim, critério (**partes iguais**, **mesma área**, **área de cada lote** `300; 250; 250`,
     **mesma testada**, **testada de cada lote** `12; 12; 6`, resto mínimo) e direção das divisas:
     perpendicular à frente, paralela a uma divisa, por azimute ou **em leque, pelo ponto** – todas as divisas saem do mesmo vértice
     (ou ponto do contorno) e giram em torno dele até cada parte ter a área pedida (*girar ao contrário* inverte o sentido);
   - **Pelas linhas desenhadas** – clique em **Desenhar com a ferramenta Linha…**: a ferramenta Linha do Revit é ativada e uma janela
     de prévia fica aberta ao lado; **a cada linha que você desenha as partes e as áreas se atualizam**. Botões para trocar entre linha de
     modelo e de detalhe e para desfazer a última linha. Ao terminar clique em **Concluir** (pressione *Esc* no Revit se a ferramenta ainda
     estiver ativa) e a janela do desmembramento volta com as divisas. Também dá para *Selecionar linhas já desenhadas…*;
   - **Lotes já reconhecidos** – usa lotes que você já criou dentro do original.
2. **Prévia** colorida com área e testada de cada parte.
3. Tabela das partes: **nome**, **lote remanescente**, **proprietário** e **Anexar a** um lote vizinho.
   *Encaixar no vértice existente a menos de (m)* evita trechos de poucos centímetros.
4. Ao registrar: divisas desenhadas, lotes criados com confrontantes herdados, vizinhos atualizados, original arquivado, anotações
   e hachuras refeitas e, se marcado, as **pranchas** da situação atual e proposta.

**Remembramento** – marque os lotes contíguos e veja a prévia do lote unificado. O lote unificado tem **só os vértices do novo
contorno, em qualquer formato**: pontos dos lotes antigos sobre um mesmo alinhamento (divisas retas em sequência ou arcos do mesmo
círculo) são retirados e os confrontantes desses trechos são juntados ("Fulano, Beltrano e Sicrano").

**Recuos** (área edificável) – para planejar uma construção nova: recuo por divisa (ou do perfil); calcula e desenha a área onde se
pode construir.

**Construção Existente** (área edificada) – edificações que já existem dentro dos lotes:
1. desenhe o contorno de cada edificação com linhas (modelo ou detalhe) **dentro do lote** e use *Adicionar pelas linhas desenhadas…*
   (várias edificações de uma vez; cada uma é atribuída ao lote que a contém);
2. na tabela: nome, uso, **pavimentos** e, se quiser, a **área construída** informada (vazio = projeção × pavimentos);
3. o GeoPoint calcula a **área de projeção**, a área construída, a **taxa de ocupação**, avisa se parte dela está fora do lote e mostra a
   **distância da edificação até cada divisa** (frente, fundos, laterais, com o confrontante), com prévia;
4. *Salvar e desenhar na vista*: hachura cinza, rótulo (nome e área) e **cotas de afastamento** até as divisas.
As edificações aparecem no **memorial** (seção *ÁREA EDIFICADA* com as distâncias às divisas), nas **pranchas** (convenção
"Área edificada"; cotas de afastamento nas pranchas de um lote) e acompanham o lote entre fases e desmembramentos.
*Atualizar pelas linhas* relê o contorno depois de editar o desenho.

### Painel Anotação

**Anotar Lotes** – na vista de planta atual:
- cada divisa: **nome do confrontante em cima** e, logo abaixo, **`distância / AZ= azimute`** numa linha só (azimute no formato
  configurado), do lado de fora do lote. Divisa curta: só a distância. O confrontante é o **proprietário** e aparece uma vez só quando
  segue por vários lotes na mesma linha;
- **ponto preenchido** em cada vértice e o nome do vértice (numeração por lote: dentro do próprio lote);
- **rótulo do lote** no centro: nome (negrito), **proprietário**, **matrícula**, **área** e **perímetro** (sem CPF; cada linha pode ser
  desligada);
- **coordenada do ponto de referência de cada lote**: ponto, linha de chamada, nome do vértice e as coordenadas no sistema escolhido
  nas Configurações (ex.: `235.142,50 m E` / `9.299.292,99 m S`, ou `Lat:`/`Lon:`). O ponto padrão é o P-01; escolha outro nas Pranchas;
- dois confrontantes na mesma linha: o ponto de mudança é vértice (com distância e azimute de cada trecho) e recebe a marca de
  *mudança de confrontação*; se a **rua já está desenhada** na vista (Vias), o nome da rua não é repetido na divisa;
- **nenhum texto sobre outro ou sobre as linhas**, todos **alinhados à vista** (nunca tortos ou de cabeça para baixo), com a largura real
  das letras Arial;
- **prévia** à direita, com os textos na escala da vista, atualizada a cada opção;
- depois de criar, o GeoPoint **mede no Revit** o tamanho real de cada texto e avisa se algum ficou maior que o previsto (fonte mais larga
  que o Arial, por exemplo) e pode encostar em outro;
- se a **escala da vista mudou** depois da anotação, o GeoPoint avisa e já marca os lotes para anotar de novo no tamanho certo.

**Hachuras** – uma cor por lote (remanescente com hachura cruzada). Opção **Preencher parâmetros GP_*** (`GP_Lote`, `GP_Quadra`,
`GP_Uso`, `GP_Area`, `GP_Perimetro`, `GP_Testada`, `GP_AreaLiquida`, `GP_Matricula`, `GP_Proprietario`) nas hachuras, para usar
**Tabelas (Schedules)**, etiquetas e filtros nativos do Revit.

**Vias** – desenho esquemático da rua:
- **Selecionar no desenho: testadas e meio-fio**: a calçada é medida no desenho e a **pista** (de meio-fio a meio-fio) é desenhada;
- padrões: **pista de 7 m**, **calçada de 2 m**, **raio de esquina de 2 m** (cruzamentos em "+", "T" ou "L" com cantos em curva);
- abas *Vias*, *Testadas* (a via vira o confrontante da divisa) e *Cruzamentos* (2, 3 ou 4 vias);
- **prévia** do desenho com as edições ainda não gravadas (*Atualizar prévia*).

**Tabela de Coordenadas** – vista de desenho com o quadro de vértices de cada lote.

### Painel Documentos

**Memorial Descritivo** (Word .docx)
- Tipos: **Lote(s)**, **Desmembramento**, **Remembramento**, **Loteamento**.
- Sistemas: UTM SIRGAS 2000, geográficas, SAD 69, Córrego Alegre, Plano Topográfico Local, TM personalizada e coordenadas locais.
- Azimute verdadeiro, plano UTM ou geodésico; azimute ou rumo; distâncias horizontais, UTM ou geodésicas; m² e/ou ha.
- **Sem "notas técnicas"** no final. A descrição termina com **uma frase** do referencial (*"Todas as coordenadas aqui descritas estão
  georreferenciadas ao Sistema Geodésico Brasileiro, datum SIRGAS 2000, projeção UTM, fuso 23, hemisfério Sul…"*), exigida por muitos
  cartórios e pelo INCRA – edite ou apague em *Frases da descrição* (`Referencial`).
- **Construção existente** de cada lote (seção *ÁREA EDIFICADA*), com as distâncias até as divisas (opção *Construção existente* na
  janela do memorial).
- Desmembramento: por padrão **um memorial por lote resultante** (numeração própria P-01…); também só o geral ou os dois.
- Conferência antes de emitir (ver o fluxo).

**Anuência de Confrontantes** – declaração para cada confrontante, com a descrição da divisa comum e assinaturas.

**Pranchas** – cada prancha usa uma **cópia** da planta (o desenho original não é alterado):
- *Situação atual* e *Situação proposta*, juntas e/ou **cada lote individualmente**; sem operação: planta de situação dos lotes marcados;
- **modelo de prancha** escolhido na janela (botão *Modelos…* abre a biblioteca, ver abaixo);
- **mapa**: a vista é recortada exatamente na área do mapa do modelo, na **escala que melhor cabe** (a menor da lista do modelo em que o
  desenho cabe com folga para os textos) ou numa escala fixa; **grade de coordenadas** (linhas de fundo atrás do desenho) com os valores
  nas quatro bordas (graus/minutos/segundos ou `E 235.140`, `N 9.299.300`); **norte** no canto. A grade passa por fora da tabela, do norte
  e dos textos;
- **coordenadas da prancha** escolhidas na janela: **Geográficas (latitude/longitude) – SIRGAS 2000** (padrão), UTM – SIRGAS 2000 ou o
  primeiro sistema do memorial – na grade, na tabela, nas chamadas e no carimbo;
- **tabela de coordenadas** (prancha de um lote: vértice, azimute, distância e as duas coordenadas, com o sistema geodésico e o EPSG) ou
  **quadro de áreas** (prancha com vários lotes) **dentro do mapa, num canto livre** – o desenho se desloca para não encostar nela;
- **coordenada do ponto de referência de cada lote** no mapa (todas as pranchas). Nas pranchas de um lote você escolhe o vértice na
  tabela ou com *Escolher o ponto de referência no desenho…*; a escolha **fica guardada no lote** e vale para todas as pranchas dele;
- pranchas gerais: distâncias, confrontantes, rótulos e chamadas de coordenada (sem números de vértice e sem azimutes); pranchas de um lote:
  numeração própria (P-01…), todos os azimutes, pontos nos vértices e cotas de afastamento das edificações;
- **carimbo** (preenchido sozinho): *Identificação e propriedade* (proprietário, CPF/CNPJ, matrícula), *Registro e localização*
  (procedimento, **transcrição** – ex.: "UNIFICAÇÃO DOS LOTES DAS MATRÍCULAS Nº 7607 E Nº 7033, COM CONSTITUIÇÃO DE LOTE URBANO RESULTANTE
  DO REMEMBRAMENTO" –, município/UF, endereço e CEP), *Quadro de áreas e perímetros*, *Quadro de assinaturas* (responsável técnico com
  CAU/CREA e RRT/ART, e proprietário), *Convenções topográficas* (só o que aparece no desenho), *Orientação* (sistema geodésico,
  projeção, fuso, meridiano central, EPSG, **escala** e **escala gráfica**), o espaço da **imagem de localização**, o espaço da **logomarca do
  escritório** e o **título** com a **FOLHA**;
- **folhas numeradas por projeto** (FOLHA 01, 02, 03…; uma prancha substituída mantém o número);
- **imagem de localização**: espaço sem título no carimbo. Escolha a imagem em *Imagem de localização* (ajustada ao espaço) ou deixe em
  branco e insira a imagem no Revit;
- **prévia** da prancha selecionada (mesmo encaixe, carimbo e anotações), com avisos de legibilidade;
- **pranchas já geradas**: a tabela mostra se você **editou** a prancha e você escolhe *Substituir*, *Manter a atual* ou *Nova versão ao lado*;
- **conferência antes de emitir** e avisos de legibilidade (textos sem espaço, tabela reduzida, desenho apertado);
- se preferir o carimbo do escritório como família, informe-o em Configurações: o GeoPoint coloca o mapa, a grade, o norte e a tabela.
As pranchas ficam **editáveis** no Revit.

**Modelos de prancha** (botão *Modelos…* na janela de Pranchas) – biblioteca em `%AppData%\GeoPoint\modelos-prancha`:
- **novo** (retrato ou paisagem), **duplicar**, **excluir**, **importar/exportar** (`.json`, para levar a outro computador ou escritório);
- papel **A4, A3, A2 ou A1**, orientação, margens (padrão ABNT 25/7 mm), espaço entre blocos;
- **mapa**: grade (liga/desliga, espaçamento, cor), faixa dos valores, canto e tamanho do norte, tabela dentro do mapa e o canto dela,
  folga do desenho e **lista de escalas** permitidas;
- **estilos de texto** por tipo (título, cabeçalhos, rótulos, valores, tabelas, grade e textos do desenho), em mm no papel. O tamanho
  *Desenho* vale para os textos do mapa das pranchas; com 0 vale o das Configurações de anotação (o mesmo das vistas);
- **escritório**: **logomarca** e nome (opcional). O espaço da logomarca (bloco *Escritório (logomarca)*) fica entre a imagem de localização e o título e
  vem ligado no padrão. A imagem é **guardada dentro do modelo** (vai junto ao exportar); *Remover* tira a imagem. Sem imagem o espaço
  fica em branco na prancha (na prévia aparece "(logomarca do escritório)");
- **blocos do carimbo**: ligue/desligue, renomeie o cabeçalho e ajuste posição e tamanho (mm); *Reorganizar blocos* refaz a disposição
  padrão para o papel escolhido; bloco *Observações* com texto livre;
- **prévia ao vivo** com conteúdo de exemplo e avisos (blocos sobrepostos, textos pequenos demais);
- **histórico de versões**: cada gravação guarda a versão anterior (com uma nota); *Restaurar versão* volta a ela (a logomarca é sempre a atual).

**Exportar PDF** – exporta as pranchas pelo recurso do próprio Revit, em **escala real (100%)** e no papel de cada prancha (A4…A0,
retrato ou paisagem, detectado pelo tamanho da folha); um arquivo só ou um por prancha.

**Google Earth / QGIS** – KMZ, KML e GeoJSON com os lotes coloridos por uso e os vértices como pontos.

**Planilha Excel** – lotes, quadro de áreas, vértices, registro de vértices e segmentos.

### Painel Verificação

**Verificar Projeto** – geometria, divisas sem confrontante, sobreposições, **parâmetros urbanísticos** (área e testada mínimas,
áreas públicas, diferença para a área do título – o que antes ficava no *Quadro de Áreas*), linhas alteradas no desenho, norte da
vista e conferência dos pontos importados.

### Painel GeoPoint

**Configurações** – tolerâncias, numeração dos vértices, casas decimais, modelo e frases do memorial, responsável técnico,
sistemas de coordenadas do memorial, anotações, vias, KML e perfis urbanísticos. (As coordenadas das pranchas são escolhidas na janela
de Pranchas.)

**Ajuda** – fluxo, **manual**, **Restaurar uma cópia de segurança** (antes de cada operação que altera o projeto – reconhecer,
gerenciar, fases, desmembramento, remembramento, vias, construção existente, hachuras, memorial, pranchas… – o GeoPoint guarda uma
cópia compactada dos seus dados em `%AppData%\GeoPoint\copias` (uma cópia igual à anterior não é repetida); as 40 mais recentes ficam
guardadas; restaurar não altera o desenho do Revit e pode ser desfeito com
Desfazer), **Gerar arquivo de diagnóstico** (.zip na Área de Trabalho com registros, configurações sem chaves e os dados do projeto,
para enviar ao suporte) e **Criar um projeto de exemplo** (três lotes, uma via, uma edificação e a referência, num projeto vazio).

**Atalhos de teclado** – os comandos do GeoPoint aparecem na janela nativa *Atalhos de teclado* do Revit (*Vista › Interface do usuário ›
Atalhos de teclado*; pesquise "GeoPoint" ou filtre por *Ferramentas externas*). Sugestões: `GR` Reconhecer Lotes, `GG` Gerenciar Lotes,
`GA` Anotar Lotes, `GD` Desmembramento, `GP` Pranchas. Você também pode adicionar os botões à *Barra de acesso rápido*
(clique direito no botão › *Adicionar à barra de ferramentas de acesso rápido*).

---

## 4. Personalizar o memorial

1. *Configurações › Memorial › Exportar modelo padrão…* e abra o arquivo no Word.
2. Edite à vontade: logotipo, cabeçalho, rodapé, fontes, assinatura digitalizada etc.
3. Mantenha os **marcadores** onde quiser os dados:

| Marcador | Conteúdo |
|---|---|
| `{{TITULO}}`, `{{SUBTITULO}}` | "MEMORIAL DESCRITIVO" e tipo (DESMEMBRAMENTO, REMEMBRAMENTO…) |
| `{{PROPRIETARIO}}`, `{{DOCUMENTO}}` | Proprietário e CPF/CNPJ |
| `{{IMOVEL}}`, `{{ENDERECO}}`, `{{BAIRRO}}` | Denominação e endereço |
| `{{MATRICULA}}`, `{{CARTORIO}}`, `{{INSCRICAO}}`, `{{CCIR}}`, `{{CAR}}` | Registros |
| `{{MUNICIPIO}}`, `{{UF}}`, `{{COMARCA}}`, `{{FINALIDADE}}` | Localização e finalidade |
| `{{AREA}}`, `{{AREA_HA}}`, `{{PERIMETRO}}` | Área e perímetro principais |
| `{{CORPO}}` | Descrição completa (seções, descrição perimétrica, quadros e área edificada) – deixe sozinho num parágrafo |
| `{{LOCAL_DATA}}`, `{{DATA}}` | "Município – UF, 07 de julho de 2026" |
| `{{TECNICO_NOME}}`, `{{TECNICO_PROFISSAO}}`, `{{TECNICO_CONSELHO}}`, `{{TECNICO_REGISTRO}}`, `{{TECNICO_REGISTRO_TEXTO}}`, `{{TECNICO_EMPRESA}}` | Responsável técnico |
| `{{RRT_ART}}`, `{{RRT_ART_TEXTO}}` | Número do RRT/ART |
| `{{LOTE}}`, `{{QUADRA}}` | Lote principal |

- `{{CAMPO?}}` (com interrogação) **remove o parágrafo inteiro** quando o campo estiver vazio.
- Os marcadores também funcionam no cabeçalho e no rodapé.
4. *Configurações › Memorial › Escolher…* e selecione o seu modelo.

**Frases da descrição** – *Exportar frases padrão…* gera um JSON editável. Marcadores: `{VERTICE}`, `{COORDENADAS}`,
`{CONFRONTANTE}`, `{DIRECAO}` ("azimute de …" ou "rumo de …"), `{DISTANCIA}`, `{VERTICE_SEGUINTE}`, `{COORDENADAS_SEGUINTE}`,
`{RAIO}`, `{ANGULO_CENTRAL}`, `{DESENVOLVIMENTO}`, `{CORDA}`, `{AREA}`, `{PERIMETRO}` e, na frase `Referencial`, `{SISTEMA}`
(datum, projeção, fuso e meridiano central). Trechos entre `[ ]` somem quando um marcador interno está vazio
(ex.: `[ confrontando com {CONFRONTANTE},]`). Deixe `Referencial` vazio para não usar a frase do referencial.

---

## 5. Como os cálculos são feitos

- **Desenho = plano topográfico local**: as distâncias do Revit são horizontais verdadeiras. O norte é o norte verdadeiro do projeto
  (ou a orientação definida na referência).
- **Coordenadas geodésicas**: a partir do ponto de referência, cada vértice é levado do plano local (leste, norte) para o elipsoide
  GRS 80 (SIRGAS 2000) por transformação topocêntrica exata.
- **UTM / TM**: projeção Transversa de Mercator pela série de Krüger até n⁶ (precisão submilimétrica); convergência meridiana e
  fator de escala calculados em cada ponto. Conferido com o PROJ (pyproj) ao milímetro.
- **Distâncias UTM** já incluem fator de escala e redução ao elipsoide (altitude). **Distâncias geodésicas**: fórmula de Vincenty.
- **Áreas**: cálculo exato com arcos (teorema de Green). Área UTM pela projeção do contorno.
- **Datums**: SAD 69 e Córrego Alegre por translação geocêntrica com os parâmetros do IBGE/EPSG (precisão da transformação ≈ 5 m).
- **Plano Topográfico Local**: plano tangente na origem (ponto de referência ou origem municipal), na altitude indicada,
  com coordenadas de origem configuráveis (padrão 150.000 / 250.000 m).

---

## 6. Dicas e solução de problemas

- **"Nenhuma área fechada"** – veja as *falhas de conexão* no diagnóstico (geralmente uma linha que não encosta na outra).
  Use *Mostrar no Revit* e corrija com Aparar/Estender.
- **Área diferente da matrícula** – informe a *Área do título* no Gerenciador; a verificação compara com a tolerância do perfil.
- **Coordenadas deslocadas no Google Earth** – confira a coordenada colada e o norte do projeto. Com **dois pontos** o GeoPoint mostra
  a diferença de rotação e escala.
- **Projeto colaborativo (worksharing)** – os dados do GeoPoint ficam num único elemento do modelo; se outra pessoa estiver editando lotes
  ao mesmo tempo, sincronize antes.
- **Textos encavalados ou pequenos demais** – aumente a escala da vista (ex.: de 1:200 para 1:250) ou reduza o texto nas
  Configurações; a prévia do *Anotar Lotes* e das *Pranchas* mostra o resultado antes de gerar.
- **Certidão lida com erro** – confira os números destacados na aba *Documento*; foto torta ou escura piora o OCR (fotografe de frente,
  com boa luz). Corrija na tabela antes de desenhar.
- **Errei uma operação** – use *Desfazer* do Revit ou *Ajuda › Restaurar uma cópia de segurança*.
- **Registros de erro** – `%AppData%\GeoPoint\logs` (ou *Ajuda › Gerar arquivo de diagnóstico*).


---

## 7. Responsabilidade técnica

O GeoPoint é uma ferramenta de cálculo e documentação. O memorial descritivo é documento de responsabilidade do profissional
habilitado (CAU/CREA), que deve conferir os dados, a origem e a precisão das coordenadas e as exigências do cartório, da prefeitura
ou do INCRA/SIGEF.

---

## 8. Glossário

| Termo | No GeoPoint |
|---|---|
| **Lote** | Área fechada reconhecida a partir das linhas do desenho. Tem nome, quadra, uso, confrontantes, proprietário e matrícula (vazios usam os *Dados do Imóvel*). |
| **Vértice** | Ponto do contorno do lote (P-01, P-02…). A numeração pode ser própria de cada lote (padrão) ou única no projeto. |
| **Divisa** | Trecho do contorno entre dois vértices: tem distância, azimute (ou rumo) e confrontante. |
| **Confrontante** | Quem está do outro lado da divisa: um lote do projeto (aparece o **proprietário**), uma via ou um nome digitado. |
| **Testada** | Divisa de frente, voltada para a via. |
| **Mudança de confrontação** | Ponto numa linha reta onde o confrontante muda; vira vértice, com distância e azimute de cada trecho. |
| **Remanescente** | No desmembramento, o lote que fica com a matrícula do lote original (hachura cruzada). |
| **Desmembramento** | Divisão de um lote em vários; o original fica arquivado no histórico. |
| **Remembramento** | União de lotes contíguos num só, só com os vértices do novo contorno; memorial sem histórico e sem matrícula. |
| **Fase** | Etapa do projeto (ex.: situação atual, proposta) com os seus lotes; as operações criam fases. |
| **Desenhar Perímetro** | Desenha o contorno de um lote por azimutes e distâncias ou pelas coordenadas dos vértices, com conferência do texto. |
| **Referência** | Coordenada conhecida de um vértice e a orientação do norte: liga o desenho às coordenadas (UTM, geográficas…). |
| **Datum** | Sistema geodésico das coordenadas: SIRGAS 2000 (oficial), WGS 84 (≈ SIRGAS), SAD 69 e Córrego Alegre (antigos). |
| **Ponto de referência do lote** | Vértice cuja coordenada aparece com chamada no mapa (P-01 por padrão; escolha outro nas Pranchas). |
| **Modelo de prancha** | Papel, margens, blocos do carimbo, grade, norte, estilos de texto e logomarca; fica na biblioteca de modelos. |
| **Prancha geral / prancha do lote** | Geral: vários lotes, sem números de vértice nem coordenadas. Do lote: numeração própria, azimutes e tabela de coordenadas. |
| **Imagem de localização** | Espaço do carimbo para a imagem de localização (inserida por você). |
| **Certidão (inteiro teor)** | Documento do cartório com a descrição do imóvel; o *Desenhar Perímetro* lê o perímetro, os confrontantes e os dados dela. |
| **OCR** | Reconhecimento de texto em imagem (PDF escaneado ou foto), feito pelo próprio Windows. |
| **Recuos (área edificável)** | Onde se pode construir no lote, descontando o recuo de cada divisa. |
| **Construção existente (área edificada)** | Edificações que já existem no lote: área de projeção, área construída, taxa de ocupação e distância até cada divisa. |
| **Conferência** | Verificação feita antes de emitir memorial e pranchas (dados faltando, área diferente da matrícula, divisas sem confrontante). |
| **Cópia de segurança** | Cópia dos dados do GeoPoint gravada antes de cada comando que altera o projeto (*Ajuda › Restaurar*). |
