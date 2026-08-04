using System.Text;
using DestinyTogether.Data;
using DestinyTogether.Sim;
using UnityEngine;

namespace DestinyTogether.UI
{
    /// <summary>
    /// HUD da partida. Feio de proposito e descartavel por design — o que NAO e descartavel e a
    /// informacao que ele mostra, escolhida antes de existir tela:
    ///
    ///   1. O antes-e-depois do predio na mao (a divida do crescimento);
    ///   2. O Prognostico por Faixa, ao vivo, durante todo o Preparo;
    ///   3. O Silo, porque e a unica coisa que faz as torres pararem de funcionar.
    ///
    /// Quando a UI definitiva entrar, ela consome exatamente estes mesmos dados.
    /// </summary>
    public sealed class GameHud
    {
        private readonly StringBuilder _sb = new StringBuilder(256);

        public void Draw(UiStyles s, MatchSimulation sim, InputRouter input, LaneForecast[] forecast)
        {
            DrawStatusBar(s, sim);
            DrawLaneForecast(s, sim, forecast);

            switch (sim.State.Phase)
            {
                case PhaseId.Preparo: DrawPreparo(s, sim, input); break;
                case PhaseId.Assalto: DrawAssalto(s, sim); break;
                case PhaseId.Balanco: DrawBalanco(s, sim, input); break;
            }
        }

        // ------------------------------------------------------------------------------

        private void DrawStatusBar(UiStyles s, MatchSimulation sim)
        {
            var st = sim.State;
            GUILayout.BeginArea(new Rect(12, 12, 480, 126), s.Panel);

            GUILayout.Label($"<b>Turno {st.TurnNumber}/{sim.Content.Rules.TotalTurns}</b>  ·  " +
                            $"{PhaseName(st.Phase)}  ·  <i>{sim.CurrentTurnWaves?.Label}</i>", s.Title);

            float hp = st.TownHallMaxHealth > 0f ? st.TownHallHealth / st.TownHallMaxHealth : 0f;
            GUILayout.Label($"Prefeitura  {Bar(hp, 22)}  <b>{st.TownHallHealth:0}</b>/{st.TownHallMaxHealth:0}", s.Mono);

            float silo = st.SiloCapacity > 0f ? st.SiloWood / st.SiloCapacity : 0f;
            string warn = st.SiloWood <= 0f ? "  <color=#ff5555><b>SILO VAZIO — torres a 50%</b></color>" : "";
            GUILayout.Label($"Silo        {Bar(silo, 22)}  <b>{st.SiloWood:0}</b>/{st.SiloCapacity:0}{warn}", s.Mono);

            GUILayout.Label($"Pedra <b>{st.Stone:0}</b>  ·  Nivel da cidade <b>{st.CityLevel}</b>  ·  " +
                            $"XP {st.Xp:0}/{st.XpToNextLevel:0}  ·  Monstros <b>{st.Monsters.Count}</b>", s.Label);

            GUILayout.EndArea();
        }

        private void DrawLaneForecast(UiStyles s, MatchSimulation sim, LaneForecast[] forecast)
        {
            if (forecast == null) return;

            GUILayout.BeginArea(new Rect(Screen.width - 280, 12, 268, 232), s.Panel);
            GUILayout.Label("<b>BUSSOLA DE AMEACA</b>", s.Title);
            GUILayout.Label(sim.State.Phase == PhaseId.Preparo
                ? "Proxima Investida — tudo revelado"
                : $"Investida {sim.State.SurgeIndex + 1}", s.Small);
            GUILayout.Space(4);

            bool any = false;
            for (int i = 0; i < forecast.Length; i++)
            {
                var f = forecast[i];
                if (f.IncomingCount <= 0) continue;
                any = true;

                string color = ColorOf(f.Band);
                GUILayout.Label($"<b>{LaneGeometry.ShortName(f.Lane),-2}</b>  {f.IncomingCount,3} inim." +
                                $"  {f.ApproachSeconds,4:0.0}s  " +
                                $"<color={color}><b>{ForecastSystem.BandLabel(f.Band)}</b></color>" +
                                (f.EstimatedLeak > 0 ? $" <color={color}>{f.EstimatedLeak}</color>" : ""), s.Mono);
            }

            if (!any) GUILayout.Label("<i>Nada vem por enquanto.</i>", s.Small);

            GUILayout.EndArea();
        }

        private void DrawPreparo(UiStyles s, MatchSimulation sim, InputRouter input)
        {
            var player = input?.LocalPlayer;
            if (player == null) return;

            DrawHand(s, sim, input, player);
            DrawPlacementImpact(s, sim, input, player);
            DrawReadyPanel(s, sim, player);
        }

        private void DrawHand(UiStyles s, MatchSimulation sim, InputRouter input, PlayerState player)
        {
            GUILayout.BeginArea(new Rect(12, Screen.height - 152, 700, 140), s.Panel);
            GUILayout.Label("<b>SUA MAO</b>  <size=11>teclas 1-8 selecionam · clique ergue · " +
                            "so no seu Quadrante e encostando na cidade</size>", s.Title);

            if (player.Hand.Count == 0)
            {
                GUILayout.Label("<i>Sem cartas. Elas chegam quando a cidade sobe de nivel, no Balanco.</i>", s.Label);
            }
            else
            {
                GUILayout.BeginHorizontal();
                for (int i = 0; i < player.Hand.Count && i < 8; i++)
                {
                    var spec = sim.Content.GetTower(player.Hand[i]);
                    bool selected = input.SelectedCardIndex == i;

                    var style = new GUIStyle(s.Label)
                    {
                        alignment = TextAnchor.UpperLeft,
                        padding = new RectOffset(8, 8, 6, 6)
                    };
                    if (selected) style.normal.textColor = UiStyles.Accent;

                    string tag = spec != null && spec.Tag != BuildingTag.Nenhuma ? spec.Tag.ToString() : "—";
                    GUILayout.Label($"{(selected ? "▶ " : "")}<b>[{i + 1}] {spec?.DisplayName}</b>\n" +
                                    $"<size=11>{tag}\n{spec?.Description}</size>",
                                    style, GUILayout.Width(160), GUILayout.Height(98));
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
        }

        /// <summary>
        /// O painel que o roadmap chama de gate: se o jogador nao hesitar ao ver estes numeros
        /// mudarem, o design precisa de outra divida.
        /// </summary>
        private void DrawPlacementImpact(UiStyles s, MatchSimulation sim, InputRouter input, PlayerState player)
        {
            if (input.SelectedCardIndex < 0 || !input.HoveredCell.IsValid) return;
            if (!sim.State.Grid.InBounds(input.HoveredCell)) return;
            if (input.SelectedCardIndex >= player.Hand.Count) return;

            var def = player.Hand[input.SelectedCardIndex];
            var impact = sim.EvaluatePlacement(def, input.HoveredCell);

            GUILayout.BeginArea(new Rect(Screen.width * 0.5f - 260f, 150f, 520f, 112f), s.Panel);

            string lane = LaneGeometry.ShortName(impact.Lane);
            string approachColor = impact.ApproachDelta < -0.01f ? "#ff6b6b"
                                 : impact.ApproachDelta > 0.01f ? "#6bff8f" : "#cccccc";
            string perimeterColor = impact.PerimeterDelta > 0 ? "#ff6b6b" : "#cccccc";

            _sb.Clear();
            _sb.Append($"<b>{lane}</b>: aproximacao {impact.ApproachBefore:0.0}s → ");
            _sb.Append($"<color={approachColor}><b>{impact.ApproachAfter:0.0}s</b></color>");
            _sb.Append($"     perimetro exposto {impact.PerimeterBefore} → ");
            _sb.Append($"<color={perimeterColor}><b>{impact.PerimeterAfter}</b></color>");
            GUILayout.Label(_sb.ToString(), s.Mono);

            _sb.Clear();
            _sb.Append($"PROGNOSTICO <color={ColorOf(impact.BandBefore)}>{ForecastSystem.BandLabel(impact.BandBefore)}</color>");
            _sb.Append($" → <color={ColorOf(impact.BandAfter)}><b>{ForecastSystem.BandLabel(impact.BandAfter)}</b></color>");
            if (impact.LeakAfter > 0) _sb.Append($" <color={ColorOf(impact.BandAfter)}>{impact.LeakAfter}</color>");
            _sb.Append($"     DPS na Faixa {impact.DpsBefore:0} → <b>{impact.DpsAfter:0}</b>");
            GUILayout.Label(_sb.ToString(), s.Mono);

            if (!input.HoverValid)
                GUILayout.Label("<color=#ff6b6b>Colocacao invalida aqui.</color>", s.Label);
            else if (impact.WorsensBand)
                GUILayout.Label("<color=#ffcc44><b>Este predio piora a defesa desta Faixa.</b> " +
                                "Ele encurta o corredor mais do que compensa em dano.</color>", s.Label);

            GUILayout.EndArea();
        }

        private void DrawReadyPanel(UiStyles s, MatchSimulation sim, PlayerState player)
        {
            GUILayout.BeginArea(new Rect(Screen.width - 280, 252, 268, 136), s.Panel);
            GUILayout.Label($"<b>PREPARO</b>  {sim.SecondsRemainingInPhase():0}s", s.Title);

            for (int i = 0; i < sim.State.Players.Count; i++)
            {
                var p = sim.State.Players[i];
                string hex = ColorUtility.ToHtmlStringRGB(PlaceholderVisuals.PlayerColor(p.Id.Index));
                string mark = p.IsAutomaton ? "<i>automato</i>" : (p.IsReady ? "<b>PRONTO</b>" : "planejando");
                GUILayout.Label($"<color=#{hex}>■</color> {p.DisplayName} — {mark}", s.Label);
            }

            GUILayout.Space(4);
            GUILayout.Label(player.IsReady ? "<b>[R]</b> cancelar Pronto" : "<b>[R]</b> marcar Pronto", s.Label);
            GUILayout.EndArea();
        }

        private void DrawAssalto(UiStyles s, MatchSimulation sim)
        {
            GUILayout.BeginArea(new Rect(Screen.width * 0.5f - 160f, 12f, 320f, 64f), s.Panel);

            if (sim.State.InBreather)
                GUILayout.Label("<b>RESPIRO</b>", s.Big);
            else
                GUILayout.Label($"<b>INVESTIDA {sim.State.SurgeIndex + 1}</b>", s.Big);

            GUILayout.Label(sim.State.InBreather
                ? "Deposite, repare — ou insista em dois kills."
                : "WASD move · o ataque sai na direcao do movimento", s.Small);
            GUILayout.EndArea();
        }

        private void DrawBalanco(UiStyles s, MatchSimulation sim, InputRouter input)
        {
            var player = input?.LocalPlayer;
            if (player == null) return;

            GUILayout.BeginArea(new Rect(Screen.width * 0.5f - 330f, Screen.height * 0.5f - 140f, 660f, 280f), s.Panel);
            GUILayout.Label($"<b>BALANCO</b> — cidade nivel {sim.State.CityLevel}  ·  " +
                            $"{sim.SecondsRemainingInPhase():0}s", s.Title);

            if (player.PendingDraftPicks <= 0)
            {
                GUILayout.Label("Nada a escolher neste turno.", s.Label);
                GUILayout.Label("<size=11>Cartas chegam quando a cidade sobe de nivel. XP vem de matar " +
                                "e de DEPOSITAR — quem abastece o Silo tambem faz a cidade crescer.</size>", s.Small);
            }
            else
            {
                GUILayout.Label($"<b>Escolha uma carta</b> ({player.PendingDraftPicks} restante(s))  ·  " +
                                $"[Q] rerrolar por {sim.Content.Rules.DraftRerollCost:0} ouro " +
                                $"(voce tem {player.Gold:0})", s.Label);
                GUILayout.Label("<size=11>Draft privado: cada jogador escolhe o seu, ao mesmo tempo.</size>", s.Small);
                GUILayout.Space(6);

                GUILayout.BeginHorizontal();
                for (int i = 0; i < player.DraftOptions.Count; i++)
                {
                    var spec = sim.Content.GetTower(player.DraftOptions[i]);
                    GUILayout.Label($"<b>[{i + 1}] {spec?.DisplayName}</b>\n<size=11>{spec?.Tag}\n\n{spec?.Description}</size>",
                                    new GUIStyle(s.Label) { padding = new RectOffset(10, 10, 8, 8) },
                                    GUILayout.Width(202), GUILayout.Height(150));
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------------------

        private static string PhaseName(PhaseId phase) => phase switch
        {
            PhaseId.Preparo => "PREPARO",
            PhaseId.Assalto => "ASSALTO",
            PhaseId.Balanco => "BALANCO",
            PhaseId.Fim => "FIM",
            _ => "—"
        };

        private static string ColorOf(ForecastBand band) => band switch
        {
            ForecastBand.Segura => "#4ddb73",
            ForecastBand.Vaza => "#fac633",
            _ => "#ff4040"
        };

        private static string Bar(float ratio, int width)
        {
            int filled = Mathf.Clamp(Mathf.RoundToInt(ratio * width), 0, width);
            return "[" + new string('|', filled) + new string('.', width - filled) + "]";
        }
    }
}
