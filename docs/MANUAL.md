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
3. **Gerenciar Lotes** – informe nome/número, quadra, uso, matrícula, **confrontantes** e marque as **testadas**.
4. **Dados do Imóvel** – proprietário, município, matrícula, RRT/ART, perfil urbanístico.
5. **Definir Referência** – cole a coordenada de **um** vértice (Google Maps/Earth, QGIS, GPS ou UTM).
6. **Google Earth / QGIS** – confira o desenho sobre a imagem de satélite.
7. **Memorial Descritivo**, **Planilha Excel**, **Anotar Lotes**, **Tabela de Coordenadas**, **Quadro de Áreas**.

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
- Tabela de divisas: digite o **confrontante** de cada divisa e marque **Testada**. Quando a divisa é compartilhada com outro lote
  do projeto, o vizinho aparece em *Sugerido* (botão **Usar sugestões nas divisas vazias**).
- Arquivar/reativar e excluir lotes (excluir não apaga as linhas do desenho).

**Atualizar Lotes** – depois de editar as linhas no desenho, recalcula os lotes. Confrontantes e testadas são mantidos nas divisas correspondentes.

**Dados do Imóvel** – campos do cabeçalho do memorial e o **perfil urbanístico** usado nas verificações.

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
   - **Norte verdadeiro do projeto** (recomendado – o que está em *Gerenciar › Localização do projeto*);
   - **Azimute de uma linha** que parte do ponto (depois do OK, clique na linha);
   - **Dois pontos** com coordenadas conhecidas – o GeoPoint mostra a diferença de distância e de rotação entre o desenho e as coordenadas e pergunta qual orientação usar.

A partir desse ponto, todas as coordenadas são calculadas pelas **distâncias e azimutes do desenho** (o desenho é tratado como um
plano topográfico local, com distâncias horizontais verdadeiras).

> **Precisão:** coordenadas do Google Maps/Earth e de GPS de navegação têm erro de alguns metros. Servem muito bem para estudos,
> implantação e conferência sobre satélite. Para registro em cartório ou SIGEF/INCRA, a coordenada de partida precisa vir de
> levantamento geodésico (GNSS RTK/PPP ou marco oficial) feito por profissional habilitado.

**Desenhar por Azimutes** – cole o texto de um memorial antigo: o GeoPoint encontra os pares *azimute/rumo + distância*
(também "medindo", "extensão"), mostra o **erro de fechamento** e a **precisão (1:x)**, distribui o erro (Bowditch, opcional)
e desenha as linhas a partir de um ponto clicado ou do ponto de referência. Arcos: informe o raio (a distância é a corda).

### Painel Operações

**Desmembramento** – desenhe as novas divisas (ou use *Dividir Lote*), reconheça os lotes resultantes, escolha o lote original e
confira: soma das áreas, sobreposições e se os resultantes estão dentro do original. O original é arquivado (continua no memorial).

**Remembramento** – marque os lotes contíguos; o GeoPoint calcula o contorno unificado (com arcos e divisas em "T"),
confere a área e cria o lote resultante. As linhas internas continuam no desenho (apague-as se quiser).

**Dividir Lote** (simulação de parcelamento)
- Frente (testada), direção das divisas (perpendicular à frente, paralela a uma divisa ou por azimute) e critério:
  **partes iguais**, **área por lote** (com regra para o resto) ou **testada fixa**.
- Prévia com área e testada de cada parte e verificação do perfil urbanístico (área e testada mínimas).
- Ao confirmar: desenha as divisas (estilo *GeoPoint - Divisa proposta*), cria os lotes e, se marcado, registra o desmembramento.
- Áreas calculadas de forma exata, inclusive com divisas em arco.

**Área Edificável** – recuo por divisa (ou do perfil: frente/laterais/fundos). Calcula a área edificável (cantos reentrantes
arredondados, como manda a definição de afastamento) e desenha o contorno (estilo *GeoPoint - Área edificável*).

### Painel Anotação

**Anotar Lotes** – na vista de planta atual: azimute/rumo e distância de cada divisa, nomes dos vértices e nome/área no centro.
O tamanho é dado em mm no papel e acompanha a escala da vista. Executar de novo substitui as anotações anteriores do lote nesta vista.

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

**Google Earth / QGIS** – KMZ, KML e GeoJSON com os lotes coloridos por uso, vértices e (opcional) divisas com azimute e distância.
No QGIS: *Camada › Adicionar camada › Vetorial* e escolha o `.geojson` ou `.kml`.

**Planilha Excel** – abas *Lotes*, *Quadro de áreas*, *Vértices* (todas as coordenadas, convergência e fator de escala) e *Segmentos*.

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
