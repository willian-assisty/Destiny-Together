using System.Collections.Generic;
using DestinyTogether.Core;
using DestinyTogether.Data;
using DestinyTogether.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace DestinyTogether.Presentation
{
    /// <summary>
    /// Desenha o tabuleiro: chao, tiles por Quadrante, Prefeitura, Escombros, Tumulos, o anel
    /// dos Arredores e os oito pilares de Faixa.
    ///
    /// Os pilares sao a Bussola de Ameaca em forma diegetica: durante o Preparo eles ficam com a
    /// cor do Prognostico daquela Faixa (verde SEGURA / ambar VAZA / vermelho ARROMBA) e crescem
    /// com o volume que vem por ali. Com arte placeholder, cor e altura SAO a telegrafia — nao
    /// um enfeite que a arte final vai substituir.
    /// </summary>
    public sealed class BoardRenderer
    {
        private readonly PlaceholderFactory _factory;
        private readonly Transform _root;
        private readonly BoardGrid _grid;

        private readonly Renderer[] _tileRenderers;
        private readonly CellState[] _lastState;
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();

        private readonly Transform[] _lanePillars = new Transform[LaneGeometry.LaneCount];
        private readonly Renderer[] _laneRenderers = new Renderer[LaneGeometry.LaneCount];

        private Transform _highlight;
        private Renderer _highlightRenderer;

        // Paleta escura. Os tiles precisam ser LEGÍVEIS sem competir com a arte: eles informam
        // (onde dá para construir, de quem é o Quadrante), não decoram.
        //
        // Eles ficam ABAIXO da faixa de ambiente (35-55%) de propósito, e isso não é uma exceção
        // à regra de cor — é a regra. Tile não é `env.base`: ele diz onde dá para construir, onde
        // um prédio caiu e onde um herói morreu, e o token manda todo elemento de GAMEPLAY viver
        // fora da faixa. Com o chão subindo para ~41%, um tabuleiro dentro da faixa ficaria a 2
        // níveis de 255 do campo em volta e a fronteira vila/mundo — "a vila é construída, o lado
        // de fora é bruto" — deixaria de existir. Aqui ela vale ~13× em luminância.
        //
        // Entre os estados a razão é sempre >= 2×, senão Escombro e Túmulo, que são cicatrizes
        // PERMANENTES, virariam o mesmo quadrado cinza com matiz diferente.
        private static readonly Color EmptyTile = new Color(0.306f, 0.322f, 0.376f);
        private static readonly Color TownHallTile = new Color(0.486f, 0.451f, 0.333f);
        private static readonly Color RubbleTile = new Color(0.361f, 0.227f, 0.200f);
        private static readonly Color GraveTile = new Color(0.173f, 0.173f, 0.227f);

        /// <summary>
        /// Tile sob um prédio. Constante explícita em vez de <c>EmptyTile * 0,7</c>: multiplicar em
        /// sRGB não é mensurável no sítio, e a regra de luminância precisa ser conferível lendo.
        /// </summary>
        private static readonly Color BuildingTile = new Color(0.188f, 0.204f, 0.259f);

        private readonly VisualsProfile _profile;

        private readonly int _seed;
        private readonly Vec2 _cityCenter;

        public BoardRenderer(PlaceholderFactory factory, Transform root, BoardGrid grid,
                             IContentDatabase content, VisualsProfile profile = null,
                             int seed = 0, Vec2 cityCenter = default)
        {
            _factory = factory;
            _root = root;
            _grid = grid;
            _profile = profile;
            _seed = seed;
            _cityCenter = cityCenter;

            _tileRenderers = new Renderer[grid.Size * grid.Size];
            _lastState = new CellState[grid.Size * grid.Size];

            BuildGround(content);
            BuildTiles();
            BuildLanePillars(content);
            BuildHighlight();
            Refresh();
        }

        /// <summary>
        /// Lado do horizonte, em unidades. É a laje lisa que preenche o fundo além do relevo —
        /// ela vive dentro da névoa e não precisa de detalhe nenhum.
        /// </summary>
        private const float HorizonSize = 900f;

        /// <summary>
        /// Lado de uma faceta. Vem de <see cref="GroundShape"/> porque a oitava curta do relevo tem
        /// exatamente este período — é o casamento entre os dois que produz o facetado.
        /// </summary>
        private const float FacetSize = GroundShape.FacetSize;

        /// <summary>Facetas por lado do tapete. 72 × 3,5 = 252 unidades — a borda cai fora da névoa diurna.</summary>
        private const int FacetsPerSide = 72;

        /// <summary>
        /// Altura máxima do relevo, em unidades.
        ///
        /// Medido: 0,9 dá inclinação média de 5,7° e máxima de 17°, com o terreno variando ±0,77 —
        /// meia altura de herói. É onde a faceta se lê sem que o chão vire duna: mais que isso e o
        /// herói sobe e desce de um jeito que compete com a leitura de ONDE ele está, que é a única
        /// coisa que o jogador precisa ler o tempo todo.
        ///
        /// É o único botão do relevo. 0,6 dá 3,8°/11,7° (mais discreto); 1,2 dá 7,6°/22,5°.
        /// </summary>
        public const float Relief = 0.9f;

        /// <summary>
        /// Verde dessaturado #97B48C — a cor de tudo que é chão, e o fallback de RegionGroundTints.
        ///
        /// Era #4F6B45, que media 12,6% de luminância linear. O token de ambiente pede a faixa de
        /// 35–55%, e a razão é que todo elemento de gameplay vive FORA dela: com o chão a 12,6% os
        /// tokens escuros (horda a 12%, elite 22%, chefe 25%, XP 28%) caíam no mesmo valor do chão
        /// em que pisam. A 41,1% eles voltam a ficar abaixo do fundo, que é o que a regra compra.
        ///
        /// A matiz não mudou (104°): o verde continua verde, mas sobe 3,3× em luminância e cai de
        /// 35% para 22% de saturação — a faixa pede dessaturado, e croma alto no chão disputaria
        /// com a cor de gameplay, que é a única coisa que tem licença para ser saturada.
        /// </summary>
        private static readonly Color GrassColor = new Color(0.592f, 0.706f, 0.549f);

        /// <summary>
        /// Quanto a malha do chão afunda em relação à altura que as entidades usam.
        ///
        /// Os tiles do tabuleiro têm 0,06 de espessura centrados em zero, e o realce de célula fica
        /// em 0,06: com o chão exatamente em zero eles disputariam profundidade com ele. Afundar
        /// 4 cm põe tudo isso claramente por cima, e 4 cm é invisível debaixo de um herói de 1,8.
        /// </summary>
        private const float GroundDrop = 0.04f;

        /// <summary>
        /// Topo da laje do horizonte. Abaixo do ponto mais fundo que o relevo alcança, senão ela
        /// atravessaria o tapete por baixo e apareceria como um plano flutuando dentro dos vales.
        /// </summary>
        private const float HorizonTop = -0.95f;

        private Transform _horizon;
        /// <summary>Quantas regiões existem. Uma submalha e um material por região.</summary>
        private const int RegionCount = 4;

        private Mesh _groundMesh;
        private Vector3[] _groundVertices;
        private Vector3[] _groundNormals;
        private List<int>[] _regionIndices;
        private Material[] _regionMaterials;
        private Vector2 _groundOrigin = new Vector2(float.MaxValue, float.MaxValue);

        private void BuildGround(IContentDatabase content)
        {
            // Um material por região, na ordem de RegionKind.
            _regionMaterials = new Material[RegionCount];
            for (int i = 0; i < RegionCount; i++)
                _regionMaterials[i] = _factory.GetMaterial(RegionTint(i));

            // A laje do horizonte usa a cor da Mata: ela vive dentro da névoa, e escolher a cor da
            // região sob os pés faria o horizonte inteiro piscar ao cruzar uma fronteira.
            var horizon = GameObject.CreatePrimitive(PrimitiveType.Cube);
            horizon.name = "Horizonte";
            Object.Destroy(horizon.GetComponent<Collider>());
            horizon.transform.SetParent(_root, false);
            horizon.transform.localScale = new Vector3(HorizonSize, 0.2f, HorizonSize);
            horizon.transform.position = GridToWorld.ToFlatWorld(_grid.Center, HorizonTop - 0.1f);
            horizon.GetComponent<Renderer>().sharedMaterial = _regionMaterials[0];
            _horizon = horizon.transform;

            // O tapete facetado. Vive em coordenadas de MUNDO com o transform na origem: assim a
            // malha é a própria verdade sobre onde o relevo está, sem uma segunda conta de offset
            // que possa discordar de GroundShape.
            var carpet = new GameObject("Chao");
            carpet.transform.SetParent(_root, false);

            _groundMesh = new Mesh { name = "ChaoFacetado", indexFormat = IndexFormat.UInt32 };
            _groundMesh.MarkDynamic();
            _groundMesh.subMeshCount = RegionCount;

            carpet.AddComponent<MeshFilter>().sharedMesh = _groundMesh;
            var renderer = carpet.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = _regionMaterials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;   // chão não projeta sobre si mesmo

            BuildFacetTopology();
            TickGround(_grid.Center);
        }

        private Color RegionTint(int index)
        {
            var tints = _profile != null ? _profile.RegionGroundTints : null;
            if (tints != null && index < tints.Length && tints[index].a > 0.01f) return tints[index];
            return index == 0 ? GrassColor : GrassColor * 0.9f;
        }

        /// <summary>
        /// Monta índices e UVs uma vez só. Só as POSIÇÕES mudam quando o chão acompanha o foco.
        ///
        /// Seis vértices por faceta, sem nenhum compartilhado: é o que dá a cada triângulo a
        /// própria normal. Vértice compartilhado produziria a normal média dos vizinhos — que é
        /// exatamente a superfície suave que a referência não é.
        /// </summary>
        private void BuildFacetTopology()
        {
            int quads = FacetsPerSide * FacetsPerSide;
            _groundVertices = new Vector3[quads * 6];
            _groundNormals = new Vector3[quads * 6];

            _regionIndices = new List<int>[RegionCount];
            for (int i = 0; i < RegionCount; i++) _regionIndices[i] = new List<int>(quads * 6 / 2);

            _groundMesh.Clear();
            _groundMesh.subMeshCount = RegionCount;
            _groundMesh.vertices = _groundVertices;
        }

        /// <summary>
        /// Faz o chão acompanhar quem está olhando.
        ///
        /// O mundo não acaba, mas o tapete tem 224 unidades. A posição é travada em múltiplos do
        /// tamanho da faceta, então os vértices caem SEMPRE na mesma grade do mundo — e como a
        /// altura vem de <see cref="GroundShape"/>, que é função da posição no mundo, reconstruir
        /// o tapete redesenha exatamente a mesma superfície. O terreno fica parado enquanto a
        /// malha corre atrás da câmera.
        /// </summary>
        public void TickGround(Vec2 focus)
        {
            if (_groundMesh == null) return;

            float half = FacetsPerSide * FacetSize * 0.5f;
            float x0 = Mathf.Round(focus.X / FacetSize) * FacetSize - half;
            float z0 = Mathf.Round(focus.Y / FacetSize) * FacetSize - half;

            if (_horizon != null)
                _horizon.position = GridToWorld.ToFlatWorld(
                    new Vec2(Mathf.Round(focus.X / FacetSize) * FacetSize,
                             Mathf.Round(focus.Y / FacetSize) * FacetSize), HorizonTop - 0.1f);

            if (Mathf.Approximately(x0, _groundOrigin.x) && Mathf.Approximately(z0, _groundOrigin.y)) return;
            _groundOrigin = new Vector2(x0, z0);

            RebuildFacets(x0, z0);
        }

        /// <summary>
        /// Reescreve posições e normais do tapete.
        ///
        /// A altura de um vértice vem SÓ de <see cref="GroundShape"/>, sem nenhuma correção que
        /// dependa de onde o tapete está. Isso não é preciosismo: uma atenuação de borda — que
        /// seria a maneira óbvia de esconder a emenda com o horizonte — faria a altura de um mesmo
        /// ponto do mundo mudar conforme o jogador anda, e as entidades (que leem a altura direto,
        /// sem atenuação) passariam a flutuar perto da borda. Malha e entidades têm de concordar
        /// sobre onde o chão está, sempre.
        ///
        /// A emenda com a laje do horizonte fica então a 126 unidades do foco — além da névoa
        /// diurna — e o degrau máximo lá é de 0,6 unidade visto quase de perfil.
        /// </summary>
        private void RebuildFacets(float x0, float z0)
        {
            var ground = GridToWorld.Ground;
            for (int i = 0; i < RegionCount; i++) _regionIndices[i].Clear();

            int v = 0;

            for (int gz = 0; gz < FacetsPerSide; gz++)
            {
                float za = z0 + gz * FacetSize;
                float zb = za + FacetSize;

                for (int gx = 0; gx < FacetsPerSide; gx++)
                {
                    float xa = x0 + gx * FacetSize;
                    float xb = xa + FacetSize;

                    var p00 = new Vector3(xa, ground.HeightAt(new Vec2(xa, za)) - GroundDrop, za);
                    var p10 = new Vector3(xb, ground.HeightAt(new Vec2(xb, za)) - GroundDrop, za);
                    var p01 = new Vector3(xa, ground.HeightAt(new Vec2(xa, zb)) - GroundDrop, zb);
                    var p11 = new Vector3(xb, ground.HeightAt(new Vec2(xb, zb)) - GroundDrop, zb);

                    // A região é resolvida por FACETA, e é isso que dá a fronteira a leitura certa:
                    // ela corre pelas arestas dos triângulos, em degraus, em vez de ser um degradê.
                    // Chão low-poly com transição borrada briga com a própria gramática.
                    var region = WorldLattice.RegionAt(_seed, new Vec2(xa + FacetSize * 0.5f,
                                                                      za + FacetSize * 0.5f),
                                                       _cityCenter);
                    var bucket = _regionIndices[(int)region];

                    // A diagonal alterna em xadrez. Com a diagonal sempre no mesmo sentido o chão
                    // ganha um listrado regular que denuncia a grade na hora.
                    if (((gx + gz) & 1) == 0)
                    {
                        v = Facet(v, p00, p01, p11, bucket);
                        v = Facet(v, p00, p11, p10, bucket);
                    }
                    else
                    {
                        v = Facet(v, p01, p11, p10, bucket);
                        v = Facet(v, p01, p10, p00, bucket);
                    }
                }
            }

            _groundMesh.vertices = _groundVertices;
            _groundMesh.normals = _groundNormals;
            for (int i = 0; i < RegionCount; i++)
                _groundMesh.SetTriangles(_regionIndices[i], i, calculateBounds: false);

            // Bounds à mão: RecalculateNormals e RecalculateBounds varreriam 31 mil vértices duas
            // vezes por segundo, e as duas respostas já são conhecidas aqui.
            float side = FacetsPerSide * FacetSize;
            _groundMesh.bounds = new Bounds(new Vector3(x0 + side * 0.5f, 0f, z0 + side * 0.5f),
                                            new Vector3(side, Relief * 4f, side));
        }

        /// <summary>Um triângulo com a MESMA normal nos três vértices — é isto que faceta.</summary>
        private int Facet(int v, Vector3 a, Vector3 b, Vector3 c, List<int> bucket)
        {
            var normal = Vector3.Cross(b - a, c - a).normalized;

            bucket.Add(v); _groundVertices[v] = a; _groundNormals[v++] = normal;
            bucket.Add(v); _groundVertices[v] = b; _groundNormals[v++] = normal;
            bucket.Add(v); _groundVertices[v] = c; _groundNormals[v++] = normal;
            return v;
        }

        private void BuildTiles()
        {
            var container = new GameObject("Tiles").transform;
            container.SetParent(_root, false);

            for (int y = 0; y < _grid.Size; y++)
            {
                for (int x = 0; x < _grid.Size; x++)
                {
                    var cell = new GridCoord(x, y);
                    var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tile.name = $"Tile_{x}_{y}";
                    Object.Destroy(tile.GetComponent<Collider>());
                    tile.transform.SetParent(container, false);
                    tile.transform.localScale = new Vector3(0.94f, 0.06f, 0.94f);
                    tile.transform.position = GridToWorld.ToWorld(cell, 0f);
                    _tileRenderers[y * _grid.Size + x] = tile.GetComponent<Renderer>();
                    _tileRenderers[y * _grid.Size + x].sharedMaterial = _factory.GetMaterial(EmptyTile);
                }
            }
        }

        private void BuildLanePillars(IContentDatabase content)
        {
            var container = new GameObject("Faixas").transform;
            container.SetParent(_root, false);

            for (int i = 0; i < LaneGeometry.LaneCount; i++)
            {
                var lane = (Lane)i;
                var pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pillar.name = $"Faixa_{LaneGeometry.ShortName(lane)}";
                Object.Destroy(pillar.GetComponent<Collider>());
                pillar.transform.SetParent(container, false);

                // Os pilares NÃO ficam no anel de spawn: com névoa densa eles sumiriam justamente
                // quando mais importam. Eles marcam a DIREÇÃO da ameaça, não o ponto exato de
                // nascimento, então vivem a meio caminho — dentro do alcance de visão.
                var pos = _grid.Center + LaneGeometry.DirectionOf(lane) * (content.Arena.OutskirtsRadius * 0.55f);
                pillar.transform.position = GridToWorld.ToWorld(pos, 0.5f);
                pillar.transform.localScale = new Vector3(1.6f, 1f, 1.6f);

                _lanePillars[i] = pillar.transform;
                _laneRenderers[i] = pillar.GetComponent<Renderer>();
                _laneRenderers[i].sharedMaterial = _factory.GetMaterial(Color.gray);
            }
        }

        private void BuildHighlight()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Highlight";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_root, false);
            go.transform.localScale = new Vector3(1f, 0.12f, 1f);
            _highlight = go.transform;
            _highlightRenderer = go.GetComponent<Renderer>();
            _highlightRenderer.sharedMaterial = _factory.GetMaterial(Color.white);
            go.SetActive(false);
        }

        /// <summary>Recolore apenas os tiles que mudaram de estado.</summary>
        public void Refresh()
        {
            for (int y = 0; y < _grid.Size; y++)
            {
                for (int x = 0; x < _grid.Size; x++)
                {
                    int index = y * _grid.Size + x;
                    var cell = new GridCoord(x, y);
                    var state = _grid.Get(cell);
                    if (state == _lastState[index] && _tileRenderers[index] != null) continue;

                    _lastState[index] = state;
                    Tint(_tileRenderers[index], ColorFor(cell, state));
                }
            }
        }

        private Color ColorFor(GridCoord cell, CellState state)
        {
            switch (state)
            {
                case CellState.Prefeitura: return TownHallTile;
                case CellState.Escombro: return RubbleTile;
                case CellState.Tumulo: return GraveTile;
                case CellState.Predio: return BuildingTile;
                default:
                    // Tile vazio recebe um tom do Quadrante: soberania visivel sem UI nenhuma.
                    var q = _grid.QuadrantOf(cell);
                    var tint = PlaceholderVisuals.PlayerColor((int)q);
                    return Color.Lerp(EmptyTile, tint, 0.14f);
            }
        }

        /// <summary>Realce de celula durante o arrasto: verde valido, vermelho invalido.</summary>
        public void ShowHighlight(GridCoord cell, bool valid)
        {
            _highlight.gameObject.SetActive(true);
            _highlight.position = GridToWorld.ToWorld(cell, 0.06f);
            Tint(_highlightRenderer, valid ? new Color(0.35f, 0.95f, 0.45f) : new Color(0.95f, 0.30f, 0.30f));
        }

        public void HideHighlight() => _highlight.gameObject.SetActive(false);

        /// <summary>
        /// Atualiza os pilares com o Prognostico. Altura = volume que vem, cor = risco.
        /// </summary>
        public void UpdateLaneForecast(LaneForecast[] forecasts)
        {
            if (forecasts == null) return;

            for (int i = 0; i < LaneGeometry.LaneCount && i < forecasts.Length; i++)
            {
                var f = forecasts[i];
                var pillar = _lanePillars[i];
                if (pillar == null) continue;

                // Teto 4,0 e nao 6,0: com pitch 50° um corpo de altura H esconde 0,84·H celulas de
                // chao ATRAS de si, e o que esta atras do pilar e exatamente o corredor por onde a
                // horda daquela Faixa entra. A 6,0 a telegrafia passava a ocluir a ameaca que
                // telegrafa, e ocupava 57% da altura da tela no zoom minimo. A 4,0 esconde 3,4
                // celulas e ocupa 27%, continuando acima de todo comum (1,6) e de todo elite (2,4).
                //
                // Se o teto saturar cedo demais, alargue X/Z em vez de subir Y: largura nao oclui.
                float height = f.IncomingCount <= 0 ? 1.0f : Mathf.Clamp(2.0f + f.IncomingCount * 0.12f, 2.0f, 4f);
                pillar.localScale = new Vector3(1.2f, height, 1.2f);
                pillar.position = new Vector3(pillar.position.x, height * 0.5f, pillar.position.z);

                var color = f.IncomingCount <= 0
                    ? new Color(0.25f, 0.26f, 0.30f)
                    : PlaceholderVisuals.BandColor(f.Band);
                Tint(_laneRenderers[i], color);
            }
        }

        /// <summary>Pulso na Faixa 1.5s antes do spawn — a "Buzina". Com placeholder, cor e som SAO a telegrafia.</summary>
        public void PulseLane(Lane lane)
        {
            var pillar = _lanePillars[(int)lane];
            if (pillar == null) return;
            // O 1,15 e CUMULATIVO e nada o desfaz: TickPillars devolve X e Z ao repouso, nunca Y,
            // e durante a Noite o Prognostico nao e reavaliado (Bootstrap so o refaz na virada de
            // fase e no Dia). Sem o teto, cada Buzina da noite multiplicava a altura de novo e o
            // pilar crescia sem parar. O teto e o mesmo da altura de Prognostico.
            pillar.localScale = new Vector3(2.6f, Mathf.Min(pillar.localScale.y * 1.15f, 4f), 2.6f);
        }

        public void TickPillars(float dt)
        {
            for (int i = 0; i < _lanePillars.Length; i++)
            {
                var p = _lanePillars[i];
                if (p == null) continue;
                var s = p.localScale;
                s.x = Mathf.Lerp(s.x, 1.6f, 1f - Mathf.Exp(-6f * dt));
                s.z = Mathf.Lerp(s.z, 1.6f, 1f - Mathf.Exp(-6f * dt));
                p.localScale = s;
            }
        }

        private void Tint(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            renderer.GetPropertyBlock(_block);
            _block.SetColor(ShaderIds.BaseColor, color);
            renderer.SetPropertyBlock(_block);
        }
    }
}
