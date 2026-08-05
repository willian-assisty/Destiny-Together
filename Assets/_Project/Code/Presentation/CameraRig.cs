using DestinyTogether.Core;
using UnityEngine;

namespace DestinyTogether.Presentation
{
    /// <summary>
    /// Camera em PERSPECTIVA com FOV baixo — a leitura de isometrica sem abrir mao de
    /// profundidade.
    ///
    /// FOV 35 e a peca central do enquadramento, nao um detalhe: perto o bastante de uma
    /// projecao paralela para que duas torres do mesmo tamanho continuem parecendo do mesmo
    /// tamanho em pontos diferentes da tela (que e o que uma grade precisa), e longe o bastante
    /// para que uma peca alta ainda projete a silhueta que a gramatica de placeholder depende.
    /// Ortografica pura mataria essa segunda metade.
    ///
    /// Segue o proprio heroi com folga e nunca faz snap forcado: no co-op, cada jogador tem a
    /// sua camera e ninguem tem a tela sequestrada por acao alheia.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        [Header("Enquadramento")]
        [Tooltip("Baixo de proposito: simula isometrica mantendo profundidade.")]
        [Range(15f, 70f)] public float FieldOfView = 35f;

        public float Pitch = 50f;
        public float Yaw = 45f;

        // Enquadramento de CORPO, não de tabuleiro. Em 16:9, no zoom máximo (25) a tela cobre
        // ~±10 células de profundidade e ~±14 de largura; como o tabuleiro entra girado 45°, o
        // que conta é a meia-diagonal dele, ~14,9. Ou seja: quase toda a cidade cabe, faltando
        // um pouco na profundidade.
        //
        // A consequência é de design, não de código: a leitura GLOBAL das oito Faixas passa a ser
        // do painel da Bússola no HUD, e os pilares viram indicador LOCAL — você enxerga o do
        // lado que está defendendo, não os oito de uma vez.
        public float Distance = 22f;
        public float MinDistance = 18f;
        public float MaxDistance = 25f;

        // Faixa de zoom curta (7 unidades) pede passo curto: com o 8 antigo, um clique de scroll
        // atravessava o intervalo inteiro.
        public float ZoomSpeed = 2.5f;

        [Header("Seguimento")]
        public float FollowSmoothing = 6f;
        [Tooltip("Raio morto: dentro dele a camera nao se mexe, evitando tremor constante.")]
        public float DeadZone = 1.5f;

        private Camera _camera;
        private Vector3 _focus;
        private float _targetDistance;

        public Camera Camera => _camera;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            if (_camera == null) _camera = gameObject.AddComponent<Camera>();

            ApplyLens();
            _targetDistance = Mathf.Clamp(Distance, MinDistance, MaxDistance);
            Distance = _targetDistance;
        }

        /// <summary>
        /// Escreve projecao e FOV na camera. Chamado tambem em OnValidate para que mexer no valor
        /// com o jogo rodando mostre o resultado na hora — enquadramento e a coisa que mais
        /// precisa ser ajustada vendo, nao calculando.
        /// </summary>
        private void ApplyLens()
        {
            if (_camera == null) return;
            _camera.orthographic = false;
            _camera.fieldOfView = FieldOfView;
        }

        private void OnValidate()
        {
            if (MaxDistance < MinDistance) MaxDistance = MinDistance;
            if (_camera != null) ApplyLens();
        }

        public void SetFocusImmediate(Vec2 simPosition)
        {
            _focus = GridToWorld.ToWorld(simPosition);
            ApplyTransform(1f);
        }

        public void SetFocus(Vec2 simPosition)
        {
            var target = GridToWorld.ToWorld(simPosition);
            if ((target - _focus).sqrMagnitude > DeadZone * DeadZone)
                _focus = target;
        }

        public void Zoom(float delta)
        {
            _targetDistance = Mathf.Clamp(_targetDistance - delta * ZoomSpeed, MinDistance, MaxDistance);
        }

        private void LateUpdate()
        {
            ApplyLens();
            Distance = Mathf.Lerp(Distance, _targetDistance, 1f - Mathf.Exp(-8f * Time.deltaTime));
            ApplyTransform(1f - Mathf.Exp(-FollowSmoothing * Time.deltaTime));
        }

        private void ApplyTransform(float t)
        {
            var rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            var desired = _focus - rotation * Vector3.forward * Distance;
            transform.position = Vector3.Lerp(transform.position, desired, t);
            transform.rotation = rotation;
        }

        /// <summary>Ponto do plano do chao sob o cursor — como o clique vira GridCoord.</summary>
        public bool TryGetGroundPoint(Vector2 screenPosition, out Vector3 worldPoint)
        {
            worldPoint = default;
            if (_camera == null) return false;

            var ray = _camera.ScreenPointToRay(screenPosition);
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (!plane.Raycast(ray, out float enter)) return false;

            worldPoint = ray.GetPoint(enter);
            return true;
        }
    }
}
