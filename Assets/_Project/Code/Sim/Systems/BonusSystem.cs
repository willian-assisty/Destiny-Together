using System.Collections.Generic;
using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    /// <summary>
    /// Recalcula Ruas, Distritos e buffs de adjacencia. Roda inteiro sempre que o tabuleiro muda
    /// (construir, fundir, destruir) — no maximo 121 celulas, entao a forca bruta e correta aqui.
    ///
    /// Distritos dao REGRA, nunca numero: e a licao mais cara aprendida com o jogo de referencia,
    /// onde desbloqueio que so soma % faz o jogador achar que perdeu por falta de grind.
    /// Distritos e Ruas atravessam fronteira de Quadrante de proposito — sao o unico sistema
    /// do jogo que obriga dois jogadores a negociar.
    /// </summary>
    public static class BonusSystem
    {
        public static void Recalculate(MatchState state, IContentDatabase content)
        {
            var towers = state.Towers;

            for (int i = 0; i < towers.Count; i++)
            {
                towers[i].FireRateMultiplier = 1f;
                towers[i].BonusRange = 0f;
                towers[i].InDistrict = false;
                towers[i].DistrictTag = BuildingTag.Nenhuma;
            }

            ApplyAdjacencyBuffs(state, content);
            ApplyStreets(state);
            ApplyDistricts(state, content);
        }

        /// <summary>Oficina e Posto de Vigia: buff nos vizinhos ortogonais.</summary>
        private static void ApplyAdjacencyBuffs(MatchState state, IContentDatabase content)
        {
            for (int i = 0; i < state.Towers.Count; i++)
            {
                var source = state.Towers[i];
                var spec = content.GetTower(source.Def);
                if (spec == null) continue;
                if (spec.AdjacentFireRateBonus <= 0f && spec.AdjacentRangeBonus <= 0f) continue;

                for (int n = 0; n < GridCoord.Orthogonal.Length; n++)
                {
                    var neighbourCell = source.Cell + GridCoord.Orthogonal[n];
                    var neighbour = state.GetTower(state.Grid.OccupantOf(neighbourCell));
                    if (neighbour == null) continue;
                    neighbour.FireRateMultiplier += spec.AdjacentFireRateBonus;
                    neighbour.BonusRange += spec.AdjacentRangeBonus;
                }
            }
        }

        /// <summary>
        /// RUA: linha reta (horizontal ou vertical) de 4+ predios contiguos da +1 de alcance
        /// a todas as torres da linha; 6+ da +2. Recompensa forma, nao quantidade.
        /// </summary>
        private static void ApplyStreets(MatchState state)
        {
            var grid = state.Grid;
            var run = new List<TowerState>(16);

            for (int y = 0; y < grid.Size; y++)
            {
                run.Clear();
                for (int x = 0; x <= grid.Size; x++)
                {
                    TowerState t = x < grid.Size ? state.GetTower(grid.OccupantOf(new GridCoord(x, y))) : null;
                    if (t != null) run.Add(t);
                    else { FlushStreet(run); run.Clear(); }
                }
            }

            for (int x = 0; x < grid.Size; x++)
            {
                run.Clear();
                for (int y = 0; y <= grid.Size; y++)
                {
                    TowerState t = y < grid.Size ? state.GetTower(grid.OccupantOf(new GridCoord(x, y))) : null;
                    if (t != null) run.Add(t);
                    else { FlushStreet(run); run.Clear(); }
                }
            }
        }

        private static void FlushStreet(List<TowerState> run)
        {
            if (run.Count < 4) return;
            float bonus = run.Count >= 6 ? 2f : 1f;
            for (int i = 0; i < run.Count; i++)
                run[i].BonusRange += bonus;
        }

        /// <summary>
        /// DISTRITO: 4+ predios ortogonalmente conectados com a MESMA tag.
        /// Flood fill por tag; cada componente com 4+ membros vira distrito.
        /// </summary>
        private static void ApplyDistricts(MatchState state, IContentDatabase content)
        {
            var visited = new HashSet<EntityId>();
            var component = new List<TowerState>(32);
            var stack = new Stack<TowerState>();

            for (int i = 0; i < state.Towers.Count; i++)
            {
                var seed = state.Towers[i];
                if (visited.Contains(seed.Id)) continue;

                var spec = content.GetTower(seed.Def);
                var tag = spec?.Tag ?? BuildingTag.Nenhuma;
                if (tag == BuildingTag.Nenhuma) { visited.Add(seed.Id); continue; }

                component.Clear();
                stack.Clear();
                stack.Push(seed);
                visited.Add(seed.Id);

                while (stack.Count > 0)
                {
                    var current = stack.Pop();
                    component.Add(current);

                    for (int n = 0; n < GridCoord.Orthogonal.Length; n++)
                    {
                        var neighbour = state.GetTower(state.Grid.OccupantOf(current.Cell + GridCoord.Orthogonal[n]));
                        if (neighbour == null || visited.Contains(neighbour.Id)) continue;
                        var nSpec = content.GetTower(neighbour.Def);
                        if ((nSpec?.Tag ?? BuildingTag.Nenhuma) != tag) continue;
                        visited.Add(neighbour.Id);
                        stack.Push(neighbour);
                    }
                }

                if (component.Count < 4) continue;

                for (int c = 0; c < component.Count; c++)
                {
                    component[c].InDistrict = true;
                    component[c].DistrictTag = tag;
                    // Oficio e o unico distrito com efeito economico direto; os demais sao lidos
                    // no momento do disparo (Igneo/Gelo/Ferro) ou da morte (Pedra).
                    if (tag == BuildingTag.Oficio)
                        component[c].FireRateMultiplier += 0.15f;
                }
            }
        }

        public static string DistrictRuleText(BuildingTag tag)
        {
            switch (tag)
            {
                case BuildingTag.Igneo: return "Torres aplicam Queimadura";
                case BuildingTag.Gelo: return "Inimigos saem lentos por 4s";
                case BuildingTag.Oficio: return "Producao dobrada e auto-reparo";
                case BuildingTag.Ferro: return "Ignora 50% da armadura de Brutos";
                case BuildingTag.Pedra: return "Predios nao viram Escombro";
                default: return "";
            }
        }
    }
}
