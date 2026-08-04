using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    /// <summary>Torre/predio construido. Ocupa um tile e e o alvo preferencial dos monstros.</summary>
    public sealed class TowerState
    {
        public EntityId Id;
        public DefId Def;
        public PlayerId Owner;
        public GridCoord Cell;
        public int Tier = 1;

        public float Health;
        public float MaxHealth;

        public float ShotCooldown;
        /// <summary>Cache de buffs de vizinhanca, recalculado ao construir/destruir.</summary>
        public float FireRateMultiplier = 1f;
        public float BonusRange = 0f;
        public bool InDistrict;
        public BuildingTag DistrictTag = BuildingTag.Nenhuma;

        public bool IsAlive => Health > 0f;
        public Vec2 Position => Cell.Center;
    }

    public sealed class MonsterState
    {
        public EntityId Id;
        public DefId Def;
        public Lane Lane;

        public Vec2 Position;
        public Vec2 Facing;
        public float Health;
        public float MaxHealth;

        public float AttackCooldown;
        public float SpawnCooldown;

        /// <summary>Slow ativo: multiplicador de velocidade (1 = sem slow) e tempo restante.</summary>
        public float SpeedMultiplier = 1f;
        public float SlowRemaining;

        /// <summary>Queimadura do Distrito Igneo: dano por segundo e tempo restante. Stacka em intensidade.</summary>
        public float BurnDps;
        public float BurnRemaining;

        /// <summary>Alvo estrutural atual. Recalculado periodicamente, nao a cada tick.</summary>
        public EntityId TargetStructure;
        public GridCoord TargetCell = GridCoord.Invalid;
        public float RetargetCooldown;

        public bool IsAlive => Health > 0f;
    }

    public sealed class HeroState
    {
        public EntityId Id;
        public DefId Def;
        public PlayerId Owner;

        public Vec2 Position;
        /// <summary>Ultima direcao de movimento nao-nula. O auto-ataque sai por aqui: o unico verbo e ESTAR.</summary>
        public Vec2 Facing = new Vec2(0f, 1f);
        public Vec2 MoveInput;

        public float Health;
        public float MaxHealth;
        public float AttackCooldown;

        /// <summary>Carga transportada, ainda NAO creditada. Precisa voltar e depositar na Prefeitura.</summary>
        public float CarriedWood;
        public float CarriedStone;
        public float CarriedGold;
        public float CarriedTotal => CarriedWood + CarriedStone + CarriedGold;

        public EntityId HarvestingNode;
        public float HarvestProgress;
        public float DepositProgress;

        /// <summary>
        /// Ultimo chunk em que este heroi esteve. O streamer do mundo so refaz trabalho quando
        /// alguem TROCA de chunk — andar dentro do mesmo quadrado de 24 celulas nao muda nada, e
        /// isso e a maior parte dos ticks.
        /// </summary>
        public int LastChunkX = int.MinValue;
        public int LastChunkZ = int.MinValue;

        /// <summary>Morto vira Espectro por alguns segundos: camera livre e ping — nunca fica sem funcao.</summary>
        public bool IsSpectre;
        public float RespawnRemaining;
        /// <summary>Urna caida no ponto da morte: companheiro que a levar a Prefeitura ANULA a morte.</summary>
        public bool HasPendingUrn;
        public Vec2 UrnPosition;

        public bool IsActive => !IsSpectre;
    }

    public sealed class HarvestNodeState
    {
        public EntityId Id;
        public HarvestNodeKind Kind;
        public Vec2 Position;
        /// <summary>Quantidade restante. Cota FIXA por dia: nao regenera alem disso.</summary>
        public float Remaining;
        public float TotalPerHarvest = 5f;
        public bool IsDepleted => Remaining <= 0f;

        /// <summary>
        /// Identidade no mundo procedural, ou 0 se veio dos bolsoes da vila.
        ///
        /// A diferenca e de regra, nao de origem: bolsao repovoa a cada amanhecer (cota fixa, e o
        /// que impede o Dia de virar farm), mundo la fora e de uso unico. Esta chave e o que
        /// permite lembrar SO o que foi consumido, em vez de guardar o mundo inteiro.
        /// </summary>
        public long WorldKey;
        public bool FromWorld => WorldKey != 0L;
    }

    /// <summary>
    /// Esconderijo escondido na mata. Nasce invisivel; um heroi que passa perto o REVELA, e
    /// encostar nele o recolhe.
    ///
    /// Dois raios em vez de um, e a diferenca e o jogo inteiro: revelar de longe transforma
    /// vagar pela floresta em algo que da retorno visivel ANTES de dar retorno mecanico. Um raio
    /// so faria o jogador andar no escuro sem saber se esta perto — o que se sente como sorte,
    /// nao como exploracao.
    /// </summary>
    public sealed class CacheState
    {
        public EntityId Id;
        public CacheKind Kind;
        public Vec2 Position;

        /// <summary>Ja apareceu na tela de alguem.</summary>
        public bool Revealed;
        /// <summary>Ja foi recolhido. Recolhido sai do mundo; revelado apenas acende.</summary>
        public bool Collected;

        /// <summary>Sobe o nivel da CIDADE — logo, uma carta para cada jogador.</summary>
        public float Xp;
        /// <summary>Pessoal de quem achou: reroll de draft.</summary>
        public float Gold;

        /// <summary>Identidade no mundo procedural, ou 0 se e um Esconderijo do dia, perto da vila.</summary>
        public long WorldKey;
        public bool FromWorld => WorldKey != 0L;
    }
}
