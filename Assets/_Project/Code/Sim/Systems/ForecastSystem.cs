using System;
using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    /// <summary>Metricas de uma Faixa, mostradas ao vivo no HUD durante o Preparo.</summary>
    public struct LaneForecast
    {
        public Lane Lane;
        /// <summary>Segundos entre o spawn e o primeiro contato com a cidade nesta Faixa.</summary>
        public float ApproachSeconds;
        /// <summary>Dano por segundo que as torres conseguem despejar no corredor desta Faixa.</summary>
        public float LaneDps;
        /// <summary>HP total que vem por aqui na proxima Investida.</summary>
        public float IncomingHealth;
        public int IncomingCount;
        public ForecastBand Band;
        /// <summary>Quantos monstros sobram apos o corredor. 0 = SEGURA.</summary>
        public int EstimatedLeak;
    }

    /// <summary>Antes-e-depois de colocar um predio numa celula. Alimenta o HUD durante o arrasto.</summary>
    public struct PlacementImpact
    {
        public Lane Lane;
        public float ApproachBefore, ApproachAfter;
        public int PerimeterBefore, PerimeterAfter;
        public ForecastBand BandBefore, BandAfter;
        public int LeakBefore, LeakAfter;
        public float DpsBefore, DpsAfter;

        /// <summary>Colocar aqui piora o prognostico desta Faixa?</summary>
        public bool WorsensBand => (int)BandAfter > (int)BandBefore;
        public float ApproachDelta => ApproachAfter - ApproachBefore;
        public int PerimeterDelta => PerimeterAfter - PerimeterBefore;
    }

    /// <summary>
    /// O sistema central do jogo.
    ///
    /// O jogo de referencia produz tensao na construcao porque a cidade anda e cada predio novo
    /// aumenta a chance de ela travar em uma arvore. Com a cidade estatica esse trade-off some,
    /// entao ele foi substituido: cada predio colocado para FORA sobe o DPS e ENCURTA o corredor
    /// daquela Faixa. O HUD imprime os dois numeros na mesma tela, no mesmo segundo, enquanto o
    /// jogador ainda esta arrastando. Arrependimento antecipado e o combustivel emocional do jogo.
    ///
    /// Calculo ESTATICO de proposito (DPS x tempo vs HP), nunca simulacao: 90% do valor social a
    /// um custo que roda 60 vezes por segundo enquanto o predio esta na mao.
    /// </summary>
    public static class ForecastSystem
    {
        /// <summary>Meio-angulo do corredor de uma Faixa. 8 faixas de 45 graus => 22.5 para cada lado.</summary>
        public const float LaneHalfAngle = 22.5f;

        /// <summary>Velocidade usada quando a onda tem varios tipos: a do mais rapido manda no relogio.</summary>
        private const float FallbackSpeed = 4f;

        public static LaneForecast Evaluate(MatchState state, IContentDatabase content, Lane lane,
                                            SurgeSpec surge, GridCoord? hypotheticalCell = null,
                                            DefId hypotheticalDef = default)
        {
            var result = new LaneForecast { Lane = lane };

            bool applied = TryApplyHypothetical(state, hypotheticalCell, out var savedState, out var savedOccupant);
            try
            {
                float speed = FastestIncomingSpeed(content, surge, lane);
                result.ApproachSeconds = ApproachSeconds(state, content, lane, speed, hypotheticalCell, hypotheticalDef);
                result.LaneDps = LaneDps(state, content, lane, hypotheticalDef, hypotheticalCell);
            }
            finally
            {
                if (applied) RevertHypothetical(state, hypotheticalCell.Value, savedState, savedOccupant);
            }

            SumIncoming(content, surge, lane, out result.IncomingHealth, out result.IncomingCount);

            float killable = result.LaneDps * result.ApproachSeconds;
            if (result.IncomingHealth <= 0f)
            {
                result.Band = ForecastBand.Segura;
                result.EstimatedLeak = 0;
            }
            else
            {
                float ratio = killable / result.IncomingHealth;
                float avgHealth = result.IncomingCount > 0 ? result.IncomingHealth / result.IncomingCount : 1f;
                float survivingHealth = Math.Max(0f, result.IncomingHealth - killable);
                result.EstimatedLeak = (int)Math.Ceiling(survivingHealth / Math.Max(1f, avgHealth));

                if (ratio >= 1f) result.Band = ForecastBand.Segura;
                else if (ratio >= 0.55f) result.Band = ForecastBand.Vaza;
                else result.Band = ForecastBand.Arromba;
            }

            return result;
        }

        public static void EvaluateAll(MatchState state, IContentDatabase content, SurgeSpec surge,
                                       LaneForecast[] destination)
        {
            if (destination == null || destination.Length < LaneGeometry.LaneCount) return;
            for (int i = 0; i < LaneGeometry.LaneCount; i++)
                destination[i] = Evaluate(state, content, (Lane)i, surge);
        }

        /// <summary>
        /// Tempo entre o spawn e o primeiro contato com a cidade. E o numero que o jogador ve
        /// cair enquanto arrasta um predio para fora.
        /// </summary>
        public static float ApproachSeconds(MatchState state, IContentDatabase content, Lane lane,
                                            float speed, GridCoord? hypotheticalCell = null,
                                            DefId hypotheticalDef = default)
        {
            if (speed <= 0f) speed = FallbackSpeed;

            var origin = state.CityCenter + LaneGeometry.DirectionOf(lane) * content.Arena.OutskirtsRadius;

            float distance;
            if (state.Grid.TryGetNearestCityTile(origin, out _, out distance) == false)
                distance = content.Arena.OutskirtsRadius;

            float seconds = distance / speed;

            // Muralhas no corredor somam atraso: e o unico predio que compra TEMPO em vez de dano.
            for (int i = 0; i < state.Towers.Count; i++)
            {
                var t = state.Towers[i];
                if (!t.IsAlive || !IsInLaneCorridor(state, t.Position, lane)) continue;
                var spec = content.GetTower(t.Def);
                if (spec != null) seconds += spec.ApproachDelay;
            }

            if (hypotheticalCell.HasValue && hypotheticalDef.IsValid &&
                IsInLaneCorridor(state, hypotheticalCell.Value.Center, lane))
            {
                var spec = content.GetTower(hypotheticalDef);
                if (spec != null) seconds += spec.ApproachDelay;
            }

            return seconds;
        }

        /// <summary>DPS que as torres conseguem aplicar em quem vem por esta Faixa.</summary>
        public static float LaneDps(MatchState state, IContentDatabase content, Lane lane,
                                    DefId hypotheticalDef = default, GridCoord? hypotheticalCell = null)
        {
            float dps = 0f;

            for (int i = 0; i < state.Towers.Count; i++)
            {
                var t = state.Towers[i];
                if (!t.IsAlive) continue;
                var spec = content.GetTower(t.Def);
                if (spec == null || !spec.CanAttack || spec.ShotInterval <= 0f) continue;

                // Uma torre so conta para a Faixa se o corredor passa dentro do alcance dela.
                float reach = spec.Range + t.BonusRange;
                if (!CorridorIntersects(state, t.Position, lane, reach)) continue;

                float tierDamage = 1f + 0.8f * (t.Tier - 1);
                float tierRate = 1f + 0.35f * (t.Tier - 1);
                dps += spec.Damage * tierDamage * tierRate * t.FireRateMultiplier / spec.ShotInterval;
            }

            // A torre hipotetica so entra na conta se ela realmente cobrir ESTA Faixa — senao o
            // HUD prometeria dano em um corredor que a torre nem alcanca.
            if (hypotheticalDef.IsValid && hypotheticalCell.HasValue)
            {
                var spec = content.GetTower(hypotheticalDef);
                if (spec != null && spec.CanAttack && spec.ShotInterval > 0f &&
                    CorridorIntersects(state, hypotheticalCell.Value.Center, lane, spec.Range))
                    dps += spec.Damage / spec.ShotInterval;
            }

            // Silo vazio derruba a cadencia pela metade — o prognostico precisa ser honesto sobre isso.
            if (state.SiloWood <= 0f) dps *= 0.5f;

            return dps;
        }

        private static void SumIncoming(IContentDatabase content, SurgeSpec surge, Lane lane,
                                        out float totalHealth, out int count)
        {
            totalHealth = 0f;
            count = 0;
            if (surge == null) return;

            for (int i = 0; i < surge.Entries.Length; i++)
            {
                ref readonly var e = ref surge.Entries[i];
                if (e.Lane != lane) continue;
                var spec = content.GetMonster(e.Monster);
                if (spec == null) continue;
                totalHealth += spec.MaxHealth * e.Count;
                count += e.Count;
            }
        }

        private static float FastestIncomingSpeed(IContentDatabase content, SurgeSpec surge, Lane lane)
        {
            float fastest = 0f;
            if (surge != null)
            {
                for (int i = 0; i < surge.Entries.Length; i++)
                {
                    ref readonly var e = ref surge.Entries[i];
                    if (e.Lane != lane) continue;
                    var spec = content.GetMonster(e.Monster);
                    if (spec != null && spec.Speed > fastest) fastest = spec.Speed;
                }
            }
            return fastest > 0f ? fastest : FallbackSpeed;
        }

        /// <summary>Um ponto esta no corredor da Faixa se o angulo dele em relacao ao centro cabe nos 45 graus.</summary>
        public static bool IsInLaneCorridor(MatchState state, Vec2 point, Lane lane)
        {
            var delta = point - state.CityCenter;
            if (delta.SqrMagnitude < 0.01f) return true;
            float diff = AngleDifference(delta.CompassDegrees, LaneGeometry.DegreesOf(lane));
            return diff <= LaneHalfAngle;
        }

        /// <summary>
        /// A torre cobre a Faixa se o eixo do corredor passa a menos de <paramref name="reach"/> dela.
        /// Distancia ponto-reta, com a reta saindo do centro na direcao da Faixa.
        /// </summary>
        private static bool CorridorIntersects(MatchState state, Vec2 towerPos, Lane lane, float reach)
        {
            var dir = LaneGeometry.DirectionOf(lane);
            var delta = towerPos - state.CityCenter;
            float along = Vec2.Dot(delta, dir);
            if (along < -reach) return false; // torre do lado oposto da cidade
            var closest = state.CityCenter + dir * Math.Max(0f, along);
            return Vec2.SqrDistance(towerPos, closest) <= reach * reach;
        }

        private static float AngleDifference(float a, float b)
        {
            float d = Math.Abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }

        /// <summary>Perimetro exposto CASO um predio fosse colocado nesta celula.</summary>
        public static int PerimeterWith(MatchState state, GridCoord cell)
        {
            bool applied = TryApplyHypothetical(state, cell, out var saved, out var occupant);
            try { return state.Grid.ExposedPerimeter(); }
            finally { if (applied) RevertHypothetical(state, cell, saved, occupant); }
        }

        private static bool TryApplyHypothetical(MatchState state, GridCoord? cell,
                                                 out CellState savedState, out EntityId savedOccupant)
        {
            savedState = CellState.Vazio;
            savedOccupant = EntityId.None;
            if (!cell.HasValue || !state.Grid.InBounds(cell.Value)) return false;

            savedState = state.Grid.Get(cell.Value);
            savedOccupant = state.Grid.OccupantOf(cell.Value);
            if (savedState != CellState.Vazio) return false;

            state.Grid.Set(cell.Value, CellState.Predio, EntityId.None);
            return true;
        }

        private static void RevertHypothetical(MatchState state, GridCoord cell,
                                               CellState savedState, EntityId savedOccupant)
            => state.Grid.Set(cell, savedState, savedOccupant);

        public static string BandLabel(ForecastBand band)
        {
            switch (band)
            {
                case ForecastBand.Segura: return "SEGURA";
                case ForecastBand.Vaza: return "VAZA";
                default: return "ARROMBA";
            }
        }
    }
}
