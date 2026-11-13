# GeoPoint – Roteiro de teste no Revit 2027

Use este roteiro a cada versão nova, **num projeto de teste** (não no projeto de um cliente). Cada passo diz o que fazer e o que
deve acontecer. Marque `[x]` no que passou e anote o que não passou (com a mensagem, se houver) para enviar junto com o
*arquivo de diagnóstico* (*Ajuda › Gerar arquivo de diagnóstico*).

Tempo estimado: 40 a 60 minutos.

---

## 0. Instalação

- [ ] Feche o Revit, rode `Instalar.bat`, abra o Revit 2027 e escolha **Sempre carregar**.
      **Esperado:** aba **GeoPoint** com os painéis *Lotes, Georreferência, Operações, Anotação, Documentos, Verificação, GeoPoint*.
- [ ] *Ajuda*: a janela mostra a versão nova.
- [ ] Em *Operações*, os botões se chamam **Recuos** e **Construção Existente**.

## 1. Projeto de exemplo (ponto de partida)

- [ ] Crie um projeto novo (modelo arquitetônico), abra uma planta e use *Ajuda › Criar um projeto de exemplo*.
      **Esperado:** três lotes (01, 02 e 03 – Quadra A), a Rua Exemplo na frente e uma edificação no lote 02.
- [ ] *Verificar Projeto*. **Esperado:** sem erros de geometria.

## 2. Lotes

- [ ] Desenhe um retângulo de 12 × 30 m com **Linha de modelo** ao lado dos lotes do exemplo e use **Reconhecer Lotes**.
      **Esperado:** lote novo com área 360,00 m² e perímetro 84,00 m.
- [ ] **Gerenciar Lotes**: dê nome/quadra, informe um confrontante numa divisa e grave.
      **Esperado:** campos vazios mostram em cinza os *Dados do Imóvel*.
- [ ] Mova um vértice do retângulo (arraste a linha) e use **Atualizar Lotes**. **Esperado:** área nova; confrontantes mantidos.
- [ ] **Desfazer (Ctrl+Z)** duas vezes. **Esperado:** volta a área anterior e depois o lote antes de atualizar (os dados do GeoPoint
      acompanham o Desfazer do Revit).

## 3. Georreferência

- [ ] **Definir Referência** num vértice com latitude/longitude (ex.: `-6,333656, -47,394191`).
      **Esperado:** pergunta a orientação; *Verificar Projeto* não acusa falta de referência.
- [ ] **Importar Pontos › Colar**: copie para a área de transferência as linhas abaixo (com o título) e importe.
      ```
      Datum: SAD 69
      Vértice;E;N
      M1;235142,50;9299292,99
      M2;235113,25;9299307,98
      ```
      **Esperado:** aviso "Datum SAD 69 (informado na planilha), convertido para SIRGAS 2000." Cruzes laranja nos pontos.
- [ ] Repita sem a linha `Datum: SAD 69`. **Esperado:** aviso "Datum não informado… considerado SIRGAS 2000"; os pontos ficam
      algumas dezenas de metros deslocados em relação aos anteriores (diferença entre os datums).

### Desenhar Perímetro

- [ ] **Desenhar Perímetro**, aba *Azimutes e distâncias*: o modelo de texto aparece; troque um azimute e veja a prévia mudar.
      **Esperado:** erro de fechamento e área; *Desenhar* → clique na planta → 4 linhas selecionadas.
- [ ] Aba *Coordenadas*: o modelo (memorial com coordenadas UTM) já vem lido.
      **Esperado:** 4 vértices, sistema UTM, datum SIRGAS 2000 (do texto), coluna *Conferência* com "✓ confere (plano UTM)" nos 4 trechos.
- [ ] No texto, troque `distância de 12,00 m até o vértice P-03` por `12,40 m`.
      **Esperado:** a linha do P-02 fica vermelha ("⚠ texto: … dif. … 0,40 m") e o trecho em vermelho na prévia; ao Desenhar, o GeoPoint pergunta.
- [ ] Projeto **com** referência: *Na posição das coordenadas* vem marcado. Desenhe.
      **Esperado:** o lote cai no lugar das coordenadas (confira com *Importar Pontos* das mesmas coordenadas: as cruzes caem nos cantos).
- [ ] Projeto **sem** referência (novo): *Clicar onde fica o primeiro vértice* + *Usar o primeiro vértice como ponto de referência* marcados.
      **Esperado:** depois de clicar, a mensagem diz que o P-01 virou o ponto de referência; *Verificar Projeto* não acusa falta de referência.
- [ ] Cole uma planilha local (`1 1000 1000` / `2 1030 1000` / `3 1030 988` / `4 1000 988`, uma por linha).
      **Esperado:** sistema *Locais*, área 360,00 m², datum e fuso desligados.
- [ ] Cole um trecho do SIGEF com `Longitude: … Latitude: …`. **Esperado:** sistema *Geográficas* e os nomes dos vértices do SIGEF.
- [ ] **Desfazer (Ctrl+Z)** depois de desenhar. **Esperado:** as linhas somem e, se a referência foi definida, ela volta ao que era.

## 4. Anotação

- [ ] **Anotar Lotes** na planta, todos os lotes. Mude a altura do texto na janela e veja a **prévia** mudar.
      **Esperado:** prévia e resultado iguais; nenhum texto sobre outro; rótulo do lote (nome, proprietário, matrícula, área,
      perímetro) dentro do lote; confrontante em cima e `distância / AZ= azimute` embaixo, fora do lote.
- [ ] Mude a escala da vista (ex.: 1:200 → 1:500) e abra **Anotar Lotes** de novo. **Esperado:** aviso de escala alterada e os lotes já marcados.
- [ ] **Hachuras** com *Preencher parâmetros GP_\**. **Esperado:** uma cor por lote; os parâmetros aparecem nas propriedades da hachura.
- [ ] **Vias**: selecione testadas e meio-fio. **Esperado:** pista de 7 m, calçada de 2 m, esquina de 2 m; o nome da rua não se repete nas divisas.
- [ ] **Construção Existente**: abra no lote 02 do exemplo. **Esperado:** área de projeção, taxa de ocupação e distância até cada divisa,
      com prévia; *Salvar e desenhar* cria hachura cinza e cotas.
- [ ] **Recuos**: 5 m na frente e 1,5 m nas laterais de um lote. **Esperado:** contorno verde da área edificável e a área informada.

## 5. Desmembramento e remembramento

- [ ] **Desmembramento** do retângulo de 12 × 30 m em **3 partes iguais** (automático). **Esperado:** prévia colorida com 120,00 m² cada.
      Marque uma parte como **remanescente** e registre.
      **Esperado:** só o remanescente fica com a matrícula do original; o original vai para o histórico; anotações refeitas.
- [ ] **Desfazer (Ctrl+Z)**. **Esperado:** volta o lote original e as partes somem (desenho e dados).
- [ ] Refaça com **Desenhar com a ferramenta Linha…**: desenhe 2 divisas atravessando o lote.
      **Esperado:** a cada linha a janela de prévia atualiza partes e áreas; *Desfazer última linha* remove a última.
- [ ] Com a janela de prévia aberta, **feche o projeto** (ou abra outro e volte). **Esperado:** ao fechar, a janela de prévia fecha
      junto e nada dá erro; com outro projeto ativo, *Concluir* pede para voltar ao projeto certo.
- [ ] **Remembramento** de duas partes vizinhas. **Esperado:** lote unido só com os vértices do novo contorno; pontos de mudança de
      confrontante (dois confrontantes na mesma reta) continuam como vértice.

## 6. Documentos

- [ ] **Memorial Descritivo** de um lote do desmembramento. Deixe o CPF vazio em *Dados do Imóvel*.
      **Esperado:** conferência com a pendência e a opção **Selecionar no desenho os lotes com pendência** (seleciona e enquadra o lote).
      Gere mesmo assim: Word abre; o memorial fala só do próprio lote; o lote não remanescente não tem matrícula.
- [ ] Memorial do lote **remembrado**. **Esperado:** sem histórico dos lotes unidos e sem matrícula.
- [ ] **Anuência de Confrontantes** de um lote. **Esperado:** uma página por confrontante, com o proprietário.
- [ ] **Google Earth / QGIS** (KMZ). Abra no Google Earth. **Esperado:** lotes no lugar certo; o balão do lote não remanescente não mostra matrícula.
- [ ] **Planilha Excel**. **Esperado:** abas de lotes, vértices e segmentos.

## 7. Pranchas e logomarca

- [ ] **Pranchas › Modelos…**: escolha o modelo *GeoPoint – Padrão*. **Esperado:** na prévia aparece o espaço
      "(logomarca do escritório)" entre o croqui e o título.
- [ ] Em *Escritório › Logomarca*, clique em **Escolher…** e escolha um PNG ou JPG. **Esperado:** a prévia mostra a imagem na
      proporção certa, centralizada; o campo mostra formato, tamanho em px e "guardada no modelo". Grave o modelo.
- [ ] Preencha o *Nome* do escritório. **Esperado:** logomarca à esquerda e o nome ao lado.
- [ ] **Exportar** o modelo para um `.json`, apague a imagem original do disco e **Importe** o modelo com outro nome.
      **Esperado:** a logomarca continua no modelo importado.
- [ ] Repita no modelo *GeoPoint – Paisagem*. **Esperado:** espaço da logomarca entre o croqui e o título, na coluna da direita.
- [ ] Gere as pranchas do desmembramento (situação atual, proposta e cada lote).
      **Esperado:** logomarca no lugar em todas as folhas; FOLHA 01, 02, 03…; prancha geral sem números de vértice e sem
      coordenadas, com os pontos nos vértices; pranchas de lote com tabela de coordenadas e chamada do ponto de referência.
- [ ] Modelo gravado numa versão anterior (se você tiver): abra em *Modelos…*, clique em **Reorganizar blocos**.
      **Esperado:** o espaço da logomarca aparece.
- [ ] Clique em **Remover** a logomarca e gere de novo. **Esperado:** o espaço fica em branco na prancha (sem texto).
- [ ] **Exportar PDF** das pranchas. **Esperado:** PDF em escala 100% no tamanho de cada folha, com a logomarca.

## 8. Croqui e chave do Google

- [ ] *Configurações › Croqui*: escolha **Google**, cole uma chave de teste e grave.
      **Esperado:** em *Painel de Controle › Gerenciador de Credenciais › Credenciais do Windows* aparece `GeoPoint/GoogleMapsStaticKey`;
      o arquivo `%AppData%\GeoPoint\configuracoes.json` **não** contém a chave.
- [ ] Feche e abra o Revit, abra *Configurações › Croqui*. **Esperado:** a chave continua lá.
- [ ] Apague a chave e grave. **Esperado:** a credencial some do Gerenciador de Credenciais.

## 9. Cópias de segurança

- [ ] Rode **Anotar Lotes** duas vezes seguidas sem mudar nada e abra *Ajuda › Restaurar uma cópia de segurança*.
      **Esperado:** a lista não tem duas cópias iguais seguidas; os arquivos em `%AppData%\GeoPoint\copias\<projeto>` terminam em `.json.gz`.
- [ ] Restaure uma cópia anterior. **Esperado:** os dados voltam; *Desfazer* desfaz a restauração.

## 10. Encerramento

- [ ] *Ajuda › Gerar arquivo de diagnóstico*. **Esperado:** `.zip` na Área de Trabalho com `sistema.txt`, `configuracoes.json` (sem a chave),
      `projeto-geopoint.json` e os registros.
- [ ] Salve, feche e abra o `.rvt`. **Esperado:** lotes, fases, operações e pranchas continuam lá.
