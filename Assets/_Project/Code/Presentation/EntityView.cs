using DestinyTogether.Sim;
using UnityEngine;

// Unity 6 tem seu proprio UnityEngine.EntityId (sistema de entidades da engine).
// O alias deixa explicito que aqui EntityId e sempre a identidade da NOSSA simulacao.
using EntityId = DestinyTogether.Sim.EntityId;

namespace DestinyTogether.Presentation
{
    /// <summary>Acoes visuais. E a ponte para o Animator quando a arte definitiva chegar.</summary>
    public enum ViewActionId { Idle, Attack, Hit, Die, Build }

    public interface IEntityView
    {
        EntityId Id { get; }
        void Bind(EntityId id);
        void SetWorldPosition(Vector3 position);
        void SetFacing(Vector3 forward);
        void SetHealthRatio(float ratio);
        void PlayAction(ViewActionId action, float duration);
        void Despawn();
    }

    /// <summary>
    /// View base. Todo o visual mora no filho "Visual" — este objeto so carrega o contrato.
    ///
    /// Regra que faz a troca de arte ser barata: nenhum presenter chama Animator.Play("Attack").
    /// Todos chamam PlayAction(ViewActionId, duracao). O placeholder implementa como punch de
    /// escala e flash de cor; a versao final implementa com Animator ou Timeline. Mesma assinatura,
    /// mesma duracao, zero mudanca fora desta classe.
    /// </summary>
    public class EntityView : MonoBehaviour, IEntityView
    {
        [SerializeField] private Transform _visual;

        private EntityId _id;
        private Vector3 _targetPosition;
        private Vector3 _targetForward = Vector3.forward;
        private Vector3 _baseScale = Vector3.one;

        private float _actionRemaining;
        private float _actionDuration;
        private ViewActionId _action;

        private Renderer _renderer;
        private MaterialPropertyBlock _block;
        private Color _baseColor = Color.white;

        /// <summary>Suavizacao entre ticks de simulacao (20 Hz) e frames de render (60+ Hz).</summary>
        public float PositionSmoothing = 18f;
        public float RotationSmoothing = 14f;

        private static readonly Color HitFlash = new Color(1f, 0.45f, 0.42f);

        public EntityId Id => _id;
        public Transform Visual => _visual != null ? _visual : transform;

        public virtual void Bind(EntityId id)
        {
            _id = id;
            _targetPosition = transform.position;
            if (_visual != null) _baseScale = _visual.localScale;
        }

        public void SetVisual(Transform visual, Renderer renderer, Color baseColor)
        {
            _visual = visual;
            _renderer = renderer;
            _baseColor = baseColor;
            _baseScale = visual != null ? visual.localScale : Vector3.one;
            _block = new MaterialPropertyBlock();
        }

        public void SetWorldPosition(Vector3 position) => _targetPosition = position;

        public void SetFacing(Vector3 forward)
        {
            if (forward.sqrMagnitude > 0.0001f) _targetForward = forward.normalized;
        }

        public virtual void SetHealthRatio(float ratio) { }

        public void PlayAction(ViewActionId action, float duration)
        {
            _action = action;
            _actionDuration = Mathf.Max(0.05f, duration);
            _actionRemaining = _actionDuration;
        }

        public virtual void Despawn()
        {
            if (this != null && gameObject != null) Destroy(gameObject);
        }

        /// <summary>Teleporta sem interpolar. Usado ao nascer e nas fronteiras de fase.</summary>
        public void SnapTo(Vector3 position)
        {
            _targetPosition = position;
            transform.position = position;
        }

        /// <summary>
        /// Escala permanente do visual — usada quando duas torres iguais se fundem e a peça
        /// precisa ficar mais alta. Atualiza a escala de repouso, senão o próximo punch de
        /// ataque desfaria o crescimento.
        ///
        /// Funciona igual para primitiva e para arte comprada: a posição local é escalada junto,
        /// o que preserva tanto o pivot central da primitiva quanto a base no chão do prefab.
        /// </summary>
        public void ScaleVisual(Vector3 factor)
        {
            var visual = Visual;
            if (visual == null) return;

            visual.localScale = Vector3.Scale(visual.localScale, factor);
            visual.localPosition = Vector3.Scale(visual.localPosition, factor);
            _baseScale = visual.localScale;
        }

        protected virtual void Update()
        {
            float dt = Time.deltaTime;

            transform.position = Vector3.Lerp(transform.position, _targetPosition,
                                              1f - Mathf.Exp(-PositionSmoothing * dt));

            if (_targetForward.sqrMagnitude > 0.0001f)
            {
                var target = Quaternion.LookRotation(_targetForward, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, target,
                                                      1f - Mathf.Exp(-RotationSmoothing * dt));
            }

            TickAction(dt);
        }

        private void TickAction(float dt)
        {
            if (_actionRemaining <= 0f) return;
            _actionRemaining -= dt;
            float t = Mathf.Clamp01(1f - _actionRemaining / _actionDuration);

            var visual = Visual;
            switch (_action)
            {
                case ViewActionId.Attack:
                    // Punch de escala: le como "eu ataquei" mesmo com 200 primitivas em tela.
                    visual.localScale = _baseScale * (1f + 0.28f * Mathf.Sin(t * Mathf.PI));
                    break;

                case ViewActionId.Hit:
                    // Flash avermelhado voltando para a cor base. Com arte texturizada um flash
                    // branco seria invisivel — a maioria dos materiais ja tem base branca.
                    Tint(Color.Lerp(HitFlash, _baseColor, t));
                    break;

                case ViewActionId.Build:
                    visual.localScale = Vector3.Lerp(_baseScale * 0.2f, _baseScale, EaseOutBack(t));
                    break;
            }

            if (_actionRemaining <= 0f)
            {
                visual.localScale = _baseScale;
                Tint(_baseColor);
            }
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        protected void Tint(Color color)
        {
            if (_renderer == null) return;
            _block ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_block);
            _block.SetColor(ShaderIds.BaseColor, color);
            _renderer.SetPropertyBlock(_block);
        }
    }

    public static class ShaderIds
    {
        public static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        /// <summary>Tiling e offset do albedo. x,y = repeticoes; z,w = deslocamento.</summary>
        public static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");
    }
}
