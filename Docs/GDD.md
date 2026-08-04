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
| Monstros **por turno** | Some a distância como relógio/placar/termômetro (Verdade 5) | Substituída por **9 turnos com Bússola de Ameaça**: telegrafia total no lugar de pressão contínua |
| **4 jogadores** | Risco de jogador-alfa e de gente ociosa | **Quadrante soberano** + quatro empregos que são lugares físicos distintos |

---

## 2. O tabuleiro

- Terreno construível **21×21 tiles**. Prefeitura ocupa o **5×5 central**, imóvel, **600 HP**.
- O anel é dividido em 4 **Quadrantes**. Com 4 jogadores, um cada — ninguém constrói no dos outros.
  Com menos gente, os quadrantes vagos são repartidos (solo leva os quatro).
- **8 Faixas** de aproximação (N, NE, L, SE, S, SO, O, NO) trazem monstros dos **Arredores**
  (raio 36 células). Os recursos **não** ficam espalhados nesse anel: madeira, pedra e ouro
  vivem em quatro bolsões nas diagonais (NE, SE, SO, NO), cada um sob a guarda natural de um
  Quadrante. Colher deixa de ser "andar em volta" e vira decisão de rota — ir ao canto custa
  tempo e distância da Faixa quente.
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

## 3. O turno

**Partida = 9 turnos · 3 Atos de 3 · Kaiju no turno 9 · ~30 min a 4 jogadores.**

### Fase A — PREPARO (teto 60s no Ato 1, 75s depois)

Mundo em tempo real, sem inimigos. Heróis nascem na Prefeitura.

A **Bússola de Ameaça** revela o Assalto inteiro, Investida por Investida: quais Faixas, quantos,
quais arquétipos. Os oito pilares na borda do mapa ganham a cor do Prognóstico e crescem com o
volume que vem por ali — com placeholder, cor e altura **são** a telegrafia.

Ações simultâneas: **Erguer** (só no próprio Quadrante, só encostando na cidade, custo zero — o XP
já pagou; duplicata sobre duplicata funde em tier maior), **Colher** (cota FIXA por turno: 6
árvores, 4 rochas, 2 baús — ficar 5 min não rende mais que ficar 60s, e é isso que torna a fase
sem timer duro não-explorável), **Reparar**, **Limpar Escombro**, **Doar carta**.

Cada um aperta `R`. **Ao terceiro Pronto, o relógio trava em 15s restantes** — mata o monólogo do
veterano sem impor timer duro em ninguém.

### Fase B — ASSALTO (Investidas de 25-30s, Respiros de 8s)

Ato 1: 2 Investidas. Atos 2 e 3: 3.

Torres adquirem alvo e atiram sozinhas, drenando madeira do **Silo compartilhado**. Silo vazio =
todas caem para 50% de cadência — é por isso que Logística é um emprego de verdade.

Heróis movem em 8 direções; o auto-ataque sai na direção do **movimento**. Sem mira, sem clique.

A última Investida do turno é a **Culminância**: mais Faixas simultâneas, e a partir do turno 6 um
**Ninho** que nasce **fora do alcance de qualquer torre**. Só mão humana mata Ninho. Força o time a
se dividir 2/2 — pressão agendada no Preparo, nunca emergente.

Morte de herói: vira **Espectro** por 6s e cai uma **Urna**. Companheiro que levar a Urna à
Prefeitura **anula** a morte. Ninguém foi = **Túmulo** permanente no Quadrante do morto.

Fim da Investida: os monstros restantes se dissolvem. Fronteira de fase limpa, zero entidades
pendentes — requisito de netcode.

### Fase C — BALANÇO (25s, mundo congelado)

Aspiração de loot, produção das Serrarias/Pedreiras, resolução dos níveis da cidade.

Cada nível dá **uma carta para CADA jogador**, em drafts **privados e simultâneos** de 3 opções,
reroll por 4 ouro. **Regra dura: nunca um draft compartilhado** — seria o monólogo do veterano de
novo. A curva de XP é multiplicada pelo número de jogadores **e o volume das ondas escala junto**,
então a agência per capita é idêntica com 1, 2, 3 ou 4.

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
| **Ninho** | esfera preta | 300 HP, estático, 3 Enxames/5s | Nasce fora do alcance das torres. Força o split 2/2 |
| **Mãe-Aranha** | cubo preto 2×2 | 4000 HP | Kaiju do turno 9 |

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

O jogador mais forte escolhe **um** e abre mão dos outros três. Ele domina uma Faixa, nunca a
partida.

---

## 5. Roadmap

Ordem inegociável: **nunca abrir o netcode antes de o jogo existir single-player local.**

### ✅ Fase 0-2 — Núcleo jogável solo `FEITO`

Grid 21×21 com Prefeitura, herói com auto-ataque na direção do movimento, colheita com carga e
depósito, 10 prédios, 5 monstros + kaiju, flow direto sem NavMesh, 3 fases por turno, 9 turnos,
Ruas, Distritos, draft privado, Escombros, Túmulos, Urnas, Bússola de Ameaça, Prognóstico ao vivo,
HUD com a dívida durante o arrasto, câmera isométrica, gramática de placeholders.

**Medido em simulação headless (sem herói combatendo — é o piso, não o teto):**

| Perfil | Resultado |
|---|---|
| 4p compacto | 600/600 até o turno 4 · derrota no turno 7 |
| 4p espalhado | derrota no turno 6 |
| 2p | derrota no turno 4 |
| Solo | derrota no turno 4 |
| 400 monstros | 0,05 ms/tick — 0,1% do orçamento de 20 Hz |

Jogar compacto ganha de jogar espalhado. A dívida está funcionando.

> **Escala é balanceamento.** Ao dobrar o mapa (11→21), a dupla despencou do turno 5 para o 2:
> mesmas cartas e mesmo alcance de torre para cobrir 4× a área. Alcance das torres, mão inicial e
> curva de XP tiveram de acompanhar. Qualquer mudança futura no tamanho do tabuleiro exige rodar
> `Destiny Together > Simular partida no console` de novo.

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

*Entregável:* **Ato 1 completo — 3 turnos, 4 jogadores, ~9 minutos, com tela de resultado.**

*Critérios de aceite:* (a) mediana do Preparo abaixo de 90s; (b) ninguém ocioso por mais de 5s
durante uma Investida; (c) pelo menos um Distrito cruzando fronteira de Quadrante por partida;
(d) o grupo consegue nomear o turno depois de jogar ("o turno do Ninho").

### Depois do slice

Feira e barracas · até 4 armas por herói · meta-progressão (Bússolas, Coleção) · migração de host
(no slice, se o host cai, a run acaba) · entrada tardia · rotação de Quadrantes por Ato · banimento
de carta · arte e animação definitivas.

---

## 6. Riscos aceitos, com gatilho de correção

1. **Legibilidade em tempo real com primitivas** — o maior risco do desenho escolhido, e o jogo de
   referência é criticado exatamente nisso. Por isso a gramática de cor e silhueta é sistema, não
   arte temporária. *Gatilho:* se no playtest alguém confundir Estourador com Enxame, adicionar
   anel de chão pulsante antes de adicionar qualquer polígono.
2. **Preparo virando monólogo** — mitigado por cota fixa de colheita e clamp de 15s.
   *Gatilho:* se a mediana passar de 2 min, timer duro vira padrão.
3. **Distritos cruzando fronteira morrerem sem voz** — *gatilho:* se menos de 1 por partida,
   destacar Distrito quase-fechado na tela dos dois vizinhos antes de mexer no custo.
4. **Solo/dupla desbalanceados** — o volume escala sublinearmente (1p=1,0 · 2p=1,5 · 3p=2,0 ·
   4p=2,5) e os quadrantes vagos são repartidos. *Gatilho:* medir de novo depois do playtest com
   herói combatendo de verdade.

---

## 7. Incertezas herdadas da pesquisa

O dossiê sobre o jogo de referência tem itens que **não** foram confirmados por fonte primária e
que não devem ser tratados como especificação: a existência de um recurso "água" (provável
alucinação de guias gerados); se prédios afastados precisam de walkways ou apenas alinhamento;
se há inimigos voadores; e todos os valores numéricos de sistema (HP, velocidades, custos), que
nenhuma fonte pública documenta. Nada disso bloqueia o projeto — nossos números vêm de simulação
própria, não de engenharia reversa.
