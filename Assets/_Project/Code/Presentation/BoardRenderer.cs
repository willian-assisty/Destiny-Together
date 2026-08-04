using DestinyTogether.Core;
using DestinyTogether.Data;
using DestinyTogether.Sim;
using UnityEngine;

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
        private static readonly Color GroundColor = new Color(0.11f, 0.12f, 0.14f);
        private static readonly Color EmptyTile = new Color(0.18f, 0.19f, 0.22f);
        private static readonly Color TownHallTile = new Color(0.42f, 0.38f, 0.28f);
        private static readonly Color RubbleTile = new Color(0.26f, 0.13f, 0.11f);
        private static readonly Color GraveTile = new Color(0.09f, 0.09f, 0.12f);

        private readonly VisualsProfile _profile;

        public BoardRenderer(PlaceholderFactory factory, Transform root, BoardGrid grid,
                             IContentDatabase content, VisualsProfile profile = null)
        {
            _factory = factory;
            _root = root;
            _grid = grid;
            _profile = profile;

            _tileRenderers = new Renderer[grid.Size * grid.Size];
            _lastState = new CellState[grid.Size * grid.Size];

            BuildGround(content);
            BuildTiles();
            BuildLanePillars(content);
            BuildHighlight();
            Refresh();
        }

        private void BuildGround(IContentDatabase content)
        {
            float radius = content.Arena.OutskirtsRadius;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            Object.Destroy(ground.GetComponent<Collider>());
            ground.transform.SetParent(_root, false);
            ground.transform.localScale = new Vector3(radius * 2.6f, 0.2f, radius * 2.6f);
            ground.transform.position = GridToWorld.ToWorld(_grid.Center, -0.15f);

            var groundRenderer = ground.GetComponent<Renderer>();
            if (_profile != null && _profile.GroundMaterial != null)
            {
                groundRenderer.sharedMaterial = _profile.GroundMaterial;

                // Escurece e repete a textura por property block, sem editar o material do pack:
                // esticada uma única vez por 47 unidades ela vira uma mancha lisa cor de areia,
                // que foi parte do "não vejo nada além de uma pedra enorme".
                float tiling = radius * 2.6f / 4f;
                groundRenderer.GetPropertyBlock(_block);
                _block.SetColor(ShaderIds.BaseColor, _profile.GroundTint);
                _block.SetVector(ShaderIds.BaseMapST, new Vector4(tiling, tiling, 0f, 0f));
                groundRenderer.SetPropertyBlock(_block);
            }
            else
            {
                groundRenderer.sharedMaterial = _factory.GetMaterial(GroundColor);
            }
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
                case CellState.Predio: return EmptyTile * 0.7f;
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

                float height = f.IncomingCount <= 0 ? 0.4f : Mathf.Clamp(0.8f + f.IncomingCount * 0.18f, 0.8f, 6f);
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
            pillar.localScale = new Vector3(2.6f, pillar.localScale.y * 1.15f, 2.6f);
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
