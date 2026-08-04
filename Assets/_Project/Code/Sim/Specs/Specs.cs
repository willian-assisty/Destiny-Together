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

        /// <summary>
        /// Rondador: ignora predio e persegue o heroi mais proximo de QUALQUER distancia.
        /// Os outros arquetipos so trocam de alvo se um heroi entrar no alcance de ataque —
        /// este comeca cacando. E o que da preco a estar longe de casa quando a noite cai.
        /// </summary>
        public bool HuntsHeroes = false;

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

        /// <summary>
        /// Nasce em QUALQUER ponto do anel, nao no setor de uma Faixa.
        ///
        /// A Faixa continua existindo — ela e derivada do angulo do ponto sorteado, entao o
        /// prognostico e o HUD seguem funcionando. O que muda e a leitura: em vez de oito jorros
        /// nascendo em oito pontos, a noite vira um cerco continuo. Faixa passa a ser o nome de
        /// um SETOR do cerco, que e como um time fala mesmo ("vaza no Norte").
        /// </summary>
        public bool AnyDirection;
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
        /// <summary>Lado do grid construivel. Impar para a Prefeitura ficar centrada de verdade.</summary>
        public int GridSize = 21;
        /// <summary>Lado da Prefeitura em celulas (5 => ocupa 5x5 no centro).</summary>
        public int TownHallSize = 5;
        /// <summary>Raio dos Arredores em celulas, medido do centro. Monstros nascem nesse anel.</summary>
        public float OutskirtsRadius = 36f;

        /// <summary>
        /// Recursos ficam agrupados nos QUATRO CANTOS em vez de espalhados em anel.
        /// Colher deixa de ser "andar em volta" e vira uma decisao de rota: ir ao canto custa
        /// tempo e distancia da Faixa quente, e cada canto fica naturalmente sob a guarda de um
        /// Quadrante — o que da a cada jogador um lugar seu sem impedir ninguem de socorrer.
        /// </summary>
        public float CornerSpread = 7f;

        // A cota subiu ~70% junto com a mudanca de ritmo, e nao e generosidade: sao 5 dias no
        // lugar de 9 turnos, entao a economia de uma partida inteira teria encolhido 44% ao
        // mesmo tempo em que a exposicao ao combate subiu 40%. Sem este ajuste o time entra na
        // noite 3 com a defesa que o modelo antigo tinha no turno 2.
        public int TreeCount = 20;
        public int RockCount = 14;
        public int ChestCount = 7;
        /// <summary>Cota FIXA por dia: ficar os 5 minutos inteiros nao rende 1 de madeira a mais
        /// que sair em 60s. E o que permite o Dia ter relogio longo sem virar farm obrigatorio —
        /// o tempo extra so vale para EXPLORAR, que e onde esta o retorno crescente.</summary>
        public float WoodPerTree = 15f;
        public float StonePerRock = 10f;
        public float GoldPerChest = 20f;

        // --- Exploracao ---

        /// <summary>Esconderijos escondidos na mata a cada amanhecer.</summary>
        public int CacheCount = 14;
        /// <summary>Fracao deles que e Relicario (o resto e Suprimento).</summary>
        public float RelicFraction = 0.22f;

        /// <summary>
        /// Anel onde os Esconderijos nascem, medido do centro.
        ///
        /// Comeca em 27 porque os bolsoes de recurso ficam em ~23,6 (ver MatchFactory): com o
        /// anel comecando em 22, quem so colhia tropecava nos Esconderijos de graca e explorar
        /// valia exatamente 0% a mais — foi o que a medicao mostrou. O limite externo encosta no
        /// anel de spawn de proposito: o achado mais valioso fica onde a horda nasce.
        /// </summary>
        public float CacheInnerRadius = 27f;
        public float CacheOuterRadius = 35f;

        /// <summary>Distancia em que o Esconderijo acende na tela.</summary>
        public float CacheRevealRadius = 7f;
        /// <summary>Distancia em que ele e recolhido ao passar por cima.</summary>
        public float CachePickupRadius = 1.4f;

        // Calibrado contra a colheita, nao no vacuo. Depositar rende XpPerResourceDeposited por
        // unidade, e um dia inteiro de colheita rende algumas centenas de XP — se o Esconderijo
        // valesse menos que isso por minuto investido, explorar seria uma armadilha educada, e a
        // medicao mostrou exatamente isso (-6% de nivel de cidade para quem saia da rota).
        // Explorar tambem CUSTA posicao: quem esta na mata quando escurece volta atrasado para a
        // defesa. O premio tem de pagar o risco, nao so o tempo.
        public float XpPerSupplyCache = 30f;
        public float GoldPerSupplyCache = 8f;
        /// <summary>Relicario vale ~4 Suprimentos. Achar um costuma valer um nivel de cidade.</summary>
        public float XpPerRelicCache = 115f;
        public float GoldPerRelicCache = 20f;

        // --- O mundo procedural alem da vila ---

        /// <summary>
        /// Ate onde o heroi pode se afastar. Nao e um limite de design, e um limite de seguranca:
        /// a 7 celulas/s levaria ~10 minutos correndo em linha reta para encostar nele, mais que
        /// um ciclo inteiro. Existe para que nenhuma coordenada saia do razoavel.
        /// </summary>
        public float ExplorableRadius = 4000f;

        /// <summary>Folga entre o anel de spawn e o inicio do mundo procedural, em celulas.</summary>
        public float WorldClearance = 10f;

        /// <summary>
        /// Em quantas celulas a fronteira vai de "pobre" a "rica". Curto demais e o gradiente
        /// vira degrau; longo demais e ninguem chega a sentir que valeu andar.
        /// </summary>
        public float WorldRichnessRange = 320f;

        /// <summary>
        /// Chance de um chunk ter Esconderijo, na borda do mundo procedural e bem longe.
        ///
        /// Baixo de proposito: um achado a cada ~15 chunks perto e a cada ~4 longe. Densidade alta
        /// transforma a mata num tapete de itens e o jogador para de escolher para onde ir — ele
        /// so anda. O que faz a distancia valer e o VALOR crescer, nao a quantidade.
        /// </summary>
        public float WorldCacheChanceNear = 0.07f;
        public float WorldCacheChanceFar = 0.26f;

        /// <summary>Raio, em chunks, em que a simulacao materializa o conteudo do mundo.</summary>
        public int WorldStreamRadiusChunks = 4;
    }

    public sealed class MatchRulesSpec
    {
        /// <summary>
        /// Noites ate a vitoria. Cinco, nao nove: com o ciclo de 5+5 minutos, nove turnos dariam
        /// uma sessao de 90 minutos. Cinco noites cabem em ~35-50 min, que e a duracao em que a
        /// partida ainda termina numa sentada — e o Dia encurta sozinho quando o time marca
        /// Pronto, entao um grupo rapido fecha em bem menos.
        /// </summary>
        public int TotalTurns = 5;

        /// <summary>
        /// A cidade e a unica barra de vida, e agora ela fica exposta por 255s por noite contra
        /// os ~100s de Assalto do modelo antigo. 900 preserva a sensacao de desgaste que 600
        /// dava: mesma fracao de barra por minuto de pressao, num minuto que ficou mais longo.
        /// </summary>
        public float TownHallMaxHealth = 900f;
        /// <summary>Nenhum golpe unico tira mais que esta fracao do HP maximo. Anti-one-shot.</summary>
        public float MaxSingleHitFraction = 0.25f;

        public float SiloBaseCapacity = 400f;
        public float SiloStartingWood = 160f;
        public float StartingStone = 0f;
        public float StartingGoldPerPlayer = 0f;

        /// <summary>Duracao do Dia. Teto, nao piso: todos Prontos antecipa a noite.</summary>
        public float DiaSeconds = 300f;
        /// <summary>Duracao da Noite. Piso E teto — a noite acaba na hora, sempre.</summary>
        public float NoiteSeconds = 300f;

        /// <summary>
        /// Quanto antes do amanhecer os monstros param de nascer.
        ///
        /// Sem isto, o alvorecer dissolveria uma horda inteira que acabou de entrar, e o jogador
        /// aprenderia a simplesmente esperar. Com isto, o ultimo minuto e uma limpeza de campo
        /// que o time ganha ou perde — e o que sobra ao amanhecer e residuo, nao a onda.
        /// </summary>
        public float SpawnCutoffBeforeDawn = 45f;

        /// <summary>Quando o TERCEIRO jogador aperta Pronto, o relogio do Dia trava neste teto.</summary>
        public float PreparoClampOnThirdReady = 20f;

        /// <summary>Curva de XP multiplicada pelo n de jogadores: agencia per capita identica a 1, 2, 3 ou 4.</summary>
        public float XpPerCityLevelBase = 38f;
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
