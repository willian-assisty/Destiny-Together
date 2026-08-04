using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    /// <summary>
    /// Pilota os herois de assentos sem humano.
    ///
    /// Um Automato COLHE e DEPOSITA, e nada mais: nunca constroi, nunca gasta o Sino, nunca
    /// escolhe carta. A regra vem do desenho de desconexao — se um jogador cair no meio da run,
    /// o corpo dele continua util para o time sem tomar nenhuma decisao que era dele. O mesmo
    /// codigo serve de preenchimento para partidas com menos de quatro pessoas.
    ///
    /// De proposito e burro: se ele jogasse bem, o time humano ficaria pior com quatro pessoas
    /// do que com uma. Automato e rede de seguranca, nao companheiro.
    /// </summary>
    public static class AutomatonSystem
    {
        public static void DriveIfNeeded(MatchState state, HeroState hero, HeroSpec spec)
        {
            var player = state.GetPlayer(hero.Owner);
            if (player == null || !player.IsAutomaton) return;

            // Carga cheia: volta e entrega.
            if (hero.CarriedTotal >= spec.CarryCapacity - 0.01f)
            {
                hero.MoveInput = SteerTowards(hero.Position, state.CityCenter);
                return;
            }

            var node = NearestNode(state, hero.Position);
            if (node == null)
            {
                // Sem nada para colher, fica junto da cidade em vez de vagar pelo mapa.
                float distance = Vec2.Distance(hero.Position, state.CityCenter);
                hero.MoveInput = distance > 4f ? SteerTowards(hero.Position, state.CityCenter) : Vec2.Zero;
                return;
            }

            hero.MoveInput = SteerTowards(hero.Position, node.Position);
        }

        private static Vec2 SteerTowards(Vec2 from, Vec2 to)
        {
            var delta = to - from;
            return delta.SqrMagnitude < 0.04f ? Vec2.Zero : delta.Normalized;
        }

        private static HarvestNodeState NearestNode(MatchState state, Vec2 from)
        {
            HarvestNodeState best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < state.Nodes.Count; i++)
            {
                var node = state.Nodes[i];
                if (node.IsDepleted) continue;
                float sqr = Vec2.SqrDistance(node.Position, from);
                if (sqr < bestSqr) { bestSqr = sqr; best = node; }
            }

            return best;
        }
    }
}
