using DestinyTogether.Core;
using UnityEngine;

namespace DestinyTogether.Presentation
{
    /// <summary>
    /// Camera top-down com inclinacao isometrica, como o jogo de referencia.
    ///
    /// Segue o proprio heroi com folga e nunca faz snap forcado: no co-op, cada jogador tem a
    /// sua camera e ninguem tem a tela sequestrada por acao alheia. Zoom livre para alternar
    /// entre ler o tabuleiro inteiro (Preparo) e acompanhar o corpo (Assalto).
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        [Header("Enquadramento")]
        public float Pitch = 52f;
        public float Yaw = 45f;
        public float Distance = 26f;
        public float MinDistance = 12f;
        public float MaxDistance = 46f;
        public float ZoomSpeed = 6f;

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
            _targetDistance = Distance;
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
