using System.Text;
using DestinyTogether.Core;
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
                case PhaseId.Dia: DrawDia(s, sim, input); break;
                case PhaseId.Noite: DrawNoite(s, sim); break;
            }

            DrawExpedition(s, sim, input);
        }

        /// <summary>
        /// A bussola do explorador: a que distancia da vila voce esta e se da tempo de voltar.
        ///
        /// Com o mundo procedural nao existe mais borda de mapa dizendo "voce foi longe demais",
        /// e o jogador precisa de ALGUM jeito de responder a unica pergunta que importa la fora —
        /// "consigo estar em casa quando escurecer?". Sem isto, ir longe vira aposta cega, que e
        /// exatamente o que o Dia seguro existe para evitar.
        ///
        /// So aparece quando voce ja saiu da area da vila: perto de casa e ruido.
        /// </summary>
        private void DrawExpedition(UiStyles s, MatchSimulation sim, InputRouter input)
        {
            var player = input?.LocalPlayer;
            var hero = player != null ? sim.State.GetHero(player.Hero) : null;
            if (hero == null) return;

            float distance = Vec2.Distance(hero.Position, sim.State.CityCenter);
            float home = sim.Content.Arena.OutskirtsRadius;
            if (distance < home) return;

            var spec = sim.Content.GetHero(hero.Def);
            float speed = spec != null && spec.MoveSpeed > 0.01f ? spec.MoveSpeed : 6f;
            float travel = distance / speed;
            float remaining = sim.SecondsRemainingInPhase();

            bool day = sim.State.Phase == PhaseId.Dia;
            // Margem de 20%: o caminho de volta nunca e uma reta limpa.
            bool canReturn = !day || travel * 1.2f <= remaining;

            GUILayout.BeginArea(new Rect(Screen.width * 0.5f - 190f, Screen.height - 210f, 380f, 62f), s.Panel);

            string tint = canReturn ? "#9fd6a0" : "#ff8a5c";
            GUILayout.Label($"<b>EXPEDICAO</b>   {distance - home:0} celulas alem dos Arredores", s.Mono);
            GUILayout.Label(day
                    ? $"volta em <color={tint}><b>{Clock(travel)}</b></color>   ·   " +
                      (canReturn ? "da tempo" : "<color=#ff8a5c><b>a noite te pega no caminho</b></color>")
                    : $"<color=#ff8a5c><b>voce esta fora na noite</b></color>   ·   volta em <b>{Clock(travel)}</b>",
                s.Small);

            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------------------

        private void DrawStatusBar(UiStyles s, MatchSimulation sim)
        {
            var st = sim.State;
            GUILayout.BeginArea(new Rect(12, 12, 520, 148), s.Panel);

            GUILayout.Label($"<b>Noite {st.TurnNumber}/{sim.Content.Rules.TotalTurns}</b>  ·  " +
                            $"{PhaseName(st.Phase)}  ·  <i>{sim.CurrentTurnWaves?.Label}</i>", s.Title);

            // O relogio do ciclo. Barra que esvazia e a leitura mais direta de "quanto falta",
            // e o rotulo diz o que vem DEPOIS — porque a pergunta do jogador nunca e "quanto
            // tempo de dia?", e sim "da tempo de ir ate ali e voltar antes de escurecer?".
            float remaining = sim.SecondsRemainingInPhase();
            float left = st.PhaseDuration > 0.01f ? remaining / st.PhaseDuration : 0f;
            bool day = st.Phase == PhaseId.Dia;
            string cycleColor = day ? "#ffd479" : "#7f9fd6";
            string next = day ? "ate anoitecer" : "ate amanhecer";

            GUILayout.Label($"<color={cycleColor}><b>{(day ? "SOL" : "LUA")}</b></color>  {Bar(left, 22)}  " +
                            $"<b>{Clock(remaining)}</b> {next}", s.Mono);

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
            GUILayout.Label(sim.State.Phase == PhaseId.Dia
                ? "A noite que vem — tudo revelado"
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

        private void DrawDia(UiStyles s, MatchSimulation sim, InputRouter input)
        {
            var player = input?.LocalPlayer;
            if (player == null) return;

            DrawHand(s, sim, input, player);
            DrawPlacementImpact(s, sim, input, player);
            DrawReadyPanel(s, sim, player);
            DrawDraft(s, sim, player);
        }

        private void DrawHand(UiStyles s, MatchSimulation sim, InputRouter input, PlayerState player)
        {
            GUILayout.BeginArea(new Rect(12, Screen.height - 152, 700, 140), s.Panel);
            GUILayout.Label("<b>SUA MAO</b>  <size=11>teclas 1-8 selecionam · clique ergue · " +
                            "so no seu Quadrante e encostando na cidade</size>", s.Title);

            if (player.Hand.Count == 0)
            {
                GUILayout.Label("<i>Sem cartas. Elas chegam quando a cidade sobe de nivel — " +
                                "depositar recurso e achar Esconderijo sao as duas fontes de XP.</i>", s.Label);
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
            GUILayout.BeginArea(new Rect(Screen.width - 280, 252, 268, 152), s.Panel);
            GUILayout.Label($"<b>DIA</b>  {Clock(sim.SecondsRemainingInPhase())}", s.Title);

            for (int i = 0; i < sim.State.Players.Count; i++)
            {
                var p = sim.State.Players[i];
                string hex = ColorUtility.ToHtmlStringRGB(PlaceholderVisuals.PlayerColor(p.Id.Index));
                string mark = p.IsAutomaton ? "<i>automato</i>" : (p.IsReady ? "<b>PRONTO</b>" : "no mapa");
                GUILayout.Label($"<color=#{hex}>■</color> {p.DisplayName} — {mark}", s.Label);
            }

            GUILayout.Space(4);
            GUILayout.Label(player.IsReady ? "<b>[R]</b> cancelar Pronto" : "<b>[R]</b> Pronto — antecipa a noite", s.Label);
            GUILayout.EndArea();
        }

        /// <summary>
        /// O draft agora e um painel de canto durante o Dia, nao uma tela que congela o mundo.
        ///
        /// A troca importa mais do que parece: a carta deixa de ser escolhida num vacuo e passa a
        /// ser escolhida COM o mapa a vista — dava para ver onde falta cobertura enquanto se le a
        /// opcao. E ninguem fica esperando os outros tres lerem.
        /// </summary>
        private void DrawDraft(UiStyles s, MatchSimulation sim, PlayerState player)
        {
            if (player.PendingDraftPicks <= 0 || player.DraftOptions.Count == 0) return;

            GUILayout.BeginArea(new Rect(Screen.width - 280, 412, 268, 268), s.Panel);
            GUILayout.Label($"<b>AMANHECEU — ESCOLHA</b>  ({player.PendingDraftPicks})", s.Title);
            GUILayout.Label($"<size=11>[1]-[3] escolhe · [Q] rerrola por {sim.Content.Rules.DraftRerollCost:0} " +
                            $"ouro (voce tem {player.Gold:0})</size>", s.Small);
            GUILayout.Space(4);

            for (int i = 0; i < player.DraftOptions.Count; i++)
            {
                var spec = sim.Content.GetTower(player.DraftOptions[i]);
                GUILayout.Label($"<b>[{i + 1}] {spec?.DisplayName}</b>  <size=11>{spec?.Tag}</size>\n" +
                                $"<size=11>{spec?.Description}</size>",
                                new GUIStyle(s.Label) { padding = new RectOffset(8, 8, 5, 5) },
                                GUILayout.Height(66));
            }

            GUILayout.EndArea();
        }

        private void DrawNoite(UiStyles s, MatchSimulation sim)
        {
            GUILayout.BeginArea(new Rect(Screen.width * 0.5f - 180f, 12f, 360f, 68f), s.Panel);

            bool dawnComing = sim.SecondsRemainingInPhase() <= sim.Content.Rules.SpawnCutoffBeforeDawn;

            if (dawnComing)
                GUILayout.Label("<color=#ffd479><b>O CEU ESTA CLAREANDO</b></color>", s.Big);
            else if (sim.State.InBreather)
                GUILayout.Label("<b>RESPIRO</b>", s.Big);
            else
                GUILayout.Label($"<b>INVESTIDA {sim.State.SurgeIndex + 1}</b>", s.Big);

            GUILayout.Label(dawnComing
                    ? "Nada mais vai nascer. Limpe o campo."
                    : sim.State.InBreather
                        ? "Deposite, repare — ou insista em dois kills."
                        : "WASD move · o ataque sai na direcao do movimento",
                s.Small);
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------------------

        private static string PhaseName(PhaseId phase) => phase switch
        {
            PhaseId.Dia => "DIA",
            PhaseId.Noite => "NOITE",
            PhaseId.Fim => "FIM",
            _ => "—"
        };

        private static string Clock(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int total = Mathf.CeilToInt(seconds);
            return $"{total / 60}:{total % 60:00}";
        }

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
