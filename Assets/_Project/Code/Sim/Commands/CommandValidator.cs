using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    /// <summary>
    /// A MESMA validacao roda no cliente (feedback imediato: botao cinza, tile vermelho) e no
    /// servidor (autoridade). Por isso vive na camada pura e nao depende de nada de engine.
    /// </summary>
    public static class CommandValidator
    {
        public static ValidationResult Validate(MatchState state, IContentDatabase content, in PlayerCommand cmd)
        {
            if (state == null) return ValidationResult.Fail("Sem estado");
            if (state.IsOver) return ValidationResult.Fail("Partida encerrada");

            var player = state.GetPlayer(cmd.Player);
            if (player == null) return ValidationResult.Fail("Jogador desconhecido");

            switch (cmd.Type)
            {
                case CommandType.BuildTower:
                    return ValidateBuild(state, content, player, cmd.Def, cmd.Cell);

                // Reparar e a unica acao de construcao que sobrevive a noite: a cura tem de estar
                // disponivel no momento em que o dano acontece, senao a Pedra viraria um recurso
                // que so se gasta depois que ja nao importa.
                case CommandType.Repair:
                    if (state.Stone <= 0f) return ValidationResult.Fail("Sem pedra");
                    if (!state.Grid.IsCityTile(cmd.Cell)) return ValidationResult.Fail("Nao ha nada para reparar ai");
                    return ValidationResult.Ok;

                case CommandType.ClearRubble:
                    if (state.Phase != PhaseId.Dia) return ValidationResult.Fail("So durante o Dia");
                    if (state.Grid.Get(cmd.Cell) != CellState.Escombro) return ValidationResult.Fail("Nao ha Escombro ai");
                    return ValidationResult.Ok;

                case CommandType.SetReady:
                    if (state.Phase != PhaseId.Dia) return ValidationResult.Fail("Pronto so vale durante o Dia");
                    if (player.IsAutomaton) return ValidationResult.Fail("Automato nao decide");
                    return ValidationResult.Ok;

                // O heroi anda de dia e de noite. Nao existe mais "mundo congelado": o amanhecer
                // e um instante, nao uma tela.
                case CommandType.MoveHero:
                case CommandType.StartHarvest:
                    if (state.Phase == PhaseId.Fim) return ValidationResult.Fail("Partida encerrada");
                    return ValidationResult.Ok;

                case CommandType.DonateCard:
                    if (state.Phase != PhaseId.Dia) return ValidationResult.Fail("Doacao so durante o Dia");
                    if (!player.Hand.Contains(cmd.Def)) return ValidationResult.Fail("Carta nao esta na sua mao");
                    if (state.GetPlayer(new PlayerId(cmd.IntValue)) == null) return ValidationResult.Fail("Destinatario invalido");
                    return ValidationResult.Ok;

                // O draft e oferecido no amanhecer e resolvido a qualquer momento do Dia. Antes
                // ele tinha uma fase so para si, que congelava o mundo enquanto quatro pessoas
                // liam tres cartas cada — tempo morto que agora e tempo de jogo.
                case CommandType.PickCard:
                    if (state.Phase != PhaseId.Dia) return ValidationResult.Fail("Draft so durante o Dia");
                    if (player.PendingDraftPicks <= 0) return ValidationResult.Fail("Nada para escolher");
                    if (cmd.IntValue < 0 || cmd.IntValue >= player.DraftOptions.Count)
                        return ValidationResult.Fail("Opcao inexistente");
                    return ValidationResult.Ok;

                case CommandType.RerollDraft:
                    if (state.Phase != PhaseId.Dia) return ValidationResult.Fail("Draft so durante o Dia");
                    if (player.PendingDraftPicks <= 0) return ValidationResult.Fail("Nada para rerrolar");
                    if (player.Gold < content.Rules.DraftRerollCost) return ValidationResult.Fail("Ouro insuficiente");
                    return ValidationResult.Ok;

                default:
                    return ValidationResult.Fail("Comando desconhecido");
            }
        }

        /// <summary>
        /// As tres regras de colocacao, na ordem em que o jogador as descobre:
        /// (1) tem a carta na mao, (2) e o seu Quadrante, (3) encosta na cidade.
        /// </summary>
        public static ValidationResult ValidateBuild(MatchState state, IContentDatabase content,
                                                     PlayerState player, DefId def, GridCoord cell)
        {
            // Construir e acao de DIA, e essa e a regra que da forma ao ciclo inteiro: a noite
            // e jogada com a base que voce ergueu enquanto havia luz, nunca com a que voce
            // improvisa quando ja esta apanhando.
            if (state.Phase != PhaseId.Dia)
                return ValidationResult.Fail("So da para erguer durante o Dia");
            if (player.IsAutomaton)
                return ValidationResult.Fail("Automato nao constroi");
            if (!player.Hand.Contains(def))
                return ValidationResult.Fail("Carta nao esta na sua mao");
            if (content.GetTower(def) == null)
                return ValidationResult.Fail("Predio desconhecido");
            if (!state.Grid.InBounds(cell))
                return ValidationResult.Fail("Fora do terreno");
            if (!player.OwnsQuadrant(state.Grid.QuadrantOf(cell)))
                return ValidationResult.Fail("Fora do seu Quadrante");

            var occupancy = state.Grid.Get(cell);
            if (occupancy == CellState.Escombro) return ValidationResult.Fail("Tem Escombro aqui");
            if (occupancy == CellState.Tumulo) return ValidationResult.Fail("Tem um Tumulo aqui");
            if (occupancy == CellState.Prefeitura) return ValidationResult.Fail("A Prefeitura ocupa este tile");

            // Duplicata sobre duplicata funde em tier maior — o unico upgrade intra-partida.
            if (occupancy == CellState.Predio)
            {
                var existing = state.GetTower(state.Grid.OccupantOf(cell));
                if (existing == null) return ValidationResult.Fail("Tile ocupado");
                if (existing.Def != def) return ValidationResult.Fail("So funde com predio igual");
                return ValidationResult.Ok;
            }

            if (!state.Grid.IsAdjacentToCity(cell))
                return ValidationResult.Fail("Precisa encostar na cidade");

            return ValidationResult.Ok;
        }
    }
}
