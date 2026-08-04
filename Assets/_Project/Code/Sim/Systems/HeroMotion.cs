using DestinyTogether.Core;

namespace DestinyTogether.Sim
{
    /// <summary>
    /// O integrador de movimento do heroi, isolado como funcao pura.
    ///
    /// Existe separado de <see cref="HeroSystem"/> por uma razao de rede, nao de organizacao.
    /// Quando o cliente prever o proprio heroi para esconder a latencia, ele tera de chamar
    /// EXATAMENTE a funcao que o host autoritativo chama. Duas copias do integrador divergem por
    /// construcao — nao por bug, por existirem: basta alguem corrigir uma delas. Uma funcao, dois
    /// chamadores, e a reconciliacao passa a medir latencia em vez de medir divergencia de codigo.
    ///
    /// Nada aqui le o estado de input do heroi: a direcao entra por parametro justamente porque o
    /// preditor a reaplica a partir do proprio anel de historico, nao do campo atual.
    /// </summary>
    public static class HeroMotion
    {
        /// <summary>Folga alem do anel de spawn. O heroi pode ir um pouco alem de onde a horda nasce.</summary>
        public const float OutskirtsMargin = 2.5f;

        /// <summary>
        /// Um tick de movimento. Devolve true se houve deslocamento.
        ///
        /// Sem input a posicao E o Facing ficam parados — de proposito: o auto-ataque sai na direcao
        /// do movimento, entao zerar o Facing ao soltar a tecla faria o heroi desapontar a mira
        /// sozinho no instante em que o jogador para para atacar.
        /// </summary>
        public static bool Step(MatchState state, HeroState hero, HeroSpec spec, Vec2 input, float dt)
        {
            if (state == null || hero == null || spec == null) return false;

            if (input.SqrMagnitude > 1f) input = input.Normalized;
            if (input.SqrMagnitude <= MathUtil.Epsilon) return false;

            hero.Facing = input.Normalized;
            hero.Position = ClampToWorld(state, hero.Position + input * (spec.MoveSpeed * dt));
            return true;
        }

        /// <summary>
        /// Limite circular do mundo. Publico porque a predicao do cliente precisa do mesmo limite:
        /// se o host clampa e o preditor nao, todo jogador que encosta na borda gera rollback.
        /// </summary>
        public static Vec2 ClampToWorld(MatchState state, Vec2 position)
        {
            var center = state.CityCenter;
            var offset = position - center;
            float limit = state.WorldRadius;
            return offset.Magnitude > limit ? center + offset.Normalized * limit : position;
        }
    }
}
