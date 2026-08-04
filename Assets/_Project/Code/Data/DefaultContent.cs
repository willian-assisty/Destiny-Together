using System.Collections.Generic;
using DestinyTogether.Core;
using DestinyTogether.Sim;

namespace DestinyTogether.Data
{
    /// <summary>
    /// Conteudo padrao embutido em codigo: 10 predios, 6 monstros, 4 herois e os 9 turnos.
    ///
    /// Existe por dois motivos praticos. Primeiro, o projeto roda ao apertar Play sem nenhum
    /// asset criado — nao ha estado "quase configurado" em que o jogo nao abre. Segundo, os
    /// testes EditMode montam uma partida inteira sem carregar um unico ScriptableObject.
    /// O ContentDatabase (asset) sobrepoe isto quando o designer quiser balancear na mao.
    /// </summary>
    public sealed class DefaultContent : IContentDatabase
    {
        // ---- Nomes canonicos: mudar um destes muda o DefId e invalida replays antigos ----
        public const string Balestra = "Balestra";
        public const string Braseiro = "Braseiro";
        public const string TorreDeGelo = "TorreDeGelo";
        public const string BalistaDeImpacto = "BalistaDeImpacto";
        public const string Serraria = "Serraria";
        public const string Pedreira = "Pedreira";
        public const string Oficina = "Oficina";
        public const string Muralha = "Muralha";
        public const string PostoDeVigia = "PostoDeVigia";
        public const string Deposito = "Deposito";

        public const string Enxame = "Enxame";
        public const string Estourador = "Estourador";
        public const string Bruto = "Bruto";
        public const string Cuspidor = "Cuspidor";
        public const string Rondador = "Rondador";
        public const string Ninho = "Ninho";
        public const string MaeAranha = "MaeAranha";

        public const string Guarda = "Guarda";
        public const string Lenhador = "Lenhador";
        public const string Golem = "Golem";
        public const string Arauto = "Arauto";

        private readonly Dictionary<DefId, TowerSpec> _towers = new Dictionary<DefId, TowerSpec>();
        private readonly Dictionary<DefId, MonsterSpec> _monsters = new Dictionary<DefId, MonsterSpec>();
        private readonly Dictionary<DefId, HeroSpec> _heroes = new Dictionary<DefId, HeroSpec>();
        private readonly List<DefId> _towerPool = new List<DefId>();
        private readonly List<DefId> _heroPool = new List<DefId>();
        private readonly Dictionary<int, TurnWaveSpec> _waveCache = new Dictionary<int, TurnWaveSpec>();

        public MatchRulesSpec Rules { get; } = new MatchRulesSpec();
        public ArenaSpec Arena { get; } = new ArenaSpec();
        public IReadOnlyList<DefId> TowerPool => _towerPool;
        public IReadOnlyList<DefId> HeroPool => _heroPool;
        public int ContentHash { get; private set; }

        public DefaultContent()
        {
            BuildTowers();
            BuildMonsters();
            BuildHeroes();
            ContentHash = ComputeHash();
        }

        public TowerSpec GetTower(DefId id) => _towers.TryGetValue(id, out var s) ? s : null;
        public MonsterSpec GetMonster(DefId id) => _monsters.TryGetValue(id, out var s) ? s : null;
        public HeroSpec GetHero(DefId id) => _heroes.TryGetValue(id, out var s) ? s : null;

        public static DefId Def(string name) => DefId.FromName(name);

        // ------------------------------------------------------------------------------
        // PREDIOS — pool curado do dia 1. Todo item precisa mudar uma DECISAO, nao um numero.
        // ------------------------------------------------------------------------------
        private void BuildTowers()
        {
            AddTower(new TowerSpec
            {
                Id = Def(Balestra), DisplayName = "Balestra", Tag = BuildingTag.Ferro,
                MaxHealth = 150f, Damage = 16f, Range = 6.2f, ShotInterval = 1.2f, WoodPerShot = 1f,
                Description = "Dano direto de longo alcance. O predio-base da defesa."
            });

            AddTower(new TowerSpec
            {
                Id = Def(Braseiro), DisplayName = "Braseiro", Tag = BuildingTag.Igneo,
                MaxHealth = 150f, Damage = 10f, Range = 4.2f, ShotInterval = 1f, SplashRadius = 1.6f, WoodPerShot = 1f,
                Targeting = TargetingRule.MaisProximo,
                Description = "Dano em area curta. Come pacote de Enxame."
            });

            AddTower(new TowerSpec
            {
                Id = Def(TorreDeGelo), DisplayName = "Torre de Gelo", Tag = BuildingTag.Gelo,
                CanAttack = true, Damage = 0f, Range = 4.8f, ShotInterval = 0.8f, WoodPerShot = 0.5f,
                SlowFactor = 0.4f, SlowDuration = 2.5f, Targeting = TargetingRule.MaisAvancado,
                Description = "Nao causa dano: compra tempo para as outras torres."
            });

            AddTower(new TowerSpec
            {
                Id = Def(BalistaDeImpacto), DisplayName = "Balista de Impacto", Tag = BuildingTag.Ferro,
                Damage = 6f, Range = 4.2f, ShotInterval = 1.5f, Knockback = 1.2f, WoodPerShot = 1f,
                Description = "Empurra. Quando o inimigo vira esponja, empurrao vale mais que dano."
            });

            AddTower(new TowerSpec
            {
                Id = Def(Serraria), DisplayName = "Serraria", Tag = BuildingTag.Oficio,
                CanAttack = false, MaxHealth = 100f,
                ProducesResource = ResourceKind.Madeira, ProductionPerTurn = 12f,
                ProductionPerSameNeighbour = 6f,
                Description = "Municao passiva. Escala com Serrarias vizinhas."
            });

            AddTower(new TowerSpec
            {
                Id = Def(Pedreira), DisplayName = "Pedreira", Tag = BuildingTag.Oficio,
                CanAttack = false, MaxHealth = 100f,
                ProducesResource = ResourceKind.Pedra, ProductionPerTurn = 6f,
                Description = "Pedra e a unica cura do jogo — e cura a cidade, nunca o heroi."
            });

            AddTower(new TowerSpec
            {
                Id = Def(Oficina), DisplayName = "Oficina", Tag = BuildingTag.Oficio,
                CanAttack = false, MaxHealth = 110f, AdjacentFireRateBonus = 0.25f,
                Description = "+25% de cadencia nas torres ortogonalmente adjacentes."
            });

            AddTower(new TowerSpec
            {
                Id = Def(Muralha), DisplayName = "Muralha", Tag = BuildingTag.Pedra,
                CanAttack = false, MaxHealth = 400f, ApproachDelay = 0.8f,
                Description = "Compra TEMPO na Faixa. O unico predio que melhora o prognostico sem dar dano."
            });

            AddTower(new TowerSpec
            {
                Id = Def(PostoDeVigia), DisplayName = "Posto de Vigia", Tag = BuildingTag.Ferro,
                CanAttack = false, MaxHealth = 90f, AdjacentRangeBonus = 1f,
                Description = "+1 de alcance nas torres adjacentes."
            });

            AddTower(new TowerSpec
            {
                Id = Def(Deposito), DisplayName = "Deposito", Tag = BuildingTag.Pedra,
                CanAttack = false, MaxHealth = 140f, SiloCapacityBonus = 100f,
                Description = "+100 no teto do Silo. Sustenta o Assalto longo."
            });
        }

        private void AddTower(TowerSpec spec)
        {
            _towers[spec.Id] = spec;
            _towerPool.Add(spec.Id);
        }

        // ------------------------------------------------------------------------------
        // MONSTROS — 5 arquetipos + kaiju. Cada um existe para forcar um comportamento diferente.
        // ------------------------------------------------------------------------------
        private void BuildMonsters()
        {
            _monsters[Def(Enxame)] = new MonsterSpec
            {
                Id = Def(Enxame), DisplayName = "Enxame", Archetype = MonsterArchetype.Enxame,
                MaxHealth = 8f, Speed = 4f, ContactDamage = 3f, AttackInterval = 1f,
                AttackRange = 0.9f, XpReward = 2f, BodyRadius = 0.3f
            };

            _monsters[Def(Estourador)] = new MonsterSpec
            {
                Id = Def(Estourador), DisplayName = "Estourador", Archetype = MonsterArchetype.Estourador,
                MaxHealth = 40f, Speed = 3f, ContactDamage = 0f, AttackInterval = 0.5f,
                AttackRange = 1.2f, ExplodesOnDeath = true, ExplosionRadius = 2f, ExplosionDamage = 30f,
                XpReward = 6f, BodyRadius = 0.45f
            };

            _monsters[Def(Bruto)] = new MonsterSpec
            {
                Id = Def(Bruto), DisplayName = "Bruto", Archetype = MonsterArchetype.Bruto,
                MaxHealth = 200f, Speed = 1.8f, Armor = 4f, SlowResistance = 0.6f,
                // 40 de dano a cada 2s: derruba um predio de 150 HP em ~8s, tempo suficiente para
                // o time reagir. Com 60 ele apagava a linha inteira antes de alguem chegar.
                ContactDamage = 40f, AttackInterval = 2f, AttackRange = 1.1f,
                XpReward = 20f, GoldReward = 3f, BodyRadius = 0.6f
            };

            _monsters[Def(Cuspidor)] = new MonsterSpec
            {
                Id = Def(Cuspidor), DisplayName = "Cuspidor", Archetype = MonsterArchetype.Cuspidor,
                MaxHealth = 60f, Speed = 3f, ContactDamage = 15f, AttackInterval = 1.5f,
                // Alcance 6 e deliberado: fica FORA do alcance base da Balestra (4.5), mas DENTRO
                // do de uma torre com Posto de Vigia ou numa Rua de 6+. Assim ele tem duas
                // respostas — layout esperto ou o heroi indo la — em vez de ser impune as torres.
                AttackRange = 6f, XpReward = 10f, BodyRadius = 0.4f
            };

            _monsters[Def(Rondador)] = new MonsterSpec
            {
                Id = Def(Rondador), DisplayName = "Rondador", Archetype = MonsterArchetype.Rondador,
                // Rapido e fraco, e as duas coisas sao o mesmo argumento: ele PRECISA alcancar
                // quem esta longe (velocidade 6.5 supera todos os herois menos o Arauto), mas
                // nao pode ser uma sentenca — quem for pego e voltar correndo para as torres
                // sobrevive, quem insistir em ficar na mata, nao. A resposta e recuar, e recuar
                // e uma decisao; morrer sem resposta nao seria.
                MaxHealth = 34f, Speed = 6.5f, HuntsHeroes = true,
                ContactDamage = 9f, AttackInterval = 0.8f, AttackRange = 1f,
                XpReward = 8f, GoldReward = 2f, BodyRadius = 0.35f
            };

            _monsters[Def(Ninho)] = new MonsterSpec
            {
                Id = Def(Ninho), DisplayName = "Ninho", Archetype = MonsterArchetype.Ninho,
                MaxHealth = 300f, Speed = 0f, IsStationary = true,
                SpawnsMonster = Def(Enxame), SpawnInterval = 5f, SpawnCount = 3,
                XpReward = 30f, GoldReward = 10f, BodyRadius = 0.8f
            };

            _monsters[Def(MaeAranha)] = new MonsterSpec
            {
                Id = Def(MaeAranha), DisplayName = "Mae-Aranha", Archetype = MonsterArchetype.Kaiju,
                MaxHealth = 4000f, Speed = 1.2f, Armor = 8f, SlowResistance = 0.8f,
                ContactDamage = 90f, AttackInterval = 2.5f, AttackRange = 2f,
                XpReward = 200f, GoldReward = 50f, BodyRadius = 1.4f
            };
        }

        // ------------------------------------------------------------------------------
        // HEROIS — placeholder e cor + primitiva, mas a FUNCAO ja e distinta desde o dia 1.
        // ------------------------------------------------------------------------------
        private void BuildHeroes()
        {
            AddHero(new HeroSpec
            {
                Id = Def(Guarda), DisplayName = "Guarda", Class = HeroClass.Guarda,
                MoveSpeed = 7.0f, MaxHealth = 130f, AttackDamage = 14f, AttackRadius = 2.9f,
                AttackInterval = 0.45f, CarryCapacity = 10f
            });

            AddHero(new HeroSpec
            {
                Id = Def(Lenhador), DisplayName = "Lenhador", Class = HeroClass.Lenhador,
                MoveSpeed = 7.4f, MaxHealth = 100f, AttackDamage = 10f, AttackRadius = 3.1f,
                AttackInterval = 0.5f, CarryCapacity = 20f, HarvestSpeedMultiplier = 2f,
                DepositTime = 0.25f
            });

            AddHero(new HeroSpec
            {
                Id = Def(Golem), DisplayName = "Golem", Class = HeroClass.Golem,
                MoveSpeed = 5.7f, MaxHealth = 200f, AttackDamage = 18f, AttackRadius = 2.7f,
                AttackInterval = 0.7f, CarryCapacity = 14f
            });

            AddHero(new HeroSpec
            {
                Id = Def(Arauto), DisplayName = "Arauto", Class = HeroClass.Arauto,
                MoveSpeed = 8.8f, MaxHealth = 85f, AttackDamage = 9f, AttackRadius = 3.6f,
                AttackInterval = 0.35f, CarryCapacity = 10f
            });
        }

        private void AddHero(HeroSpec spec)
        {
            _heroes[spec.Id] = spec;
            _heroPool.Add(spec.Id);
        }

        // ------------------------------------------------------------------------------
        // ONDAS — 9 turnos em 3 Atos. A composicao inteira e revelada no Preparo.
        // ------------------------------------------------------------------------------
        public TurnWaveSpec GetTurnWaves(int turnNumber, int playerCount)
        {
            int key = turnNumber * 16 + playerCount;
            if (_waveCache.TryGetValue(key, out var cached)) return cached;
            var built = WaveBuilder.BuildTurn(turnNumber, Rules.TotalTurns, playerCount);
            _waveCache[key] = built;
            return built;
        }

        private int ComputeHash()
        {
            unchecked
            {
                int hash = 17;
                foreach (var kv in _towers) hash = hash * 31 + kv.Key.Value;
                foreach (var kv in _monsters) hash = hash * 31 + kv.Key.Value;
                foreach (var kv in _heroes) hash = hash * 31 + kv.Key.Value;
                hash = hash * 31 + Rules.TotalTurns;
                hash = hash * 31 + Arena.GridSize;
                return hash;
            }
        }
    }
}
