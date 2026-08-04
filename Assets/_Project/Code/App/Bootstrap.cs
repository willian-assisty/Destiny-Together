using DestinyTogether.Data;
using DestinyTogether.Presentation;
using DestinyTogether.Sim;
using DestinyTogether.UI;
using UnityEngine;
using UnityEngine.InputSystem;

// Desambigua do UnityEngine.EntityId introduzido no Unity 6.
using EntityId = DestinyTogether.Sim.EntityId;

namespace DestinyTogether.App
{
    /// <summary>
    /// Composition root e fluxo do aplicativo. O UNICO lugar que enxerga todas as camadas.
    ///
    /// Monta a cena inteira por codigo — camera, luz, tabuleiro, telas — porque nesta fase tudo
    /// e placeholder e uma cena versionada em YAML so produziria conflito de merge sem entregar
    /// nada. Quando a arte definitiva chegar, este script vira o carregador dela.
    ///
    /// Ordem sagrada: nunca abrir o netcode antes de o jogo existir single-player local.
    /// A simulacao ja e servidor-autoritativa por construcao; o multiplayer sera trocar o
    /// roteamento de PlayerCommand por RPC, sem tocar em simulacao, apresentacao ou UI.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class Bootstrap : MonoBehaviour
    {
        private enum AppState { Menu, Jogando, Pausado, Resultado }

        [Header("Partida")]
        [Tooltip("Assets de balanceamento. Vazio = conteudo padrao embutido em codigo.")]
        public ContentDatabase Content;

        [Tooltip("Arte do jogo. Vazio = tudo em primitivas. Trocar este asset troca a aparencia " +
                 "inteira em um clique; entradas nao mapeadas continuam em primitiva.")]
        public VisualsProfile Visuals;

        [Tooltip("Luz, nevoa e ceu. Vazio = crepusculo frio padrao.")]
        public AtmosphereProfile Atmosfera;

        [Tooltip("Pula o menu e cai direto numa partida com estes valores. Util para iterar rapido.")]
        public bool PularMenu = false;
        [Range(1, 4)] public int JogadoresAoPularMenu = 1;

        [Header("Diagnostico")]
        public bool LogPhaseChanges = true;

        private AppState _state = AppState.Menu;

        private IContentDatabase _content;
        private MatchSimulation _sim;
        private PresentationDirector _presentation;
        private InputRouter _input;
        private CameraRig _camera;
        private GameObject _worldRoot;

        private readonly UiStyles _styles = new UiStyles();
        private readonly Screens _screens = new Screens();
        private readonly GameHud _hud = new GameHud();
        private readonly DebugPanel _debug = new DebugPanel();

        private LaneForecast[] _forecast;
        private PhaseId _lastPhase = PhaseId.None;
        private MatchSetup _lastSetup;
        private AtmosphereProfile _atmosphere;

        public MatchSimulation Simulation => _sim;

        // ------------------------------------------------------------------------------

        private void Awake()
        {
            _content = Content != null ? (IContentDatabase)Content : new DefaultContent();
            if (Content != null) Content.Rebake();

            _forecast = new LaneForecast[LaneGeometry.LaneCount];
            BuildEnvironment();

            if (PularMenu)
            {
                var setup = MatchSetup.Default(JogadoresAoPularMenu);
                _screens.Setup = setup;
                StartMatch(setup);
            }
            else
            {
                EnterMenu();
            }
        }

        private void BuildEnvironment()
        {
            var camGo = new GameObject("Camera Iso");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.farClipPlane = 200f;
            camGo.AddComponent<AudioListener>();
            _camera = camGo.AddComponent<CameraRig>();

            var lightGo = new GameObject("Sol");
            var sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional;

            // Sem asset de atmosfera o jogo ainda abre — e já abre escuro, porque o clima
            // fechado é premissa do desenho, não um acabamento opcional.
            _atmosphere = Atmosfera != null ? Atmosfera : AtmosphereProfile.CreateDefaultDark();
            AtmosphereApplier.Apply(_atmosphere, cam, sun);
        }

        // ------------------------------------------------------------------------------
        // Transicoes de estado
        // ------------------------------------------------------------------------------

        private void EnterMenu()
        {
            TearDownMatch();
            _state = AppState.Menu;
            Time.timeScale = 0f;
        }

        private void StartMatch(MatchSetup setup)
        {
            TearDownMatch();
            _lastSetup = setup;

            int seed = setup.Seed != 0 ? setup.Seed : Random.Range(1, int.MaxValue);
            _sim = new MatchSimulation(_content, seed, setup.PlayerCount, new UnityLogSink(), setup.Heroes);

            // Assentos sem humano viram Automatos: colhem e depositam, nunca constroem nem
            // escolhem carta. Sem isso, o Preparo esperaria um Pronto que nunca vem.
            for (int i = 0; i < _sim.State.Players.Count; i++)
                if (i != setup.LocalPlayerIndex) _sim.State.Players[i].IsAutomaton = true;

            var local = _sim.State.GetPlayer(new PlayerId(setup.LocalPlayerIndex));
            if (local != null) local.DisplayName = "Voce";

            _worldRoot = new GameObject("World");
            _presentation = new PresentationDirector(_sim, _worldRoot.transform, Visuals);
            _input = new InputRouter(_sim, _camera, _presentation.Board, new PlayerId(setup.LocalPlayerIndex));

            _camera.SetFocusImmediate(_sim.State.CityCenter);
            _lastPhase = PhaseId.None;
            RefreshForecast();

            _state = AppState.Jogando;
            Time.timeScale = _debug.Speed;

            Debug.Log($"[Destiny Together] Partida iniciada · seed {seed} · " +
                      $"{setup.PlayerCount} assento(s) · tabuleiro {_sim.State.Grid.Size}x{_sim.State.Grid.Size}");
        }

        private void TearDownMatch()
        {
            _presentation?.Dispose();
            _presentation = null;
            _input = null;
            _sim = null;

            if (_worldRoot != null)
            {
                Destroy(_worldRoot);
                _worldRoot = null;
            }
        }

        // ------------------------------------------------------------------------------
        // Loop
        // ------------------------------------------------------------------------------

        private void Update()
        {
            var keyboard = Keyboard.current;

            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame && _state != AppState.Menu)
                _debug.Toggle();

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (_state == AppState.Jogando) Pause();
                else if (_state == AppState.Pausado) Resume();
            }

            if (_state != AppState.Jogando || _sim == null) return;

            float dt = Time.deltaTime;

            _input.Tick();
            _sim.Advance(dt);
            _presentation.Tick(dt);

            var localPlayer = _sim.State.GetPlayer(new PlayerId(_lastSetup.LocalPlayerIndex));
            var hero = _sim.State.GetHero(localPlayer?.Hero ?? EntityId.None);
            if (hero != null) _camera.SetFocus(hero.Position);

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

            if (_sim.State.IsOver)
            {
                _state = AppState.Resultado;
                Time.timeScale = 0f;
            }
        }

        private void Pause()
        {
            _state = AppState.Pausado;
            Time.timeScale = 0f;
        }

        private void Resume()
        {
            _state = AppState.Jogando;
            Time.timeScale = _debug.Speed;
        }

        private void RefreshForecast()
        {
            _sim.EvaluateForecast(_forecast);
            _presentation?.Board.UpdateLaneForecast(_forecast);
        }

        // ------------------------------------------------------------------------------
        // Telas
        // ------------------------------------------------------------------------------

        private void OnGUI()
        {
            _styles.EnsureBuilt();

            switch (_state)
            {
                case AppState.Menu:
                    HandleAction(_screens.DrawMainMenu(_styles, _content));
                    break;

                case AppState.Jogando:
                    _hud.Draw(_styles, _sim, _input, _forecast);
                    _debug.Draw(_styles, _sim, new PlayerId(_lastSetup.LocalPlayerIndex));
                    break;

                case AppState.Pausado:
                    _hud.Draw(_styles, _sim, _input, _forecast);
                    HandleAction(_screens.DrawPause(_styles, _sim));
                    break;

                case AppState.Resultado:
                    HandleAction(_screens.DrawResult(_styles, _sim));
                    break;
            }
        }

        private void HandleAction(ScreenAction action)
        {
            switch (action)
            {
                case ScreenAction.Jogar:
                    StartMatch(_screens.Setup);
                    break;

                case ScreenAction.Continuar:
                    Resume();
                    break;

                case ScreenAction.Reiniciar:
                    // Nova seed a cada reinicio, a menos que o menu tenha fixado uma.
                    StartMatch(_lastSetup);
                    break;

                case ScreenAction.VoltarAoMenu:
                    EnterMenu();
                    break;

                case ScreenAction.Sair:
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                    break;
            }
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;
            TearDownMatch();
            _styles.Dispose();
        }
    }
}
