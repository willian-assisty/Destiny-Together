using System.Text;
using DestinyTogether.Data;
using DestinyTogether.Sim;
using UnityEngine;

namespace DestinyTogether.UI
{
    /// <summary>
    /// HUD placeholder em IMGUI. Feio de proposito e descartavel por design — o que NAO e
    /// descartavel e a informacao que ele mostra, que foi escolhida antes da arte:
    ///
    ///   1. O antes-e-depois do predio na mao (a divida do crescimento);
    ///   2. O Prognostico por Faixa, ao vivo, durante todo o Preparo;
    ///   3. O Silo, porque e a unica coisa que faz as torres pararem de funcionar.
    ///
    /// Quando a UI definitiva entrar (UI Toolkit), ela consome exatamente estes mesmos dados.
    /// </summary>
    public sealed class GameHud : MonoBehaviour
    {
        private MatchSimulation _sim;
        private InputRouter _input;
        private LaneForecast[] _forecast;

        private GUIStyle _panel, _label, _title, _big, _mono;
        private Texture2D _panelTex;
        private readonly StringBuilder _sb = new StringBuilder(256);

        public void Initialize(MatchSimulation sim, InputRouter input, LaneForecast[] forecast)
        {
            _sim = sim;
            _input = input;
            _forecast = forecast;
        }

        private void EnsureStyles()
        {
            if (_panel != null) return;

            _panelTex = new Texture2D(1, 1);
            _panelTex.SetPixel(0, 0, new Color(0.05f, 0.06f, 0.08f, 0.86f));
            _panelTex.Apply();

            _panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(12, 12, 10, 10) };
            _panel.normal.background = _panelTex;

            _label = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
            _label.normal.textColor = new Color(0.88f, 0.90f, 0.94f);

            _title = new GUIStyle(_label) { fontSize = 15, fontStyle = FontStyle.Bold };
            _big = new GUIStyle(_label) { fontSize = 22, fontStyle = FontStyle.Bold };
            _mono = new GUIStyle(_label) { fontSize = 14, font = Font.CreateDynamicFontFromOSFont("Consolas", 14) };
        }

        private void OnGUI()
        {
            if (_sim == null) return;
            EnsureStyles();

            DrawStatusBar();
            DrawLaneForecast();

            switch (_sim.State.Phase)
            {
                case PhaseId.Preparo: DrawPreparo(); break;
                case PhaseId.Assalto: DrawAssalto(); break;
                case PhaseId.Balanco: DrawBalanco(); break;
                case PhaseId.Fim: DrawFim(); break;
            }
        }

        // ------------------------------------------------------------------------------

        private void DrawStatusBar()
        {
            var s = _sim.State;
            GUILayout.BeginArea(new Rect(12, 12, 470, 128), _panel);

            GUILayout.Label($"<b>DESTINY TOGETHER</b>  ·  Turno {s.TurnNumber}/{_sim.Content.Rules.TotalTurns}" +
                            $"  ·  {PhaseName(s.Phase)}  ·  {_sim.CurrentTurnWaves?.Label}", _title);

            float hpRatio = s.TownHallMaxHealth > 0f ? s.TownHallHealth / s.TownHallMaxHealth : 0f;
            GUILayout.Label($"Prefeitura  {Bar(hpRatio, 22)}  <b>{s.TownHallHealth:0}</b>/{s.TownHallMaxHealth:0}", _mono);

            float siloRatio = s.SiloCapacity > 0f ? s.SiloWood / s.SiloCapacity : 0f;
            string siloWarn = s.SiloWood <= 0f ? "  <color=#ff5555><b>SILO VAZIO — torres a 50%</b></color>" : "";
            GUILayout.Label($"Silo        {Bar(siloRatio, 22)}  <b>{s.SiloWood:0}</b>/{s.SiloCapacity:0}{siloWarn}", _mono);

            GUILayout.Label($"Pedra <b>{s.Stone:0}</b>   ·   Nivel da cidade <b>{s.CityLevel}</b>   " +
                            $"·   XP {s.Xp:0}/{s.XpToNextLevel:0}   ·   Monstros vivos <b>{s.Monsters.Count}</b>", _label);

            GUILayout.EndArea();
        }

        private void DrawLaneForecast()
        {
            if (_forecast == null) return;

            GUILayout.BeginArea(new Rect(Screen.width - 268, 12, 256, 226), _panel);
            GUILayout.Label("<b>BUSSOLA DE AMEACA</b>", _title);
            GUILayout.Label(_sim.State.Phase == PhaseId.Preparo
                ? "Proxima Investida"
                : $"Investida {_sim.State.SurgeIndex + 1}", _label);
            GUILayout.Space(4);

            for (int i = 0; i < _forecast.Length; i++)
            {
                var f = _forecast[i];
                if (f.IncomingCount <= 0) continue;

                string color = ColorOf(f.Band);
                GUILayout.Label($"<b>{LaneGeometry.ShortName(f.Lane),-2}</b>  " +
                                $"{f.IncomingCount,3} inim.  {f.ApproachSeconds,4:0.0}s  " +
                                $"<color={color}><b>{ForecastSystem.BandLabel(f.Band)}</b></color>" +
                                (f.EstimatedLeak > 0 ? $" <color={color}>{f.EstimatedLeak}</color>" : ""), _mono);
            }

            GUILayout.EndArea();
        }

        private void DrawPreparo()
        {
            var player = _input?.LocalPlayer;
            if (player == null) return;

            DrawHand(player);
            DrawPlacementImpact(player);
            DrawReadyPanel(player);
        }

        private void DrawHand(PlayerState player)
        {
            GUILayout.BeginArea(new Rect(12, Screen.height - 150, 640, 138), _panel);
            GUILayout.Label($"<b>SUA MAO</b>  (teclas 1-{Mathf.Max(1, player.Hand.Count)} para selecionar · " +
                            $"clique para erguer · botao direito cancela)", _title);

            if (player.Hand.Count == 0)
            {
                GUILayout.Label("<i>Sem cartas. Elas vem quando a cidade sobe de nivel no Balanco.</i>", _label);
            }
            else
            {
                GUILayout.BeginHorizontal();
                for (int i = 0; i < player.Hand.Count; i++)
                {
                    var spec = _sim.Content.GetTower(player.Hand[i]);
                    bool selected = _input.SelectedCardIndex == i;
                    string name = spec?.DisplayName ?? "?";
                    string tag = spec != null && spec.Tag != BuildingTag.Nenhuma ? spec.Tag.ToString() : "-";

                    var style = new GUIStyle(_label)
                    {
                        alignment = TextAnchor.UpperLeft,
                        wordWrap = true,
                        padding = new RectOffset(8, 8, 6, 6)
                    };
                    if (selected) style.normal.textColor = new Color(1f, 0.92f, 0.45f);

                    GUILayout.Label($"{(selected ? "▶ " : "")}<b>[{i + 1}] {name}</b>\n" +
                                    $"<size=11>{tag}\n{spec?.Description}</size>",
                                    style, GUILayout.Width(150), GUILayout.Height(96));
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
        }

        /// <summary>
        /// O painel que o roadmap chama de gate da Fase 1: se o jogador nao hesitar ao ver
        /// estes numeros mudarem, o design precisa de outra divida.
        /// </summary>
        private void DrawPlacementImpact(PlayerState player)
        {
            if (_input.SelectedCardIndex < 0 || !_input.HoveredCell.IsValid) return;
            if (!_sim.State.Grid.InBounds(_input.HoveredCell)) return;

            var def = player.Hand[_input.SelectedCardIndex];
            var impact = _sim.EvaluatePlacement(def, _input.HoveredCell);

            var rect = new Rect(Screen.width * 0.5f - 250f, 150f, 500f, 108f);
            GUILayout.BeginArea(rect, _panel);

            string lane = LaneGeometry.ShortName(impact.Lane);
            string approachColor = impact.ApproachDelta < -0.01f ? "#ff6b6b"
                                 : impact.ApproachDelta > 0.01f ? "#6bff8f" : "#cccccc";
            string perimeterColor = impact.PerimeterDelta > 0 ? "#ff6b6b" : "#cccccc";

            _sb.Clear();
            _sb.Append($"<b>{lane}</b>: aproximacao ");
            _sb.Append($"{impact.ApproachBefore:0.0}s → <color={approachColor}><b>{impact.ApproachAfter:0.0}s</b></color>");
            _sb.Append($"     perimetro exposto {impact.PerimeterBefore} → ");
            _sb.Append($"<color={perimeterColor}><b>{impact.PerimeterAfter}</b></color>");
            GUILayout.Label(_sb.ToString(), _mono);

            _sb.Clear();
            _sb.Append("PROGNOSTICO ");
            _sb.Append($"<color={ColorOf(impact.BandBefore)}>{ForecastSystem.BandLabel(impact.BandBefore)}</color>");
            _sb.Append(" → ");
            _sb.Append($"<color={ColorOf(impact.BandAfter)}><b>{ForecastSystem.BandLabel(impact.BandAfter)}</b></color>");
            if (impact.LeakAfter > 0) _sb.Append($" <color={ColorOf(impact.BandAfter)}>{impact.LeakAfter}</color>");
            _sb.Append($"     DPS na Faixa {impact.DpsBefore:0} → <b>{impact.DpsAfter:0}</b>");
            GUILayout.Label(_sb.ToString(), _mono);

            if (impact.WorsensBand)
                GUILayout.Label("<color=#ffcc44><b>Este predio piora a defesa desta Faixa.</b> " +
                                "Ele encurta o corredor mais do que compensa em dano.</color>", _label);
            else if (!_input.HoverValid)
                GUILayout.Label("<color=#ff6b6b>Colocacao invalida: so no seu Quadrante e encostando na cidade.</color>", _label);

            GUILayout.EndArea();
        }

        private void DrawReadyPanel(PlayerState player)
        {
            GUILayout.BeginArea(new Rect(Screen.width - 268, 248, 256, 132), _panel);
            GUILayout.Label($"<b>PREPARO</b>  {_sim.SecondsRemainingInPhase():0}s", _title);

            for (int i = 0; i < _sim.State.Players.Count; i++)
            {
                var p = _sim.State.Players[i];
                var c = PlaceholderVisuals.PlayerColor(p.Id.Index);
                string hex = ColorUtility.ToHtmlStringRGB(c);
                string mark = p.IsReady ? "PRONTO" : "planejando";
                GUILayout.Label($"<color=#{hex}>■</color> {p.DisplayName} — {mark}", _label);
            }

            GUILayout.Space(4);
            GUILayout.Label(player.IsReady
                ? "<b>[R]</b> cancelar Pronto"
                : "<b>[R]</b> marcar Pronto", _label);
            GUILayout.EndArea();
        }

        private void DrawAssalto()
        {
            var s = _sim.State;
            var rect = new Rect(Screen.width * 0.5f - 150f, 12f, 300f, 62f);
            GUILayout.BeginArea(rect, _panel);

            if (s.InBreather)
                GUILayout.Label($"<b>RESPIRO</b> — deposite, repare, respire", _big);
            else
                GUILayout.Label($"<b>INVESTIDA {s.SurgeIndex + 1}</b>", _big);

            GUILayout.Label("WASD move · o ataque sai na direcao do movimento", _label);
            GUILayout.EndArea();
        }

        private void DrawBalanco()
        {
            var player = _input?.LocalPlayer;
            if (player == null) return;

            var rect = new Rect(Screen.width * 0.5f - 320f, Screen.height * 0.5f - 130f, 640f, 260f);
            GUILayout.BeginArea(rect, _panel);
            GUILayout.Label($"<b>BALANCO</b> — cidade nivel {_sim.State.CityLevel}  ·  {_sim.SecondsRemainingInPhase():0}s", _title);

            if (player.PendingDraftPicks <= 0)
            {
                GUILayout.Label("Nada a escolher. Aguardando os outros jogadores.", _label);
            }
            else
            {
                GUILayout.Label($"<b>Escolha uma carta</b> ({player.PendingDraftPicks} restante(s)) · " +
                                $"[Q] rerrolar por {_sim.Content.Rules.DraftRerollCost:0} ouro (voce tem {player.Gold:0})", _label);
                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                for (int i = 0; i < player.DraftOptions.Count; i++)
                {
                    var spec = _sim.Content.GetTower(player.DraftOptions[i]);
                    GUILayout.Label($"<b>[{i + 1}] {spec?.DisplayName}</b>\n<size=11>{spec?.Tag}\n{spec?.Description}</size>",
                                    new GUIStyle(_label) { wordWrap = true, padding = new RectOffset(10, 10, 8, 8) },
                                    GUILayout.Width(196), GUILayout.Height(140));
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
        }

        private void DrawFim()
        {
            bool win = _sim.State.Outcome == MatchOutcome.Vitoria;
            var rect = new Rect(Screen.width * 0.5f - 220f, Screen.height * 0.5f - 70f, 440f, 140f);
            GUILayout.BeginArea(rect, _panel);
            GUILayout.Label(win ? "<color=#6bff8f><b>A CIDADE RESISTIU</b></color>"
                                : "<color=#ff6b6b><b>A PREFEITURA CAIU</b></color>", _big);
            GUILayout.Label($"Turnos sobrevividos: <b>{_sim.State.TurnNumber}</b> de {_sim.Content.Rules.TotalTurns}", _label);
            GUILayout.Label($"Nivel final da cidade: <b>{_sim.State.CityLevel}</b>  ·  " +
                            $"Predios de pe: <b>{_sim.State.Towers.Count}</b>", _label);
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------------------

        private static string PhaseName(PhaseId phase) => phase switch
        {
            PhaseId.Preparo => "PREPARO",
            PhaseId.Assalto => "ASSALTO",
            PhaseId.Balanco => "BALANCO",
            PhaseId.Fim => "FIM",
            _ => "-"
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

        private void OnDestroy()
        {
            if (_panelTex != null) Destroy(_panelTex);
        }
    }
}
