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

Packs em uso: **Polylised — Medieval Desert City** (construções, árvores mortas, penhascos,
props) e **Fantasy Forest Environment Free Sample** (material de terra do chão).

`Destiny Together > Aplicar arte importada` faz tudo: converte os materiais para URP, monta o
`VisualsProfile`, cria a atmosfera e liga ambos ao Bootstrap. Roda sozinho na primeira compilação
depois do import.

**O mapeamento é por silhueta, não por nome** — a mesma gramática dos placeholders. Torre redonda
atira longe, octógono congela, retangular empurra: três formas distintas para três comportamentos
distintos, legíveis de cima e no escuro. Casa civil nunca vira torre, porque casa não deve parecer
que atira. Para trocar qualquer escolha: `Destiny Together > Mapear arte importada`.

Monstros continuam em primitivas até a arte deles chegar. Definição sem prefab cai para primitiva
sozinha — nunca existe um estado "meio migrado" em que o jogo não abre.

### Personagens

Arte própria do projeto vive em `Assets/_Project/Art/Final/Characters/` — os packs de loja ficam
na raiz de `Assets/`, e a separação é intencional: reimportar um `.unitypackage` não pode pisar no
que é nosso.

Para adicionar um personagem: solte o FBX na pasta e acrescente uma linha na tabela `Heroes` do
`ArtSetup`. **Não edite o `VisualsProfile` à mão** — `Apply()` limpa `Entries` e reconstrói, então
mapeamento feito no inspector some no próximo setup.

Três regras que a primeira importação (Azure Sentinel → Guarda) estabeleceu:

- **Altura manda, largura não.** Para prédio, `TargetCells` (largura) é o valor que controla; para
  personagem é `MaxHeightCells`. O motivo é a pose: medido, o modelo tem 1,90 de envergadura por
  1,40 de altura — T-pose. Normalizar pela largura faria um personagem em T sair baixinho e, no
  dia em que fosse riggado com os braços ao lado do corpo, crescer sozinho. Deixe `TargetCells`
  folgado (3.0) e a altura em **1,7 células** — casa com as cápsulas de placeholder e ocupa ~12%
  da tela na câmera atual.
- **Sem correção de eixo.** Ao contrário do Polylised, o FBX do personagem já traz
  `Lcl Rotation (-90, 0, 0)` no próprio nó e o Unity aplica sozinho. Rodar de novo o deitaria.
- **A cor é do assento, não do modelo.** `ViewFactory.ApplyOwnerTint` carimba a cor do jogador na
  arte importada. Com quatro heróis usando a mesma malha, "de quem é esse" some se o modelo mandar
  na cor — e isso é regra de leitura, não decoração. De quebra cobre o modelo que chega **sem
  material**: em URP um renderer sem material desenha magenta, e atribuir o material de
  placeholder resolve as duas coisas de uma vez.

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
que transforma explorar numa escolha informada; em 0.045 o mesmo bosque some, e é isso que faz
atravessá-lo custar coragem. `FogDensity` noturna acima de ~0.05 começa a apagar os pilares de
Faixa, que são a telegrafia da ameaça — clima que esconde informação de jogo sai caro.

**O céu é HUD.** O clarear do amanhecer começa exatamente quando os monstros param de nascer
(`SpawnCutoffBeforeDawn`), e o sol desce ao longo do entardecer. Isso é deliberado: "última onda
já entrou" e "está clareando" são a mesma informação, dita uma vez só, sem ocupar tela.

**O mundo procedural.** `WorldPropStreamer` desenha florestas e pedreiras em volta de quem está
olhando e apaga o que ficou para trás, chamando `WorldGen` direto — sem passar pela simulação. Os
dois streamers têm raios diferentes de propósito: a simulação materializa o que dá para **tocar**
(`WorldStreamRadiusChunks`, 4 chunks), a tela o que dá para **ver** (`PropRadiusChunks`, 5).

Sem os packs de arte a mata continua existindo, em primitivas com a mesma gramática de silhueta —
cone escuro é pinheiro, octaedro cinza é rocha. Nunca há um estado "meio migrado" em que a
floresta simplesmente não aparece.

**O chão segue.** É UM cubo de 900 unidades que acompanha o foco, travado em múltiplos do tamanho
da textura, com o offset de UV compensando o deslocamento — senão o terreno desliza sob os pés, que
é o artefato clássico de chão que persegue a câmera. Chão por chunk custaria centenas de objetos
para desenhar uma superfície plana.

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

**Balanceamento medido** (`.\Tools\Headless\run.ps1`, 8 seeds, jogo competente):

| | noite alcançada de 5 | nível de cidade |
|---|---:|---:|
| 1 jogador | 1,8 | 4,8 |
| 2 jogadores | 2,5 | 7,1 |
| 4 jogadores | **4,5** | 10,3 |
| 4 jogadores, sem explorar | 4,4 | 9,0 |

Explorar vale **+14% de nível de cidade** — o número existe para provar que o sistema de
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
