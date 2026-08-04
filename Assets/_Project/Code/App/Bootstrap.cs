using DestinyTogether.Data;
using DestinyTogether.Presentation;
using DestinyTogether.Sim;
using DestinyTogether.UI;
using UnityEngine;

// Desambigua do UnityEngine.EntityId introduzido no Unity 6.
using EntityId = DestinyTogether.Sim.EntityId;

namespace DestinyTogether.App
{
    /// <summary>
    /// Composition root. O UNICO lugar do projeto que enxerga todas as camadas ao mesmo tempo.
    ///
    /// Monta a cena inteira por codigo — camera, luz, tabuleiro, HUD — porque nesta fase toda
    /// a arte e placeholder e uma cena versionada em YAML so produziria conflito de merge sem
    /// entregar nada. Quando a arte definitiva chegar, este script vira o carregador dela.
    ///
    /// Ordem sagrada: nunca abrir o netcode antes do jogo existir single-player local.
    /// A simulacao ja e servidor-autoritativa por construcao; o multiplayer sera trocar o
    /// roteamento de PlayerCommand por RPC, sem tocar em simulacao, apresentacao ou UI.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class Bootstrap : MonoBehaviour
    {
        [Header("Partida")]
        [Tooltip("Assets de balanceamento. Vazio = conteudo padrao embutido em codigo.")]
        public ContentDatabase Content;

        [Tooltip("Assentos na partida. Os assentos sem jogador humano viram Automatos.")]
        [Range(1, 4)] public int PlayerCount = 1;

        [Tooltip("Qual assento o teclado controla.")]
        [Range(0, 3)] public int LocalPlayerIndex = 0;

        [Tooltip("0 = sorteia uma seed a cada Play. Fixe um valor para repetir a mesma partida.")]
        public int Seed = 0;

        [Header("Diagnostico")]
        public bool LogPhaseChanges = true;

        private MatchSimulation _sim;
        private PresentationDirector _presentation;
        private InputRouter _input;
        private CameraRig _camera;
        private GameHud _hud;
        private LaneForecast[] _forecast;
        private PhaseId _lastPhase = PhaseId.None;

        public MatchSimulation Simulation => _sim;

        private void Awake()
        {
            IContentDatabase content = Content != null ? Content : new DefaultContent();
            if (Content != null) Content.Rebake();

            int seed = Seed != 0 ? Seed : Random.Range(1, int.MaxValue);
            _sim = new MatchSimulation(content, seed, PlayerCount, new UnityLogSink());

            // Assentos sem humano viram Automatos: colhem e depositam, nunca constroem nem
            // escolhem carta. Sem isso, o Preparo esperaria um Pronto que nunca vem.
            for (int i = 0; i < _sim.State.Players.Count; i++)
                if (i != LocalPlayerIndex) _sim.State.Players[i].IsAutomaton = true;

            _forecast = new LaneForecast[LaneGeometry.LaneCount];

            BuildEnvironment();

            var worldRoot = new GameObject("World").transform;
            _presentation = new PresentationDirector(_sim, worldRoot);
            _input = new InputRouter(_sim, _camera, _presentation.Board, new PlayerId(LocalPlayerIndex));

            _hud = gameObject.AddComponent<GameHud>();
            _hud.Initialize(_sim, _input, _forecast);

            _camera.SetFocusImmediate(_sim.State.CityCenter);
            RefreshForecast();

            Debug.Log($"[Destiny Together] Partida iniciada · seed {seed} · {PlayerCount} assento(s) · " +
                      $"tabuleiro {_sim.State.Grid.Size}x{_sim.State.Grid.Size}");
        }

        private void BuildEnvironment()
        {
            var camGo = new GameObject("Camera Iso");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.07f, 0.09f);
            cam.farClipPlane = 200f;
            camGo.AddComponent<AudioListener>();
            _camera = camGo.AddComponent<CameraRig>();

            var lightGo = new GameObject("Sol");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.97f, 0.9f);
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(52f, 30f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.28f, 0.30f, 0.36f);
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            _input.Tick();
            _sim.Advance(dt);
            _presentation.Tick(dt);

            var localHero = _sim.State.GetHero(_sim.State.GetPlayer(new PlayerId(LocalPlayerIndex))?.Hero
                                               ?? EntityId.None);
            if (localHero != null) _camera.SetFocus(localHero.Position);

            if (_sim.State.Phase != _lastPhase)
            {
                _lastPhase = _sim.State.Phase;
                RefreshForecast();
                if (LogPhaseChanges)
                    Debug.Log($"[Destiny Together] Turno {_sim.State.TurnNumber} · {_lastPhase}");
            }
            else if (_sim.State.Phase == PhaseId.Preparo)
            {
                // Durante o Preparo o Prognostico precisa acompanhar cada predio erguido.
                RefreshForecast();
            }
        }

        private void RefreshForecast()
        {
            _sim.EvaluateForecast(_forecast);
            _presentation?.Board.UpdateLaneForecast(_forecast);
        }

        private void OnDestroy() => _presentation?.Dispose();
    }
}
