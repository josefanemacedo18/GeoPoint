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

## Novidades da versão 1.10 – o desenho só sai direto quando confere

**Precisão do próprio documento**
- Cada número lido guarda a precisão com que foi escrito, pelas casas decimais:
  - "12,00 metros" → centímetro;
  - "208°37'12"" → segundo;
  - "06°20'12.79"" → centésimo de segundo, cerca de ±15 cm no terreno;
  - "30 metros" → metro.
- As conferências usam essa precisão como tolerância. Uma diferença só é apontada quando o arredondamento do próprio documento não
  explica. Antes, a tolerância era fixa (5 cm, ou 10 cm nas certidões).

**Conferência antes de desenhar**
- O GeoPoint confere, antes de desenhar:
  - cada trecho escrito (azimute e distância) com as coordenadas;
  - o fechamento do polígono;
  - a área escrita;
  - o perímetro escrito.

  O resultado aparece em *Resultado e conferência* e no *Diagnóstico*:
  - **CONFERIDO** – o desenho confere com o documento dentro da precisão em que ele foi escrito. Desenha direto.
  - **CONFERIDO EM PARTE** – nada diverge, mas há o que confirmar: uma correção automática, uma dúvida da leitura, ou o documento
    sem área nem perímetro.
  - **SEM CONFERÊNCIA** – o documento não traz como conferir: só coordenadas, ou só as medidas dos lados.
  - **NÃO CONFERE** – algum número foi lido ou está escrito errado. Os trechos em vermelho mostram onde.

  Fora do *Conferido*, o GeoPoint **pergunta antes de desenhar**, mostrando o que não conferiu, e o padrão é **não** desenhar.
- **Coordenadas grossas, trechos precisos.** Alguns memoriais têm coordenadas com 2 casas nos segundos (±15 cm) e azimutes e
  distâncias ao centímetro, como os de retificação calculados no SGL. Nesse caso o desenho é feito **pelos azimutes e distâncias**,
  a partir do primeiro vértice, e sai com exatamente as medidas do documento. As coordenadas conferem cada trecho, dentro da
  resolução delas.
- **Correção automática só quando é a única possível.** Um número lido errado só é corrigido se existe uma única leitura parecida
  (um dígito, a vírgula, o quadrante do rumo) que faz tudo bater dentro da precisão do documento. Com duas ou mais possíveis, por
  exemplo 5,42 ou 5,43 m, o GeoPoint não escolhe: avisa onde está o erro. Toda correção pede a sua confirmação antes de desenhar.
  Valor ilegível calculado pelo fechamento fica marcado como "calculado".
- **Dúvidas da leitura:**
  - um valor com casas decimais diferentes das dos outros do mesmo tipo (sinal de dígito perdido ou a mais no OCR) vira dúvida;
  - **dupla leitura**: o OCR lê a foto duas vezes, com configurações diferentes, e o número lido diferente nas duas vira dúvida.
- **Foto girada.** Uma página fotografada de lado ou de cabeça para baixo (comum em foto encaminhada pelo celular) é percebida e lida
  na posição certa. Na aba Documento, a página aparece girada também.
- **Certidões:**
  - "pela direita" e "pela esquerda" passam a ser reconhecidos nas medidas dos lados;
  - texto com palavras e números soltos não vira mais tabela de coordenadas;
  - "mº" lido no lugar de m² é corrigido;
  - o travessão em "208°37'12" – 7,26 metros" não vira sinal de menos.
- **Resultado medido** em 51 memoriais fictícios de 17 modelos, cada um em texto e em OCR de folha limpa, escaneada e fotografada,
  com e sem colunas lidas em blocos (357 leituras):
  - **nenhuma leitura errada sai como conferida**;
  - 334 desenham certo: 171 direto e 163 pedindo confirmação, porque o documento não traz como conferir;
  - as 23 leituras erradas são todas avisadas;
  - nenhuma leitura conferida tem qualquer número diferente do documento.
- **Documentos reais testados:**
  - memorial de retificação (foto girada): **conferido**, desenhado pelos trechos;
  - memorial SGL: **conferido**;
  - uma certidão cujas distâncias diferem das próprias coordenadas em 3 a 8 cm: **não confere**. A leitura está certa; o documento
    é que é inconsistente;
  - certidões só com as medidas dos lados: pedem confirmação, porque o desenho supõe ângulos retos.

## Novidades da versão 1.9

**Leitura inteligente de coordenadas, azimutes e distâncias**
- O GeoPoint agora **entende o papel de cada número** do texto, em vez de procurar só frases prontas. Cada número vira coordenada E ou
  N, latitude, longitude, azimute, rumo, distância, área, ou é ignorado. Ele decide por três sinais:
  - o **rótulo** antes ou depois do número ("N", "E", "Este", "Norte (Y)", "Latitude", "azimute", "rumo", "distância", "m", "m²",
    "235142,50 mE");
  - a **faixa de valores** (N de UTM tem 7 dígitos e E tem 6; latitude no Brasil fica entre −34° e +6°, longitude entre −74° e −28°);
  - o **cabeçalho da tabela** (Vértice | Este | Norte | Azimute | Distância…, inclusive com colunas vazias ou a mais).
- **Modelos lidos**:
  - memorial INCRA/SIGEF em UTM ou em latitude/longitude;
  - E antes de N ou N antes de E, com o rótulo antes ou depois do número;
  - tabelas de coordenadas UTM, geográficas (com S/W ou com sinal) e locais (X, Y);
  - quadro do SIGEF (longitude, latitude, altitude, azimute, distância);
  - graus decimais;
  - números com espaço no milhar (`8 828 696,86`) ou no formato americano (`8,105,458.48`);
  - rumos (`45°30' NE`, `N 45°30' E`, `NE 45°30'`);
  - azimutes em graus decimais;
  - só a coordenada do ponto inicial, seguida de azimutes e distâncias;
  - colunas de tabela que vieram uma depois da outra (texto copiado de PDF).
- **Ignora** números de documentos: matrícula, CPF, CNPJ, CEP, datas, valores em R$, protocolo, "SIRGAS 2000", fuso e meridiano.
- **Erros comuns de OCR corrigidos antes da leitura**:
  - número partido na quebra de linha (`729.830,` / `10m`);
  - espaço depois da vírgula (`8295860, 18`);
  - "!" no lugar do apóstrofo dos minutos;
  - letras no lugar de dígitos nos minutos (`os` → `05`);
  - marca dos minutos perdida (`24°2701,840"`);
  - letra do hemisfério ilegível (no Brasil não há longitude leste).
- **Conferência cruzada, que corrige o número lido errado.** O memorial diz a mesma coisa de mais de um jeito: as coordenadas, os
  azimutes e distâncias, o polígono que fecha e a área. O GeoPoint usa isso para achar o número errado:
  - **trechos que não fecham** – procura o único trecho em que trocar um dígito, a vírgula, os graus, os minutos, os segundos ou o
    quadrante do rumo fecha o polígono (e confere a área, se o documento traz);
  - **trecho com valor ilegível** – calcula o que falta pelo fechamento;
  - **coordenada que não bate com os dois trechos vizinhos** – recalcula pelo azimute e distância do texto;
  - **coordenada muito longe das outras** (dígito trocado ou perdido) – acha o dígito que a traz para junto das outras.

  O GeoPoint só corrige quando a correção é única e explica tudo. Num retângulo, por exemplo, um erro na frente e nos fundos explica
  igual: aí ele só avisa onde pode estar o erro. Toda correção aparece em amarelo na janela e no Diagnóstico ("lido 36,44 m, ficou
  31,44 m"). Confira no documento.
- **Modelo do texto** (no alto da janela): *Automático* é o padrão e identifica sozinho. Se a leitura errar o tipo, escolha *UTM*,
  *Geográficas*, *Locais*, *Azimutes e distâncias* ou *Rumos e distâncias*, e o texto é lido de novo seguindo o modelo.
- **"Leitura:"** abaixo da barra de importação mostra o modelo identificado e como ele foi reconhecido (rótulos, faixa de valores,
  cabeçalho). A aba **Diagnóstico** ganhou as etapas *Modelo do texto* e *Correções da conferência*.
- **Resultado medido** num conjunto de 48 memoriais fictícios em 16 modelos, cada um em texto e em OCR de folha limpa, escaneada e
  fotografada (336 leituras): **92,9% de acerto** (eram 47% na 1.8). Nos textos sem OCR, **todos os modelos** são lidos. Os seus 5
  documentos reais continuam corretos.

**Imagens de satélite: galeria sem erro e fácil de achar**
- **Corrigido o erro "Object reference not set to an instance of an object"** ao abrir a galeria. Ele acontecia logo na abertura da
  janela e podia fechar o Revit.
- **Proteção contra queda**: um erro dentro de uma janela do GeoPoint vira uma mensagem, a janela continua aberta e o Revit não fecha.
  Os detalhes ficam no registro, em `%AppData%\GeoPoint\logs` (envie para o suporte).
- **Onde fica a galeria**:
  - na janela de **Pranchas**, no quadro azul **Imagem de satélite (localização)** logo abaixo da prévia: botão **Galeria de imagens…**
    (ou clique no espaço da imagem na própria prévia);
  - na faixa do GeoPoint, botão **Galeria de Imagens**, de qualquer vista: adicione arquivos ou cole o print, renomeie, remova e
    escolha a imagem geral. Com uma prancha do GeoPoint aberta, ele abre a galeria **para essa prancha**.
- A coluna de opções da janela de Pranchas tem **barra de rolagem**: em telas mais baixas, nada fica cortado.

## Novidades da versão 1.8

**Leitura de certidões e memoriais**
- **Memoriais com coordenadas geográficas e "azimute e distância"**: lê o modelo *"…com azimute e distância: 112°12'38" e 12,00m até o
  vértice M-02, de coordenadas Longitude:-47°22'50,886", Latitude:-6°20'34,920"…"*, inclusive *"com os seguintes azimutes e distâncias"*
  e *"deste segue com 292°12'38" e 12,00m"* (o trecho sem confrontante herda o do trecho anterior). Lê as **divisas** (*Frente*,
  *Fundos*, *Lado direito*, *Lado esquerdo*) e o **quadro de CONFRONTAÇÕES** do fim do memorial, que serve de conferência.
- **OCR mais forte e sem internet**: fotos e PDFs escaneados são lidos pelo **Tesseract** (instalado junto com o GeoPoint), com a imagem
  preparada antes (ampliada, contraste e nitidez); se a leitura não fechar, o GeoPoint tenta de novo de outros jeitos (correção de
  sombra, bloco único e o OCR do Windows) e fica com a **melhor leitura**. Erros comuns de foto são corrigidos sozinhos: grau sumido
  ("1121238"" → 112°12'38"), grau lido como aspas ou dois-pontos, aspas repetidas, sinal de menos perdido, "&" no lugar de "e", "m?" no
  lugar de "m²". Na sua foto do memorial: 6 de 6 trechos conferidos e área de 599,50 m² × 600 m² do documento.
- **Aviso de qualidade da foto** antes de ler (pequena, escura, sem contraste, tremida ou com sombra): você lê assim mesmo ou tira outra.
- **Aba Diagnóstico**: as etapas da leitura (foto → texto → descrição → vértices → trechos → confrontantes, com ✓ ⚠ ✗ mostrando onde
  parou), a **conferência** do perímetro (fechamento e precisão, trechos × texto, área e perímetro × documento, quadro de confrontações,
  confrontante em todas as divisas, fuso/datum) e as tentativas de OCR. *Copiar diagnóstico* gera um relatório para enviar, **sem a
  imagem e com CPF, documentos e nomes mascarados**.
- **O que foi lido, em cores** (aba Documento): coordenadas em azul, azimutes em laranja, distâncias e área em verde, confrontantes em
  roxo. **Clique numa linha da tabela** e o documento rola até a palavra correspondente (marcada em vermelho).
- **Coluna Divisa** nas tabelas (Frente, Fundos, Lado direito, Lado esquerdo): vai para o lote no *Reconhecer Lotes*.
- **Correções aprendidas**: corrigiu um confrontante lido do documento? Na próxima leitura em que o mesmo nome aparecer, ele já vem
  corrigido (as correções ficam nas Configurações do seu computador; números não são "aprendidos").
- **Vários documentos de uma vez** (ex.: o memorial e as matrículas de uma fusão): o de melhor leitura é desenhado; os outros servem de
  conferência (soma das áreas × desenho e vértices em comum). Escolha qual ver na aba Documento.
- **Confrontante ligado ao lote do projeto**: no *Reconhecer Lotes*, uma divisa "Lote 09 – …" vira ligação com o Lote 09 que já existe
  no projeto (mesma quadra), e a divisa (frente, fundos, laterais) vem do documento.

**Imagens de satélite**
- **Galeria do projeto**: guarde quantas imagens quiser (arquivo ou print colado), com nome e miniatura. Fica dentro do projeto.
- **Imagem por prancha, escolhida clicando no espaço**: na prévia da janela de *Pranchas*, **clique no espaço da imagem** (ou em
  *Galeria de imagens…*); ou, com a prancha aberta no Revit, use o botão **Galeria de Imagens** (na 1.8, *Imagem de Localização*). A galeria abre:
  escolha a imagem, **arraste para enquadrar e use a roda do mouse para aproximar**, e clique em *Usar nesta prancha*, *Usar em todas as
  pranchas da fase* (menos as **fixadas**), *Usar como imagem geral* ou *Sem imagem nesta prancha*. Prancha já gerada é atualizada na hora.
- **Contorno do lote sobre a imagem**: liga o contorno (norte verdadeiro para cima), ajusta o tamanho e, com Ctrl + arrastar, a
  posição – para conferir se a imagem está no lugar certo. Sai na prancha em vermelho.
- **Revisão automática da prancha** (na prévia e ao gerar): grade sobre textos ou desenho, imagem com resolução baixa no espaço (menos de
  150 dpi) e convenções que não batem com o desenho.

**Tabelas do Desenhar Perímetro**
- **Remover selecionados** e **Remover todos**: selecione vários trechos (ou vértices) com Ctrl ou Shift + clique e remova só eles (a
  tecla Delete também remove), ou esvazie a tabela de uma vez (o texto continua; *Interpretar texto* lê de novo).
- **▲ Subir / ▼ Descer** (ou Alt + ↑/↓): muda a ordem dos trechos selecionados, juntos. Confrontante, divisa e testada vão com o trecho,
  e a prévia e a conferência são refeitas na hora. As linhas são numeradas à esquerda.
- **Linhas de detalhe (vista atual)** passam a ser o padrão do desenho (no Desenhar Perímetro e no Gerar Lote). Fora de uma vista de
  planta, o perímetro é desenhado com linhas de modelo.

## Ajustes da versão 1.7.1

- **Grade só no fundo**: as linhas da grade de coordenadas não passam mais por cima do desenho – param antes da área dos lotes
  (inclusive os vizinhos que aparecem no recorte), das divisas, dos pontos dos vértices, das edificações e do desenho das vias, além da
  tabela, do norte e dos textos, como já era.
- **Convenções topográficas iguais ao desenho**: a amostra da área tem a hachura a 45° na cor de cada lote (cruzada no remanescente),
  o limite do imóvel tem a mesma linha fina das divisas e os vértices e a coordenada de referência têm o ponto cheio. Os símbolos são
  desenhados com linhas, então aparecem em qualquer Revit (antes, em alguns casos, a hachura e os pontos não apareciam na folha). A
  seta do norte e a escala gráfica também ganham o preenchimento em linhas.

## Novidades da versão 1.7

- **Imagens de satélite guardadas no projeto**: na janela de *Pranchas*, botão **Imagens…** ao lado de *Imagem de satélite*. Escolha o
  arquivo do print (ou da imagem salva) ou clique em **Colar print** depois de tirar o print (tecla *Print Screen*, *Win+Shift+S* ou
  *Copiar imagem* no navegador/Google Earth). A imagem fica **dentro do projeto** (não depende mais da pasta do arquivo) e entra
  **sozinha no espaço de localização** da prancha, **recortada para preencher o espaço sem distorcer** (as sobras das bordas ficam de
  fora). Um print Full HD (1920×1080) é o ideal.
- **Imagem geral e imagens por etapa**: a **imagem geral** vale para todas as pranchas; cada **fase** (situação atual, desmembramento,
  remembramento, regularização…) pode ter a **sua própria imagem**, usada nas pranchas daquela fase. Fase sem imagem própria usa a
  geral. A janela mostra a miniatura com o recorte exato que vai para a prancha, e a prévia das pranchas já mostra a imagem.
- **Google Earth – cor da hachura, 45% de cor e um lote por arquivo**: cada lote vai para o Google Earth com o polígono **na mesma cor
  da hachura do lote no projeto**, preenchimento com **45% de cor** (dá para ver a imagem por baixo), contorno na cor, os vértices
  com as coordenadas e as divisas. Por padrão sai **um arquivo para cada lote** (cada lote entra separado no Google Earth); os
  arquivos abrem um de cada vez. A conferência logo depois de *Definir Referência* também abre cada lote separado (você escolhe quais).
- A imagem de localização escolhida nas versões anteriores (só o caminho do arquivo) passa a ser a imagem geral guardada no projeto,
  na primeira vez que você abre *Pranchas*.

## Ajustes da versão 1.6.2

- **Certidões antigas (só com as medidas dos lados)**: o *Importar certidão* agora entende os lados escritos como nas certidões mais
  antigas – *"limita-se ao Norte, com a Avenida X, medindo 76 metros; ao Sul, com a Rua Y, medindo 76 metros, a Leste, … e a Oeste, …"*
  e *"Frente com a Rua G, medindo 12,00 metros; ao fundo limitando-se com o Lote 07, …; pelo lado direito limitando-se com …"*. Cada
  lado vai para a tabela de azimutes com o seu confrontante (ruas marcadas como testada).
- **Norte, sul, leste e oeste**: o desenho já sai orientado (norte para cima), com ângulos retos nos cantos do lado sul; o lado norte
  liga as pontas e é conferido com a medida do documento. Lado citado duas vezes (erro de digitação do cartório) gera aviso.
- Essas certidões não trazem ângulos: o desenho supõe cantos retos e avisa para conferir. Letras trocadas pela digitalização
  ("mctros", "Lcste") são aceitas.

## Ajustes da versão 1.6.1

- **Certidão: só o perímetro e os confrontantes.** A importação lê apenas a **descrição do perímetro** ("DESCRIÇÃO DA PROPRIEDADE:
  Partindo do vértice P-01 de coordenadas E …/N … segue até o vértice P-02 em uma distância de 7,35 m, confrontando com RUA A; …
  fechando assim uma área de 761,46 m²"): as **coordenadas** (ou os azimutes e distâncias) de cada trecho e o **confrontante de cada
  divisa**. Partes, registros, CPF, selos e emolumentos ficam de fora – os Dados do Imóvel e o cadastro do lote continuam com você.
- **Leitura certa do modelo mais comum de certidão de inteiro teor**: o texto vertical da margem (código da assinatura digital), os
  rodapés em letra miúda e o espaçamento do texto justificado não confundem mais a leitura; números soltos (protocolo, CEP, valores)
  não viram vértices.
- **Conferência só pelas distâncias**: certidões que trazem coordenadas e distâncias, sem azimutes, são conferidas trecho a trecho
  pela distância (10 cm ou 0,5%), além da área do desenho × a da certidão.
- **Fuso e datum não informados**: o GeoPoint usa SIRGAS 2000 e o fuso do projeto (23 S na região) e avisa para conferir se o imóvel é
  da região.
- **Confrontantes nas tabelas**: as colunas *Testada* e *Confrontante* ficam nas próprias tabelas de azimutes e de coordenadas (lidos do
  texto – inclusive de memorial colado – e corrigíveis). Ao desenhar, eles ficam **guardados com as linhas**; quando você usa
  **Reconhecer Lotes** nesse contorno, o **lote já sai com o confrontante de cada divisa** (ruas como testada). Divisas que já tinham
  confrontante não são alteradas; se você apagar as linhas, os confrontantes guardados delas são descartados.
- A aba *Dados da certidão* saiu; a barra da aba *Documento* foi corrigida (botões do tamanho certo, explicação embaixo); a janela abre
  mais larga (até a largura da tela).

## Novidades da versão 1.6

- **Certidão em PDF ou imagem no Desenhar Perímetro**: botão *Importar certidão ou memorial*. O GeoPoint lê o texto (PDF digital: o
  texto do próprio PDF, exato; PDF escaneado, foto ou imagem: reconhecimento de texto – OCR – do próprio Windows, sem enviar o documento
  para fora do computador) e encontra o **perímetro** (coordenadas, azimutes e distâncias, tabela de vértices ou só as medidas de frente,
  fundos e laterais) e o **confrontante de cada divisa** (na 1.6.1, só isso – ver acima). Tudo aparece para conferir: as páginas do
  documento ao lado, com **os números usados no desenho destacados em amarelo**, e a área e o perímetro do desenho comparados com os da
  certidão.
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
  o **fuso** vem do texto ("fuso 23 S", "meridiano central 45° W") ou, se o texto não diz, da referência do projeto (ou das
  Configurações) – com aviso para conferir.
- Tabela de vértices: nome, coordenadas, **raio até o próximo** (arcos) e lado do arco, o **azimute e a distância até o próximo** no
  formato do projeto e a **conferência com o texto**: cada trecho é comparado com o azimute e a distância escritos no memorial
  (✓ confere – o resultado diz se bateu com o azimute **plano UTM** ou o **verdadeiro**; ⚠ com a diferença, em vermelho na tabela e na
  prévia). A tolerância é a precisão com que o documento foi escrito: o arredondamento das duas coordenadas do trecho, meia casa da
  distância e do azimute escritos, e a diferença de escala entre o plano UTM, o plano local e o elipsoide. Texto **só com distâncias**
  (sem azimutes, comum nas certidões): confere a distância com a mesma regra. Antes de desenhar, o GeoPoint dá o veredito (*Conferido*,
  *Conferido em parte*, *Sem conferência* ou *Não confere*) e só desenha direto o que está conferido – veja as Novidades da 1.10.
- *Fechar o polígono* liga o último vértice ao primeiro; o vértice inicial repetido no fim do texto é ignorado. *Inverter sentido*
  percorre os vértices ao contrário.
- Nas duas tabelas: **Remover selecionados** (Ctrl ou Shift + clique para vários; ou a tecla Delete), **Remover todos** e **▲ Subir /
  ▼ Descer** (Alt + ↑/↓) para corrigir a ordem. Na de coordenadas, o azimute e a distância escritos no texto acompanham o vértice de
  onde o trecho parte, então a conferência continua valendo depois de reordenar.
- O desenho fica no plano topográfico local do projeto (distâncias horizontais reais), como o resto do GeoPoint.

*Desenho e posição*
- **Linhas de detalhe (vista atual)** – o padrão: as linhas aparecem só na vista onde foram desenhadas. *Linhas de modelo* aparecem
  em todas as plantas do nível. Fora de uma vista de planta, só linhas de modelo.
- **Na posição das coordenadas** – com a referência do projeto definida, o lote cai exatamente onde as coordenadas indicam, junto dos
  outros lotes georreferenciados;
- **Clicar onde fica o primeiro vértice** – e, se o projeto ainda não tem referência, *Usar o primeiro vértice como ponto de
  referência do projeto* (o topo da vista é o norte): o projeto fica georreferenciado por essas coordenadas;
- **Começar no ponto de referência do projeto** (aba de azimutes).
*Confrontantes* – as duas tabelas têm as colunas **Testada**, **Divisa** e **Confrontante** (na de coordenadas, o da divisa que vai do
vértice ao próximo). Vêm preenchidas quando o texto diz "confrontando com…", "limitando-se com…", "fazendo divisa com…" (pelo
proprietário quando diz "lote 06 de propriedade de…"; ruas e avenidas marcadas como testada; a divisa – Frente, Fundos, Lado direito,
Lado esquerdo – pelo texto ou pelo quadro de confrontações) e podem ser digitadas ou corrigidas. A correção de um nome lido do documento
fica aprendida para as próximas leituras.

As linhas criadas ficam selecionadas, prontas para *Reconhecer Lotes*. Os **confrontantes ficam guardados com as linhas** e entram no
lote quando o *Reconhecer Lotes* cria o lote desse contorno (as divisas que já tinham confrontante não mudam). Tudo é desfeito com um
*Desfazer*.

*Importar certidão ou memorial (PDF, foto ou imagem)*
- **PDF digital** (gerado pelo cartório ou pelo sistema): o texto é lido do próprio PDF – os números saem exatos. **PDF escaneado, foto
  do celular ou imagem** (JPG, PNG, TIF): o texto é reconhecido por **OCR no próprio computador** (nada é enviado pela internet): o
  **Tesseract**, instalado junto com o GeoPoint, com a imagem preparada (ampliada, contraste e nitidez) e mais de uma tentativa se a
  leitura não fechar; o **OCR do Windows** fica de reserva (precisa do idioma *Português (Brasil)* no Windows). O Tesseract usa o Visual
  C++ 2015–2022 (x64), que já vem com o Revit.
- **Antes de ler uma foto**, o GeoPoint avalia a qualidade (tamanho, luz, contraste, foco, sombra) e avisa se ela pode não ser lida
  direito. Para fotografar: de frente, sem sombra, com a página inteira e boa luz.
- Dá para escolher **vários arquivos** de uma vez: o de melhor leitura é o desenhado e os outros servem de conferência (*Diagnóstico*).
- O GeoPoint lê **só a descrição do perímetro** (de "DESCRIÇÃO DA PROPRIEDADE", "Partindo do vértice…", "Inicia-se…" ou "limites e
  confrontações" até "fechando assim uma área de…"), trecho a trecho: **de qual vértice para qual**, as **coordenadas**, a **distância**,
  o **azimute** (quando há) e o **confrontante**. Partes, registros, CPF, selos e emolumentos são ignorados – nada disso vai para o
  projeto. Só essa descrição aparece no quadro de texto da aba.
- O perímetro vai para a aba certa: **coordenadas** (memorial, SIGEF ou tabela de vértices com cabeçalho E/N – como a tabela das
  pranchas do GeoPoint), **azimutes/rumos e distâncias**, ou **só as medidas dos lados** ("12,00 m de frente para a Rua X, 30,00 m pelo
  lado direito…"): neste caso o desenho supõe as laterais perpendiculares à frente e avisa para conferir os ângulos.
- O **fuso UTM** e o **datum** escritos na certidão são usados. Quando a certidão só diz "UTM" (o mais comum), vale **SIRGAS 2000** e o
  **fuso do projeto** (23 S na região), com aviso para conferir se o imóvel é da região.
- Erros comuns de OCR são corrigidos nos números (O no lugar de 0, l no lugar de 1, espaços dentro do número e do ângulo, grau lido
  como "o").
- **Aba Documento** (ao lado da prévia): as páginas, com o que foi lido **em cores** (coordenadas em azul, azimutes em laranja,
  distâncias e área em verde, confrontantes em roxo); clique numa linha da tabela para ir até ela no documento. Zoom (− / + / Largura) e
  *Abrir o arquivo*. Arraste a divisória para alargar o painel.
- **Aba Diagnóstico**: as etapas da leitura (onde parou, se parou), a conferência do perímetro (fechamento, trechos × texto, área e
  perímetro × documento, quadro de confrontações, confrontantes, fuso/datum) e as tentativas de OCR. *Copiar diagnóstico* gera um
  relatório sem a imagem e com CPF, documentos e nomes mascarados – bom para enviar quando um documento não for lido.
- O **confrontante de cada divisa** aparece nas colunas *Testada* e *Confrontante* das tabelas (ver *Confrontantes* acima).
- **Conferência**: as coordenadas são conferidas com as distâncias (e os azimutes, quando há) de cada trecho, e o resultado mostra a
  **área e o perímetro do desenho × os da certidão** – diferença acima de 1% indica número lido errado. (Distâncias de certidão costumam
  ser arredondadas: alguns centímetros e uma área uns 0,3% diferente são normais.)
- **Dados do Imóvel e cadastro do lote** ficam com você (*Dados do Imóvel*, *Reconhecer Lotes*, *Gerenciar Lotes*); o lote reconhecido
  já sai com os confrontantes da certidão.
- O documento **não é guardado** no projeto: só o perímetro desenhado e os confrontantes.

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
- **imagem de satélite**: espaço sem título no carimbo. **Clique no espaço da imagem na prévia** (ou em *Galeria de imagens…*, no quadro
  azul abaixo da prévia): abre a **galeria
  do projeto** (adicione arquivos ou cole o print), escolha a imagem, arraste para enquadrar, aproxime com a roda do mouse e, se quiser,
  ligue o **contorno do lote** por cima (tamanho no controle; Ctrl + arrastar move). Depois: *Usar nesta prancha*, *Usar em todas as
  pranchas da fase* (as **fixadas** não mudam), *Usar como imagem geral* ou *Sem imagem nesta prancha*. Cada prancha usa a sua imagem,
  senão a padrão da fase, senão a geral. As imagens ficam guardadas no projeto. Com a prancha já gerada aberta, o botão **Galeria de
  Imagens** faz o mesmo (em outra vista, ele abre a galeria para organizar as imagens);
- **prévia** da prancha selecionada (mesmo encaixe, carimbo, anotações e imagem), com avisos de legibilidade e a **revisão automática**
  (grade sobre textos ou desenho, imagem com menos de 150 dpi no espaço, convenções × desenho);
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

**Google Earth / QGIS** – KMZ, KML e GeoJSON. No Google Earth, cada lote aparece com o polígono **na cor da hachura do lote no projeto**
(lote ainda sem hachura: a cor que o comando *Hachuras* daria a ele), preenchimento com **45% de cor** (*Cor do preenchimento*, de 0 a 1),
o contorno, os **vértices com as coordenadas** e, se marcado, as divisas com azimute e distância. *Um arquivo para cada lote* (padrão)
grava `<nome> - Lote 01.kmz`, `<nome> - Lote 02.kmz`… na pasta escolhida e abre um de cada vez no Google Earth; desmarcado, sai um
arquivo só com todos os lotes. O GeoJSON (QGIS) sai sempre num arquivo só.

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
- **Certidão lida com erro** – veja a aba *Diagnóstico* (em qual etapa parou) e os números destacados na aba *Documento*; foto torta,
  escura ou com sombra piora o OCR (fotografe de frente, com boa luz). Corrija na tabela antes de desenhar. Se um modelo de documento não
  for lido, use *Copiar diagnóstico* e envie o relatório (sai sem a imagem e com os dados pessoais mascarados).
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
| **Imagem de satélite (localização)** | Print do local no satélite guardado na galeria do projeto e escolhido para cada prancha (ou o padrão da fase, ou o geral), com enquadramento. |
| **Tesseract** | Programa de OCR (reconhecimento de texto) instalado junto com o GeoPoint: lê fotos e PDFs escaneados no próprio computador. |
| **Certidão (inteiro teor)** | Documento do cartório com a descrição do imóvel; o *Desenhar Perímetro* lê dela o perímetro e os confrontantes. |
| **OCR** | Reconhecimento de texto em imagem (PDF escaneado ou foto), feito pelo próprio Windows. |
| **Recuos (área edificável)** | Onde se pode construir no lote, descontando o recuo de cada divisa. |
| **Construção existente (área edificada)** | Edificações que já existem no lote: área de projeção, área construída, taxa de ocupação e distância até cada divisa. |
| **Conferência** | Verificação feita antes de emitir memorial e pranchas (dados faltando, área diferente da matrícula, divisas sem confrontante). |
| **Cópia de segurança** | Cópia dos dados do GeoPoint gravada antes de cada comando que altera o projeto (*Ajuda › Restaurar*). |
