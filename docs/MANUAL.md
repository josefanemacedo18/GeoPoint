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

## 2. Fluxo de trabalho recomendado

1. **Desenhe as divisas** com linhas nativas do Revit (linhas de modelo ou de detalhe) usando snap nos encontros.
   Também é possível usar linhas de um **CAD vinculado/importado** (DWG/DXF) ou um **PDF** importado como base para traçar.
2. **Reconhecer Lotes** – selecione as linhas e clique dentro de cada lote (ou crie todos de uma vez).
3. **Gerar Lote** – gera o lote pelo formato, digitando a medida de cada lado:
- formatos: **quadrado**, **retângulo**, **triângulo** (3 lados), **trapézio retângulo**, **trapézio**, **quadrilátero** pelos 4 lados
  da matrícula + ângulo do canto (90° se não souber) ou + diagonal, **lote em L**, **esquina com chanfro**, **esquina com curva**
  (raio de concordância) e **polígono** por lados e ângulos internos (qualquer formato, com conferência do fechamento);
- prévia com a medida de cada lado, área e perímetro (vista da rua: frente embaixo, lateral direita à direita);
- quadrado e retângulo podem ser gerados **vários lado a lado** (as divisas comuns são uma linha só);
- posição: clique no canto **frente / lateral esquerda**; a frente fica na horizontal da tela, na direção de um 2º clique ou num azimute;
- desenha **linhas nativas** (de modelo ou de detalhe), cadastra o(s) lote(s) com nome/quadra/uso, marca a frente como testada e
  anota na vista. As linhas podem ser editadas depois (*Atualizar Lotes*).

**Gerenciar Lotes** – informe nome/número, quadra, uso, matrícula, **confrontantes** e marque as **testadas**.
4. **Dados do Imóvel** – proprietário, município, matrícula, RRT/ART, perfil urbanístico.
5. **Definir Referência** – cole a coordenada de **um** vértice (Google Maps/Earth, QGIS, GPS ou UTM).
6. **Google Earth / QGIS** – confira o desenho sobre a imagem de satélite.
7. **Vias** – cadastre as ruas, associe as testadas e os cruzamentos (esquinas).
8. **Desmembramento** / **Remembramento** – com prévia; o original fica arquivado no histórico.
9. **Pranchas A4**, **Anotar Lotes**, **Hachuras**, **Memorial Descritivo**, **Anuência de Confrontantes**, **Planilha Excel**, **Quadro de Áreas**.

### Fases do projeto
Use **Fases** (painel Lotes) para separar as etapas, ex.: *Fase 0 – Situação atual* → *Fase 1 – Remembramento* → *Fase 2 – Desmembramento*.
**Você define as fases**: o projeto pode ter uma fase só; na tabela edite o **número**, o **nome**, o **tipo** (Regularização,
Desmembramento, Remembramento, Loteamento, Usucapião…) e a **descrição** de cada fase – inclusive da primeira.
- **Nova fase**: escolha o nome, o tipo e de qual fase ela parte, e marque **quais lotes ativos entram** (cópias independentes,
  com confrontantes e testadas) – ou escolha *(nenhuma – fase vazia)* para começar sem lotes. A fase de partida não muda.
- **Excluir fase**: qualquer fase pode ser excluída (inclusive a primeira), desde que não seja a única e nenhuma outra fase parta dela.
- Cada fase tem seus **lotes, operações, numeração de vértices (começa em P-01)**, memoriais e pranchas (número `GP-F1-01`…).
- Cada fase tem uma **vista de trabalho** ("F1 – Remembramento"), cópia da planta. Nela as divisas internas dos lotes da fase ficam
  ocultas (só linhas de divisa são ocultadas; outras linhas do seu desenho continuam visíveis). A vista em que você estava passa a
  ser a vista da fase de partida.
- **Abrir a vista de uma fase torna essa fase a ativa.** Todos os comandos (Gerenciar, Anotar, Memorial, Pranchas, Excel, Google Earth)
  trabalham só com os lotes da fase ativa.
- As fases anteriores ficam preservadas como estavam (o histórico do processo).

### Numeração única dos vértices
Cada ponto do projeto tem **um único número** (registro de vértices). Num desmembramento os vértices do lote original mantêm
o número e só os pontos novos (onde as novas divisas encontram o contorno) recebem números novos. A numeração pode ser por
**projeto** ou por **quadra** – escolha na janela do memorial. A planilha Excel traz a aba *Registro de vértices*.

---

## 3. Comandos

### Painel Lotes

**Reconhecer Lotes**
- Selecione as linhas antes do comando ou selecione depois (janela/cerca e *Concluir*).
- O GeoPoint une as extremidades que se tocam (tolerância de 1 mm), **quebra as linhas em todos os encontros** (inclusive
  em "T", quando uma divisa termina no meio de outra) e encontra todas as áreas fechadas.
- A janela de diagnóstico mostra, por exemplo:
  - **Possível falha de conexão** – a linha termina a poucos milímetros de outra sem tocar (corrija com Aparar/Estender do Revit);
  - **Linha solta** – não fecha nenhuma área;
  - **Linhas sobrepostas** – trechos desenhados duas vezes;
  - **Cruzamento sem vértice** – duas linhas se cruzam (o cruzamento vira vértice).
  O botão **Mostrar no Revit** seleciona as linhas do problema.
- Modos de criação:
  - **Clicar dentro de cada lote** – cada clique cria um lote (a área clicada fica destacada em azul); *Esc* termina;
  - **Criar todos** – todas as áreas fechadas ainda não cadastradas viram lotes.
- Para CAD com vários layers, o GeoPoint pergunta quais layers contêm as divisas.

**Gerenciar Lotes**
- Dados do lote; **sentido** da descrição (horário/anti-horário); **vértice inicial**.
- Tabela de divisas, para cada divisa:
  - **Tipo** do confrontante: *Texto / automático* (texto livre; vazio = vizinho detectado), *Lote do projeto* ou *Via* cadastrada.
    Lote do projeto e via **acompanham desmembramentos e remembramentos automaticamente** (o nome é atualizado e os lotes
    resultantes herdam os confrontantes das divisas originais);
  - **Lado** do lote: *Automático* (calculado pela testada: frente, fundos, lateral direita/esquerda vistas da rua) ou fixo;
  - **Testada** (divisa de frente para a via);
  - *No memorial* mostra como a divisa vai sair no texto.
- **Dividir divisa…** – quando uma divisa tem mais de um confrontante (ex.: fundos com os lotes 16, 17 e 18), informe os
  comprimentos dos trechos (ex.: `8; 4`). São criados vértices sobre a divisa e cada trecho recebe o seu confrontante.
  Nas anotações aparece a extensão de cada confrontante (cota vermelha e marcas tracejadas). **Juntar trecho** desfaz.
- Arquivar/reativar e excluir lotes (excluir não apaga as linhas do desenho).

**Atualizar Lotes** – depois de editar as linhas no desenho, recalcula os lotes. Confrontantes e testadas são mantidos nas divisas correspondentes.

**Dados do Imóvel** – campos do cabeçalho do memorial, a **situação** do processo (Regularização, Desmembramento,
Remembramento, Unificação, Desdobro, Retificação de área, Usucapião, Loteamento, Condomínio, Levantamento planimétrico)
e o **perfil urbanístico** usado nas verificações.

### Painel Georreferência

**Definir Referência**
1. Clique no vértice cuja coordenada você conhece (o snap de extremidade/interseção é usado).
2. Informe a coordenada:
   - **Latitude/longitude** – cole como vem do Google Maps (clique direito no mapa → copiar coordenadas), Google Earth, QGIS ou GPS.
     Aceita `-6.337512, -47.390125`, `6°20'15,04"S 47°23'24,45"W`, `-6,337512; -47,390125` etc.
   - **UTM** – fuso, hemisfério, E e N.
   - **Marcador de KML/KMZ** – abra um arquivo com marcadores do Google Earth/QGIS e escolha o ponto.
   - Datum: WGS 84 (GPS/Google, considerado equivalente ao SIRGAS 2000), SIRGAS 2000, SAD 69 ou Córrego Alegre (transformados para SIRGAS 2000 com os parâmetros oficiais).
   - **Altitude** aproximada do terreno (influencia o fator de escala: 100 m ≈ 1,6 cm por km).
   - **Origem da coordenada** – declarada no memorial com a precisão estimada.
3. Orientação:
   - **O topo da tela é o norte – como você desenhou** (padrão): o "para cima" da vista em que você clicou é o norte.
     É o jeito certo quando você desenha com o norte para cima na tela;
   - **Norte verdadeiro gravado no Revit** (*Gerenciar › Localização do projeto*): use só se você ajustou o norte verdadeiro do Revit;
   - **Azimute de uma linha** que parte do ponto (depois do OK, clique na linha);
   - **Para cima na vista atual** – o topo da tela é o norte (útil quando o desenho foi feito "com o norte para cima" sem ajustar o norte verdadeiro do Revit);
   - **Dois pontos** com coordenadas conhecidas – o GeoPoint mostra a diferença de distância e de rotação entre o desenho e as coordenadas e pergunta qual orientação usar.
   A janela mostra para onde aponta o topo da vista atual pelo norte verdadeiro do projeto (ex.: "o topo da vista aponta para 270° – Oeste").
   Se o norte verdadeiro gravado no Revit não aponta para o topo da tela, a janela mostra um aviso em vermelho.
   **Projetos já referenciados:** na primeira vez que você gerar anotações, memorial, Google Earth, Excel, pranchas ou anuência,
   o GeoPoint pergunta qual é o norte do desenho (o topo da tela ou o norte do Revit) e grava a escolha – não é preciso refazer a referência.
   **Verificar Projeto** mostra para onde aponta o topo da vista pelo norte usado.

A partir desse ponto, todas as coordenadas são calculadas pelas **distâncias e azimutes do desenho** (o desenho é tratado como um
plano topográfico local, com distâncias horizontais verdadeiras).

> **Precisão:** coordenadas do Google Maps/Earth e de GPS de navegação têm erro de alguns metros. Servem muito bem para estudos,
> implantação e conferência sobre satélite. Para registro em cartório ou SIGEF/INCRA, a coordenada de partida precisa vir de
> levantamento geodésico (GNSS RTK/PPP ou marco oficial) feito por profissional habilitado.

**Importar KML/KMZ** – importa os pontos (marcadores) de um arquivo do Google Earth ou do QGIS:
1. escolha o arquivo e marque os pontos (o nome pode ser editado);
2. posição: **clique no desenho onde fica um dos pontos** (esse ponto passa a ser a referência do projeto e o topo da tela é o norte)
   ou use a referência já definida (os pontos vão direto para o lugar certo);
3. cada ponto aparece **sempre** no desenho como uma cruz laranja com o ponto e o nome. Desenhe as linhas de limite usando o
   **snap de interseção** no cruzamento da cruz.
Os pontos acompanham mudanças da referência e aparecem também nas vistas de fase. **Verificar Projeto** e a planilha Excel
(aba *Pontos de controle*) mostram a distância de cada ponto ao vértice mais próximo do desenho (conferência).

**Desenhar por Azimutes** – cole o texto de um memorial antigo: o GeoPoint encontra os pares *azimute/rumo + distância*
(também "medindo", "extensão"), mostra o **erro de fechamento** e a **precisão (1:x)**, distribui o erro (Bowditch, opcional)
e desenha as linhas a partir de um ponto clicado ou do ponto de referência. Arcos: informe o raio (a distância é a corda).

### Painel Operações

**Desmembramento** (inclui a antiga ferramenta *Dividir Lote*: dividir um lote é desmembrá-lo)
1. Escolha o **lote a desmembrar** e o modo:
   - **Automático** – frente (testada), início/fim, critério (**partes iguais**, **mesma área**, **áreas personalizadas** como `300; 250; 250`,
     **mesma testada**, **testadas personalizadas** como `12; 12; 6`, resto mínimo) e direção das divisas (perpendicular à frente,
     paralela a uma divisa ou por azimute);
   - **Pelas linhas desenhadas** – desenhe as novas divisas no Revit como quiser (retas, arcos, poligonais) e clique em
     *Selecionar linhas no Revit…*; o GeoPoint reconhece as partes;
   - **Lotes já reconhecidos** – usa lotes que você já criou dentro do original.
2. **Prévia** colorida com área e testada de cada parte, atualizada a cada mudança.
3. Na tabela das partes: **nome**, **Lote Remanescente** (você escolhe um), **proprietário** de cada parte e, se for o caso,
   **Anexar a** um lote vizinho (a parte é remembrada com ele automaticamente).
   Em *Encaixar no vértice existente a menos de (m)* (padrão 0,30 m), uma divisa que terminaria a poucos centímetros de um vértice
   é levada até ele (evita vértices "colados" e trechos minúsculos); a tabela já mostra as áreas com o ajuste.
4. Ao registrar: as divisas são desenhadas (estilo *GeoPoint - Divisa proposta*), os lotes resultantes são criados com os confrontantes
   herdados (a divisa entre duas partes vira confrontante "Lote X"), os vizinhos passam a confrontar com os novos lotes, o original é
   arquivado (as anotações dele saem da planta) e, se o original estava anotado/hachurado numa vista, os lotes novos são anotados/hachurados
   nela automaticamente. Se marcado, as **pranchas A4** da situação atual e proposta são geradas.

**Remembramento** – marque os lotes contíguos e veja a **prévia** do lote unificado (contorno vermelho). Informe nome, quadra, uso,
matrícula e proprietário. O contorno é calculado com arcos e divisas em "T"; os confrontantes e os números dos vértices são herdados.
As linhas internas continuam no desenho original (nas pranchas da situação proposta elas ficam ocultas).

**Área Edificável** – recuo por divisa (ou do perfil: frente/laterais/fundos). Calcula a área edificável (cantos reentrantes
arredondados, como manda a definição de afastamento) e desenha o contorno (estilo *GeoPoint - Área edificável*).

### Painel Anotação

**Anotar Lotes** – na vista de planta atual:
- distância (junto da linha) e azimute/rumo (logo abaixo) de cada divisa, **do lado de fora do lote**;
- **ponto preenchido** em cada vértice (2,2 mm no papel; ajuste em Configurações) e o nome do vértice fora do conjunto de lotes;
- nome e área no centro (o remanescente aparece em destaque);
- **confrontantes**: nome do confrontante do lado de fora e, quando uma divisa tem vários confrontantes, a **extensão de cada um**
  (cota vermelha com marcas tracejadas nos pontos de mudança);
- divisas e vértices compartilhados entre lotes vizinhos são anotados **uma vez só**;
- **nenhum texto fica sobre outro ou sobre as linhas**: o GeoPoint testa várias posições ao longo de cada divisa e em volta de cada vértice;
  em divisas muito curtas fica só a distância, afastada e com linha de chamada (o azimute continua no memorial e nos quadros);
- textos sempre legíveis (nunca de cabeça para baixo) e fundo opaco (legíveis sobre hachuras);
- anotações de lotes **arquivados** (ex.: o original de um desmembramento) são removidas da vista;
- opção *Somente remover as anotações* limpa a vista.
O tamanho é dado em mm no papel e acompanha a escala da vista. Executar de novo substitui as anotações anteriores do lote nesta vista.

**Hachuras** – hachura cada lote com uma cor (o **remanescente** recebe hachura cruzada), com bordas invisíveis.

**Vias** – desenho esquemático simples da rua:
- **Selecionar no desenho: testadas e meio-fio** (recomendado): para a via escolhida, selecione as linhas das **testadas** (frente dos
  lotes) e depois a linha do **meio-fio** do lado dos lotes (que você desenhou). A **calçada é medida** no desenho (da testada ao
  meio-fio) e a **pista de 12 m** (leito inteiro, de meio-fio a meio-fio) é desenhada a partir do seu meio-fio, com o meio-fio e
  a calçada do outro lado e o nome da rua no meio. Seu meio-fio não é redesenhado;
- aba *Vias*: nome, largura do leito (pista inteira, padrão 12 m), calçada (padrão 2,5 m; medida quando há meio-fio) e prolongamento das pontas;
- aba *Testadas*: escolha a via de cada divisa de frente (a via vira o confrontante da divisa);
- aba *Cruzamentos*: esquinas de **2, 3 ou 4 vias** – marque *atravessa* para a via que continua do outro lado (L: nenhuma; T: a principal; +: as duas);
  raio de concordância do meio-fio.
O desenho tem meio-fio, alinhamento da calçada e o nome da rua no meio do leito; serve para vias retas, em L, em U ou contornando a quadra.

**Tabela de Coordenadas** – cria uma vista de desenho por lote ("GeoPoint - Coordenadas - …") com o quadro de vértices,
coordenadas, azimutes, distâncias e confrontantes. Arraste a vista para a prancha.

**Elementos Nativos** – cria **Linhas de Propriedade** nativas do Revit 2027 (área calculada pelo Revit, etiquetas e tabelas nativas)
e/ou **regiões preenchidas** coloridas por uso, com parâmetros compartilhados `GP_Lote`, `GP_Quadra`, `GP_Uso`, `GP_Area`,
`GP_Perimetro`, `GP_Testada`, `GP_AreaLiquida`, `GP_Matricula`, `GP_Proprietario`. A área do Revit é comparada com a do GeoPoint.
Use as **Tabelas (Schedules)** nativas do Revit para montar quadros com esses parâmetros.

### Painel Documentos

**Memorial Descritivo** (Word .docx)
- Tipos: **Lote(s)**, **Desmembramento**, **Remembramento**, **Loteamento** (gleba + quadro de áreas + todas as áreas por quadra).
- Coordenadas na descrição e nos quadros: UTM SIRGAS 2000, geográficas, UTM SAD 69, UTM Córrego Alegre, geográficas SAD 69,
  **Plano Topográfico Local (NBR 14166)**, **TM personalizada (RTM/LTM municipal)** e coordenadas locais do projeto.
- Azimute **verdadeiro**, **plano UTM** ou **geodésico**; **azimute ou rumo**; distâncias **horizontais**, **planas UTM** ou **geodésicas**;
  área no plano local, UTM ou ambas; m² e/ou hectares.
- Quadros de coordenadas e de segmentos, notas técnicas (sistema de referência, convenções e origem da coordenada).
- Um arquivo por lote (opcional).
- **Desmembramento**: por padrão gera **um memorial por lote resultante** (só individuais), cada um com a origem (lote desmembrado,
  matrícula e área, lote remanescente e demais lotes) e a **numeração própria de vértices** (P-01… como nas pranchas individuais).
  Também é possível gerar só o geral ou os dois.

**Anuência de Confrontantes** – declaração de anuência (concordância com o limite) para cada confrontante do lote, com a descrição
da divisa comum (vértices, azimutes, distâncias e coordenadas), campos de assinatura do confrontante e de testemunhas. Opcionalmente
inclui as vias (declaração do poder público).

**Pranchas A4** – pranchas por situação, cada uma numa **cópia** da planta (o desenho original não é alterado):
- *Situação atual* (todos juntos e/ou cada lote individualmente) e *Situação proposta* (total e/ou cada lote individualmente);
- **pranchas com vários lotes (geral)** são de visualização: distâncias, nome e área de cada lote, **proprietário** e confrontantes –
  **sem** números de vértice e **sem** azimutes;
- **pranchas de um lote** têm a **numeração própria do lote** (P-01…), **todos os azimutes**, pontos nos vértices e o quadro de coordenadas;
- **coordenada de referência**: escolha, para cada prancha, o vértice cuja coordenada vai no carimbo (lista ou *Escolher o ponto de
  referência no desenho…*). O ponto é marcado no desenho com um triângulo vermelho ("PR" nas pranchas gerais);
- **pranchas já geradas**: a tabela mostra se você **editou** a prancha depois de gerada (textos movidos, linhas, cotas…) e você escolhe
  *Substituir*, *Manter a atual* (padrão para as editadas) ou *Nova versão ao lado*. Antes de substituir uma prancha editada o GeoPoint avisa;
- sem operação (regularização, levantamento…): planta de situação dos lotes marcados;
- folha A4 **retrato** (carimbo embaixo) ou **paisagem** (carimbo à direita), margens ABNT (25 mm à esquerda, 7 mm nas demais);
- carimbo com título, situação, proprietário, imóvel, município/UF, matrícula, área, perímetro, coordenada de referência,
  responsável técnico (com RRT/ART), assinaturas, **imagem de localização** (escolha um PNG/JPG, ex.: print do Google Earth),
  escala, data, folha 01/03 e número da prancha;
- escala automática (a menor escala usual em que o desenho cabe) ou fixa; seta do **norte**; quadro de áreas (vários lotes) ou
  **quadro de coordenadas** (prancha de um lote só: vértice, E, N, azimute, distância e confrontante);
- a cópia da vista é girada para o mesmo norte usado nos azimutes e no Google Earth;
- anotações, hachuras e vias de cada situação; as divisas internas que não pertencem à situação ficam ocultas na cópia.
As pranchas ficam **editáveis** no Revit; depois de revisar, exporte em PDF pelo recurso nativo (*Arquivo › Exportar › PDF*), em A4. Se preferir o seu carimbo (família), informe-o em
Configurações e o GeoPoint coloca a vista e o quadro na prancha com o seu carimbo.

**Google Earth / QGIS** – KMZ, KML e GeoJSON com os lotes coloridos por uso, **todos os vértices como pontos** (pasta *Vértices*,
com nome, latitude/longitude e UTM ao clicar) e (opcional) divisas com azimute e distância.
No QGIS: *Camada › Adicionar camada › Vetorial* e escolha o `.geojson` ou `.kml`.

**Planilha Excel** – abas *Lotes*, *Quadro de áreas*, *Vértices* (todas as coordenadas, convergência e fator de escala),
*Registro de vértices* (cada ponto uma vez, com os lotes atuais e do histórico em que aparece) e *Segmentos* (com confrontante e lado).

**Quadro de Áreas** – áreas por uso (lotes, viário, verde, institucional, APP…), percentuais, áreas públicas e verificação do perfil:
área mínima, testada mínima, áreas públicas mínimas, projeção e construção máximas (taxa de ocupação e coeficiente).

### Painel Verificação

**Verificar Projeto** – geometria, divisas sem confrontante, sobreposições entre lotes, parâmetros urbanísticos, diferença para a área do
título (matrícula), linhas apagadas/alteradas no desenho e precisão da referência.

**Área sobre Toposolid** – área real (inclinada) de cada lote sobre o terreno e altitude de cada vértice
(as altitudes podem entrar nos quadros do memorial).

### Painel GeoPoint

**Configurações** – tolerâncias, numeração dos vértices, casas decimais, formato da zona UTM, modelo do memorial, frases da
descrição, responsável técnico, RTM/LTM, Plano Topográfico Local, anotações, KML e **perfis urbanísticos** (um por município/zona).

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
| `{{CORPO}}` | Descrição completa (seções, descrição perimétrica, quadros e notas) – deixe sozinho num parágrafo |
| `{{LOCAL_DATA}}`, `{{DATA}}` | "Município – UF, 07 de julho de 2026" |
| `{{TECNICO_NOME}}`, `{{TECNICO_PROFISSAO}}`, `{{TECNICO_CONSELHO}}`, `{{TECNICO_REGISTRO}}`, `{{TECNICO_REGISTRO_TEXTO}}`, `{{TECNICO_EMPRESA}}` | Responsável técnico |
| `{{RRT_ART}}`, `{{RRT_ART_TEXTO}}` | Número do RRT/ART |
| `{{LOTE}}`, `{{QUADRA}}` | Lote principal |

- `{{CAMPO?}}` (com interrogação) **remove o parágrafo inteiro** quando o campo estiver vazio.
- Os marcadores também funcionam no cabeçalho e no rodapé.
4. *Configurações › Memorial › Escolher…* e selecione o seu modelo.

**Frases da descrição** – *Exportar frases padrão…* gera um JSON editável. Marcadores: `{VERTICE}`, `{COORDENADAS}`,
`{CONFRONTANTE}`, `{DIRECAO}` ("azimute de …" ou "rumo de …"), `{DISTANCIA}`, `{VERTICE_SEGUINTE}`, `{COORDENADAS_SEGUINTE}`,
`{RAIO}`, `{ANGULO_CENTRAL}`, `{DESENVOLVIMENTO}`, `{CORDA}`, `{AREA}`, `{PERIMETRO}`. Trechos entre `[ ]` somem quando um marcador
interno está vazio (ex.: `[ confrontando com {CONFRONTANTE},]`).

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
- **Registros de erro** – `%AppData%\GeoPoint\logs`.

---

## 7. Responsabilidade técnica

O GeoPoint é uma ferramenta de cálculo e documentação. O memorial descritivo é documento de responsabilidade do profissional
habilitado (CAU/CREA), que deve conferir os dados, a origem e a precisão das coordenadas e as exigências do cartório, da prefeitura
ou do INCRA/SIGEF.
