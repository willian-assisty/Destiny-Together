using DestinyTogether.Sim;

namespace DestinyTogether.UI
{
    /// <summary>
    /// O que o menu decide antes de a partida existir.
    ///
    /// Separado do Bootstrap de proposito: quando o multiplayer entrar, e este objeto que o host
    /// envia aos clientes no handshake — nao um punhado de campos espalhados pelo inspector.
    /// </summary>
    public struct MatchSetup
    {
        public int PlayerCount;
        public int LocalPlayerIndex;
        public int Seed;
        /// <summary>Heroi escolhido por assento. Assentos sem escolha caem para o pool.</summary>
        public DefId[] Heroes;

        public static MatchSetup Default(int playerCount = 1) => new MatchSetup
        {
            PlayerCount = playerCount,
            LocalPlayerIndex = 0,
            Seed = 0,
            Heroes = new DefId[4]
        };
    }
}
