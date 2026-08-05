# Destiny Together

Tower-survivor cooperativo para até 4 jogadores. Uma cidade **estática** é a única barra de vida
da partida, e o turno é um **ciclo de dia e noite** de 5 + 5 minutos: de dia o mapa é seguro e se
constrói, colhe e **explora**; de noite a horda vem do anel inteiro. Inspirado em *Monsters are
Coming! Rock & Road* (Ludogram/Raw Fury, 2025), com desvios deliberados: multiplayer, cidade
parada, ondas discretas em vez de spawn contínuo, e um dia de verdade em vez de uma tela de
preparo.

**Cinco noites** é a vitória. O Dia tem relógio de 5 min mas é teto, não piso — todos Prontos
antecipa a noite, então um time rápido fecha a partida em bem menos que os ~40 min nominais.

Em volta da vila há um **mundo procedural sem fim**: florestas e pedreiras geradas por função
pura da semente, exploráveis em todas as direções. Não há borda de mapa.

Unity **6000.3.21f1** · URP · C# · PC primeiro.

## Como rodar

Abrir `Assets/_Project/Scenes/Arena.unity` e dar Play. Cai no **menu inicial**: número de
assentos, qual assento você controla, classe de cada um e seed. O `Bootstrap` monta câmera, luz,
tabuleiro e telas por código — não há nada para configurar. Se a cena estiver corrompida:
`Destiny Together > Recriar cena Arena`.

Para iterar rápido sem passar pelo menu, marque **`Pular Menu`** no componente `Bootstrap`.

**Controles**

| | |
|---|---|
| `WASD` | move — o ataque sai na direção do movimento, sem mira |
| `1`-`8` | seleciona carta da mão · clique ergue · botão direito cancela |
| `R` | marca Pronto — antecipa a noite (ao terceiro Pronto o Dia trava em 20s) |
| `1`-`3` | escolhe a carta do draft quando há uma pendente · `Q` rerrola |
| scroll | zoom (18 a 25 unidades — faixa curta de propósito) |
| `ESC` | pausa (continuar / reiniciar / menu) |
| `F1` | painel de teste |

> Enquanto há draft pendente, `1`-`3` pertencem ao draft, não à mão. A precedência está em
> `InputRouter`: sem ela, apertar `1` para pegar a carta oferecida selecionaria a primeira carta
> da mão, e o jogador aprenderia que o teclado mente.

**Painel de teste (`F1`)** — velocidade 0,25× a 8×, pular fase, pular noite, injetar recursos e
cartas, limpar a horda, curar a cidade. Existe porque uma partida dura ~40 min e o Kaiju só
aparece na noite 5: sem isso, o Ato final nunca seria calibrado. Nada ali faz parte das regras.

**Menus do editor** (`Destiny Together`):
- `Simular partida no console` — roda uma partida headless em milissegundos. Mede **deadlock**,
  não balanceamento: os assentos são Autômatos e Autômato não constrói nem explora.
- `Gerar assets de conteudo padrao` — materializa os ScriptableObjects para balancear na mão.

Para **balanceamento** use `.\Tools\Headless\run.ps1` — ele joga de verdade (constrói, drafta,
colhe, explora, caça Ninho) e compara estratégias. Ver "Verificar sem abrir o Unity".

> `activeInputHandler` está em **Both** (legado + Input System). O menu e o HUD são IMGUI, que
> depende dos eventos legados; o gameplay usa o Input System novo. Trocar para "New only"
> quebra as telas. Mudanças nessa opção exigem reiniciar o editor.

## Arquitetura

Sete assemblies, com uma regra que sustenta todas as outras: **a simulação não conhece a engine.**

**O mundo é uma fórmula, não um dado.** `WorldGen.Generate(semente, cx, cz)` é pura: entrar, sair
e voltar reconstrói exatamente a mesma mata. Isso compra três coisas de uma vez — "quase
infinito" sem mundo salvo, streaming barato (`WorldStreamer` materializa só o que está perto de
um herói), e multiplayer sem trafegar um byte de terreno, porque os quatro clientes derivam a
mesma floresta da mesma semente.

O único estado persistente do mundo é `MatchState.ConsumedWorldItems`: um `long` por item já
recolhido. Guardar o mundo seria impossível (ele não acaba) e desnecessário (ele é uma fórmula);
guardar só o **desvio** em relação a ela cabe num HashSet.

```
DT.Core          C# puro, sem UnityEngine    Vec2, GridCoord, Rng, EventBus
DT.Sim           C# puro, sem UnityEngine    estado, fases, sistemas, prognóstico  ← o jogo
DT.Data          ScriptableObjects           definições, conteúdo padrão, ondas
DT.Net.Api       C# puro                     contratos de rede (ainda vazio)
DT.Presentation  views, câmera, efeitos      lê a simulação, nunca escreve nela
DT.UI            HUD e input                 emite PlayerCommand
DT.App           composition root            o único que enxerga tudo
```

`DT.Sim` e `DT.Core` têm `noEngineReferences: true`. Isso não é purismo: é o que dá (a) testes de
partida completa em milissegundos, (b) servidor headless quando o multiplayer entrar, e (c) a
garantia de que trocar placeholder por arte final **não pode** quebrar regra de jogo.
`ArchitectureTests` falha o build se alguém violar isso.

**Dois canais, nunca misturados.** `PlayerCommand` sobe (UI → simulação). `SimEvent` desce
(simulação → apresentação/rede). Posição de entidade **não** trafega como evento — a apresentação
sincroniza com o estado por frame e interpola. Eventos são só o que *acontece* e não pode ser
inferido: nasceu, morreu, atirou, explodiu.

**Tick fixo de 20 Hz** (`MatchSimulation.FixedDelta`). A Noite é tempo real, mas a simulação
avança em passos determinísticos — mesma seed + mesmos comandos = mesma partida.

**Duas fases, não três.** `PhaseId` é `Dia` / `Noite` / `Fim`. O amanhecer não é fase: é um
instante (`BreakDawn`) que dissolve o que sobrou, credita produção, resolve níveis e oferece o
draft. O draft fica pendente e é escolhido a qualquer momento do Dia, com o mapa à vista — não
existe mais uma tela que congela o mundo enquanto quatro pessoas leem três cartas cada.

**A Noite acaba no relógio, sempre** — nunca porque a lista de Investidas terminou. Quem sai para
explorar precisa saber quanto tempo tem. `Noite_DuraOTempoDasRegras_NaoOTempoDasInvestidas`
protege isso.

## Convenções

- **Unidades:** comprimento em CÉLULAS, tempo em SEGUNDOS, velocidade em células/s.
  1 célula = 1 unidade Unity (`GridToWorld.CellSize`). A simulação não sabe o que é um metro.
- **`EntityId`:** Unity 6 tem um `UnityEngine.EntityId` próprio. Todo arquivo de camada Unity que
  use o nosso precisa de `using EntityId = DestinyTogether.Sim.EntityId;`.
- **Placeholder é sistema, não arte temporária.** `PlaceholderVisuals` define a gramática:
  silhueta diz o que a coisa **faz** (esfera = enxame, cápsula = explode, cubo = tanque,
  cilindro = ataca à distância, **octaedro = caça você**, **pirâmide = Kaiju**, **cone = achado**);
  cor diz de que **lado** está. A arte final herda essa gramática.
- **Formas além das do motor.** O Unity só traz cubo, esfera, cápsula e cilindro — quatro
  silhuetas para sete comportamentos. `ProceduralShapes` gera octaedro, cone e pirâmide com
  normais facetadas. A alternativa (mesma forma, cor diferente) quebraria a regra acima, porque
  cor já está ocupada dizendo lado.
- **Alcance do herói vem do conteúdo.** `MatchState.WorldRadius` = `OutskirtsRadius` + folga. Era
  uma constante no integrador, e o resultado foi um mapa em que os Relicários nasciam fora do
  alcance do herói — inalcançáveis, sem nenhum aviso.
  `TodoEsconderijo_CabeDentroDoMundoAlcancavel` existe por causa disso.
- **Views:** toda arte mora no filho `Visual`. Presenters chamam
  `IEntityView.PlayAction(ViewActionId, duração)`, nunca `Animator.Play("...")`.
- **Locomoção é procedural até haver rig.** `EntityView.Locomotion` anima o corpo inteiro
  (balanço, rolagem de peso, inclinação) a partir do deslocamento real — sem esqueleto, sem
  Animator, sem clipe. A fase avança com a **distância percorrida**, não com o tempo: é o que
  prende a passada ao chão e separa "andando" de "patinando" em qualquer velocidade. Mexe só em
  posição e rotação do `Visual`; a escala fica livre para o punch de ataque. Quando a malha
  voltar riggada, isto desliga num bool e o Animator entra pelo mesmo `PlayAction`.
- **Arte real:** 1 célula = 1 unidade, pivot nos pés, +Z para a frente. `VisualFitter` normaliza
  automaticamente por bounds, então pack de loja fora de escala não é problema.
- **Nomes de conteúdo:** `DefId` vem do hash FNV-1a do nome. Renomear um prédio é uma mudança de
  conteúdo que invalida replays — deliberadamente visível.

## As regras de design que o código protege

Estas não são preferências; são o que sobrou depois de estudar por que o jogo de referência
funciona. Quebrar qualquer uma exige mudar o teste correspondente primeiro.

1. **A cidade é a única barra de vida.** Herói morrer nunca encerra a partida — vira Espectro,
   deixa uma Urna resgatável, e sem resgate custa um Túmulo permanente no tabuleiro. A punição é
   espacial, nunca um game over.
2. **O único verbo é ESTAR.** Auto-ataque sai na direção do movimento. Sem mira, sem clique em
   alvo. Toda a expressão do jogador é *onde eu estou*.
3. **Todo prédio é também uma dívida.** Construir para fora sobe o DPS **e** encurta o corredor
   daquela Faixa. O HUD imprime os dois números enquanto o jogador ainda está arrastando.
   `ConstruirParaFora_EncurtaOCorredorDaFaixa` existe para que isso nunca se perca num refactor.
4. **Quatro moedas ortogonais.** XP = cartas novas · Madeira = munição do Silo · Pedra = reparo ·
   Ouro = reroll. **Nada compra construção** — construir vem exclusivamente de subir de nível.
   É por isso que um Relicário entrega XP e não uma carta: achar coisa na mata acelera o progresso
   do time inteiro sem abrir uma segunda porta para construir.
5. **Todo desbloqueio muda uma decisão, não um número.** Distritos dão regra (Queimadura, ignora
   armadura, não vira Escombro), nunca "+X%". Foi a crítica mais votada ao jogo de referência.
6. **Explorar paga em progresso; colher paga em manutenção.** Esconderijo dá XP e Ouro; nó de
   recurso dá Madeira e Pedra. Essa separação é o que dá ao Dia duas atividades em vez de duas
   fontes do mesmo recurso — e como XP sobe o nível da **cidade**, o achado de um vira carta para
   os quatro. `Esconderijo_PagaEmProgresso_NuncaEmManutencao` protege a fronteira.

   O motivo mecânico é o teto de carga: um herói carrega o mesmo tanto perto ou longe, então
   **distância nunca pode pagar em recurso carregável** — seria matematicamente pior que colher
   no quintal, por mais bonita que fosse a floresta. XP e Ouro creditam na hora, sem viagem de
   volta: são a única moeda em que distância pode pagar.
7. **O Dia é seguro, a Noite não.** Nenhum monstro nasce de dia — é essa garantia que torna sair
   do mapa uma decisão em vez de uma aposta. `Noite_GeraMonstros_EODiaNao` falha se alguém
   quebrar isso.
8. **Nada perto de casa rende com o tempo; nada longe rende duas vezes.** A cota de colheita é
   fixa por dia (ficar parado não rende); o mundo procedural é de uso único (bosque exaurido
   continua exaurido); e cada Esconderijo do dia vale menos que o anterior, por jogador,
   zerando no amanhecer. As três regras defendem a mesma coisa: **campo infinito de recompensa
   com valor fixo faz o ganho crescer linearmente com o tempo, e o Dia vira farm.** A medição
   mostrou isso sem sutileza — 299 Esconderijos recolhidos num único dia antes do decaimento.
   Efeito colateral que virou o melhor da regra: como os primeiros achados de cada um valem
   mais, quatro pessoas espalhadas rendem mais que quatro na mesma trilha.

## Arte

Packs em uso: **Polylised — Medieval Desert City** (construções e props) e **Fantasy Forest
Environment Free Sample** (material de terra do chão).

**A mata e a pedreira são 100% arte própria** — as árvores mortas e os penhascos do pack saíram das
duas listas. Eram do deserto, e conviviam mal com uma floresta viva: misturados, o mundo lia como
dois biomas sobrepostos em vez de um. Um bioma tem de parecer um bioma.

`Destiny Together > Aplicar arte importada` faz tudo: converte os materiais para URP, monta o
`VisualsProfile`, cria a atmosfera e liga ambos ao Bootstrap. Roda sozinho na primeira compilação
depois do import.

**O mapeamento é por silhueta, não por nome** — a mesma gramática dos placeholders. Torre redonda
atira longe, octógono congela, retangular empurra: três formas distintas para três comportamentos
distintos, legíveis de cima e no escuro. Casa civil nunca vira torre, porque casa não deve parecer
que atira. Para trocar qualquer escolha: `Destiny Together > Mapear arte importada`.

Monstros continuam em primitivas até a arte deles chegar. Definição sem prefab cai para primitiva
sozinha — nunca existe um estado "meio migrado" em que o jogo não abre.

### Tokens de escala

Altura é token, não gosto. 1 célula = 1 unidade Unity = 1 metro, e cada coisa tem uma altura:

| | altura |
|---|---:|
| swarm (`Enxame`) | 1,0 |
| comum (`Estourador`, `Bruto`, `Cuspidor`) | 1,6 |
| elite (`Rondador`, `Ninho`) | 2,4 |
| **herói** | **1,8** |
| chefe (`MaeAranha`) | 4,0 |
| prop pequeno (barril, caixa, pedra, Baú) | 0,8 |
| prop médio (árvore, poste) | 3,0 |

Prédios não estão na tabela, então têm uma **escada própria em múltiplos de 0,5**: 1,0 base
(Muralha, Serraria, Pedreira, Depósito) · 1,5 utilitário (Braseiro, Oficina) · 2,0 torre
(as três de tiro) · 3,0 vigia · 5,5 Prefeitura.

As três torres de tiro ficam na **mesma** altura de propósito: qual torre é qual já é dito pela
silhueta, e a gramática reserva forma para função. Altura passa a dizer só "isto atira" — e todas
elas ficam acima do herói (1,8) e de todo comum (1,6), que é a leitura que importa quando a horda
encosta.

**Duas hierarquias que o código protege.** A Prefeitura é a coisa mais alta do *tabuleiro* e o
rochedo a mais alta do *mundo*. A primeira estava desprotegida: `BuildSystem` faz `existing.Tier++`
**sem teto nenhum**, e a apresentação multiplicava a altura a cada fusão — uma progressão
geométrica que levava um Posto de Vigia a passar a Prefeitura no quinto tier. `MaxMergedHeight`
(4,0) em `PresentationDirector` fecha isso, medindo os bounds do renderer para valer igual em
primitiva e em prefab.

**Altura oclui.** Com pitch 50°, um corpo de altura H esconde 0,84·H células de chão atrás de si.
É a conta a fazer antes de subir qualquer coisa: foi ela que travou o pilar de Faixa em 4,0 (a
6,0 a telegrafia passava a ocluir o corredor que telegrafa) e é ela que deixa uma dívida aberta no
Kaiju — ver "Dívidas conhecidas".

### Tokens de cor do ambiente

O cenário inteiro vive entre **35% e 55% de luminância**, dessaturado; todo elemento de gameplay
vive **fora** dessa faixa, acima ou abaixo. O teste é o print da tela em escala de cinza: se
jogador, inimigo e pickup não se distinguem, a paleta falhou.

**A métrica é luminância relativa *linear* (WCAG/Rec.709), e isso não é detalhe.** Sob luma gama
os tokens de inimigo caem em 37–44%, dentro da faixa do ambiente, e a regra não fecha. Só sob a
linear os onze tokens de gameplay ficam todos fora dela. Medir errado invalida o teste.

O chão era `#4F6B45` = 12,6%, ou seja mais escuro que o token de horda (12%): cenário e ameaça no
mesmo valor. Hoje é `#97B48C` = 41,1%, e a horda volta a ficar abaixo do chão em que pisa.

**Tile não é cenário.** Os tiles do tabuleiro, o Escombro e o Túmulo ficam deliberadamente
*abaixo* da faixa — eles dizem onde dá para construir e onde um herói morreu, e o token manda
gameplay viver fora dela. Um tabuleiro dentro da faixa ficaria a dois níveis de 255 do campo em
volta, e "a vila é construída, o lado de fora é bruto" deixaria de ser visível. Hoje a fronteira
vale ~4,8× em luminância.

O custo da faixa, medido e aceito: dentro de 35–55% o contraste máximo entre dois elementos de
ambiente é 1,57×, então **mata e campo se separam menos** do que antes. A separação que o jogo
precisa mesmo — cenário contra gameplay — é o que se compra em troca.

### Cenário próprio

`Assets/_Project/Art/Final/Nature/<Nome>/` — a mesma estrutura de um personagem, sem esqueleto:
`<Nome>.fbx` mais `Textures/<Nome>_BaseColor|_Normal|_Metallic|_Roughness.png`. Uma linha na tabela
certa do `ArtSetup` e a peça entra no mundo:

| tabela | vira | largura alvo |
|---|---|---|
| `Trees` | mata (`ScatterProps`) | 2,4 células · teto de **altura** 3,0 |
| `Rocks` | pedra pequena (`QuarryProps`) | 3,0 · altura 2,0 |
| `Boulders` | rochedo grande (`CliffProps`) | 6,5 · altura 6,5 |

A largura continua sendo o alvo, mas quem manda agora é a **altura**: os tokens de escala dão
árvore = 3,0 (prop médio) e o rochedo é a única peça deliberadamente fora da tabela.

**Pedra e rochedo em listas separadas.** `WorldPropKind` sempre distinguiu `Pedra` de `Penhasco`, e a
apresentação ignorava a distinção: os dois sorteavam da mesma lista com a mesma medida, então um
"penhasco" saía do tamanho de um seixo — o tipo existia no código e não existia na tela. Num mundo
sem fim e sem minimapa, marco de terreno é o que permite voltar, e é por isso que o rochedo é a
única peça do mundo maior que a Prefeitura (6,5 contra 5,5 de altura): a vila é a coisa mais alta do
**tabuleiro**, não do mundo.

O nó **colhível** usa a mesma arte do cenário e entra menor (1,8 contra 2,4 na mata; 1,6 contra 3,0
na pedreira). É o que sobrou para dizer "esta dá para colher" agora que a arte do pack saiu — a
diferença de tamanho é real, mas fraca. Anel no chão sob o nó resolveria muito melhor, e é o mesmo
recurso que a dívida de "de quem é este herói" está esperando.

O montador é o mesmo dos personagens (`CharacterSetup`), com a lista de clipes vazia — a operação é
idêntica (FBX texturizado vira peça jogável) e manter dois montadores quase iguais custaria a
próxima correção ser feita só em um deles.

Texturas de natureza entram em **1024**, metade de um personagem: a árvore ocupa 2 células numa tela
de ~28 e existem **milhares** dela em memória ao mesmo tempo. Num personagem a textura é o rosto do
jogo; numa árvore de mata fechada é uma mancha de cor dentro da névoa.

Os `.zip` de origem do Meshy ficam em `ArtSource/` na raiz do projeto — **fora de `Assets/`**, senão
o Unity indexa centenas de MB de arquivo que ele não sabe ler, e fora do git, porque o que importa é
o extraído em `Art/Final/`.

### Personagens

Arte própria do projeto vive em `Assets/_Project/Art/Final/Characters/` — os packs de loja ficam
na raiz de `Assets/`, e a separação é intencional: reimportar um `.unitypackage` não pode pisar no
que é nosso.

Estrutura por personagem, e os nomes são contrato — `CharacterSetup` e o postprocessor os usam
para saber o que é malha, o que é clipe e o que é textura:

```
Characters/<Nome>/
    <Nome>.fbx              malha riggada  (o nome IGUAL ao da pasta = é a malha)
    <Nome>_Run.fbx          clipe          (qualquer outro FBX na pasta = é clipe)
    Textures/<Nome>_BaseColor|_Normal|_Metallic|_Emissive.png ou .jpg
```

**Formato: FBX, não GLB.** O Unity **não importa `.glb`** — nem nativamente, nem sem instalar um
pacote de glTF. E mesmo com o pacote, todo o pipeline daqui é `ModelImporter` (avatar, clipes,
configuração de import), coisa que importador de glTF não expõe: adotar GLB significaria manter
duas pipelines para a mesma coisa.

Personagem que chegar em GLB **converte na entrada**, e aí a pipeline continua sendo uma só:

```powershell
python .\Tools\GlbToFbx\glb2fbx.py entrada.glb Assets\_Project\Art\Final\Characters\Nome Nome Run
```

Saem `Nome.fbx` (malha + esqueleto + skin) e `Nome_Run.fbx` (**só** esqueleto + curvas — o clipe
liga por caminho de transform, então levar os vértices junto custaria 12 MB por clipe sem mudar um
quadro). Ver `Tools/GlbToFbx/README.md` para o que a conversão garante e por quê.

**Confira pelo gabarito, não pela vibe.** O conversor imprime a posição de alguns ossos calculada
direto do GLB, e `Destiny Together > Diagnosticar personagens` imprime os mesmos números depois do
import. É a única prova de que a conversão saiu certa: um erro de ordem de rotação ainda produz um
clipe que "anima", com ossos girando e caminhos casando — só que na pose errada. Medido no
Arqueiro, Y e Z batem exatamente e X vem negado, que é a conversão destro→canhoto que o Unity
aplica a todo FBX (igual para malha e clipe, e portanto invisível).

**Malha sem esqueleto não ganha Animator.** `CharacterSetup` procura `.fbx` e depois `.obj`, e
quando não há clipe nenhum ele *remove* o Animator do prefab em vez de deixá-lo vazio: Animator
sem controlador ainda assume as transformações e congela o corpo na pose de bind — apagando até a
locomoção procedural que serviria de plano B. Sem Animator, o plano B volta a valer.

**"Sem clipe" e "clipe ainda não importado" são estados diferentes, e confundi-los custou a corrida
do primeiro personagem.** O setup roda de dentro de um postprocessador de import, então o FBX de
animação pode não ter sido processado quando a pergunta é feita. Tratar isso como "não tem animação"
gravava um prefab sem Animator — e como o gate de versão já estava satisfeito, ele nunca mais era
reconstruído: a animação sumia em definitivo, sem um único erro no console. Por isso a pergunta é
feita ao **disco** (`File.Exists`) e não ao `AssetDatabase`; arquivo presente e clipe ausente força
reimport, e se ainda assim falhar o Animator **fica** e o erro é logado. O gate agora também
**verifica o resultado** (`RigIsBroken`) em vez de confiar no número, porque versão é boa para "o
setup mudou" e péssima para "o setup falhou".

**Postprocessador é palpite; setup é garantia** — e o Arqueiro mostrou uma segunda cara da mesma
regra. `OnPreprocessModel` roda ANTES de o arquivo ser lido: na primeira passada `transformPaths`
vem **vazio** e o avatar do personagem ainda não existe. Ler vazio como "malha estática" importava
o herói sem esqueleto e o clipe sem curva, e gravava um `.meta` dizendo "estático" que nunca mais
era revisto. Agora vazio significa "ainda não sei" (para FBX de personagem, a resposta segura é
"tem esqueleto"), e `CharacterSetup.EnsureRig` **corrige e reimporta** depois — ali o arquivo já foi
lido, então a pergunta tem resposta. Quem chama diz se espera animação: uma árvore passa lista de
clipes vazia, um herói passa `{"Run"}`. Sem esse discriminador o mesmo montador forçaria rig Generic
e um Avatar em cada tronco da floresta.

`Destiny Together > Diagnosticar personagens` imprime o estado de import, quantos caminhos de curva
do clipe casam com a hierarquia do prefab e — amostrando o clipe — quanto o corpo **realmente** se
mexe e quanto o quadril deriva. Foi ele que provou que o rig estava correto quando a leitura do YAML
sugeria o contrário: 25 caminhos, 0 sem correspondência, 107° de rotação de osso. Nada disso aparece
no inspector lado a lado, e diagnosticar animação lendo `.meta` é adivinhação.

Para adicionar um personagem: extraia nessa estrutura e acrescente uma linha na tabela `Heroes` do
`ArtSetup`. Para adicionar um clipe: solte o FBX como `<Nome>_<Clipe>.fbx` e acrescente o sufixo em
`HeroClips`. **Não edite o `VisualsProfile` à mão** — `Apply()` limpa `Entries` e reconstrói, então
mapeamento feito no inspector some no próximo setup.

`CharacterSetup` monta o que é derivado (material URP, `AnimatorController`, prefab) e pode ser
apagado a qualquer momento: volta igual no próximo setup.

As regras que as duas importações do Azure Sentinel estabeleceram:

- **Altura manda, largura não.** Para prédio, `TargetCells` (largura) é o valor que controla; para
  personagem é `MaxHeightCells`. O motivo é a pose: medido, o modelo tem 1,90 de envergadura por
  1,40 de altura — T-pose. Normalizar pela largura faria um personagem em T sair baixinho e, no
  dia em que fosse riggado com os braços ao lado do corpo, crescer sozinho. Deixe `TargetCells`
  folgado (3.0) e a altura em **1,8 células** — o token de escala do player, e o mesmo valor das
  cápsulas de placeholder, que ocupa ~13%
  da tela na câmera atual.
- **Orientação é MEDIDA, nunca fixada.** `VisualEntry.AutoUpright` mede os bounds e endireita se
  a profundidade passar a altura — o invariante é que gente é sempre mais alta do que funda.
  A largura fica fora da conta de propósito: personagem de braços abertos é mais largo que alto, e
  usar largura daria falso positivo em T-pose.

  Isso não é preciosismo. Medido: a malha **estática** do Azure Sentinel veio Z-up (pedia `-90` em
  X) e a versão **riggada do mesmo personagem** veio Y-up, em que o mesmo `-90` a deitaria. As duas
  declaram `UpAxis=Y` e as duas trazem `Lcl Rotation (-90,0,0)` no nó. Não dá para saber lendo o
  cabeçalho, então rotação fixa em tabela é uma aposta que um dia sai errada.
- **Quando algo parecer torto, meça antes de tentar rotações.** Os scripts do scratchpad
  (`fbxsilhouette.py`, `fbxrender.py`, `fbxfacing.py`) leem o FBX binário e imprimem a silhueta em
  ASCII nos três planos, mais os sinais de "para que lado ele olha" (ponta do pé e nariz). Uma
  olhada resolve o que três tentativas de rotação não resolvem.
- **Rig Generic, não Humanoid.** Clipe e malha vêm do mesmo esqueleto, então a ligação por caminho
  de transform casa exata e não há retarget para falhar. Humanoid destravaria a biblioteca do
  Mixamo ao custo de um mapeamento de avatar que pode falhar — troca que vale depois de o jogo
  rodar, não antes. O campo é `animationType` no `OnPreprocessModel`.
- **Root motion desligado, sempre.** A posição vem da simulação. Root motion faria a animação
  disputar o controle do corpo com o servidor — e no multiplayer, perder.
- **A cor do assento não pisa em arte texturizada.** `ViewFactory.ApplyOwnerTint` carimba a cor do
  jogador quando o material **não tem albedo** — o que cobre primitivas e o modelo que chega sem
  material nenhum (em URP, renderer sem material desenha magenta). Com textura pintada ele não
  toca em nada: carimbar por cima transformaria o trabalho do artista numa silhueta chapada.

  Isso deixa uma dívida aberta e vale saber: com quatro heróis usando a mesma malha texturizada,
  "de quem é esse" não tem resposta visual. A regra "cor diz de que LADO está" continua valendo —
  ela só precisa de um recurso que não dispute o albedo. Anel colorido no chão sob os pés é o
  candidato óbvio, e é o que fazer antes do primeiro teste a quatro.

`OnPreprocessModel` no `ArtSetup` aplica os import settings (sem material, sem animação, sem
blendshape) para qualquer FBX dessa pasta. Fica em código e não no `.meta` porque `.meta` é gerado
pelo editor: arquivo largado na pasta chega sem ele, e "esqueci de conferir o inspector" é o modo
de falha mais comum de pipeline de arte.

**Materiais.** Os packs vêm com shader built-in; num projeto URP isso renderiza magenta.
`UrpMaterialUpgrader` converte para `URP/Lit` preservando cor, albedo, normal e emissão. Ele
**altera os .mat dos packs** — reimportar o `.unitypackage` desfaz.

**Atmosfera.** `Atmosfera_Nebuloso.asset` guarda **dois climas num asset só**: os campos sem
prefixo descrevem a NOITE, os prefixados com `Day` descrevem o DIA, e `AtmosphereApplier`
interpola entre eles por frame usando a curva de `DayNightCycle`. Um asset em vez de dois porque o
entardecer precisa ser contínuo — dois perfis produziriam um corte.

A diferença que mais importa entre dia e noite não é o brilho, é o **alcance de visão**: névoa em
0.010 abre o horizonte para ~140 unidades e a mata dos cantos fica visível do centro da cidade, o
que transforma explorar numa escolha informada; em 0.022 o mesmo bosque some, e é isso que faz
atravessá-lo custar coragem.

**A névoa mede da CÂMERA, não do herói** — e a câmera fica a 18–25 unidades atrás dele. É por isso
que o teto noturno é bem mais baixo do que parece: medido em `FogDensity` 0.045, sobra 37% de
visibilidade no *próprio herói* e 8,7% no pilar de Faixa do lado oposto, ou seja a telegrafia da
ameaça desaparece. Em 0.022 são 79% e 56%. Se a noite precisar fechar mais, o teto é ~0.030.

**O céu é HUD.** O clarear do amanhecer começa exatamente quando os monstros param de nascer
(`SpawnCutoffBeforeDawn`). Isso é deliberado: "última onda já entrou" e "está clareando" são a mesma
informação, dita uma vez só, sem ocupar tela.

**O sol atravessa o céu, e o relógio dele não é o da luz.** `DayNightCycle.Sun` é dirigido pelo
**progresso da fase**; `NightAmount` continua dirigindo a escuridão. São coisas diferentes e
precisavam ser separadas: `NightAmount` fica em zero nos primeiros 70% do Dia — é o que mantém o
mapa claro e seguro — então um sol preso a ela ficava **parado** no mesmo ponto do céu por três
minutos e meio e depois despencava.

Ele nasce à direita da tela, varre 180° e se põe à esquerda dentro dos 5 minutos; a Noite completa
a volta por baixo. A elevação segue um seno (rente nas pontas, a pino no meio) e as duas fases
começam e terminam em `HorizonElevation`, então a emenda entre Dia e Noite é contínua — não existe
um quadro em que o sol salta. `SunAngles.y` diz só onde fica o **meio-dia**: 45° casa com o yaw da
câmera, o que põe o sol atrás de quem olha ao meio-dia (luz frontal, que é o que um jogo visto de
cima quer) e a 90° do eixo da câmera nas pontas do dia, de onde vem a luz rasante.

`HorizonElevation` é 8° e não 0: a 0 grau a sombra tende ao infinito e o mapa inteiro vira uma
mancha escura, que lê como bug de iluminação e não como manhã.

O alaranjado entra na cor **de dia** e só depois o resultado é interpolado para a noite — se fosse
aplicado por último, reacenderia um céu já escuro. A névoa esquenta menos que o sol (0,75): ar
totalmente laranja engoliria a silhueta das peças, e silhueta é a gramática de leitura do jogo
inteiro.

**O mundo procedural.** `WorldPropStreamer` desenha florestas e pedreiras em volta de quem está
olhando e apaga o que ficou para trás, chamando `WorldGen` direto — sem passar pela simulação. Os
dois streamers têm raios diferentes de propósito: a simulação materializa o que dá para **tocar**
(`WorldStreamRadiusChunks`, 4 chunks), a tela o que dá para **ver** (`PropRadiusChunks`, 5).

A simulação pede `includeProps: false`: cenário não é entidade, e ela nunca tocou num prop.

**Regiões: o mundo é dividido por uma malha, não por mais um campo de ruído.** `WorldLattice`
espalha sítios numa grade de 640 células com jitter; a região de um ponto é a do sítio mais próximo,
buscado num 3×3. Mais uma camada de ruído daria mancha do mesmo jeito, mas cada bioma novo viraria
mais uma linha na arbitragem "quem ganha de quem" — em quatro regiões isso já é uma cascata de `if`.
Com a malha a pergunta é sempre a mesma, e a quinta região é uma linha na tabela. Um sítio também é
um **centro**, que é o que uma construção especial vai precisar para nascer em algum lugar que
signifique alguma coisa.

3×3 basta e é demonstrável: com jitter preso em [0,25 .. 0,75] da célula, o sítio da própria célula
está no máximo a 1,06 célula e qualquer sítio fora do 3×3 está a pelo menos 1,25. Com jitter em
[0..1] a garantia cai e a fronteira ganha lascas nos cantos. O ponto é **torcido** antes da consulta
(`domain warp`), senão a fronteira é o lado reto de um polígono de Voronoi e lê como corte de mapa.

**Região muda o que se VÊ, nunca o que se GANHA.** `GenerateProps` aplica as massas da região;
`GenerateSpawns` usa a densidade pura. Com a economia cega à região, nenhuma tabela de bioma
consegue mover o balanceamento medido — a garantia é estrutural, não um cuidado. Medido:

| | árvores/chunk | pedras/chunk |
|---|---:|---:|
| Mata | **216,8** | 3,8 |
| Pedreira | 5,0 | **13,8** |
| Pasto | 17,6 | 2,0 |

A massa média ponderada tem de ficar perto de 1: região **redistribui** a paisagem, não a reduz. A
primeira tabela tinha todas as massas abaixo de 1 exceto uma, e a medição acusou na hora — 37% das
árvores e 70% das pedras evaporaram e a mata fechada caiu pela metade. É invariante, não acidente
(`regiao redistribui a mata, nao a reduz`).

A vila é **sempre Mata**, garantido por um raio de 120 células que fica dentro do cinturão de mata
que já a emoldura — o círculo é invisível e a partida nunca começa cercada de Pântano por sorteio.
Viés por raio de *sítio* não garantiria nada: com jitter, o sítio mais próximo da vila pode estar a
mais de uma célula e pertencer a qualquer vizinho.

**Slots fixos, e nunca um contador.** `ItemKey` deriva do `Index` e `ConsumedWorldItems` é a única
memória do mundo. Com `index++`, no dia em que um chunk deixasse de gerar o nó de madeira o
Esconderijo desceria de 1 para 0 — e todo consumo já gravado passaria a apontar para outro item. Um
recurso ressuscita, outro some, sem erro e sem log.

**Todos os saques são feitos incondicionalmente, em ordem fixa**, e a posição de um item vem de
`hash(chunk, slot)` e não do fluxo do `Rng`. Os dois consertam a mesma classe de bug: saques dentro
de um `&&` fazem o curto-circuito do C# acoplar a economia à paisagem, e `ScatterIn` consumindo do
fluxo fazia aceitar um nó deslocar todos os itens seguintes daquele chunk. `WorldVersion` entra no
`ContentHash` para que dois builds com geradores diferentes não se digam iguais no handshake.

**Mata fechada é contraste, não volume.** O teto é 360 candidatos por chunk de 24×24, mas a média
medida fica em 83: o `Ramp` passa por um smoothstep que **afasta os dois extremos** — borda de
bosque afina, núcleo fecha. Sem ele, subir o teto engrossaria o mundo inteiro por igual, e mundo
uniformemente denso não tem para onde explorar.

Na **Mata** isso dá **217 árvores por chunk** — uma a cada ~1,3 célula, com copa de 2,2: as copas se
sobrepõem e não dá para ver através. Fora dela a paisagem ficou onde estava (Pasto 18, Pedreira 14
pedras): o teto é global, então triplicar só a floresta exigiu dividir as massas das outras regiões
por três. Densidade de uma região é `massa × teto`, e mexer no teto mexe em todas de uma vez.

Um **cinturão nas quatro diagonais** emoldura a vila logo depois da clareira (`CornerForest`, 140
células). Nas diagonais e não nos eixos: a horda vem do anel inteiro e os pilares de Faixa são a
telegrafia da ameaça — emoldurar é bom, tapar informação de jogo não. O cinturão é **só cenário**;
`GenerateSpawns` usa a densidade pura, senão a vila ganharia um campo de madeira encostado nela e a
regra "nada perto de casa rende com o tempo" iria pelo ralo.

**Qual malha desenhar vem da variante, não do tipo.** `WorldProp.Variant` sai do gerador. Quando a
escolha era um hash do `Kind`, todo prop do mesmo tipo desenhava a mesma malha — importar cinco
árvores teria produzido uma floresta com duas. Como a variante é função pura da semente, a mata
continua idêntica ao voltar e igual entre os quatro clientes.

**Não existe GameObject por árvore.** Foi o que permitiu triplicar a densidade. Com um objeto por
peça, 217 por chunk vezes 121 chunks desenhados são ~26 mil objetos — e o gargalo não é a GPU (são
~250 triângulos por árvore), é CPU administrando Transform, hierarquia e culling individual de
coisas que ninguém toca. Prop de cenário é o caso perfeito para instancing: nunca se move, nunca é
clicado, nunca entra na simulação. O que sobra dele é uma **matriz**.

`PropBatcher` mede o molde de cada prefab **uma vez** (instancia, encaixa com `VisualFitter`, lê
malha/material/matriz, destrói) e daí em diante uma árvore custa 64 bytes numa lista; o desenho sai
por `Graphics.RenderMeshInstanced`. A medição passa pelo mesmo `VisualFitter` de propósito:
reimplementar o encaixe daria duas fórmulas de normalização que divergem no dia em que alguém
corrigir só uma, e o sintoma seria arte de tamanho diferente conforme o caminho de desenho.

As matrizes são **assadas na montagem**, já com a posição do pedaço dentro do molde embutida. A
primeira versão multiplicava `matriz × pedaço` na hora de desenhar — 26 mil multiplicações 4×4 por
frame para produzir sempre o mesmo resultado. Assar troca trabalho por frame por memória, uma vez.

Três descartes, do mais barato para o mais caro:

| | |
|---|---|
| **Névoa** | não desenha o que a névoa já esconde. O alcance é lido de `RenderSettings.fogDensity`, então acompanha o ciclo sozinho: ~240 unidades ao meio-dia, ~53 à meia-noite. Rende mais justamente quando mais importa — a noite é quando há 141 monstros vivos disputando o mesmo frame. |
| **Frustum** | cada chunk é um lote com a própria caixa, então o motor descarta o que está fora da tela sem olhar peça por peça. |
| **Sombra** | só dentro de 70 unidades. Sombra de árvore a 150 unidades cai fora da cascata mais distante de qualquer jeito, e mandar 26 mil peças para o mapa de sombra custa mais que desenhá-las. |

Descarregar um chunk agora é soltar listas — não há `Destroy` de milhares de objetos, que era o pico
que atravessar uma fronteira custava. Montar continua com orçamento por frame (`PropsPerFrame`), do
mais perto para o mais longe: troca o engasgo por algumas árvores aparecendo na borda da tela, que é
o lado certo da troca.

Se ainda ficar pesado, os botões são `PropRadiusChunks` (quantos chunks aparecem), `ShadowDistance`
e `MaxPropsPerChunk` — nessa ordem, porque o primeiro é linear na contagem e não muda o desenho do
mundo, e o último mexe em todas as regiões de uma vez.

Sem os packs de arte a mata continua existindo, em primitivas com a mesma gramática de silhueta —
cone escuro é pinheiro, octaedro cinza é rocha. Nunca há um estado "meio migrado" em que a
floresta simplesmente não aparece.

**O chão é uma malha facetada low-poly**, verde chapado `#97B48C`, sem textura: o volume vem das
**normais**, uma por triângulo. Seis vértices por faceta, nenhum compartilhado — vértice
compartilhado receberia a normal média dos vizinhos, que é exatamente a superfície suave que
low-poly não é.

**Faceta não vem de amplitude, vem de FREQUÊNCIA comparável ao tamanho do triângulo.** O relevo tem
três oitavas, e a curta tem o **período de uma faceta** (3,5 células), então os cantos de um
triângulo caem em pontos vizinhos da grade do ruído e recebem valores independentes. Medido: só com
as oitavas larga e média a inclinação média era **0,9°** — geometricamente correto e visualmente
chapado; com a oitava de faceta vai a **5,7° (máx 17°)**. `BoardRenderer.Relief` é o único botão:
0,6 dá 3,8°/11,7°, 1,2 dá 7,6°/22,5°.

**A altura é função pura da posição no MUNDO** (`GroundShape`), não do pedaço de malha que a
desenha. É o que resolve o chão que persegue a câmera: o tapete de 252 unidades snapa em múltiplos
da faceta, os vértices caem sempre na mesma grade do mundo, e reconstruir redesenha exatamente a
mesma superfície. O terreno fica parado enquanto a malha corre atrás.

Nada de atenuação na borda do tapete, e isso é deliberado: seria a maneira óbvia de esconder a
emenda com o horizonte, mas faria a altura de um mesmo ponto do mundo **mudar conforme o jogador
anda** — e as entidades, que leem a altura direto, passariam a flutuar perto da borda. Malha e
entidades têm de concordar sobre onde o chão está, sempre. A emenda fica a 126 unidades, além da
névoa diurna.

**O relevo entra por `GridToWorld.ToWorld`**, a única ponte entre o espaço da simulação e o do
mundo. Por baixo dela ele alcança tudo que pisa no chão — herói, monstro, árvore, Esconderijo — de
uma vez; a alternativa era somar a altura em quinze pontos de chamada e descobrir o décimo sexto
quando algo aparecesse flutuando. `ToFlatWorld` existe para o que precisa de plano.

**O centro fica chapado** (`OutskirtsRadius`, com rampa de 26 células). Prédio em terreno inclinado
ou flutua ou afunda, e o tabuleiro é uma grade de peças de 1×1; o anel também fica plano porque é
onde os pilares de Faixa marcam a ameaça. A vila é construída, o lado de fora é bruto — e a
fronteira entre os dois vira leitura de graça.

A simulação **não sabe** que existe relevo: ela é plana, herói anda em `Vec2`, alcance de ataque é
medido no plano. Relevo é leitura, não regra.

**Câmera.** Perspectiva com **FOV 35**, pitch 50°, yaw 45°, distância 18–25. FOV baixo é a peça
central: perto o bastante de uma projeção paralela para que duas torres iguais pareçam iguais em
pontos diferentes da tela (o que uma grade precisa), e longe o bastante para que peça alta ainda
projete silhueta (o que a gramática de placeholder precisa). Ortográfica pura mataria a segunda
metade.

O enquadramento é de **corpo, não de tabuleiro**: em 16:9 no zoom máximo a tela cobre ~±10 células
de profundidade e ~±14 de largura, e o tabuleiro entra girado 45° com meia-diagonal ~14,9 — quase
cabe, faltando um pouco na profundidade. Consequência de design a acompanhar no playtest: a
leitura **global** das oito Faixas passa a ser do painel da Bússola no HUD, e os pilares viram
indicador **local** — você enxerga o do lado que está defendendo, não os oito de uma vez.

**Escala.** `VisualFitter` mede os bounds reais e normaliza para `TargetCells`, base no chão,
centro em XZ. É por isso que packs em escalas diferentes convivem sem ninguém tocar em import
settings de FBX — e por isso trocar de pack depois continua barato.

**Como a normalização funciona.** `VisualFitter` mede os bounds reais dos renderers e escala a
peça para caber em `TargetCells`, com a base no chão e centrada em XZ. É por isso que um pack cujas
árvores têm 12 unidades e outro cujas casas têm 0,4 convivem sem ninguém mexer em import settings
de FBX — e por isso trocar de pack depois é barato.

## Estado atual

Jogável solo com placeholders: 5 noites, ciclo Dia/Noite de 5+5 min, **mundo procedural sem fim**,
10 prédios, 6 arquétipos de monstro + kaiju, 4 classes de herói, Esconderijos da mata, Ruas,
Distritos, draft, Escombros, Túmulos, Urnas.

Arte própria em campo: **Sentinela** e **Arqueiro** como heróis, os dois riggados e com ciclo de
corrida (o Arqueiro veio em GLB e entra pelo conversor); **cinco árvores, três pedras e três
rochedos** compondo a mata e a pedreira. O nome que aparece no menu descreve o personagem
que o jogador vê, não a classe interna — enquanto todos eram cápsula colorida os dois podiam ser a
mesma palavra, mas "Arauto" em cima de um arqueiro faz o menu mentir. A constante do `DefaultContent`
não muda junto: `DefId` vem do hash dela, e renomeá-la invalidaria replay.

**Balanceamento medido** (`.\Tools\Headless\run.ps1`, 8 seeds, jogo competente):

| | noite alcançada de 5 | nível de cidade |
|---|---:|---:|
| 1 jogador | 1,7 | 4,5 |
| 2 jogadores | 2,3 | 6,6 |
| 4 jogadores | **4,6** | 10,6 |
| 4 jogadores, sem explorar | 4,3 | 8,7 |

*(24 seeds. A tabela anterior, de 8 seeds, foi medida num gerador com o viés descrito abaixo.)*

Explorar vale **+23% de nível de cidade** — e esse número subiu de 15% quando o acoplamento entre
densidade e sorteio foi removido, não quando alguém mexeu numa recompensa. Os saques do Esconderijo
ficavam **dentro** dos `&&` que testam densidade, então o curto-circuito do C# fazia chunks de mata
fechada consumirem mais aleatoriedade antes do sorteio do achado: a chance de Esconderijo estava
correlacionada com a floresta sem que ninguém tivesse pedido. O 15% era medido num gerador
enviesado; o 23% é o valor real. Se ele parecer generoso, o botão é
`WorldCacheChanceNear`/`Far` — mas mexa nele com a medição nova como referência, não com a antiga.

O número existe para provar que o sistema de
Esconderijos muda uma decisão em vez de decorar o mapa. Ele já esteve em 0% duas vezes (os
Esconderijos nasciam onde já se colhia; depois nasciam fora do alcance de movimento do herói) e em
excesso uma vez, quando o mundo infinito fez os batedores recolherem 299 num dia. Refaça a medição
a cada mudança em `WaveBuilder`, nos raios de conteúdo, no relógio ou na densidade do mundo.

Solo continua duro por desenho (mesma proporção do modelo anterior): o jogo é co-op primeiro.

**Multiplayer ainda não existe** — mas está projetado e medido em `Docs/Netcode.md`, com as
versões de pacote verificadas contra documentação oficial. Nenhum pacote de rede está instalado, e
isso é intencional: o Bloco A inteiro do plano roda sem instalar nada.

Três decisões desse documento que restringem código daqui em diante:

- **O cliente não simula.** O host é a única fonte de posição. Lockstep foi rejeitado porque
  `MesmaSeed_ProduzMesmaPartida` prova determinismo em um runtime, não entre IL2CPP e Mono.
- **`HeroMotion.Step` é a única cópia do integrador de movimento.** Host e predição do cliente
  chamam a mesma função; duas cópias divergem por construção.
- **`sim.Events` tem um dreno só.** Hoje é a apresentação; quando o host existir, será dele, e a
  apresentação passa a ler o espelho — inclusive offline. `DrainInto` esvazia, então dois drenos
  significam que um dos dois vê zero eventos, sem erro e sem log.

Assentos sem humano viram Autômatos, que colhem e depositam mas nunca constroem nem escolhem carta.

Ver `Docs/GDD.md` para o design completo e o roadmap.

### Dívidas conhecidas

**O Kaiju esconde o herói, e é medível.** Com pitch 50° um corpo de altura H oculta 0,84·H células
atrás de si. O Kaiju está em 4,0 (a ponta baixa do token de chefe) e tem `BodyRadius` 1,4, o que
põe o limiar de reaparecimento em 3,25 células — acima do `AttackRadius` de **três** das quatro
classes (Golem 2,7 · Guarda 2,9 · Lenhador 3,1). Ou seja: no clímax da noite 5, atacando no
alcance máximo, o jogador perde o próprio herói de vista atrás do chefe. Num jogo cujo único verbo
é ESTAR, é o defeito mais caro possível.

Não dá para resolver mexendo na câmera: `Yaw` é fixo em 45° e não existe comando de rotação. As
saídas são (a) fade/dither do que oclui o herói, que é como o gênero resolve, ou (b) baixar o
Kaiju para 3,0, o que sai do token. A opção (a) preserva o token e é a certa; até ela existir, a
dívida é real e conhecida.

**Três texturas não importam.** `PedraMontanha_BaseColor.png`, `RochedoCume_BaseColor.png` e
`RochedoFacetado_BaseColor.png` são **WebP renomeados** `.png`, que o Unity não decodifica (os
`.meta` dos três não têm bloco `platformSettings`, contra 143 linhas nos que importaram). As três
peças entrariam brancas em 100% de luminância — o pior valor possível para cenário. `CharacterSetup`
passou a carimbar um cinza da faixa de ambiente quando **não há albedo**, o que é remendo, não
conserto: some sozinho no dia em que os arquivos virarem PNG de verdade.

## Verificar sem abrir o Unity

`DT.Core` e `DT.Sim` compilam com o Roslyn do próprio Unity, fora do editor:

```powershell
$dotnet = "C:\Program Files\Unity\Hub\Editor\6000.3.21f1\Editor\Data\NetCoreRuntime\dotnet.exe"
$csc    = "C:\Program Files\Unity\Hub\Editor\6000.3.21f1\Editor\Data\DotNetSdkRoslyn\csc.dll"
$ns     = "C:\Program Files\Unity\Hub\Editor\6000.3.21f1\Editor\Data\NetStandard\ref\2.1.0\netstandard.dll"
$files  = Get-ChildItem Assets\_Project\Code\Core, Assets\_Project\Code\Sim -Recurse -Filter *.cs |
          ForEach-Object { $_.FullName }
& $dotnet $csc -target:library -nostdlib -noconfig -langversion:9.0 "-r:$ns" -out:DT.Sim.dll $files
```

Para as camadas de engine, acrescente `-r:` de cada DLL em `Editor\Data\Managed\UnityEngine\`.

**E dá para rodar, não só compilar.** O Unity traz um runtime .NET 6 junto (`--list-runtimes`
confirma; não há SDK, e não precisa). Trocando `-target:library` por `-target:exe` e escrevendo um
`runtimeconfig.json` de três linhas ao lado do `.dll`, uma partida headless roda em ~300 ms:

```powershell
'{ "runtimeOptions": { "tfm": "net6.0", "framework":
   { "name": "Microsoft.NETCore.App", "version": "6.0.0" } } }' | Out-File -Encoding utf8 DT.Headless.runtimeconfig.json
& $dotnet DT.Headless.dll
```

`Data/DefaultContent.cs` e `Data/WaveBuilder.cs` também são livres de engine, então o conteúdo
inteiro entra na compilação — o resto de `DT.Data` é ScriptableObject e fica de fora.

Isso tudo está empacotado em **`.\Tools\Headless\run.ps1`** (~4 s, sem instalar nada):

```powershell
.\Tools\Headless\run.ps1            # 8 seeds por configuração
.\Tools\Headless\run.ps1 -Seeds 24  # mais amostras
```

Ele roda três coisas: as **verificações** (integrador do herói, estado do PRNG, contador de tick,
alcançabilidade do conteúdo, ortogonalidade das moedas), a **medição de balanceamento** por número
de jogadores e estratégia, e um **traço fase a fase** de uma partida mostrando para onde vão HP,
madeira e pedra.

O traço é a ferramenta mais útil das três. Foi ele que mostrou que o time chegava a nível 8 de
cidade com 25 prédios — cinquenta cartas viraram altura num núcleo denso enquanto o perímetro
seguia descoberto. Média não revela isso; traço revela.
