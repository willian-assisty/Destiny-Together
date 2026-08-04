using System.Collections.Generic;
using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    public enum SimEventType
    {
        None = 0,
        PhaseChanged,
        TurnStarted,
        SurgeStarted,
        BreatherStarted,

        TowerBuilt,
        TowerMerged,
        TowerDamaged,
        TowerDestroyed,
        RubbleCleared,
        TowerRepaired,

        MonsterSpawned,
        MonsterDamaged,
        MonsterDied,
        MonsterExploded,
        MonsterAttacked,

        HeroAttacked,
        HeroDamaged,
        HeroDied,
        HeroRespawned,
        HeroDeposited,
        UrnRecovered,

        TowerFired,
        NodeHarvested,
        NodeDepleted,

        /// <summary>Um no do mundo procedural entrou em cena. Cell nao se aplica; Position sim.</summary>
        NodeAppeared,

        /// <summary>Um heroi chegou perto o bastante: o Esconderijo acendeu na tela.</summary>
        CacheRevealed,
        /// <summary>Recolhido. Amount = XP, IntValue = CacheKind, Player = quem achou.</summary>
        CacheCollected,
        /// <summary>Revelado mas nao recolhido, e o chunk saiu de alcance. So apaga a view.</summary>
        CacheHidden,

        /// <summary>Um chunk do mundo procedural foi materializado. Cell carrega (cx, cz).</summary>
        WorldChunkLoaded,
        /// <summary>Um chunk saiu de alcance e foi desmaterializado. Cell carrega (cx, cz).</summary>
        WorldChunkUnloaded,

        CityDamaged,
        CityLevelUp,
        SiloEmpty,
        ResourceGained,

        DraftOffered,
        CardPicked,
        MatchEnded
    }

    /// <summary>
    /// O contrato COMPLETO entre a simulacao e o resto do mundo.
    /// Struct de valor, sem tempo, sem Vector3, sem referencia a prefab: e por isso que a arte
    /// definitiva pode substituir os placeholders sem tocar em uma linha de logica, e e por isso
    /// que o mesmo tipo serve de payload de rede.
    /// </summary>
    public struct SimEvent
    {
        public SimEventType Type;
        public EntityId Entity;
        public EntityId Other;
        public DefId Def;
        public PlayerId Player;
        public GridCoord Cell;
        public Vec2 Position;
        public float Amount;
        public int IntValue;

        public static SimEvent Make(SimEventType type) => new SimEvent { Type = type };
    }

    /// <summary>
    /// Buffer append-only drenado a cada frame pela apresentacao. Tambem e o payload que o host
    /// envia aos clientes: um unico mecanismo, dois consumidores.
    /// </summary>
    public sealed class SimEventLog
    {
        private readonly List<SimEvent> _events = new List<SimEvent>(256);

        public int Count => _events.Count;
        public SimEvent this[int i] => _events[i];
        public IReadOnlyList<SimEvent> Events => _events;

        public void Add(in SimEvent e) => _events.Add(e);
        public void Clear() => _events.Clear();

        public void Emit(SimEventType type, EntityId entity = default, float amount = 0f,
                         Vec2 position = default, DefId def = default, PlayerId player = default,
                         GridCoord cell = default, EntityId other = default, int intValue = 0)
        {
            _events.Add(new SimEvent
            {
                Type = type,
                Entity = entity,
                Other = other,
                Def = def,
                Player = player,
                Cell = cell,
                Position = position,
                Amount = amount,
                IntValue = intValue
            });
        }

        /// <summary>Copia e limpa — a apresentacao consome, a simulacao segue em frente.</summary>
        public void DrainInto(List<SimEvent> destination)
        {
            destination.AddRange(_events);
            _events.Clear();
        }
    }
}
