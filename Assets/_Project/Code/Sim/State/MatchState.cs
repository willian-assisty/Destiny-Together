using System.Collections.Generic;
using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    /// <summary>
    /// Raiz do estado da partida. Tudo que a simulacao sabe esta aqui dentro.
    /// Nenhum campo depende de UnityEngine: isso e o que permite rodar a partida
    /// inteira num teste EditMode e, mais tarde, num servidor sem tela.
    /// </summary>
    public sealed class MatchState
    {
        public int MatchSeed;
        public MatchOutcome Outcome = MatchOutcome.EmAndamento;

        public int TurnNumber = 1;
        public PhaseId Phase = PhaseId.None;
        /// <summary>Tempo decorrido na fase atual, em segundos.</summary>
        public float PhaseElapsed;
        /// <summary>Teto da fase atual. O clamp do Preparo reduz este valor em tempo real.</summary>
        public float PhaseDuration;

        // --- Assalto ---
        public int SurgeIndex;
        public bool InBreather;
        public float SurgeElapsed;

        // --- Cidade ---
        public BoardGrid Grid;

        /// <summary>
        /// Ate onde o heroi pode andar, medido do centro.
        ///
        /// Vem do conteudo (raio dos Arredores + folga) em vez de ser uma constante no
        /// integrador. Era uma constante, e o resultado foi um mapa em que os Relicarios nasciam
        /// FORA do alcance do heroi — inalcancaveis, sem nenhum aviso, porque nada no codigo
        /// ligava a distancia de spawn de conteudo ao limite de movimento. Amarrar os dois aqui
        /// faz um desandar junto com o outro em vez de silenciosamente.
        /// </summary>
        public float WorldRadius = 40f;
        public float TownHallHealth;
        public float TownHallMaxHealth;
        /// <summary>Teto de dano por golpe, como fracao do HP maximo. Copiado das regras para que
        /// Combat aplique a regra sem precisar conhecer a base de conteudo.</summary>
        public float MaxSingleHitFraction = 0.25f;

        public int CityLevel = 1;
        public float Xp;
        public float XpToNextLevel;

        // --- Recursos compartilhados ---
        /// <summary>Silo de madeira: municao das torres. Vazio = todas caem para 50% de cadencia.</summary>
        public float SiloWood;
        public float SiloCapacity;
        public float Stone;

        // --- Entidades ---
        public readonly List<PlayerState> Players = new List<PlayerState>();
        public readonly List<TowerState> Towers = new List<TowerState>();
        public readonly List<MonsterState> Monsters = new List<MonsterState>();
        public readonly List<HeroState> Heroes = new List<HeroState>();
        public readonly List<HarvestNodeState> Nodes = new List<HarvestNodeState>();
        /// <summary>Esconderijos da mata. Repovoados a cada amanhecer.</summary>
        public readonly List<CacheState> Caches = new List<CacheState>();

        private int _nextEntityId = 1;
        public EntityId NewEntityId() => new EntityId(_nextEntityId++);

        /// <summary>
        /// Contador de ids, exposto para o arquivo de retomada. Se ele voltar a 1 num carregamento,
        /// entidades novas reciclam ids que os clientes ainda tem em tabela — e um monstro morto
        /// ressuscita como torre. O id precisa ser monotonico atraves de um save.
        /// </summary>
        public int NextEntityIdSeed { get => _nextEntityId; set => _nextEntityId = value; }

        // --- Indices ---
        private readonly Dictionary<EntityId, TowerState> _towerById = new Dictionary<EntityId, TowerState>();
        private readonly Dictionary<EntityId, MonsterState> _monsterById = new Dictionary<EntityId, MonsterState>();
        private readonly Dictionary<EntityId, HeroState> _heroById = new Dictionary<EntityId, HeroState>();
        private readonly Dictionary<EntityId, HarvestNodeState> _nodeById = new Dictionary<EntityId, HarvestNodeState>();
        private readonly Dictionary<EntityId, CacheState> _cacheById = new Dictionary<EntityId, CacheState>();

        // --- Mundo procedural ---

        /// <summary>Chunks materializados agora. Descarregar e recarregar e barato e frequente.</summary>
        public readonly HashSet<long> LoadedChunks = new HashSet<long>();

        /// <summary>
        /// O que ja foi consumido la fora, por chave de item.
        ///
        /// Este e o unico estado persistente do mundo procedural, e e de proposito: guardar o
        /// mundo inteiro seria impossivel (ele nao acaba) e desnecessario (ele e uma formula).
        /// Guardar so o que MUDOU em relacao a formula custa um long por arvore derrubada, e
        /// permite sair de uma clareira e voltar a ela sem que a madeira ressuscite.
        /// </summary>
        public readonly HashSet<long> ConsumedWorldItems = new HashSet<long>();

        public bool IsConsumed(long worldKey) => worldKey != 0L && ConsumedWorldItems.Contains(worldKey);
        public void MarkConsumed(long worldKey)
        {
            if (worldKey != 0L) ConsumedWorldItems.Add(worldKey);
        }

        public void RegisterTower(TowerState t) { Towers.Add(t); _towerById[t.Id] = t; }
        public void RegisterMonster(MonsterState m) { Monsters.Add(m); _monsterById[m.Id] = m; }
        public void RegisterHero(HeroState h) { Heroes.Add(h); _heroById[h.Id] = h; }
        public void RegisterNode(HarvestNodeState n) { Nodes.Add(n); _nodeById[n.Id] = n; }
        public void RegisterCache(CacheState c) { Caches.Add(c); _cacheById[c.Id] = c; }

        public TowerState GetTower(EntityId id) => _towerById.TryGetValue(id, out var t) ? t : null;
        public MonsterState GetMonster(EntityId id) => _monsterById.TryGetValue(id, out var m) ? m : null;
        public HeroState GetHero(EntityId id) => _heroById.TryGetValue(id, out var h) ? h : null;
        public HarvestNodeState GetNode(EntityId id) => _nodeById.TryGetValue(id, out var n) ? n : null;
        public CacheState GetCache(EntityId id) => _cacheById.TryGetValue(id, out var c) ? c : null;

        public void RemoveTower(TowerState t) { Towers.Remove(t); _towerById.Remove(t.Id); }
        public void RemoveMonster(MonsterState m) { Monsters.Remove(m); _monsterById.Remove(m.Id); }
        public void RemoveNode(HarvestNodeState n) { Nodes.Remove(n); _nodeById.Remove(n.Id); }
        public void RemoveCache(CacheState c) { Caches.Remove(c); _cacheById.Remove(c.Id); }

        public PlayerState GetPlayer(PlayerId id)
        {
            for (int i = 0; i < Players.Count; i++)
                if (Players[i].Id == id) return Players[i];
            return null;
        }

        public int ConnectedPlayerCount()
        {
            int n = 0;
            for (int i = 0; i < Players.Count; i++)
                if (Players[i].IsConnected) n++;
            return n;
        }

        public int ReadyCount()
        {
            int n = 0;
            for (int i = 0; i < Players.Count; i++)
                if (Players[i].IsReady || Players[i].IsAutomaton || !Players[i].IsConnected) n++;
            return n;
        }

        public Vec2 CityCenter => Grid != null ? Grid.Center : Vec2.Zero;
        public bool IsOver => Outcome != MatchOutcome.EmAndamento;

        /// <summary>
        /// Quanto da fase atual ja passou, de 0 a 1. A apresentacao usa isto para virar a luz:
        /// o entardecer comeca antes de a noite comecar, senao o jogador e pego de surpresa por
        /// uma transicao que ele deveria ter visto chegar.
        /// </summary>
        public float PhaseProgress01 => PhaseDuration > 0.001f
            ? MathUtil.Clamp01(PhaseElapsed / PhaseDuration)
            : 0f;

        public bool IsNight => Phase == PhaseId.Noite;
    }
}
