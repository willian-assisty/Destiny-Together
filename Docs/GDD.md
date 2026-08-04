# Destiny Together — Documento de Design

> Tower-survivor cooperativo, até 4 jogadores. Uma cidade que não anda é a única barra de vida da
> partida. A cada turno vocês veem exatamente o que vem, por onde e quando; constroem juntos uma
> cidade que fica mais forte **e mais fácil de alcançar** a cada tile; e então sobrevivem a 25
> segundos de assalto em que as torres atiram sozinhas e vocês só decidem **onde estar**.
> Nove turnos, um kaiju, meia hora, uma planta baixa com quatro caligrafias no fim.

---

## 1. De onde isto vem

Referência: **Monsters are Coming! Rock & Road** (Ludogram / Raw Fury, 20/11/2025, Steam AppID
2934220). Gênero declarado pelos autores: *"tower-survivor"* — tower defense + horde survivor
**sobre rodas**. Single-player, 15-25 min por run, 82% no Steam.

O jogo roda três camadas simultâneas: survivors-like (o herói), tower defense (o grid) e logística
(a carga). A cidade é um Town Hall sobre rodas que avança sozinho para o sul; o jogador nunca
dirige. Mata monstros e colhe automaticamente por proximidade, carrega até um teto e precisa
voltar para depositar. Depositar XP sobe o nível **da cidade**, e cada nível abre um draft de
prédios. Recursos não pagam construção: madeira sustenta a cadência das torres, pedra repara o HP,
ouro compra na loja de checkpoint.

### As cinco verdades que fazem aquilo funcionar

1. **A base é a barra de vida; o jogador é munição descartável.** Morrer não encerra a run — o
   herói respawna e deixa um túmulo que come um tile do grid **para sempre**. A economia de risco
   é espacial, não temporal.
2. **O único verbo é ESTAR.** Combate 100% automático dos dois lados. Sem mira, sem seleção de
   alvo. Toda a expressão mecânica colapsa em *onde eu estou* — à frente, longe colhendo, ou
   colado defendendo.
3. **Todo upgrade é também uma dívida.** A cidade tem colisão física real. Cada prédio aumenta o
   footprint, e footprint é hitbox: construir mais deixa a cidade mais forte **e mais fácil de
   travar**. É o único trade-off que não se resolve otimizando — só escolhendo forma.
4. **Quatro moedas ortogonais que compram tempo, não objetos.** Nada compra construção. Carga
   limitada com depósito obrigatório transforma a economia numa corrida física, não num contador.
5. **A distância é simultaneamente o relógio, o placar e o termômetro.** Uma variável diegética
   substitui timer, wave counter e score.

**Corolário, extraído das críticas:** o jogo comprometeu três delas e pagou. A punição do túmulo é
fraca demais; desbloqueios permanentes diluíam o pool de draft (corrigido em 13 dias); e a meta de
"+3% com custo exponencial" faz a derrota parecer culpa de grind insuficiente — a reclamação mais
votada. **Regra derivada: todo desbloqueio deve mudar uma decisão, não um número.**

### O que mudamos, e o que isso custou

| Mudança pedida | Consequência de design | Como resolvemos |
|---|---|---|
| Cidade **estática** | Some a colisão da cidade, que é a Verdade 3 inteira | Substituída pelo **tempo de aproximação**: cada prédio para fora encurta o corredor da Faixa |
| Monstros **por turno** | Some a distância como relógio/placar/termômetro (Verdade 5) | Substituída por **5 noites com Bússola de Ameaça**: telegrafia total no lugar de pressão contínua |
| **4 jogadores** | Risco de jogador-alfa e de gente ociosa | **Quadrante soberano** + quatro empregos que são lugares físicos distintos |
| **Ciclo dia/noite** de 5+5 min | Um preparo de 60s vira 5 minutos de mapa seguro — tempo demais para a lista de tarefas que existia | Preenchido com **exploração**: Esconderijos escondidos na mata, sem cota, pagando nas moedas de progresso. O Dia deixa de ser "arrume a casa" e vira "dá tempo de ir até lá e voltar?" |

---

## 2. O tabuleiro

- Terreno construível **21×21 tiles**. Prefeitura ocupa o **5×5 central**, imóvel, **900 HP**.
- O anel é dividido em 4 **Quadrantes**. Com 4 jogadores, um cada — ninguém constrói no dos outros.
  Com menos gente, os quadrantes vagos são repartidos (solo leva os quatro).
- **8 Faixas** de aproximação (N, NE, L, SE, S, SO, O, NO) trazem monstros dos **Arredores**
  (raio 36 células). Na Culminância de cada noite os monstros nascem em **ângulo qualquer** do
  anel e a Faixa é *derivada* do ponto — o Prognóstico continua valendo, mas oito bocas viram
  cerco.
- O herói não tem borda: `WorldRadius` é um limite de **segurança** (4000 células, ~10 minutos de
  corrida em linha reta), não de design. O que o segura perto de casa é o relógio, não uma parede.
- Quatro anéis concêntricos, e a separação é o desenho:

  | anel | o quê | por quê |
  |---|---|---|
  | ~10 a 13 | cidade construível | — |
  | ~17 a 30 | quatro **bolsões de recurso** nas diagonais, sob a guarda natural de um Quadrante | colher é uma **rota**: você sabe onde fica |
  | 27 a 35 | **Esconderijos do dia**, em ângulo qualquer, escondidos | explorar é uma **busca**: você não sabe onde fica |
  | **46 em diante** | **o mundo procedural**, sem fim, em todas as direções | ir longe é uma **expedição**: você não sabe nem o que tem lá |

### O mundo sem fim

Da clareira dos Arredores para fora não existe borda de mapa. O terreno é gerado por **função pura
da semente**: `WorldGen.Generate(semente, cx, cz)` devolve sempre a mesma mata para o mesmo pedaço,
então entrar, sair e voltar mostra exatamente o mesmo bosque, sem que nada tenha sido salvo.

Duas paisagens, por enquanto — e elas competem pelo mesmo espaço, com a rocha ganhando onde as
duas se sobrepõem, porque afloramento exposto é justamente o que impede a mata de crescer:

- **Floresta** — manchas largas (escala ~96 células) de árvores mortas e pinheiros.
- **Pedreira** — manchas menores (~68 células) de rocha e penhasco.

As manchas são grandes de propósito. Ruído fino daria um borrifo uniforme de árvores, e o jogador
não conseguiria dizer "aquilo ali é um bosque, vou naquela direção". A geração precisa produzir
**lugares**, não textura.

**O que se acha lá fora é de uso único.** Um bosque exaurido continua exaurido, e valor novo exige
ir *mais* longe. Isso mantém a exploração expansiva em vez de repetitiva — e o único estado que o
jogo guarda do mundo é a lista do que já foi recolhido, um número por item. Guardar o mundo seria
impossível; guardar o desvio em relação à fórmula cabe na memória.

**A recompensa cresce com a distância, mas o achado do dia decai.** Chunks distantes têm mais
Esconderijos e mais ricos; em compensação, cada achado que um jogador faz num mesmo dia vale menos
que o anterior, zerando no amanhecer. Sem esse decaimento, um campo infinito de recompensa com
valor fixo faria o ganho crescer linearmente com o tempo gasto andando — a exata armadilha de farm
que a cota fixa de colheita existe para evitar. Efeito colateral que virou o melhor da regra:
**quatro pessoas espalhadas rendem mais que quatro na mesma trilha.**

- Prédio: 150 HP. Muralha: 400 HP. Prédio destruído vira **Escombro** — tile bloqueado até ser limpo.

### A dívida do crescimento

O monstro caminha até o tile de cidade mais próximo e ataca o **prédio** mais próximo, nunca a
Prefeitura diretamente. Cada prédio colocado para fora encurta o corredor daquela Faixa. Ao
arrastar um prédio, o HUD imprime, ao vivo:

```
S: aproximação 4.3s → 4.0s     perímetro exposto 12 → 14
PROGNÓSTICO  ARROMBA → VAZA 3     DPS na Faixa 0 → 13
```

Construir para fora sobe o DPS e derruba o prognóstico da mesma Faixa, na mesma tela, no mesmo
segundo. **Arrependimento antecipado é o combustível emocional do jogo.** É o sistema que precisava
existir no primeiro protótipo, e é o que o teste `ConstruirParaFora_EncurtaOCorredorDaFaixa`
protege.

O Prognóstico é cálculo **estático** (DPS das torres que cobrem o corredor × tempo de aproximação
vs. HP total da onda), nunca simulação: 90% do valor social a um custo que roda 60 vezes por
segundo enquanto o prédio está na mão.

---

## 3. O ciclo de dia e noite

**Partida = 5 noites · Kaiju na noite 5 · ~40 min a 4 jogadores.**

O turno é um **ciclo de 24h comprimido**: 5 minutos de Dia, 5 minutos de Noite. Duas fases, não
três — o antigo Balanço virou um *instante* de amanhecer, não uma tela.

A troca de ritmo é a decisão central do design atual, e o que ela compra é uma pergunta que o
modelo de "Preparo → Assalto" não conseguia fazer: **dá tempo de ir até lá e voltar antes de
escurecer?** Um preparo de 60s só permite arrumar a casa. Cinco minutos de segurança permitem
*sair* dela — e sair é onde mora a exploração.

### DIA (teto de 5 min — todos Prontos antecipa a noite)

Mundo em tempo real, **zero inimigos**. Essa garantia é o alicerce: sem ela, sair do mapa seria
uma aposta em vez de uma decisão.

A **Bússola de Ameaça** revela a Noite inteira, Investida por Investida: quais Faixas, quantos,
quais arquétipos. Os oito pilares na borda ganham a cor do Prognóstico e crescem com o volume que
vem por ali — com placeholder, cor e altura **são** a telegrafia.

Quatro atividades, todas simultâneas e todas opcionais:

- **Erguer** — só no próprio Quadrante, só encostando na cidade, custo zero (o XP já pagou);
  duplicata sobre duplicata funde em tier maior.
- **Colher** — cota FIXA por dia (20 árvores, 14 rochas, 7 baús). Ficar os 5 minutos inteiros não
  rende 1 de madeira a mais que sair em 60s, e é isso que permite um relógio longo sem virar farm
  obrigatório.
- **Explorar** — os **Esconderijos**, perto de casa e no mundo sem fim. É o que o tempo que sobra
  vale, e a única atividade que recompensa ir para longe.
- **Escolher carta** — o draft do amanhecer fica pendente e é resolvido a qualquer momento, com o
  mapa à vista. Mais **Reparar**, **Limpar Escombro**, **Doar carta**.

Cada um aperta `R`. **Ao terceiro Pronto, o relógio trava em 20s restantes.**

O sol começa a cair nos últimos 90 segundos. Sombras que se alongam são a leitura mais antiga que
existe de "está ficando tarde", e não ocupam HUD nenhum.

### Os Esconderijos

Nascem **escondidos** num anel de 27 a 35 células — depois dos bolsões de recurso, de propósito:
se colher e explorar acontecessem no mesmo lugar, o Dia teria uma atividade só, feita duas vezes.

Dois raios, e a diferença é o jogo: a **7 células** o Esconderijo acende na tela; a **1,4** ele é
recolhido. Um raio só faria o jogador andar no escuro sem saber se está perto — o que se sente
como sorte, não como exploração.

| | onde | paga |
|---|---|---|
| **Suprimento** | anel inteiro | 30 XP + 8 ouro |
| **Relicário** | terço externo, encostando no anel de spawn | 115 XP + 20 ouro |

**Explorar paga em progresso; colher paga em manutenção.** XP e Ouro de um lado, Madeira e Pedra
do outro. E como XP sobe o nível da **cidade**, e nível de cidade dá carta para os quatro, o
achado de um vira o ganho do time — o que é literalmente a tese do jogo.

O Relicário dá XP e não uma carta de propósito: manter a regra de que **construir vem
exclusivamente de subir de nível**. Um achado acelera o progresso; não abre uma segunda porta.

O que ninguém acha some no amanhecer seguinte. Acumular pareceria generoso e faria o contrário:
quem deixasse de explorar por duas noites acharia o dobro na terceira, e "vale a pena sair hoje?"
viraria "saio quando der".

### NOITE (5 min fixos · 5 Investidas de 34s · Respiros de 14s)

A horda vem **do anel inteiro**. As Faixas continuam existindo como vocabulário do time ("vaza no
Norte") e como granularidade do Prognóstico, mas na Culminância os monstros nascem em ângulo
qualquer: deixa de ser oito bocas visíveis e vira cerco.

Torres adquirem alvo e atiram sozinhas, drenando madeira do **Silo compartilhado**. Silo vazio =
todas caem para 50% de cadência — por isso Logística é um emprego de verdade.

Heróis movem em 8 direções; o auto-ataque sai na direção do **movimento**. Sem mira, sem clique.

**Reparar vale a noite inteira**, e o Respiro de 14s existe para isso: é a janela em que a Pedra é
gasta. Com Respiro curto a defesa só poderia degradar ao longo dos cinco minutos, nunca se manter.

A última Investida é a **Culminância**: todas as direções, e a partir da noite 3 um **Ninho** que
nasce **fora do alcance de qualquer torre**. Só mão humana mata Ninho. Força o time a se dividir.

Morte de herói: vira **Espectro** por 6s e cai uma **Urna**. Companheiro que levar a Urna à
Prefeitura **anula** a morte. Ninguém foi = **Túmulo** permanente no Quadrante do morto.

**A Noite acaba no relógio, sempre** — nunca porque a agenda de Investidas terminou. Quem sai para
explorar precisa saber quanto tempo tem.

Os monstros param de nascer **45s antes do amanhecer**, e o céu começa a clarear na mesma janela.
"Última onda já entrou" e "está clareando" são a mesma informação, dita uma vez só. O último minuto
é limpeza de campo — que o time ganha ou perde.

### AMANHECER (um instante, não uma fase)

A luz dissolve o que sobrou: ficção e regra na mesma linha, e o jogador vê acontecer.

Aspiração de loot, produção das Serrarias/Pedreiras, resolução dos níveis da cidade, e os
Esconderijos do novo dia são escondidos na mata.

Cada nível dá **uma carta para CADA jogador**, em drafts **privados e simultâneos** de 3 opções,
reroll por 4 ouro. **Regra dura: nunca um draft compartilhado** — seria o monólogo do veterano.
A curva de XP é multiplicada pelo número de jogadores **e o volume das ondas escala junto**, então
a agência per capita é idêntica com 1, 2, 3 ou 4.

O draft não congela mais o mundo: ele fica pendente e é escolhido durante o Dia. A carta passa a
ser lida **com o mapa à vista** — dá para ver onde falta cobertura enquanto se lê a opção — e
ninguém fica esperando os outros três.

---

## 4. Conteúdo do dia 1

### Prédios (pool curado de 10)

| Prédio | TAG | Papel |
|---|---|---|
| Balestra | Ferro | 16 de dano, alcance 4,5, 1 tiro/1,2s — a base da defesa |
| Braseiro | Ígneo | Área 1,6, alcance 3 — come pacote de Enxame |
| Torre de Gelo | Gelo | Sem dano; slow 40% por 2,5s — compra tempo |
| Balista de Impacto | Ferro | Empurrão 1,2 — quando o inimigo vira esponja |
| Serraria | Ofício | +12 madeira/turno, +6 por Serraria vizinha |
| Pedreira | Ofício | +6 pedra/turno — a única cura do jogo |
| Oficina | Ofício | +25% cadência nas adjacentes |
| Muralha | Pedra | 400 HP, +0,8s de aproximação — compra TEMPO, não dano |
| Posto de Vigia | Ferro | +1 alcance nas adjacentes |
| Depósito | Pedra | +100 no teto do Silo |

**Ruas:** linha reta de 4+ prédios dá +1 de alcance a todos da linha; 6+ dá +2.
**Distritos:** 4+ prédios conectados com a mesma TAG dão uma **regra**, nunca um número —
Ígneo aplica Queimadura · Gelo deixa lento por 4s · Ofício dobra produção e auto-repara ·
Ferro ignora 50% da armadura · Pedra faz o prédio virar Ruína reparável em vez de Escombro.

> Ruas e Distritos atravessam fronteira de Quadrante de propósito. É o único sistema do jogo que
> obriga dois jogadores a negociar, e existe exclusivamente para isso.

### Monstros

| Arquétipo | Placeholder | Stats | Função de design |
|---|---|---|---|
| **Enxame** | esfera vermelha | 8 HP, vel 4 | Volume. Alimenta o combo |
| **Estourador** | cápsula laranja | 40 HP, explode raio 2 por 30 | O assassino. Mata prédio, não herói. Premia matar dentro do pacote |
| **Bruto** | cubo cinza | 200 HP, armadura 4, resiste a slow | Esponja. Só morre com foco ou Distrito Ferro |
| **Cuspidor** | cilindro roxo | 60 HP, alcance 6 | Anti-camping. Alcance 6 fica fora da Balestra (4,5) mas dentro de uma Rua ou Posto de Vigia — duas respostas, nunca impune |
| **Rondador** | **octaedro amarelo** | 34 HP, vel 6,5, caça herói a qualquer distância | Existe por causa do ciclo. Sem ele, explorar de madrugada seria só demorado, nunca arriscado — a punição viria do relógio, e relógio não assusta ninguém |
| **Ninho** | esfera preta | 300 HP, estático, 3 Enxames/5s | Nasce fora do alcance das torres. Força o split 2/2 |
| **Mãe-Aranha** | **pirâmide preta** | 4000 HP | Kaiju da noite 5 |

O Rondador é rápido **e** frágil, e as duas coisas são o mesmo argumento: ele precisa alcançar
quem está longe (6,5 supera todos os heróis menos o Arauto), mas não pode ser uma sentença. Quem
for pego e voltar correndo para as torres sobrevive; quem insistir em ficar na mata, não. **A
resposta é recuar, e recuar é uma decisão** — morrer sem resposta não seria.

O octaedro é a única silhueta pontuda em todos os eixos: a leitura de "vem atrás de *você*".
Amarelo-ácido para separá-lo do vermelho de horda — o Rondador não é mais um da onda, é um
problema pessoal.

### Heróis

| Classe | Perfil |
|---|---|
| **Guarda** (cápsula azul) | 130 HP, equilibrado, segura linha |
| **Lenhador** (cápsula laranja) | Colheita 2× mais rápida, carga 20, depósito 0,25s |
| **Golem** (cápsula verde) | 200 HP, lento, tanque |
| **Arauto** (cápsula roxa) | Rápido (6,5), frágil (85 HP), maior alcance de ataque |

Qualquer combinação de 4 é viável. Nenhuma classe é requisito. Duplicatas permitidas — o time
perde eficiência, nunca viabilidade.

### Os quatro empregos

Nenhum corpo está em dois lugares ao mesmo tempo, e toda tarefa é um lugar. O anti-dominância vem
de **geografia**, não de matemática — sem cap de dano, sem rubber-band, sem nerf ao jogador bom.

1. **Linha** — segurar a Faixa quente dentro do alcance das torres.
2. **Combo** — encadear efeitos com outro jogador. Exige DOIS.
3. **Logística** — reabastecer o Silo durante o combate. A tarefa mais ingrata é a mais decisiva no
   minuto mais tenso.
4. **Expedição** — matar o Ninho fora do alcance das torres. Sempre longe da Linha.
5. **Batedor** — varrer a mata durante o Dia. Chegou com o ciclo, e é o único emprego que acontece
   quando **não** há perigo: quem bate mato não colhe, e quem colhe não bate mato.

O jogador mais forte escolhe **um** e abre mão dos outros. Ele domina uma Faixa, nunca a partida.

**Medido:** dois batedores + dois coletores batem quatro coletores por **+34% de nível de cidade**.
Especializar não é estilo — é a jogada.

---

## 5. Roadmap

Ordem inegociável: **nunca abrir o netcode antes de o jogo existir single-player local.**

### ✅ Fase 0-2 — Núcleo jogável solo `FEITO`

Grid 21×21 com Prefeitura, herói com auto-ataque na direção do movimento, colheita com carga e
depósito, 10 prédios, 6 monstros + kaiju, flow direto sem NavMesh, **ciclo Dia/Noite de 5+5 min**,
5 noites, **Esconderijos da mata**, Ruas, Distritos, draft privado resolvido durante o Dia,
Escombros, Túmulos, Urnas, Bússola de Ameaça, Prognóstico ao vivo, HUD com a dívida durante o
arrasto, **virada de luz contínua**, câmera isométrica, gramática de placeholders com sólidos
procedurais.

**Medido em `.\Tools\Headless\run.ps1` (jogo competente: constrói, drafta, colhe, explora):**

| Perfil | Noite alcançada de 5 | Nível de cidade |
|---|---:|---:|
| 4 jogadores | **4,5** | 10,3 |
| 4 jogadores, sem explorar | 4,4 | 9,0 |
| 2 jogadores | 2,5 | 7,1 |
| Solo | 1,8 | 4,8 |
| Pico de monstros vivos (noite 5) | 140 | — |

Explorar ganha de não explorar (+14% de nível de cidade). O sistema está funcionando — e o número
já esteve errado nos dois sentidos: em **0%** quando os Esconderijos nasciam onde já se colhia e,
depois, quando nasciam fora do alcance de movimento do herói; e em excesso quando o mundo infinito
entrou e os batedores passaram a recolher **299 num único dia**, antes do decaimento por achado.

> **Escala é balanceamento — e ritmo também.** Ao dobrar o mapa (11→21), a dupla despencou do
> turno 5 para o 2. Ao trocar 9 turnos de ~100s de combate por 5 noites de 255s, três coisas
> desandaram juntas: a economia de uma partida inteira encolheu 44% (5 dias no lugar de 9 turnos)
> enquanto a exposição ao combate subiu 40%, e o volume por Investida ficou dimensionado como se
> cada uma fosse um Assalto — 524 monstros vivos no pico, derrota na noite 2. Cota de colheita,
> HP da Prefeitura, tamanho de pacote e ritmo das Investidas tiveram de acompanhar.
>
> **Refaça a medição a cada mudança em `WaveBuilder`, no relógio do ciclo, ou nos raios de
> conteúdo.** Três bugs que só a medição revelou: os Esconderijos nasciam onde já se colhia
> (explorar valia 0%); depois nasciam **fora do alcance de movimento do herói** (Relicários
> literalmente impossíveis de pegar, sem nenhum aviso); e o robô fundia cartas em torres de tier
> maior em vez de expandir o perímetro — nível 8 de cidade com 25 prédios, estratégia que era boa
> quando o ataque vinha por poucas Faixas e é ruinosa agora que vem do anel inteiro.

### ▶ Fase 3 — Playtest e calibragem `PRÓXIMO`

O gate que decide o projeto: **um jogador solo experiente hesita ao colocar um prédio para fora?**
Se não hesitar, a Verdade de Design 3 morreu e o projeto precisa de outra dívida **antes** de
qualquer linha de netcode.

Também nesta fase: som (a Buzina 1,5s antes de cada spawn — com placeholder, som e cor são a
telegrafia), e ajuste fino da curva com humano no controle.

### Fase 4 — Dois jogadores

Netcode for GameObjects + Relay/Lobby. Host autoritativo. Predição de cliente **só** para o próprio
herói. Monstros replicados por **spawn determinístico por seed**, nunca `NetworkTransform` por
unidade. Snapshot em toda fronteira de fase. Câmeras individuais.

*Critério de saída:* 2 jogadores, 3 turnos, 150 ms de ping simulado, zero dessincronização de fase.

### Fase 5 — Vertical slice a 4 `ENTREGÁVEL`

Marcas e Estouros cross-player com ping automático · Alento (buff que só pode ser mirado em
**outro** jogador) · Sintonia · Sino da Prefeitura com cargas = (jogadores − 1), então alguém
sempre abre mão · Sugestão fantasma (opinião barata, mão sempre do dono) · Mesa de Guerra ·
quatro placares **não comparáveis** (metros, depósitos, marcas, reparos — e **nenhum medidor de
dano**) · queda de jogador vira Autômato com assento reservado.

*Entregável:* **duas noites completas, 4 jogadores, ~20 minutos, com tela de resultado.**

*Critérios de aceite:* (a) ninguém ocioso por mais de 5s durante uma Investida; (b) pelo menos um
Distrito cruzando fronteira de Quadrante por partida; (c) o grupo consegue nomear a noite depois
de jogar ("a noite do Ninho"); (d) **pelo menos um jogador é pego pela noite longe de casa e
consegue voltar** — se ninguém arriscar, o Dia está longo demais ou o Esconderijo barato demais;
se ninguém conseguir voltar, o Rondador está forte demais.

### Depois do slice

**Mais coisas no mundo procedural** — hoje ele gera floresta e pedreira; a estrutura já aceita
outras camadas sem tocar em streaming nem em persistência: basta um novo `WorldPropKind` e uma
regra de densidade em `WorldGen`. Candidatos naturais: ruínas (com Esconderijo garantido dentro),
acampamentos de monstro (uma ameaça que existe de dia e recompensa quem a limpa), lagos e rios
como barreira de rota, e biomas com regra própria.

Feira e barracas · até 4 armas por herói · meta-progressão (Bússolas, Coleção) · migração de host
(no slice, se o host cai, a run acaba) · entrada tardia · rotação de Quadrantes por Ato · banimento
de carta · arte e animação definitivas.

---

## 6. Riscos aceitos, com gatilho de correção

1. **Legibilidade em tempo real com primitivas** — o maior risco do desenho escolhido, e o jogo de
   referência é criticado exatamente nisso. Por isso a gramática de cor e silhueta é sistema, não
   arte temporária. *Gatilho:* se no playtest alguém confundir Estourador com Enxame, adicionar
   anel de chão pulsante antes de adicionar qualquer polígono.
2. **Dia virando tempo morto** — cinco minutos é muito tempo se não houver o que fazer. Mitigado
   por cota fixa de colheita (que acaba) + Esconderijos (que não acabam) + clamp de 20s ao terceiro
   Pronto. *Gatilho:* se o time marcar Pronto antes dos 3 min de forma consistente, o Dia tem tempo
   demais ou a mata tem pouca coisa; medir qual antes de encurtar o relógio.
3. **Explorar virando obrigação em vez de escolha** — hoje vale +34% de nível de cidade, o que é
   forte. *Gatilho:* se no playtest ninguém colher, o Silo esvazia e as torres caem a 50% — o jogo
   se autocorrige uma vez. Se não corrigir, baixar o XP do Relicário antes de mexer em qualquer
   outra coisa.
4. **Distritos cruzando fronteira morrerem sem voz** — *gatilho:* se menos de 1 por partida,
   destacar Distrito quase-fechado na tela dos dois vizinhos antes de mexer no custo.
5. **Solo/dupla desbalanceados** — o volume escala sublinearmente (1p=1,0 · 2p=1,5 · 3p=2,0 ·
   4p=2,5) e os quadrantes vagos são repartidos. Medido: solo chega à noite 1,8 de 5, que é a mesma
   *proporção* do modelo anterior — não é regressão, é o jogo sendo co-op primeiro. *Gatilho:*
   medir de novo depois do playtest com humano no controle.

---

## 7. Incertezas herdadas da pesquisa

O dossiê sobre o jogo de referência tem itens que **não** foram confirmados por fonte primária e
que não devem ser tratados como especificação: a existência de um recurso "água" (provável
alucinação de guias gerados); se prédios afastados precisam de walkways ou apenas alinhamento;
se há inimigos voadores; e todos os valores numéricos de sistema (HP, velocidades, custos), que
nenhuma fonte pública documenta. Nada disso bloqueia o projeto — nossos números vêm de simulação
própria, não de engenharia reversa.
