using System.Collections.Generic;

namespace DestinyTogether.Sim
{
    public sealed class PlayerState
    {
        public PlayerId Id;
        public string DisplayName = "";
        public HeroClass Class;

        /// <summary>Quadrante principal — onde o heroi nasce e onde o Tumulo e plantado.</summary>
        public Quadrant Quadrant;

        /// <summary>
        /// Mascara de quadrantes em que este jogador pode construir.
        ///
        /// Com quatro jogadores e um quadrante cada: soberania total, anti-dominancia por
        /// geografia. Com menos gente, os quadrantes vagos sao distribuidos — senao um jogador
        /// solo defenderia um quarto do perimetro contra ondas que vem dos oito lados, o que a
        /// simulacao mostrou ser uma derrota garantida no turno 1.
        /// </summary>
        public int QuadrantMask;

        public bool OwnsQuadrant(Quadrant q) => (QuadrantMask & (1 << (int)q)) != 0;
        public void GrantQuadrant(Quadrant q) => QuadrantMask |= 1 << (int)q;

        public bool IsConnected = true;
        /// <summary>Desconectado vira Automato: colhe e deposita, nunca constroi nem escolhe carta.</summary>
        public bool IsAutomaton;

        public bool IsReady;
        public float Gold;

        public EntityId Hero;

        /// <summary>Cartas na mao — cada uma vira um predio no Dia. Custo de erguer e zero: o XP ja pagou.</summary>
        public readonly List<DefId> Hand = new List<DefId>();

        /// <summary>
        /// Opcoes do draft privado, oferecidas no amanhecer e escolhiveis a qualquer hora do Dia.
        /// Nunca compartilhado entre jogadores.
        /// </summary>
        public readonly List<DefId> DraftOptions = new List<DefId>();
        public bool DraftResolved;
        public int PendingDraftPicks;

        public int TurnsSurvived;
        public float MetersHarvested;
        public int Deposits;
        public int Repairs;
        /// <summary>Esconderijos recolhidos. E o placar do explorador, ao lado do do construtor.</summary>
        public int CachesFound;

        /// <summary>
        /// Recolhidos HOJE. Zera a cada amanhecer, e cada achado do dia vale menos que o anterior.
        ///
        /// E o unico limitador possivel num mundo que nao acaba: com campo infinito de recompensa,
        /// qualquer valor fixo por achado faz o ganho crescer linearmente com o tempo gasto, e o
        /// Dia vira farm — a exata armadilha que a cota fixa de colheita existe para evitar. A
        /// medicao mostrou isso sem sutileza: 299 Esconderijos recolhidos num unico dia.
        ///
        /// O decaimento tambem torna o jogo mais cooperativo, e isso e o melhor dele: como os
        /// primeiros achados de cada um valem mais, quatro pessoas espalhadas rendem mais que
        /// quatro pessoas na mesma trilha.
        /// </summary>
        public int CachesFoundToday;
    }
}
