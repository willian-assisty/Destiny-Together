# Camada de rede

Estado: **projetada e medida, não implementada.** `Packages/manifest.json` ainda não tem nenhum
pacote de rede — é intencional, o Bloco A inteiro roda sem instalar nada.

Este documento é o que sobrou depois de verificar cada versão de pacote contra documentação
oficial e depois de **medir** as premissas de volume em vez de estimá-las. Onde a medição
contradisse o projeto, o projeto mudou.

---

## A decisão que precede todas as outras

**O cliente não roda a simulação. O host é a única fonte de posição.**

A alternativa — simulação em lockstep nos quatro clientes, com só os comandos trafegando — é
sedutora porque `DT.Sim` já é determinística e `MesmaSeed_ProduzMesmaPartida` já passa. Foi
rejeitada, e a razão não é conforto:

Aquele teste prova reprodutibilidade em **um** binário, **um** runtime, **uma** máquina. Não prova
nada sobre IL2CPP em release contra Mono no editor. Há `float` em toda `MonsterSystem`, `Combat` e
`HeroSystem`; em lockstep, um único deles divergindo no tick 4000 derruba a partida inteira, sem
aviso e sem conserto. Replicar posição custa banda; lockstep custa a partida.

O que a replicação compra, e que o lockstep não compra a preço nenhum:

- **Posição passa a poder perder pacote.** Movimento é ~90% do volume e é o único fluxo que
  tolera perda. Ele vai por canal *unreliable*; o canal *reliable* fica com ~150 B/s. Fila
  reliable que nunca cresce é fila que nunca estoura — e `UnityTransport` **fecha a conexão**
  quando uma entrega reliable falha.
- **O snapshot não pode travar o movimento na tela.** Reliable e unreliable usam pipelines
  diferentes no `UnityTransport`. Não é mitigação, é propriedade estrutural.
- **Determinismo continua valendo** — para replay, testes e o arquivo de retomada. Só deixa de
  ser o que segura a partida em pé.

Custo aceito: a banda medida abaixo, e uma tabela de slots estável por entidade.

---

## O volume real (medido, não estimado)

O orçamento inteiro é linear no número de monstros vivos. O projeto assumiu 115. Medi — e depois
o jogo mudou de ritmo (ciclo dia/noite, 5 noites de 5 min no lugar de 9 turnos), o que obrigou a
medir de novo. Os dois resultados estão aqui porque a diferença entre eles é a lição.

| medição | pico de monstros vivos | quadro de Motion |
|---|---:|---:|
| projeto original (estimativa) | 115 | 581 B |
| modelo de 9 turnos (medido) | **220** | 1106 B |
| ciclo dia/noite (medido, atual) | **140** | **706 B** |

O pico caiu porque a noite de 5 minutos distribui a mesma pressão em cinco Investidas de 34s em
vez de concentrá-la — mais monstros por partida, menos monstros *ao mesmo tempo*.

A medição do teto exige cidade invulnerável, porque nem toda partida chega à noite 5. Isso é
medida de *volume*, não de balanceamento: a rede tem de aguentar a pior noite que o conteúdo
consegue produzir, não a melhor noite que a build atual alcança.

### Cadência: monstros a 5 Hz

| | 10 Hz | 5 Hz |
|---|---:|---:|
| payload por cliente | 8,05 KB/s | 4,60 KB/s |
| com framing NGO + UDP/IP (~26%) | 10,1 KB/s | 5,8 KB/s |
| **upstream do host (3 clientes)** | **30 KB/s ≈ 240 kbps** | **17 KB/s ≈ 140 kbps** |

Com 140 monstros os dois cabem, mas 5 Hz é a escolha: 140 kbps de upstream sustentado não exclui
ninguém, e a janela de interpolação de 200 ms custa meia célula de atraso visual em monstros que
andam devagar, em linha quase reta rumo à estrutura-alvo. Heróis continuam a 20 Hz, porque herói é
o que o jogador está olhando.

### O quadro de movimento é fatiado mesmo assim

140 × 5 B + 6 B = **706 bytes**, confortavelmente abaixo do teto não-fragmentado do NGO
(**1296**, `NetworkMessageManager.DefaultNonFragmentedMessageMaxSize`). Mas **entrega unreliable
não fragmenta** — ela estoura — e a medição de 220 do modelo anterior mostra o quanto esse número
se move quando o ritmo muda.

Regra, mesmo com folga hoje: **`MotionCodec` emite N pacotes de no máximo 1000 bytes de payload**,
cada um auto-descritivo (`tick` + lista de slots). Como é unreliable e por-slot, cada fatia é
aplicável sozinha — fatiar é gratuito, e não fatiar é uma falha silenciosa que só apareceria numa
noite 5 com quatro pessoas, depois de um ajuste de `WaveBuilder` que ninguém ligou a isto.

> A conta é reprodutível: `.\Tools\Headless\run.ps1`. **Refaça-a sempre que `WaveBuilder` ou o
> relógio do ciclo mudarem** — foi exatamente uma dessas mudanças que moveu o pico de 220 para 140.

---

## Pacotes — versões verificadas contra doc oficial

Só o núcleo, e só na fase de NGO (Bloco C). Nada disto entra antes.

```jsonc
"com.unity.multiplayer.playmode": "2.0.2",   // manual do 6000.3
"com.unity.netcode.gameobjects": "2.13.1",   // manual do 6000.3
"com.unity.multiplayer.tools": "2.2.9",      // manual do 6000.3 (ver alerta)
```

**Não listar** `com.unity.transport` — entra transitivo e resolve 2.7.4 pelo alinhamento com o
editor. **Não listar** `com.unity.nuget.mono-cecil` — o `packages-lock.json` já resolve 1.11.6;
fixar o 1.11.4 que o NGO declara como mínimo é downgrade e conflita com `com.unity.test-framework`.

**Nada de UGS na v1.** Para host-cliente por IP direto entre amigos, `services.multiplayer`,
`services.authentication` e `services.core` são dispensáveis — e são justamente os que **não têm
página no manual do 6000.3**, ou seja, não têm pin oficial por versão de editor.

Rejeitados na verificação, para não voltarem por tutorial de blog:

- `com.unity.services.multiplayer@2.3.0` — **não existe**; a doc `@2.3` devolve 404. A versão real
  é **2.2.1**.
- `com.unity.multiplayer.playmode@3.0.0` — existe, mas não declara suporte a 6.3. O manual do
  6000.3 aponta 2.0.2.
- "`services.relay` e `services.lobby` estão DEPRECATED" — não confirmado. As páginas oficiais
  apresentam ambos como serviços ativos.

Dois alertas que custam tempo se ignorados:

- **NGO 1.x não roda no 6000.3.** A Unity declarou que 6000.3+ só suporta 2.x. Todo tutorial com
  `[ServerRpc]`/`[ClientRpc]` é da linha 1.x — a API atual é o atributo único `[Rpc(SendTo.X)]`, e
  `RequireOwnership` está deprecado em favor de `RpcInvokePermission`.
- **`multiplayer.tools` 2.2.9 tem uma regressão** em que o adapter do NGO falha ao registrar se o
  `NetworkManager` inicializa antes do setup do pacote. A 2.2.10 corrige. Comece em 2.2.9 (versão
  do manual); se o RNSM não aparecer em runtime, suba — não é upgrade cosmético.

---

## Os canais

Um pipeline por intenção, **sempre explícito** — o default do NGO para named message é
`ReliableSequenced`, que é não-fragmentado, e aceitar o default é como o snapshot estoura em
silêncio no release.

| canal | entrega | o que passa |
|---|---|---|
| `Control` | ReliableSequenced | handshake, aprovação, resync |
| `Command` | ReliableSequenced | intenções discretas: construir, reparar, Pronto, carta |
| `Input` | UnreliableSequenced | `MoveHero`, com redundância |
| `Events` | ReliableSequenced | eventos **estruturais** |
| `Cosmetic` | UnreliableSequenced | eventos **cosméticos** |
| `Motion` | UnreliableSequenced | posição de monstros e heróis, fatiado |
| `Bulk` | ReliableFragmentedSequenced | snapshot (~2,2 KB) |

**A partição dos `SimEvent` é o coração disto.** Estrutural é o que muda o roster ou o HUD —
`MonsterSpawned`, `MonsterDied`, `TowerBuilt`, `PhaseChanged`, `CityLevelUp`, `HeroDied`… Perder um
produz um mundo errado que não se conserta sozinho: um monstro invisível que mata, um fantasma
imortal na tela. Cosmético é `TowerFired`, `MonsterDamaged`, `HeroAttacked` — 80% do volume, 0% do
estado; perder um é uma faísca que não apareceu.

`DraftOffered` e `CardPicked` vão por `SendTo(assento)`. O draft do Balanço é privado por desenho;
um snapshot indiscriminado entrega a mão dos outros três.

### `MoveHero` não é comando, é sinal amostrado

`InputRouter.TickMovement` hoje envia **todo frame** — ~60 mensagens/s por cliente. Em rede, por
canal reliable, isso é exatamente o tráfego que faz a fila crescer sob perda e o transporte
derrubar a conexão. Cinco medidas, todas do lado do cliente:

1. Canal `Input`, unreliable, nunca compartilhando pipeline com `Command`.
2. **Redundância no lugar de retransmissão**: cada quadro carrega os 3 últimos ticks (9 bytes).
   Perder um pacote é invisível; só três perdas seguidas abrem buraco.
3. Direção em 2 bytes — ângulo (1,4° de resolução) + magnitude. `float` não trafega.
4. **Banda morta com keyframe**: direção quantizada idêntica à última é suprimida, exceto a cada
   10 ticks. Herói parado no Preparo custa **zero bytes**.
5. Extrapolação de graça: `hero.MoveInput` **persiste no estado** até ser sobrescrito
   (`MatchSimulation.Apply`). Se um quadro não chega, o host continua movendo com a última direção
   — que é o comportamento certo para sinal contínuo. Basta o inbox **não** zerar em ausência de
   pacote; um timeout de 500 ms zera, para o herói de quem caiu não sair andando sozinho.

Autoridade: o host **carimba** `cmd.Player` a partir do remetente antes de qualquer validação — o
campo é sobrescrito, não conferido. Clampa `Direction` a magnitude ≤ 1 (senão o cliente manda
`(50,50)` e vira speedhack) e recusa mais de 25 quadros/s por assento.

---

## Predição do próprio herói

NGO não entrega isto. `AnticipatedNetworkVariable` e `AnticipatedNetworkTransform` despacham por
`NetworkObject`, e este desenho tem zero. É código nosso — mas como o cliente não simula, o escopo
encolhe de "re-simular a partida" para "integrar um vetor": ~150 linhas.

**Só posição é predita.** Combate, colheita, depósito, dano, morte e resgate de Urna, não. Errar
"eu matei aquilo" ou "eu depositei" é infinitamente pior que 80 ms de atraso numa faísca.

A invariante: *a predição só tem permissão de estar errada sobre posição, jamais sobre estado.*
Todo evento estrutural que toca o herói local (`HeroDied`, `HeroRespawned`, `PhaseChanged`) limpa o
anel de predição e fixa duro pelo próximo quadro autoritativo.

Reconciliação: cada quadro de `Motion` carrega `ackTick` e a posição autoritativa naquele tick.
Erro ≤ 0,05 unidade → aceita. Acima → rollback e reaplicação de `HeroMotion.Step` para cada input
desde `ackTick` (~4 iterações). **A correção nunca aparece como teleporte**: o erro residual vira
offset visual que decai em 150 ms, alimentando a interpolação que `PresentationDirector` já faz.

Isto é o que `HeroMotion` existe para garantir: **host e cliente chamam a mesma função**. Duas
cópias do integrador divergem por construção, e aí a reconciliação passa a medir divergência de
código em vez de latência. `HeroMotion_ReproduzIntegradorDoHeroSystem` falha se alguém reintroduzir
movimento dentro do `HeroSystem`.

---

## Queda, retomada e late join

**Migração de host não entra na v1**, e é decisão, não esquecimento. A autoridade é uma
`MatchSimulation` completa — estado dos dois `Rng`, acumulador, cooldowns de retarget, draft
pendente. O `SimSnapshot` que os clientes recebem é **deliberadamente** uma projeção de
apresentação: não tem nada disso, e não deve ter, porque incluiria o draft privado dos outros.

A mitigação real é de uma tarde: **`MatchResumeFile`** — a `MatchState` completa em disco local, a
cada fronteira de fase (`MatchSimulation.EnterPhase` já é o gancho único). Host cai e volta,
re-hospeda do arquivo, os três amigos reentram pelo caminho normal de late join. Perde-se **uma
fase**, não a sessão de 30 minutos.

São dois snapshots distintos e é de propósito:

| | conteúdo | destino | tamanho |
|---|---|---|---|
| `SimSnapshot` | projeção visível, sem draft alheio | rede | ~2,2 KB |
| `MatchResumeFile` | estado completo, com PRNG | disco, **nunca trafega** | — |

`Rng.GetState/SetState` e `MatchState.NextEntityIdSeed` existem para isto: a semente sozinha não
basta, porque um gerador que já sacou 4000 números não volta ao mesmo ponto por ser resemeado. E se
o contador de ids voltar a 1, entidades novas reciclam ids que os clientes ainda têm em tabela.

**Late join é sob demanda, a qualquer tick** — não só na fronteira de fase. O snapshot custa 2,2
KB; amarrá-lo à fronteira condena quem cai no meio de uma investida de 25 s a olhar tela preta.

**A queda já está resolvida na simulação, sem escrever nada**: `MatchState.ReadyCount()` conta
`!IsConnected` como pronto, então o Preparo não trava esperando um Pronto que nunca vem, e
`AutomatonSystem` mantém o assento colhendo. O assento fica reservado por 120 s contra um
`resumeToken`; fora da janela, o jogador pega o próximo livre.

---

## Os três riscos, e o sinal de cada um

Abrir mão de `NetworkObject` significa que **nenhum mecanismo da engine pega erro por nós**. Cada
risco precisa de um sinal explícito, ou o modo de falha é silêncio.

**1. O espelho do cliente diverge e ninguém percebe.** Um `MonsterSpawned` perdido é um monstro
invisível que mata. Um `MonsterDied` perdido é um fantasma imortal. Nada disso gera erro ou log — o
jogo continua rodando, errado.
*Sinal:* o host emite `(tick, rosterHash)` a 1 Hz — 8 bytes, custo nulo. `rosterHashMismatches` é
**binário**: qualquer valor diferente de zero manda parar e bissectar. O sinal de segunda ordem,
que aparece antes, é `eventBatchSeqGaps > 0`.

**2. A fila reliable estoura e derruba um jogador.** Todo o desenho existe para manter o canal
reliable em ~150 B/s. Basta uma regressão para reabrir o buraco: mandar o log acumulado em vez do
delta do tick, promover `TowerFired` para reliable "porque estava piscando errado", disparar
snapshot por tick num resync em loop.
*Sinal:* assert duro em dev se `reliableBytesPerSecond > 4 KB/s` fora de janela de snapshot. O
sintoma em campo é característico: **um jogador específico cai durante a investida mais densa
enquanto os outros três seguem conectados — nunca no começo, sempre no pico.** Se você vir esse
padrão, é isto, não é o Wi-Fi dele.

**3. Silêncio.** `CustomMessageManager.InvokeNamedMessage` faz `TryGetValue` e **retorna sem log,
sem warning, sem exceção** quando não há handler. E `ValidateMessageSize` vive sob `#if DEBUG` — a
`OverflowException` educada não existe em release. Juntos produzem o pior estado possível: um
cliente conectado, vivo, renderizando nada e imprimindo nada.
*Sinal:* três detectores, porque silêncio não se detecta por ausência. (a) Watchdog por canal — 2 s
sem nada num canal que deveria estar ativo levanta falha; o padrão diagnóstico é
`Silence(Events)` **enquanto o `serverTick` continua avançando**: transporte vivo, roteamento
morto. (b) `HelloAck` obrigatório com timeout de 3 s — nunca confiar em "mandei, logo chegou".
(c) `NetLimits` impõe o teto por canal em **todos** os builds, replicando o que o NGO só faz em
DEBUG.

---

## O plano, em blocos

O critério que separa os blocos: **quanto do trabalho pode ser feito e testado sem instalar
nada.** A resposta é: quase tudo.

### Bloco A — sem pacote, sem conta

| passo | entrega | critério de saída |
|---|---|---|
| **0** ✅ | medir o pico real de monstros | 220 (feito — ver acima) |
| **1** ✅ | `MatchSimulation.Tick`, `Rng.GetState/SetState`, `NextEntityIdSeed`, extrair `HeroMotion` | testes verdes, jogo idêntico |
| **2** | `IMatchView` + `ICommandSink` + `IMatchDebug`; trocar o tipo em apresentação e UI | `MatchSimulation` só aparece em `Sim/`, `Bootstrap`, `Editor/`, `Tests/` |
| **3** | `SimSnapshot`, projetor e aplicador | round-trip preserva o visível; não vaza draft alheio |
| **4** | `DT.Net.Api`: writer, `Quantize`, codecs | `BuildTower` cabe em 7 bytes; `Motion` fatia em ≤1000 B |

O passo 2 é o de maior alavanca do projeto inteiro: é o que faz `PresentationDirector`, `GameHud`,
`BoardRenderer` e `CameraRig` rodarem **idênticos** no host e no cliente, sem um `if (isHost)` em
lugar nenhum.

### Bloco B — o jogo inteiro rodando host+cliente dentro do processo

**Passo 5.** `INetworkSession`, `LocalLoopbackSession`, `ServerMatchHost`, `CommandInbox`,
`ClientMatchProxy`, `MirroredMatch`. `Bootstrap` monta host + cliente **mesmo em partida solo**.

Não existe "caminho offline" e "caminho online". Existe um caminho, e offline é um transporte que
nunca sai do processo. **E o loopback serializa de verdade** — `Write → byte[] → Read`, nunca a
struct por referência. Loopback que faz atalho esconde todo bug de codec até a primeira partida
real.

> ⚠️ **Armadilha achada na verificação, e é bloqueante.** `PresentationDirector.ConsumeEvents`
> chama `_sim.Events.DrainInto`, e `DrainInto` **esvazia**. Se o `ServerMatchHost` também drenar,
> quem chegar primeiro no frame ganha e o outro vê zero eventos — sem erro, sem log. O host tem de
> ser o **único** a drenar `sim.Events`; a apresentação passa a drenar do `MirroredMatch`,
> inclusive offline. Se isso ficar pela metade, o jogo fica sem apresentação.

**Passo 6.** `LoopbackConditions { DelayTicks, JitterTicks, DropChance, DuplicateChance,
ReorderChance }`, respeitando a semântica de cada canal — unreliable perde, duplica e reordena;
reliable só atrasa. Contra isso: `HeroPredictor`, `MotionInterpolator`, `NetDiagnostics`,
`rosterHash`, `ResyncRequest` (com throttle de 1 snapshot por cliente a cada 2 s no host, senão uma
tempestade de resync vira auto-DoS).

**Toda a predição fica desenvolvida e depurada antes de existir um socket.** São ~80 linhas de
simulador de condições e é o item de maior retorno do documento inteiro.

### Bloco C — NGO, duas janelas na mesma máquina

**Passo 7.** Editar o manifest (as três linhas acima) e escrever `DT.Net.NGO` — o **único**
assembly que conhece a engine. `NgoNetworkSession` inteiramente sobre `CustomMessagingManager`:
zero `NetworkObject`, zero RPC. O anti-spoof vem do `senderClientId` do `HandleNamedMessageDelegate`
— mesma garantia do `RpcParams.Receive.SenderClientId`, sem exigir objeto.

> **O critério duro:** o `git diff` do passo 7 não pode tocar nenhum arquivo fora de `Net/NGO/`,
> `Bootstrap.cs` (só a escolha de sessão), os asmdefs e o manifest. Se vazou, a fronteira
> `INetworkSession` está errada — conserte antes de seguir.

**Passo 8.** Quatro jogadores, late join, queda e retomada de assento.
**Passo 9.** `multiplayer.tools`, RNSM com `rosterHashMismatches`, `reliableBytesPerSecond`,
`rollbacksPerSecond` nas quatro telas ao mesmo tempo.

O gráfico de bytes/frame deve ser **plano** no Preparo e no Balanço, com degrau no Assalto e pico
único na fronteira. Qualquer outra forma é regressão. Se os bytes forem proporcionais à contagem de
monstros **no canal reliable**, alguém regrediu para replicação por unidade.

### Bloco D — resiliência e transporte

**Passo 10.** `MatchResumeFile`. **Passo 11.** Teste em duas máquinas (mede o que uma máquina não
mede: jitter, MTU, upstream sob três clientes). **Passo 12.** Spike de 1 dia do transporte Steam.
**Passo 13.** Relay, só se o 12 falhar.

Sobre Steam: `com.community.netcode.transport.facepunch` resolve NAT de graça para sessões privadas
entre amigos e é o desenho do Lethal Company. **Mas** o `package.json` dele declara
`netcode.gameobjects: 1.0.0-pre.4` e `unity: 2019.4` — foi escrito para a linha 1.x, cuja API de
transporte mudou na 2.x. Risco real de nem compilar. Não é pacote Unity, não tem suporte, e o
branch `main` é mutável: se adotar, **fixe por commit SHA**. Timebox de um dia, encerrado no prazo
de qualquer jeito.

---

## Decisões que ainda são suas

| # | decisão | quando | consequência |
|---|---|---|---|
| D1 | lobby: host escolhe tudo, ou cada um escolhe a própria classe | passo 8 | a segunda custa uma tela e um round-trip antes do `Welcome` |
| D2 | `DebugPanel` em rede: só host, ou desabilitado no cliente | passo 7 | `Time.timeScale` só afeta a instância local; em rede vira ilusão |
| D3 | Steam P2P, Relay, ou só IP direto entre amigos | passo 12 | só Steam e Relay resolvem NAT; IP direto exige port-forward |

---

## O que não dá para verificar sem playtest, e é honesto dizer

- **Banda real.** O loopback mede payload, não framing NGO (~54 B/lote) nem cabeçalho UDP/IP
  (28 B/pacote) — juntos, ~26% do total. Os números só se confirmam no passo 11.
- **NAT, MTU, jitter, perda real.** Uma máquina não produz nada disso. `LoopbackConditions` e o
  `debugSimulator` do `UnityTransport` exercitam o código, não a internet.
- **O risco 2 é dependente de carga.** Fila reliable estourando **não aparece em dev, aparece em
  playtest** com quatro pessoas no turno 8. O assert de 4 KB/s é proxy, não prova.
- **MPPM em CI.** Não há documentação oficial de `-batchmode`. É ferramenta de mesa; o harness
  headless é o caminho de CI.
- **IL2CPP vs Mono.** Irrelevante para convergência entre máquinas (o host é a única fonte de
  posição). Mas o `MatchResumeFile` **é** um replay determinístico: um arquivo salvo por um build
  IL2CPP e recarregado por um Mono no editor pode divergir. Não testado, não bloqueia a v1.
