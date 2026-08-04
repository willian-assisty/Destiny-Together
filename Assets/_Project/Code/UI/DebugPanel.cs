using DestinyTogether.Sim;
using UnityEngine;

namespace DestinyTogether.UI
{
    /// <summary>
    /// Painel de teste (F1).
    ///
    /// Uma partida completa dura ~30 minutos e o Kaiju so aparece no turno 9. Sem controle de
    /// velocidade e pulo de fase, ninguem calibra o Ato 3 — e conteudo que nao se testa e
    /// conteudo que nao existe. Some junto com o resto do placeholder.
    /// </summary>
    public sealed class DebugPanel
    {
        public bool Visible;

        private static readonly float[] Speeds = { 0.25f, 0.5f, 1f, 2f, 4f, 8f };
        private float _speed = 1f;

        public float Speed => _speed;

        public void Toggle() => Visible = !Visible;

        public void ResetSpeed()
        {
            _speed = 1f;
            Time.timeScale = 1f;
        }

        public void Draw(UiStyles s, MatchSimulation sim, PlayerId localPlayer)
        {
            if (!Visible) return;

            float w = 268f;
            GUILayout.BeginArea(new Rect(Screen.width - w - 12f, 396f, w, 330f), s.Panel);

            GUILayout.Label("<b>PAINEL DE TESTE</b>  <size=11>(F1 fecha)</size>", s.Title);
            GUILayout.Label("Nada aqui faz parte das regras do jogo.", s.Small);
            GUILayout.Space(6);

            GUILayout.Label("<b>VELOCIDADE</b>", s.Label);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Speeds.Length; i++)
            {
                string label = Speeds[i] < 1f ? $"{Speeds[i]:0.##}" : $"{Speeds[i]:0}x";
                if (s.Choice(label, Mathf.Approximately(_speed, Speeds[i]), GUILayout.Height(24)))
                {
                    _speed = Speeds[i];
                    Time.timeScale = _speed;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("<b>FLUXO</b>", s.Label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Pular fase", s.Button, GUILayout.Height(26))) sim.DebugSkipPhase();
            if (GUILayout.Button("Pular turno", s.Button, GUILayout.Height(26))) sim.DebugSkipTurn();
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("<b>RECURSOS</b>", s.Label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+200 madeira", s.Button, GUILayout.Height(26)))
                sim.State.SiloWood = Mathf.Min(sim.State.SiloWood + 200f, sim.State.SiloCapacity);
            if (GUILayout.Button("+100 pedra", s.Button, GUILayout.Height(26)))
                sim.State.Stone += 100f;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+1 carta", s.Button, GUILayout.Height(26)))
                sim.DebugGrantCard(localPlayer);
            if (GUILayout.Button("+50 ouro", s.Button, GUILayout.Height(26)))
            {
                var p = sim.State.GetPlayer(localPlayer);
                if (p != null) p.Gold += 50f;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("<b>SOCORRO</b>", s.Label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Limpar horda", s.Button, GUILayout.Height(26)))
                SpawnSystem.DissolveAll(sim.State, sim.Events);
            if (GUILayout.Button("Curar cidade", s.Button, GUILayout.Height(26)))
                sim.State.TownHallHealth = sim.State.TownHallMaxHealth;
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label($"<size=11>seed {sim.State.MatchSeed}   ·   monstros {sim.State.Monsters.Count}   " +
                            $"·   predios {sim.State.Towers.Count}\n" +
                            $"fase {sim.State.PhaseElapsed:0.0}/{sim.State.PhaseDuration:0.0}s   " +
                            $"·   investida {sim.State.SurgeIndex + 1}{(sim.State.InBreather ? " (respiro)" : "")}</size>",
                            s.Small);

            GUILayout.EndArea();
        }
    }
}
