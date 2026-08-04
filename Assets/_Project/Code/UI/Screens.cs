using DestinyTogether.Data;
using DestinyTogether.Sim;
using UnityEngine;

namespace DestinyTogether.UI
{
    public enum ScreenAction
    {
        None = 0,
        Jogar,
        Continuar,
        Reiniciar,
        VoltarAoMenu,
        Sair
    }

    /// <summary>
    /// Menu inicial, pausa e tela de resultado. Tudo em IMGUI e sem nenhum asset — some junto
    /// com o placeholder quando a UI definitiva entrar.
    /// </summary>
    public sealed class Screens
    {
        public MatchSetup Setup = MatchSetup.Default();

        private string _seedText = "";
        private int _configuringSeat;

        // ------------------------------------------------------------------------------
        // MENU INICIAL
        // ------------------------------------------------------------------------------

        public ScreenAction DrawMainMenu(UiStyles s, IContentDatabase content)
        {
            s.DrawVeil();
            var action = ScreenAction.None;

            float w = 620f, h = 560f;
            GUILayout.BeginArea(new Rect((Screen.width - w) * 0.5f, Mathf.Max(20f, (Screen.height - h) * 0.5f), w, h));

            GUILayout.Space(6);
            GUILayout.Label("DESTINY  TOGETHER", s.Huge);
            GUILayout.Label("<i>De dia a mata esconde coisas. De noite ela devolve monstros. " +
                            "Cinco noites — defendam juntos.</i>", s.Label);
            GUILayout.Space(14);

            GUILayout.BeginVertical(s.Panel);

            // --- Jogadores ---
            GUILayout.Label("<b>ASSENTOS NA PARTIDA</b>", s.Title);
            GUILayout.BeginHorizontal();
            for (int i = 1; i <= 4; i++)
                if (s.Choice(i.ToString(), Setup.PlayerCount == i, GUILayout.Height(34)))
                {
                    Setup.PlayerCount = i;
                    if (Setup.LocalPlayerIndex >= i) Setup.LocalPlayerIndex = 0;
                    if (_configuringSeat >= i) _configuringSeat = 0;
                }
            GUILayout.EndHorizontal();
            GUILayout.Label(Setup.PlayerCount == 1
                ? "Solo: voce recebe os quatro Quadrantes do tabuleiro."
                : $"Os 4 Quadrantes sao repartidos entre {Setup.PlayerCount}. " +
                  "Assentos sem humano viram Automatos: colhem e depositam, mas nunca constroem.",
                s.Small);

            GUILayout.Space(10);

            // --- Assento local ---
            if (Setup.PlayerCount > 1)
            {
                GUILayout.Label("<b>VOCE CONTROLA O ASSENTO</b>", s.Title);
                GUILayout.BeginHorizontal();
                for (int i = 0; i < Setup.PlayerCount; i++)
                {
                    var c = PlaceholderVisuals.PlayerColor(i);
                    string hex = ColorUtility.ToHtmlStringRGB(c);
                    if (s.Choice($"<color=#{hex}>■</color> {i + 1}", Setup.LocalPlayerIndex == i, GUILayout.Height(30)))
                        Setup.LocalPlayerIndex = i;
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(10);
            }
            else
            {
                Setup.LocalPlayerIndex = 0;
            }

            // --- Herois ---
            GUILayout.Label("<b>CLASSES</b>", s.Title);

            if (Setup.PlayerCount > 1)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Configurando assento:", s.Label, GUILayout.Width(140));
                for (int i = 0; i < Setup.PlayerCount; i++)
                    if (s.Choice((i + 1).ToString(), _configuringSeat == i, GUILayout.Width(34), GUILayout.Height(24)))
                        _configuringSeat = i;
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
            }
            else
            {
                _configuringSeat = 0;
            }

            DrawHeroPicker(s, content);

            GUILayout.Space(10);

            // --- Seed ---
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>SEED</b>", s.Title, GUILayout.Width(70));
            _seedText = GUILayout.TextField(_seedText, 12, GUILayout.Width(140), GUILayout.Height(24));
            GUILayout.Label("vazio ou 0 = aleatoria. Fixe para repetir a mesma partida.", s.Small);
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            GUILayout.Space(14);
            if (GUILayout.Button("<b>JOGAR</b>", s.ButtonOn, GUILayout.Height(46)))
            {
                Setup.Seed = int.TryParse(_seedText, out int parsed) ? parsed : 0;
                action = ScreenAction.Jogar;
            }

            GUILayout.Space(8);
            GUILayout.Label(
                "<b>WASD</b> move · o ataque sai na direcao do movimento, sem mira    " +
                "<b>1-8</b> carta · <b>clique</b> ergue · <b>botao direito</b> cancela\n" +
                "<b>R</b> Pronto · <b>scroll</b> zoom · <b>ESC</b> pausa · <b>F1</b> painel de teste",
                s.Small);

            if (GUILayout.Button("Sair", s.Button, GUILayout.Height(28)))
                action = ScreenAction.Sair;

            GUILayout.EndArea();
            return action;
        }

        private void DrawHeroPicker(UiStyles s, IContentDatabase content)
        {
            var pool = content.HeroPool;
            if (pool == null || pool.Count == 0) return;

            if (Setup.Heroes == null || Setup.Heroes.Length < 4) Setup.Heroes = new DefId[4];

            var current = Setup.Heroes[_configuringSeat];

            GUILayout.BeginHorizontal();
            for (int i = 0; i < pool.Count; i++)
            {
                var spec = content.GetHero(pool[i]);
                if (spec == null) continue;

                bool selected = current == pool[i] || (!current.IsValid && i == _configuringSeat % pool.Count);
                var color = PlaceholderVisuals.Get(pool[i]).Color;
                string hex = ColorUtility.ToHtmlStringRGB(color);

                if (s.Choice($"<color=#{hex}>●</color> {spec.DisplayName}", selected,
                             GUILayout.Height(30), GUILayout.Width(138)))
                    Setup.Heroes[_configuringSeat] = pool[i];
            }
            GUILayout.EndHorizontal();

            var chosen = content.GetHero(Setup.Heroes[_configuringSeat]);
            if (!Setup.Heroes[_configuringSeat].IsValid)
                chosen = content.GetHero(pool[_configuringSeat % pool.Count]);
            if (chosen != null)
                GUILayout.Label(HeroBlurb(chosen), s.Small);
        }

        private static string HeroBlurb(HeroSpec h)
            => $"{h.MaxHealth:0} HP · velocidade {h.MoveSpeed:0.0} · dano {h.AttackDamage:0} a cada " +
               $"{h.AttackInterval:0.00}s · alcance {h.AttackRadius:0.0} · carga {h.CarryCapacity:0}" +
               (h.HarvestSpeedMultiplier > 1f ? $" · colhe {h.HarvestSpeedMultiplier:0.#}x mais rapido" : "");

        // ------------------------------------------------------------------------------
        // PAUSA
        // ------------------------------------------------------------------------------

        public ScreenAction DrawPause(UiStyles s, MatchSimulation sim)
        {
            s.DrawVeil();
            var action = ScreenAction.None;

            float w = 380f, h = 280f;
            GUILayout.BeginArea(new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h), s.Panel);

            GUILayout.Label("<b>PAUSA</b>", s.Big);
            GUILayout.Label($"Noite {sim.State.TurnNumber}/{sim.Content.Rules.TotalTurns} · " +
                            $"{sim.State.Phase} · Prefeitura {sim.State.TownHallHealth:0}", s.Label);
            GUILayout.Space(14);

            if (GUILayout.Button("Continuar", s.ButtonOn, GUILayout.Height(38))) action = ScreenAction.Continuar;
            GUILayout.Space(6);
            if (GUILayout.Button("Reiniciar partida", s.Button, GUILayout.Height(32))) action = ScreenAction.Reiniciar;
            GUILayout.Space(6);
            if (GUILayout.Button("Voltar ao menu", s.Button, GUILayout.Height(32))) action = ScreenAction.VoltarAoMenu;

            GUILayout.EndArea();
            return action;
        }

        // ------------------------------------------------------------------------------
        // RESULTADO
        // ------------------------------------------------------------------------------

        public ScreenAction DrawResult(UiStyles s, MatchSimulation sim)
        {
            s.DrawVeil();
            var action = ScreenAction.None;
            bool win = sim.State.Outcome == MatchOutcome.Vitoria;

            float w = 560f, h = 420f;
            GUILayout.BeginArea(new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h), s.Panel);

            GUILayout.Label(win ? "<color=#4ddb73><b>A CIDADE RESISTIU</b></color>"
                                : "<color=#ff5555><b>A PREFEITURA CAIU</b></color>", s.Big);
            GUILayout.Space(4);
            GUILayout.Label(win
                ? $"Cinco amanheceres, um kaiju, e a planta baixa continua de pe."
                : $"A cidade aguentou ate a noite {sim.State.TurnNumber} de {sim.Content.Rules.TotalTurns}.",
                s.Label);

            GUILayout.Space(12);
            GUILayout.Label($"Nivel final da cidade: <b>{sim.State.CityLevel}</b>     " +
                            $"Predios de pe: <b>{sim.State.Towers.Count}</b>     " +
                            $"Silo: <b>{sim.State.SiloWood:0}</b>", s.Mono);

            GUILayout.Space(10);
            GUILayout.Label("<b>QUATRO PLACARES QUE NAO SE COMPARAM</b>", s.Title);
            GUILayout.Label("<i>De proposito: nao existe medidor de dano neste jogo.</i>", s.Small);
            GUILayout.Space(4);

            for (int i = 0; i < sim.State.Players.Count; i++)
            {
                var p = sim.State.Players[i];
                var c = PlaceholderVisuals.PlayerColor(p.Id.Index);
                string hex = ColorUtility.ToHtmlStringRGB(c);
                GUILayout.Label($"<color=#{hex}>■</color> {p.DisplayName,-11} " +
                                $"depositos <b>{p.Deposits,3}</b>   reparos <b>{p.Repairs,3}</b>   " +
                                $"achados <b>{p.CachesFound,3}</b>   noites <b>{p.TurnsSurvived,2}</b>   " +
                                $"ouro <b>{p.Gold,4:0}</b>", s.Mono);
            }

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Jogar de novo", s.ButtonOn, GUILayout.Height(38))) action = ScreenAction.Reiniciar;
            if (GUILayout.Button("Menu", s.Button, GUILayout.Height(38), GUILayout.Width(140)))
                action = ScreenAction.VoltarAoMenu;
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
            return action;
        }
    }
}
