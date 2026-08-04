using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    /// <summary>
    /// Erguer, fundir, reparar e limpar Escombro. Erguer custa ZERO recurso: o XP ja pagou quando
    /// a cidade subiu de nivel. Isso mantem as quatro moedas ortogonais — nada compra construcao.
    /// </summary>
    public static class BuildSystem
    {
        public static bool TryBuild(MatchState state, IContentDatabase content, PlayerState player,
                                    DefId def, GridCoord cell, SimEventLog log)
        {
            var validation = CommandValidator.ValidateBuild(state, content, player, def, cell);
            if (!validation.IsValid) return false;

            var spec = content.GetTower(def);
            var existing = state.GetTower(state.Grid.OccupantOf(cell));

            if (existing != null && existing.Def == def)
            {
                // Duplicata sobre duplicata funde: o unico upgrade de torre dentro da partida.
                existing.Tier++;
                existing.MaxHealth = spec.MaxHealth * (1f + 0.5f * (existing.Tier - 1));
                existing.Health = existing.MaxHealth;
                player.Hand.Remove(def);
                log.Emit(SimEventType.TowerMerged, existing.Id, existing.Tier, existing.Position, def,
                         player: player.Id, cell: cell);
            }
            else
            {
                var tower = new TowerState
                {
                    Id = state.NewEntityId(),
                    Def = def,
                    Owner = player.Id,
                    Cell = cell,
                    Tier = 1,
                    MaxHealth = spec.MaxHealth,
                    Health = spec.MaxHealth
                };

                state.RegisterTower(tower);
                state.Grid.Set(cell, CellState.Predio, tower.Id);
                player.Hand.Remove(def);

                state.SiloCapacity = content.Rules.SiloBaseCapacity + TotalSiloBonus(state, content);
                log.Emit(SimEventType.TowerBuilt, tower.Id, 0f, tower.Position, def,
                         player: player.Id, cell: cell);
            }

            BonusSystem.Recalculate(state, content);
            return true;
        }

        private static float TotalSiloBonus(MatchState state, IContentDatabase content)
        {
            float bonus = 0f;
            for (int i = 0; i < state.Towers.Count; i++)
            {
                var spec = content.GetTower(state.Towers[i].Def);
                if (spec != null) bonus += spec.SiloCapacityBonus;
            }
            return bonus;
        }

        /// <summary>Pedra e a unica cura do jogo — e cura a cidade, nunca o heroi.</summary>
        public static bool TryRepair(MatchState state, IContentDatabase content, GridCoord cell,
                                     float stoneBudget, SimEventLog log)
        {
            if (state.Stone < stoneBudget || stoneBudget <= 0f) return false;

            var cellState = state.Grid.Get(cell);

            if (cellState == CellState.Prefeitura)
            {
                if (state.TownHallHealth >= state.TownHallMaxHealth) return false;
                float healed = System.Math.Min(stoneBudget * 4f, state.TownHallMaxHealth - state.TownHallHealth);
                state.TownHallHealth += healed;
                state.Stone -= stoneBudget;
                log.Emit(SimEventType.TowerRepaired, EntityId.None, healed, state.CityCenter, cell: cell);
                return true;
            }

            var tower = state.GetTower(state.Grid.OccupantOf(cell));
            if (tower == null || tower.Health >= tower.MaxHealth) return false;

            float amount = System.Math.Min(stoneBudget * 4f, tower.MaxHealth - tower.Health);
            tower.Health += amount;
            state.Stone -= stoneBudget;
            log.Emit(SimEventType.TowerRepaired, tower.Id, amount, tower.Position, tower.Def, cell: cell);
            return true;
        }

        public static bool TryClearRubble(MatchState state, IContentDatabase content, GridCoord cell, SimEventLog log)
        {
            if (state.Grid.Get(cell) != CellState.Escombro) return false;
            state.Grid.Clear(cell);
            log.Emit(SimEventType.RubbleCleared, EntityId.None, 0f, cell.Center, cell: cell);
            BonusSystem.Recalculate(state, content);
            return true;
        }

        /// <summary>Producao passiva das Serrarias/Pedreiras, creditada no Balanco.</summary>
        public static void CollectTurnProduction(MatchState state, IContentDatabase content, SimEventLog log)
        {
            float wood = 0f, stone = 0f, gold = 0f;

            for (int i = 0; i < state.Towers.Count; i++)
            {
                var t = state.Towers[i];
                if (!t.IsAlive) continue;
                var spec = content.GetTower(t.Def);
                if (spec == null || spec.ProductionPerTurn <= 0f) continue;

                float amount = spec.ProductionPerTurn;

                if (spec.ProductionPerSameNeighbour > 0f)
                {
                    int sameNeighbours = 0;
                    for (int n = 0; n < GridCoord.Orthogonal.Length; n++)
                    {
                        var neighbour = state.GetTower(state.Grid.OccupantOf(t.Cell + GridCoord.Orthogonal[n]));
                        if (neighbour != null && neighbour.Def == t.Def) sameNeighbours++;
                    }
                    amount += spec.ProductionPerSameNeighbour * sameNeighbours;
                }

                // Distrito Oficio: producao dobrada + auto-reparo. Regra, nao numero solto.
                if (t.InDistrict && t.DistrictTag == BuildingTag.Oficio)
                {
                    amount *= 2f;
                    t.Health = System.Math.Min(t.MaxHealth, t.Health + 5f);
                }

                amount *= t.Tier;

                switch (spec.ProducesResource)
                {
                    case ResourceKind.Madeira: wood += amount; break;
                    case ResourceKind.Pedra: stone += amount; break;
                    case ResourceKind.Ouro: gold += amount; break;
                }
            }

            if (wood > 0f)
            {
                state.SiloWood = System.Math.Min(state.SiloWood + wood, state.SiloCapacity);
                log.Emit(SimEventType.ResourceGained, EntityId.None, wood, state.CityCenter,
                         intValue: (int)ResourceKind.Madeira);
            }
            if (stone > 0f)
            {
                state.Stone += stone;
                log.Emit(SimEventType.ResourceGained, EntityId.None, stone, state.CityCenter,
                         intValue: (int)ResourceKind.Pedra);
            }
            if (gold > 0f)
            {
                // Ouro de producao e dividido entre os jogadores conectados.
                int n = System.Math.Max(1, state.ConnectedPlayerCount());
                float share = gold / n;
                for (int i = 0; i < state.Players.Count; i++)
                    if (state.Players[i].IsConnected) state.Players[i].Gold += share;
            }
        }
    }
}
