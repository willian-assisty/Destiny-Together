using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    // ------------------------------------------------------------------------------------
    // Specs = dados IMUTAVEIS consumidos pela simulacao.
    // Sao "bakeados" a partir dos ScriptableObjects de DT.Data. A simulacao nunca ve um SO:
    // isso a mantem testavel em EditMode em milissegundos e executavel em servidor headless.
    // Unidades: comprimento em CELULAS, tempo em SEGUNDOS, velocidade em CELULAS/SEGUNDO.
    // ------------------------------------------------------------------------------------

    public sealed class TowerSpec
    {
        public DefId Id;
        public string DisplayName;
        public BuildingTag Tag;

        public float MaxHealth = 120f;
        /// <summary>Ocupacao em celulas (1 = 1x1). Footprint e hitbox: crescer e ficar alcancavel.</summary>
        public int FootprintSize = 1;

        public bool CanAttack = true;
        public float Damage = 10f;
        public float Range = 4f;
        public float ShotInterval = 1.2f;
        public float SplashRadius = 0f;
        public TargetingRule Targeting = TargetingRule.MaisAvancado;
        /// <summary>Madeira consumida por disparo. Silo vazio derruba a cadencia pela metade.</summary>
        public float WoodPerShot = 1f;

        /// <summary>Slow aplicado ao alvo (0..1). Torre de Gelo nao causa dano.</summary>
        public float SlowFactor = 0f;
        public float SlowDuration = 0f;
        /// <summary>Empurrao em celulas. Substituto de DPS quando o inimigo vira esponja.</summary>
        public float Knockback = 0f;

        /// <summary>Producao passiva por turno, creditada no Balanco.</summary>
        public ResourceKind ProducesResource = ResourceKind.Xp;
        public float ProductionPerTurn = 0f;
        /// <summary>Bonus de producao por vizinho ortogonal com a mesma DefId.</summary>
        public float ProductionPerSameNeighbour = 0f;

        /// <summary>Buffs concedidos a torres ortogonalmente adjacentes.</summary>
        public float AdjacentFireRateBonus = 0f;
        public float AdjacentRangeBonus = 0f;

        /// <summary>Teto extra do Silo compartilhado (Deposito).</summary>
        public float SiloCapacityBonus = 0f;
        /// <summary>Segundos somados ao tempo de aproximacao da Faixa (Muralha).</summary>
        public float ApproachDelay = 0f;

        public string Description = "";
    }

    public sealed class MonsterSpec
    {
        public DefId Id;
        public string DisplayName;
        public MonsterArchetype Archetype;

        public float MaxHealth = 8f;
        /// <summary>Celulas por segundo.</summary>
        public float Speed = 4f;
        public float Armor = 0f;
        /// <summary>Fracao do slow que o monstro ignora (Bruto resiste parcialmente).</summary>
        public float SlowResistance = 0f;

        public float ContactDamage = 3f;
        public float AttackInterval = 1f;
        /// <summary>Distancia em que para e ataca. Melee ~0.8; Cuspidor 8.</summary>
        public float AttackRange = 0.8f;

        /// <summary>Estourador: detona ao morrer ou ao encostar.</summary>
        public bool ExplodesOnDeath = false;
        public float ExplosionRadius = 0f;
        public float ExplosionDamage = 0f;

        /// <summary>Ninho: estatico e gera filhotes.</summary>
        public bool IsStationary = false;
        public DefId SpawnsMonster = default;
        public float SpawnInterval = 0f;
        public int SpawnCount = 0;

        public float XpReward = 1f;
        public float GoldReward = 0f;

        public float BodyRadius = 0.35f;
    }

    public sealed class HeroSpec
    {
        public DefId Id;
        public string DisplayName;
        public HeroClass Class;

        public float MoveSpeed = 5.5f;
        /// <summary>Heroi tem HP, mas morrer NAO encerra a run: vira Espectro e deixa uma Urna.</summary>
        public float MaxHealth = 100f;
        /// <summary>Auto-ataque: sai na direcao do MOVIMENTO. Nao ha mira. O unico verbo e ESTAR.</summary>
        public float AttackDamage = 12f;
        public float AttackRadius = 2.2f;
        public float AttackInterval = 0.45f;
        /// <summary>Meio-angulo do cone de auto-ataque em graus. 180 = circulo completo.</summary>
        public float AttackConeHalfAngle = 75f;

        public float CarryCapacity = 12f;
        public float HarvestSpeedMultiplier = 1f;
        public float DepositTime = 0.5f;

        public float RespawnDelay = 6f;
        public float BodyRadius = 0.4f;
    }

    /// <summary>Uma Investida: um pacote de monstros entrando por uma Faixa em um instante.</summary>
    public struct WaveEntry
    {
        public DefId Monster;
        public int Count;
        public Lane Lane;
        /// <summary>Segundos apos o inicio da Investida.</summary>
        public float DelaySeconds;
        /// <summary>Ninho nasce fora do alcance das torres — forca expedicao humana.</summary>
        public bool SpawnOutsideTowerRange;
    }

    /// <summary>Uma Investida completa (~25s), seguida de um Respiro.</summary>
    public sealed class SurgeSpec
    {
        public WaveEntry[] Entries = new WaveEntry[0];
        public float DurationSeconds = 25f;
        public float BreatherSeconds = 8f;
    }

    /// <summary>O Assalto inteiro de um turno. Revelado por completo na Bussola de Ameaca durante o Preparo.</summary>
    public sealed class TurnWaveSpec
    {
        public int TurnNumber;
        public SurgeSpec[] Surges = new SurgeSpec[0];
        /// <summary>Rotulo mostrado no cartao do turno ("o turno do Ninho").</summary>
        public string Label = "";
    }

    public sealed class ArenaSpec
    {
        /// <summary>Lado do grid construivel (11 => 11x11).</summary>
        public int GridSize = 11;
        /// <summary>Lado da Prefeitura em celulas (3 => ocupa 3x3 no centro).</summary>
        public int TownHallSize = 3;
        /// <summary>Raio dos Arredores em celulas, medido do centro. Monstros nascem nesse anel.</summary>
        public float OutskirtsRadius = 18f;

        public int TreeCount = 6;
        public int RockCount = 4;
        public int ChestCount = 2;
        /// <summary>Cota FIXA por turno: ficar 5 min no Preparo nao rende 1 de madeira a mais que ficar 60s.</summary>
        public float WoodPerTree = 15f;
        public float StonePerRock = 10f;
        public float GoldPerChest = 20f;
    }

    public sealed class MatchRulesSpec
    {
        public int TotalTurns = 9;
        public float TownHallMaxHealth = 600f;
        /// <summary>Nenhum golpe unico tira mais que esta fracao do HP maximo. Anti-one-shot.</summary>
        public float MaxSingleHitFraction = 0.25f;

        public float SiloBaseCapacity = 400f;
        public float SiloStartingWood = 160f;
        public float StartingStone = 0f;
        public float StartingGoldPerPlayer = 0f;

        public float PreparoMaxSeconds = 75f;
        public float PreparoFirstActSeconds = 60f;
        /// <summary>Quando o TERCEIRO jogador aperta Pronto, o relogio trava neste teto.</summary>
        public float PreparoClampOnThirdReady = 15f;
        public float BalancoSeconds = 25f;

        /// <summary>Curva de XP multiplicada pelo n de jogadores: agencia per capita identica a 1, 2, 3 ou 4.</summary>
        public float XpPerCityLevelBase = 45f;
        public float XpPerCityLevelGrowth = 1.3f;
        /// <summary>XP por unidade de recurso DEPOSITADA. Faz da Logistica um caminho de progresso,
        /// nao so de manutencao — quem passa o turno abastecendo o Silo tambem faz a cidade subir.</summary>
        public float XpPerResourceDeposited = 0.6f;

        public int DraftOptions = 3;
        public float DraftRerollCost = 4f;

        public int MaxPlayers = 4;
    }

    /// <summary>
    /// A simulacao obtem conteudo SOMENTE por esta interface, que ela mesma define.
    /// DT.Data implementa (via ScriptableObject); DT.Sim jamais depende de DT.Data.
    /// </summary>
    public interface IContentDatabase
    {
        MatchRulesSpec Rules { get; }
        ArenaSpec Arena { get; }

        TowerSpec GetTower(DefId id);
        MonsterSpec GetMonster(DefId id);
        HeroSpec GetHero(DefId id);

        /// <summary>Pool curado de cartas do dia 1. Todo item precisa mudar uma DECISAO, nunca um numero.</summary>
        System.Collections.Generic.IReadOnlyList<DefId> TowerPool { get; }
        System.Collections.Generic.IReadOnlyList<DefId> HeroPool { get; }

        /// <summary>
        /// Assalto do turno pedido (1-based), dimensionado para <paramref name="playerCount"/>.
        ///
        /// O volume PRECISA escalar com o numero de jogadores. Sem isso, a curva de XP — que e
        /// multiplicada por jogador para manter a agencia per capita — deixaria um time de quatro
        /// com menos cartas por pessoa do que um jogador solo, invertendo a intencao do design.
        /// </summary>
        TurnWaveSpec GetTurnWaves(int turnNumber, int playerCount);

        /// <summary>Hash do conteudo, comparado no handshake de rede: cliente com balanceamento diferente e rejeitado.</summary>
        int ContentHash { get; }
    }
}
