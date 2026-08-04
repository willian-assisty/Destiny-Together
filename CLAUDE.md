# Destiny Together

Tower-survivor cooperativo para até 4 jogadores. Uma cidade **estática** é a única barra de vida
da partida; os monstros vêm **por turno**. Inspirado em *Monsters are Coming! Rock & Road*
(Ludogram/Raw Fury, 2025), com três desvios deliberados: multiplayer, cidade parada e ondas
discretas em vez de spawn contínuo.

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
| `R` | marca Pronto (ao terceiro Pronto o Preparo trava em 15s) |
| scroll | zoom |
| `ESC` | pausa (continuar / reiniciar / menu) |
| `F1` | painel de teste |
| Balanço | `1`-`3` escolhe carta · `Q` rerrola |

**Painel de teste (`F1`)** — velocidade 0,25× a 8×, pular fase, pular turno, injetar recursos e
cartas, limpar a horda, curar a cidade. Existe porque uma partida dura ~30 min e o Kaiju só
aparece no turno 9: sem isso, o Ato 3 nunca seria calibrado. Nada ali faz parte das regras.

**Menus do editor** (`Destiny Together`):
- `Simular partida no console` — roda uma partida inteira headless em milissegundos.
- `Gerar assets de conteudo padrao` — materializa os ScriptableObjects para balancear na mão.

> `activeInputHandler` está em **Both** (legado + Input System). O menu e o HUD são IMGUI, que
> depende dos eventos legados; o gameplay usa o Input System novo. Trocar para "New only"
> quebra as telas. Mudanças nessa opção exigem reiniciar o editor.

## Arquitetura

Sete assemblies, com uma regra que sustenta todas as outras: **a simulação não conhece a engine.**

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

**Tick fixo de 20 Hz** (`MatchSimulation.FixedDelta`). O Assalto é tempo real, mas a simulação
avança em passos determinísticos — mesma seed + mesmos comandos = mesma partida.

## Convenções

- **Unidades:** comprimento em CÉLULAS, tempo em SEGUNDOS, velocidade em células/s.
  1 célula = 1 unidade Unity (`GridToWorld.CellSize`). A simulação não sabe o que é um metro.
- **`EntityId`:** Unity 6 tem um `UnityEngine.EntityId` próprio. Todo arquivo de camada Unity que
  use o nosso precisa de `using EntityId = DestinyTogether.Sim.EntityId;`.
- **Placeholder é sistema, não arte temporária.** `PlaceholderVisuals` define a gramática:
  silhueta diz o que a coisa **faz** (esfera = enxame, cápsula = explode, cubo = tanque,
  cilindro = ataca à distância); cor diz de que **lado** está. A arte final herda essa gramática.
- **Views:** toda arte mora no filho `Visual`. Presenters chamam
  `IEntityView.PlayAction(ViewActionId, duração)`, nunca `Animator.Play("...")`.
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
5. **Todo desbloqueio muda uma decisão, não um número.** Distritos dão regra (Queimadura, ignora
   armadura, não vira Escombro), nunca "+X%". Foi a crítica mais votada ao jogo de referência.

## Estado atual

Jogável solo com placeholders: 9 turnos, 3 fases por turno, 10 prédios, 5 arquétipos de monstro +
kaiju, 4 classes de herói, Ruas, Distritos, draft, Escombros, Túmulos, Urnas.

**Multiplayer ainda não existe.** A simulação já é servidor-autoritativa por construção
(comandos sobem, eventos descem, host resolve) — ligar o netcode é trocar o roteamento de
`PlayerCommand` por RPC, sem tocar em simulação, apresentação ou UI. Assentos sem humano viram
Autômatos, que colhem e depositam mas nunca constroem nem escolhem carta.

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
