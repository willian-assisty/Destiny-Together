# GLB → FBX

O Unity **não importa `.glb`** — nem nativamente, nem sem instalar um pacote de glTF. E mesmo com
o pacote, todo o pipeline de personagem daqui é `ModelImporter` (avatar, clipes, import settings),
coisa que importador de glTF não expõe. Adotar GLB significaria manter **duas** pipelines para a
mesma coisa; converter na entrada mantém uma.

```powershell
python .\Tools\GlbToFbx\glb2fbx.py entrada.glb Assets\_Project\Art\Final\Characters\Nome Nome Run
```

Saem dois arquivos, na convenção que `ArtSetup`/`CharacterSetup` já esperam:

| | |
|---|---|
| `Nome.fbx` | malha + esqueleto + skin, sem animação |
| `Nome_Run.fbx` | **só** o esqueleto + as curvas |

O clipe não leva a malha porque liga por **caminho de transform** — basta a hierarquia bater. Levar
os vértices junto custaria 12 MB por clipe sem mudar um quadro (310 KB contra 9,5 MB, medido).

Depois: `Destiny Together > Aplicar arte importada`.

## O que o conversor garante

**Euler, não quaternion.** FBX guarda rotação em ângulos de Euler e glTF em quaternion. A conversão
usa a ordem XYZ do FBX (`M = Rz·Ry·Rx`) e é conferida por ida-e-volta contra a matriz original —
erro medido `7,8e-16`. Cada chave é desenrolada contra a anterior: sem isso uma curva que passa de
179° para −179° é lida como um giro de 358° no sentido errado, e o membro roda ao contrário num
único quadro.

**Uma raiz só.** A malha entra **sob** a armadura, como no GLB. Não é arrumação: com a malha na
raiz o FBX teria duas raízes e o do clipe uma, e o Unity ancora os caminhos de curva de forma
diferente nos dois casos — quando há uma raiz só, ela vira o próprio objeto raiz. O sintoma era um
clipe com caminhos `Bone_000` contra uma hierarquia `UniRigArmature/Bone_000`: 47 curvas, 47 sem
correspondência, **nenhum erro no console**.

**Primeira linha.** Um FBX ASCII é reconhecido pelo comentário `; FBX 7.4.0 project file`. Sem ele
o import falha com `Couldn't read file` e nenhuma pista de qual campo estaria errado.

**Quatro influências por vértice.** O GLB traz até 12 (`JOINTS_0/1/2`); o Unity usa 4
(`maxBonesPerVertex`). Podar aqui deixa o arquivo três vezes menor **e** torna o resultado
determinístico — quem escolhe as quatro somos nós, não o importador.

## Como conferir que deu certo

O conversor imprime um **gabarito**: a posição de alguns ossos no espaço do modelo, calculada
direto do GLB. `Destiny Together > Diagnosticar personagens` imprime os mesmos números depois do
import.

É a única prova de que a conversão saiu certa. Um erro de ordem de rotação ainda produz um clipe
que "anima", com ossos girando e caminhos casando — só que na pose errada. Sem números dos dois
lados, isso passa.

Medido no Arqueiro, Y e Z batem exatamente e **X vem negado** — é a conversão destro→canhoto que o
Unity aplica a todo FBX, igual para malha e clipe, e portanto invisível.

## `fbxsplit.py` — separar peças grudadas

```powershell
python .\Tools\GlbToFbx\fbxsplit.py entrada.fbx PastaDeSaida
```

Separa as **conchas** (componentes conexas) de uma malha única em `Mago.obj` + `Cristais/*.obj`.

Existe porque gerador de malha entrega tudo grudado, e peça grudada não orbita: ela vira parte da
silhueta. O mago veio assim — 11 conchas, sendo 1 corpo de 27.734 vértices, 3 cristais de 76 a 124,
e 7 cacos degenerados (um deles com um vértice só) que o filtro de 40 vértices descarta.

Cada peça pequena sai **recentrada na própria origem**: o `CrystalOrbit` escreve a posição de mundo
delas, e uma peça cuja geometria está a meio metro do próprio pivô orbitaria em volta de um ponto
errado. O script imprime o centro original de cada uma, que é como se descobre o raio que o artista
tinha em mente.

Cuidado que custou uma tentativa: `Normals` e `UV` neste formato são `IndexToDirect`. O canto do
polígono aponta para uma entrada da tabela através de `NormalsIndex`/`UVIndex` — ler a tabela
direto pelo índice do canto estoura o vetor.

## `gerar_run_arqueiro.py`

O gerador do ciclo de corrida do Arqueiro (autoria do projeto, não deste conversor). Depende de
`numpy`, `scipy` e `pygltflib`, e é específico do esqueleto `UniRigArmature` (`Bone_000`…`Bone_055`).
Fica aqui para não se perder: ele é a fonte da animação, o `.glb` é só o resultado.
