using System.Collections.Generic;
using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    /// <summary>
    /// Materializa e desmaterializa o mundo procedural conforme os herois andam.
    ///
    /// Chunks perto de alguem viram entidades de verdade (nos colhiveis, Esconderijos); chunks
    /// longe deixam de existir. O que some nao e perdido: o conteudo e uma funcao pura de
    /// <see cref="WorldGen"/>, entao voltar reconstroi exatamente a mesma mata — menos o que foi
    /// consumido, que fica lembrado em <see cref="MatchState.ConsumedWorldItems"/>.
    ///
    /// Sem descarregar, uma partida de 40 minutos acumularia dezenas de milhares de entidades: um
    /// heroi a 7 celulas/s cobre ~2000 celulas por dia, o que sao centenas de chunks. Com
    /// descarregamento, o custo fica proporcional ao que esta perto de um jogador, nao ao que ja
    /// foi visitado.
    ///
    /// A histerese entre carregar e descarregar existe para que andar de um lado para o outro na
    /// fronteira de um chunk nao fique criando e destruindo o mesmo bosque a cada segundo.
    /// </summary>
    public static class WorldStreamer
    {
        /// <summary>Chunks a mais que o raio de carga antes de descarregar. Evita liga-desliga na borda.</summary>
        private const int UnloadHysteresis = 2;

        /// <summary>Teto de chunks materializados de uma vez. Rede de seguranca, nunca alcancado em jogo normal.</summary>
        private const int MaxLoadedChunks = 512;

        // Buffers reutilizados: a simulacao e single-thread e isto roda todo tick.
        private static readonly ChunkContent _scratch = new ChunkContent();
        private static readonly List<long> _toUnload = new List<long>(64);
        private static readonly HashSet<long> _wanted = new HashSet<long>();

        public static void Tick(MatchState state, IContentDatabase content, SimEventLog log)
        {
            var arena = content.Arena;
            int radius = arena.WorldStreamRadiusChunks;
            if (radius <= 0) return;

            // Sai cedo enquanto ninguem trocou de chunk. Sem isto, cada tick reconstruia um
            // conjunto de ~320 chaves vinte vezes por segundo para descobrir que nada mudou — e
            // o custo de simulacao de uma partida quadruplicou na primeira medicao.
            if (!AnyHeroChangedChunk(state)) return;

            _wanted.Clear();
            CollectWantedChunks(state, radius, _wanted);

            // Carrega o que falta.
            foreach (long key in _wanted)
            {
                if (state.LoadedChunks.Count >= MaxLoadedChunks) break;
                if (state.LoadedChunks.Contains(key)) continue;
                Load(state, content, key, log);
            }

            UnloadFarChunks(state, radius + UnloadHysteresis, log);
        }

        /// <summary>
        /// True quando algum heroi mudou de chunk desde a ultima chamada. Marca o novo chunk de
        /// cada um no caminho, entao chamar isto e o proprio ato de registrar a mudanca.
        /// </summary>
        private static bool AnyHeroChangedChunk(MatchState state)
        {
            bool changed = false;

            for (int i = 0; i < state.Heroes.Count; i++)
            {
                var hero = state.Heroes[i];
                if (hero.IsSpectre) continue;

                WorldGen.ChunkOf(hero.Position, out int cx, out int cz);
                if (cx == hero.LastChunkX && cz == hero.LastChunkZ) continue;

                hero.LastChunkX = cx;
                hero.LastChunkZ = cz;
                changed = true;
            }

            return changed;
        }

        private static void CollectWantedChunks(MatchState state, int radius, HashSet<long> into)
        {
            for (int h = 0; h < state.Heroes.Count; h++)
            {
                var hero = state.Heroes[h];
                if (hero.IsSpectre) continue;

                WorldGen.ChunkOf(hero.Position, out int hx, out int hz);
                for (int dz = -radius; dz <= radius; dz++)
                    for (int dx = -radius; dx <= radius; dx++)
                        into.Add(WorldGen.KeyOf(hx + dx, hz + dz));
            }
        }

        private static void UnloadFarChunks(MatchState state, int keepRadius, SimEventLog log)
        {
            _toUnload.Clear();

            foreach (long key in state.LoadedChunks)
            {
                Decode(key, out int cx, out int cz);
                if (!IsNearAnyHero(state, cx, cz, keepRadius)) _toUnload.Add(key);
            }

            for (int i = 0; i < _toUnload.Count; i++) Unload(state, _toUnload[i], log);
        }

        private static bool IsNearAnyHero(MatchState state, int cx, int cz, int radius)
        {
            for (int h = 0; h < state.Heroes.Count; h++)
            {
                var hero = state.Heroes[h];
                if (hero.IsSpectre) continue;

                WorldGen.ChunkOf(hero.Position, out int hx, out int hz);
                if (System.Math.Abs(hx - cx) <= radius && System.Math.Abs(hz - cz) <= radius) return true;
            }
            return false;
        }

        private static void Load(MatchState state, IContentDatabase content, long key, SimEventLog log)
        {
            Decode(key, out int cx, out int cz);
            state.LoadedChunks.Add(key);

            WorldGen.Generate(state.MatchSeed, cx, cz, content.Arena, state.CityCenter, _scratch);

            for (int i = 0; i < _scratch.Spawns.Count; i++)
            {
                var spawn = _scratch.Spawns[i];
                long itemKey = WorldGen.ItemKey(cx, cz, spawn.Index);
                if (state.IsConsumed(itemKey)) continue;

                if (spawn.IsCache)
                {
                    var cache = new CacheState
                    {
                        Id = state.NewEntityId(),
                        Kind = spawn.CacheKind,
                        Position = spawn.Position,
                        Xp = spawn.Amount,
                        Gold = spawn.Gold,
                        WorldKey = itemKey
                    };
                    state.RegisterCache(cache);
                }
                else
                {
                    var node = new HarvestNodeState
                    {
                        Id = state.NewEntityId(),
                        Kind = spawn.NodeKind,
                        Position = spawn.Position,
                        Remaining = spawn.Amount,
                        TotalPerHarvest = spawn.Amount,
                        WorldKey = itemKey
                    };
                    state.RegisterNode(node);
                    log?.Emit(SimEventType.NodeAppeared, node.Id, node.Remaining, node.Position,
                              intValue: (int)node.Kind);
                }
            }

            log?.Emit(SimEventType.WorldChunkLoaded, EntityId.None, 0f, WorldGen.ChunkCenter(cx, cz),
                      cell: new GridCoord(cx, cz));
        }

        private static void Unload(MatchState state, long key, SimEventLog log)
        {
            Decode(key, out int cx, out int cz);
            state.LoadedChunks.Remove(key);

            for (int i = state.Nodes.Count - 1; i >= 0; i--)
            {
                var node = state.Nodes[i];
                if (!node.FromWorld || !InChunk(node.Position, cx, cz)) continue;
                log?.Emit(SimEventType.NodeDepleted, node.Id, 0f, node.Position);
                state.RemoveNode(node);
            }

            for (int i = state.Caches.Count - 1; i >= 0; i--)
            {
                var cache = state.Caches[i];
                if (!cache.FromWorld || !InChunk(cache.Position, cx, cz)) continue;
                // Revelado mas nao recolhido: a view precisa sumir junto, senao fica um cone
                // dourado orfao boiando num pedaco de mundo que nao existe mais.
                if (cache.Revealed)
                    log?.Emit(SimEventType.CacheHidden, cache.Id, 0f, cache.Position,
                              intValue: (int)cache.Kind);
                state.RemoveCache(cache);
            }

            log?.Emit(SimEventType.WorldChunkUnloaded, EntityId.None, 0f, WorldGen.ChunkCenter(cx, cz),
                      cell: new GridCoord(cx, cz));
        }

        private static bool InChunk(Vec2 position, int cx, int cz)
        {
            WorldGen.ChunkOf(position, out int px, out int pz);
            return px == cx && pz == cz;
        }

        private static void Decode(long key, out int cx, out int cz)
        {
            cx = (int)(key >> 32);
            cz = (int)(uint)(key & 0xFFFFFFFFL);
        }
    }
}
