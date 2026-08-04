using DestinyTogether.Core;
using DestinyTogether.Presentation;
using DestinyTogether.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DestinyTogether.UI
{
    /// <summary>
    /// Traduz teclado e mouse em PlayerCommand. E o unico lugar do projeto que sabe o que e uma
    /// tecla — a simulacao recebe apenas intencao.
    ///
    /// Este roteamento e o que torna o multiplayer barato depois: hoje o comando vai direto para
    /// a simulacao local; amanha vai por RPC para o host. Nem a UI nem a simulacao percebem a
    /// diferenca, porque as duas so conhecem PlayerCommand.
    /// </summary>
    public sealed class InputRouter
    {
        private readonly MatchSimulation _sim;
        private readonly CameraRig _camera;
        private readonly BoardRenderer _board;
        private readonly PlayerId _localPlayer;

        private int _sequence;

        /// <summary>Carta selecionada na mao. Enquanto houver uma, o HUD imprime a divida ao vivo.</summary>
        public int SelectedCardIndex { get; private set; } = -1;
        public GridCoord HoveredCell { get; private set; } = GridCoord.Invalid;
        public bool HoverValid { get; private set; }

        public InputRouter(MatchSimulation sim, CameraRig camera, BoardRenderer board, PlayerId localPlayer)
        {
            _sim = sim;
            _camera = camera;
            _board = board;
            _localPlayer = localPlayer;
        }

        public PlayerState LocalPlayer => _sim.State.GetPlayer(_localPlayer);

        public void Tick()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null) return;

            TickMovement(keyboard);
            TickZoom(mouse);

            switch (_sim.State.Phase)
            {
                case PhaseId.Preparo:
                    TickCardSelection(keyboard);
                    TickPlacement(mouse);
                    TickReady(keyboard);
                    break;

                case PhaseId.Balanco:
                    TickDraft(keyboard);
                    _board.HideHighlight();
                    break;

                default:
                    _board.HideHighlight();
                    SelectedCardIndex = -1;
                    break;
            }
        }

        private void TickMovement(Keyboard keyboard)
        {
            float x = 0f, y = 0f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) x -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) x += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) y -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) y += 1f;

            // A camera esta girada 45 graus: sem esta rotacao, "para cima" no teclado nao aponta
            // para cima na tela e o controle fica desalinhado da imagem.
            var input = RotateToCamera(new Vec2(x, y));
            Send(PlayerCommand.Move(_localPlayer, input));
        }

        private Vec2 RotateToCamera(Vec2 input)
        {
            if (input.SqrMagnitude < 0.001f) return Vec2.Zero;
            float yaw = (_camera != null ? _camera.Yaw : 45f) * Mathf.Deg2Rad;
            float cos = Mathf.Cos(yaw), sin = Mathf.Sin(yaw);
            return new Vec2(input.X * cos + input.Y * sin, -input.X * sin + input.Y * cos).Normalized;
        }

        private void TickZoom(Mouse mouse)
        {
            if (mouse == null || _camera == null) return;
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f) _camera.Zoom(Mathf.Sign(scroll));
        }

        private void TickCardSelection(Keyboard keyboard)
        {
            var player = LocalPlayer;
            if (player == null) return;

            var keys = new[]
            {
                keyboard.digit1Key, keyboard.digit2Key, keyboard.digit3Key, keyboard.digit4Key,
                keyboard.digit5Key, keyboard.digit6Key, keyboard.digit7Key, keyboard.digit8Key
            };

            for (int i = 0; i < keys.Length && i < player.Hand.Count; i++)
            {
                if (keys[i] != null && keys[i].wasPressedThisFrame)
                    SelectedCardIndex = SelectedCardIndex == i ? -1 : i;
            }

            if (keyboard.escapeKey.wasPressedThisFrame) SelectedCardIndex = -1;
            if (SelectedCardIndex >= player.Hand.Count) SelectedCardIndex = -1;
        }

        private void TickPlacement(Mouse mouse)
        {
            var player = LocalPlayer;
            if (mouse == null || player == null || SelectedCardIndex < 0)
            {
                HoveredCell = GridCoord.Invalid;
                _board.HideHighlight();
                return;
            }

            if (!_camera.TryGetGroundPoint(mouse.position.ReadValue(), out var world))
            {
                _board.HideHighlight();
                return;
            }

            var cell = GridToWorld.ToCell(world);
            HoveredCell = cell;

            var def = player.Hand[SelectedCardIndex];
            HoverValid = CommandValidator
                .ValidateBuild(_sim.State, _sim.Content, player, def, cell).IsValid;

            if (_sim.State.Grid.InBounds(cell)) _board.ShowHighlight(cell, HoverValid);
            else _board.HideHighlight();

            if (mouse.leftButton.wasPressedThisFrame && HoverValid)
            {
                Send(PlayerCommand.Build(_localPlayer, def, cell));
                SelectedCardIndex = -1;
            }

            if (mouse.rightButton.wasPressedThisFrame) SelectedCardIndex = -1;
        }

        private void TickReady(Keyboard keyboard)
        {
            if (!keyboard.rKey.wasPressedThisFrame) return;
            var player = LocalPlayer;
            if (player == null) return;
            Send(PlayerCommand.Ready(_localPlayer, !player.IsReady));
        }

        private void TickDraft(Keyboard keyboard)
        {
            var player = LocalPlayer;
            if (player == null || player.PendingDraftPicks <= 0) return;

            if (keyboard.digit1Key.wasPressedThisFrame) Send(PlayerCommand.Pick(_localPlayer, 0));
            if (keyboard.digit2Key.wasPressedThisFrame) Send(PlayerCommand.Pick(_localPlayer, 1));
            if (keyboard.digit3Key.wasPressedThisFrame) Send(PlayerCommand.Pick(_localPlayer, 2));
            if (keyboard.qKey.wasPressedThisFrame)
                Send(new PlayerCommand { Type = CommandType.RerollDraft, Player = _localPlayer });
        }

        private void Send(PlayerCommand cmd)
        {
            cmd.Sequence = _sequence++;
            _sim.SubmitCommand(cmd);
        }
    }
}
