using DestinyTogether.Sim;
using UnityEngine;

namespace DestinyTogether.Data
{
    /// <summary>
    /// Base dos assets de autoria. O DefId vem do NOME do asset (hash FNV-1a estavel), entao
    /// renomear um asset e uma mudanca de conteudo — deliberadamente visivel, nunca silenciosa.
    /// </summary>
    public abstract class DefinitionBase : ScriptableObject
    {
        [Header("Identidade")]
        [Tooltip("Nome interno. Muda o DefId — renomear quebra saves e replays de proposito.")]
        public string DefName = "";

        public DefId Id => DefId.FromName(string.IsNullOrEmpty(DefName) ? name : DefName);

        protected virtual void OnValidate()
        {
            if (string.IsNullOrEmpty(DefName)) DefName = name;
        }
    }

    [CreateAssetMenu(menuName = "Destiny Together/Predio", fileName = "Predio_")]
    public sealed class TowerDefinition : DefinitionBase
    {
        [Header("Apresentacao")]
        public string DisplayName = "Predio";
        [TextArea] public string Description = "";
        public BuildingTag Tag = BuildingTag.Nenhuma;
        public Color PlaceholderColor = Color.gray;

        [Header("Estrutura")]
        public float MaxHealth = 120f;

        [Header("Ataque")]
        public bool CanAttack = true;
        public float Damage = 10f;
        [Tooltip("Alcance em CELULAS.")] public float Range = 4f;
        public float ShotInterval = 1.2f;
        public float SplashRadius = 0f;
        public TargetingRule Targeting = TargetingRule.MaisAvancado;
        [Tooltip("Madeira consumida por disparo. Silo vazio derruba todas as torres para 50% de cadencia.")]
        public float WoodPerShot = 1f;

        [Header("Controle")]
        [Range(0f, 1f)] public float SlowFactor = 0f;
        public float SlowDuration = 0f;
        [Tooltip("Empurrao em celulas.")] public float Knockback = 0f;

        [Header("Producao (creditada no Balanco)")]
        public ResourceKind ProducesResource = ResourceKind.Madeira;
        public float ProductionPerTurn = 0f;
        public float ProductionPerSameNeighbour = 0f;

        [Header("Suporte a vizinhos ortogonais")]
        public float AdjacentFireRateBonus = 0f;
        public float AdjacentRangeBonus = 0f;

        [Header("Especiais")]
        public float SiloCapacityBonus = 0f;
        [Tooltip("Segundos somados ao tempo de aproximacao da Faixa. Muralha compra TEMPO, nao dano.")]
        public float ApproachDelay = 0f;

        public TowerSpec Bake() => new TowerSpec
        {
            Id = Id,
            DisplayName = DisplayName,
            Description = Description,
            Tag = Tag,
            MaxHealth = MaxHealth,
            CanAttack = CanAttack,
            Damage = Damage,
            Range = Range,
            ShotInterval = ShotInterval,
            SplashRadius = SplashRadius,
            Targeting = Targeting,
            WoodPerShot = WoodPerShot,
            SlowFactor = SlowFactor,
            SlowDuration = SlowDuration,
            Knockback = Knockback,
            ProducesResource = ProducesResource,
            ProductionPerTurn = ProductionPerTurn,
            ProductionPerSameNeighbour = ProductionPerSameNeighbour,
            AdjacentFireRateBonus = AdjacentFireRateBonus,
            AdjacentRangeBonus = AdjacentRangeBonus,
            SiloCapacityBonus = SiloCapacityBonus,
            ApproachDelay = ApproachDelay
        };
    }

    [CreateAssetMenu(menuName = "Destiny Together/Monstro", fileName = "Monstro_")]
    public sealed class MonsterDefinition : DefinitionBase
    {
        public string DisplayName = "Monstro";
        public MonsterArchetype Archetype = MonsterArchetype.Enxame;
        public Color PlaceholderColor = Color.red;
        public float PlaceholderScale = 0.7f;

        [Header("Corpo")]
        public float MaxHealth = 8f;
        [Tooltip("Celulas por segundo.")] public float Speed = 4f;
        public float Armor = 0f;
        [Range(0f, 1f)] public float SlowResistance = 0f;
        public float BodyRadius = 0.35f;

        [Header("Ataque")]
        public float ContactDamage = 3f;
        public float AttackInterval = 1f;
        [Tooltip("Distancia em que para e ataca. Melee ~0.8; Cuspidor 8 (anti-camping).")]
        public float AttackRange = 0.8f;

        [Header("Explosao")]
        public bool ExplodesOnDeath = false;
        public float ExplosionRadius = 0f;
        public float ExplosionDamage = 0f;

        [Header("Ninho")]
        public bool IsStationary = false;
        public MonsterDefinition SpawnsMonster;
        public float SpawnInterval = 0f;
        public int SpawnCount = 0;

        [Header("Recompensa")]
        public float XpReward = 1f;
        public float GoldReward = 0f;

        public MonsterSpec Bake() => new MonsterSpec
        {
            Id = Id,
            DisplayName = DisplayName,
            Archetype = Archetype,
            MaxHealth = MaxHealth,
            Speed = Speed,
            Armor = Armor,
            SlowResistance = SlowResistance,
            BodyRadius = BodyRadius,
            ContactDamage = ContactDamage,
            AttackInterval = AttackInterval,
            AttackRange = AttackRange,
            ExplodesOnDeath = ExplodesOnDeath,
            ExplosionRadius = ExplosionRadius,
            ExplosionDamage = ExplosionDamage,
            IsStationary = IsStationary,
            SpawnsMonster = SpawnsMonster != null ? SpawnsMonster.Id : DefId.None,
            SpawnInterval = SpawnInterval,
            SpawnCount = SpawnCount,
            XpReward = XpReward,
            GoldReward = GoldReward
        };
    }

    [CreateAssetMenu(menuName = "Destiny Together/Heroi", fileName = "Heroi_")]
    public sealed class HeroDefinition : DefinitionBase
    {
        public string DisplayName = "Heroi";
        public HeroClass Class = HeroClass.Guarda;
        public Color PlaceholderColor = Color.cyan;

        public float MoveSpeed = 5.5f;
        public float MaxHealth = 100f;

        [Header("Auto-ataque (sai na direcao do MOVIMENTO — nao ha mira)")]
        public float AttackDamage = 12f;
        public float AttackRadius = 2.2f;
        public float AttackInterval = 0.45f;
        [Range(15f, 180f)] public float AttackConeHalfAngle = 75f;

        [Header("Logistica")]
        public float CarryCapacity = 12f;
        public float HarvestSpeedMultiplier = 1f;
        public float DepositTime = 0.5f;
        public float RespawnDelay = 6f;

        public HeroSpec Bake() => new HeroSpec
        {
            Id = Id,
            DisplayName = DisplayName,
            Class = Class,
            MoveSpeed = MoveSpeed,
            MaxHealth = MaxHealth,
            AttackDamage = AttackDamage,
            AttackRadius = AttackRadius,
            AttackInterval = AttackInterval,
            AttackConeHalfAngle = AttackConeHalfAngle,
            CarryCapacity = CarryCapacity,
            HarvestSpeedMultiplier = HarvestSpeedMultiplier,
            DepositTime = DepositTime,
            RespawnDelay = RespawnDelay
        };
    }

    [CreateAssetMenu(menuName = "Destiny Together/Arena", fileName = "Arena_")]
    public sealed class ArenaDefinition : ScriptableObject
    {
        [Tooltip("Lado do tabuleiro construivel.")] public int GridSize = 11;
        [Tooltip("Lado da Prefeitura, no centro.")] public int TownHallSize = 3;
        [Tooltip("Raio dos Arredores em celulas. Monstros nascem nesse anel.")]
        public float OutskirtsRadius = 18f;

        [Header("Cota FIXA de colheita por turno")]
        public int TreeCount = 6;
        public float WoodPerTree = 15f;
        public int RockCount = 4;
        public float StonePerRock = 10f;
        public int ChestCount = 2;
        public float GoldPerChest = 20f;

        public ArenaSpec Bake() => new ArenaSpec
        {
            GridSize = GridSize,
            TownHallSize = TownHallSize,
            OutskirtsRadius = OutskirtsRadius,
            TreeCount = TreeCount,
            WoodPerTree = WoodPerTree,
            RockCount = RockCount,
            StonePerRock = StonePerRock,
            ChestCount = ChestCount,
            GoldPerChest = GoldPerChest
        };
    }

    [CreateAssetMenu(menuName = "Destiny Together/Regras da Partida", fileName = "Regras_")]
    public sealed class MatchRulesDefinition : ScriptableObject
    {
        public int TotalTurns = 9;
        public float TownHallMaxHealth = 600f;
        [Range(0.05f, 1f)] public float MaxSingleHitFraction = 0.25f;

        [Header("Silo compartilhado")]
        public float SiloBaseCapacity = 400f;
        public float SiloStartingWood = 120f;
        public float StartingStone = 0f;
        public float StartingGoldPerPlayer = 0f;

        [Header("Relogio das fases")]
        public float PreparoMaxSeconds = 75f;
        public float PreparoFirstActSeconds = 60f;
        [Tooltip("Ao terceiro Pronto, o Preparo trava neste teto de segundos restantes.")]
        public float PreparoClampOnThirdReady = 15f;
        public float BalancoSeconds = 25f;

        [Header("Progressao")]
        [Tooltip("Multiplicada pelo n de jogadores: agencia per capita identica a 1, 2, 3 ou 4.")]
        public float XpPerCityLevelBase = 45f;
        public float XpPerCityLevelGrowth = 1.3f;
        [Tooltip("XP por unidade depositada — faz da Logistica um caminho de progresso.")]
        public float XpPerResourceDeposited = 0.6f;
        public int DraftOptions = 3;
        public float DraftRerollCost = 4f;

        public int MaxPlayers = 4;

        public MatchRulesSpec Bake() => new MatchRulesSpec
        {
            TotalTurns = TotalTurns,
            TownHallMaxHealth = TownHallMaxHealth,
            MaxSingleHitFraction = MaxSingleHitFraction,
            SiloBaseCapacity = SiloBaseCapacity,
            SiloStartingWood = SiloStartingWood,
            StartingStone = StartingStone,
            StartingGoldPerPlayer = StartingGoldPerPlayer,
            PreparoMaxSeconds = PreparoMaxSeconds,
            PreparoFirstActSeconds = PreparoFirstActSeconds,
            PreparoClampOnThirdReady = PreparoClampOnThirdReady,
            BalancoSeconds = BalancoSeconds,
            XpPerCityLevelBase = XpPerCityLevelBase,
            XpPerCityLevelGrowth = XpPerCityLevelGrowth,
            XpPerResourceDeposited = XpPerResourceDeposited,
            DraftOptions = DraftOptions,
            DraftRerollCost = DraftRerollCost,
            MaxPlayers = MaxPlayers
        };
    }
}
