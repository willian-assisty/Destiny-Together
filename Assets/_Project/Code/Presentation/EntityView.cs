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

        // ------------------------------------------------------------------------------
        // Locomocao procedural
        //
        // Anima o corpo INTEIRO — sem esqueleto, sem Animator, sem clipe. Existe porque a malha
        // do personagem chegou sem rig, e uma peca que desliza pelo chao lê como bug antes de ler
        // como placeholder. Balanco, rolagem de peso e inclinacao dao a leitura de "andando" a
        // partir de uma camera a 50 graus, que e de onde este jogo e visto.
        //
        // NAO substitui um ciclo de caminhada de verdade: quando a malha voltar riggada, isto se
        // desliga num bool e o Animator entra pelo mesmo PlayAction que ja existe.
        // ------------------------------------------------------------------------------

        /// <summary>Liga a locomocao. So para quem anda — predio balancando lê como terremoto.</summary>
        public bool Locomotion;

        /// <summary>Celulas percorridas por passada completa (dois passos).</summary>
        public float StrideLength = 1.25f;
        public float BobHeight = 0.07f;
        public float RollDegrees = 6f;
        public float LeanDegrees = 8f;
        /// <summary>Velocidade em que a marcha esta cheia. Abaixo disso, some proporcionalmente.</summary>
        public float FullGaitSpeed = 3f;

        private Vector3 _baseVisualPosition;
        private Vector3 _lastWorldPosition;
        private float _stridePhase;
        private float _speed;

        /// <summary>
        /// Animator da peca, quando ela chegou riggada. Presente = o clipe manda no corpo e a
        /// locomocao procedural sai de cena; ausente = procedural assume. Nunca os dois, senao o
        /// balanco somaria por cima do que a animacao ja faz.
        /// </summary>
        private Animator _animator;
        private static readonly int SpeedParam = Animator.StringToHash("Speed");

        /// <summary>Velocidade em que o clipe roda na cadencia em que foi autorado.</summary>
        public float ClipReferenceSpeed = 6f;

        public bool HasAnimator => _animator != null;

        public void SetAnimator(Animator animator)
        {
            _animator = animator;
            if (_animator != null) _animator.applyRootMotion = false;
        }

        private static readonly Color HitFlash = new Color(1f, 0.45f, 0.42f);

        public EntityId Id => _id;
        public Transform Visual => _visual != null ? _visual : transform;

        public virtual void Bind(EntityId id)
        {
            _id = id;
            _targetPosition = transform.position;
            _lastWorldPosition = transform.position;
            if (_visual != null)
            {
                _baseScale = _visual.localScale;
                _baseVisualPosition = _visual.localPosition;
            }
        }

        public void SetVisual(Transform visual, Renderer renderer, Color baseColor)
        {
            _visual = visual;
            _renderer = renderer;
            _baseColor = baseColor;
            _baseScale = visual != null ? visual.localScale : Vector3.one;
            // A primitiva nasce com localPosition.y = altura/2; o prefab nasce em zero. Guardar o
            // repouso em vez de assumir zero e o que faz a locomocao servir aos dois.
            _baseVisualPosition = visual != null ? visual.localPosition : Vector3.zero;
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
            // Sem isto o amanhecer — que teleporta os herois para a Prefeitura — mediria a
            // distancia do salto como velocidade e o personagem sairia correndo parado.
            _lastWorldPosition = position;
            _speed = 0f;
        }

        /// <summary>
        /// Escala permanente do visual — usada quando duas torres iguais se fundem e a peça
        /// precisa ficar mais alta. Atualiza a escala de repouso, senão o próximo punch de
        /// ataque desfaria o crescimento.
        ///
        /// Funciona igual para primitiva e para arte comprada: a posição local é escalada junto,
        /// o que preserva tanto o pivot central da primitiva quanto a base no chão do prefab.
        ///
        /// <paramref name="maxWorldHeight"/> é um TETO em células, e existe porque a fusão não
        /// tem teto do outro lado: <c>BuildSystem</c> faz <c>existing.Tier++</c> sem limite algum,
        /// então multiplicar a altura a cada fusão é uma progressão geométrica sem fim. Sem o
        /// teto, um Posto de Vigia de 3,0 chega a 6,2 no quinto tier e passa a Prefeitura — um
        /// prédio de 1×1 vira a coisa mais alta da vila, escondendo cinco células de tabuleiro.
        ///
        /// A altura é medida nos bounds do renderer em vez de deduzida da escala: é a única conta
        /// que vale igual para a primitiva (onde localScale.y É a altura) e para o prefab de arte
        /// (onde localScale é um fator de encaixe do VisualFitter e não diz altura nenhuma).
        /// </summary>
        public void ScaleVisual(Vector3 factor, float maxWorldHeight = 0f)
        {
            var visual = Visual;
            if (visual == null) return;

            if (maxWorldHeight > 0f && _renderer != null)
            {
                float current = _renderer.bounds.size.y;
                if (current > 0.001f)
                {
                    float allowed = maxWorldHeight / current;
                    if (allowed < factor.y) factor.y = Mathf.Max(1f, allowed);
                }
            }

            visual.localScale = Vector3.Scale(visual.localScale, factor);
            visual.localPosition = Vector3.Scale(visual.localPosition, factor);
            _baseScale = visual.localScale;
            _baseVisualPosition = visual.localPosition;
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

            TickLocomotion(dt);
            TickAction(dt);
        }

        /// <summary>
        /// Balanco, rolagem e inclinacao a partir do deslocamento real.
        ///
        /// A fase avanca com a DISTANCIA percorrida, nao com o tempo. E o detalhe que separa
        /// "andando" de "patinando": a passada fica presa ao chao em qualquer velocidade, entao
        /// um heroi lento da passos lentos e um rapido da passos rapidos sem nenhum ajuste.
        ///
        /// Mexe apenas em posicao e rotacao do visual — a escala fica livre para o punch de
        /// ataque de TickAction, que nao pode brigar com isto.
        /// </summary>
        private void TickLocomotion(float dt)
        {
            if (!Locomotion || _visual == null) return;

            var delta = transform.position - _lastWorldPosition;
            _lastWorldPosition = transform.position;
            delta.y = 0f;

            float instant = dt > 0.0001f ? delta.magnitude / dt : 0f;
            _speed = Mathf.Lerp(_speed, instant, 1f - Mathf.Exp(-9f * dt));

            if (_animator != null) { TickAnimator(dt); return; }

            _stridePhase += delta.magnitude / Mathf.Max(0.05f, StrideLength) * Mathf.PI * 2f;
            if (_stridePhase > Mathf.PI * 2f) _stridePhase -= Mathf.PI * 2f;

            float gait = Mathf.Clamp01(_speed / Mathf.Max(0.1f, FullGaitSpeed));

            // Dois toques de pe por passada (abs do seno), rolagem de peso uma vez por passada.
            float bob = Mathf.Abs(Mathf.Sin(_stridePhase)) * BobHeight * gait;
            float roll = Mathf.Sin(_stridePhase) * RollDegrees * gait;
            float lean = LeanDegrees * gait;

            // Parado o corpo respira. Sem isso o heroi em repouso vira estatua, e estatua no meio
            // de um mundo que se move lê como objeto quebrado.
            float breath = Mathf.Sin(Time.time * 1.7f) * 0.014f * (1f - gait);

            _visual.localPosition = _baseVisualPosition + new Vector3(0f, bob + breath, 0f);
            _visual.localRotation = Quaternion.Euler(lean, 0f, roll);
        }

        /// <summary>
        /// Aciona o clipe pela velocidade REAL do corpo.
        ///
        /// O ciclo de corrida foi autorado numa cadencia so; casar a taxa de reproducao com o
        /// deslocamento e o que impede o personagem de patinar (correr no lugar) ou de deslizar
        /// (andar sem mexer as pernas) — o mesmo principio da fase por distancia da versao
        /// procedural, agora aplicado ao relogio da animacao.
        ///
        /// Com um clipe so, parar congela a pose. Enquanto nao houver um Idle, um respiro sutil
        /// entra no lugar: e seguro justamente porque o clipe esta parado e nao ha o que somar.
        /// </summary>
        private void TickAnimator(float dt)
        {
            float gait = _speed / Mathf.Max(0.1f, ClipReferenceSpeed);

            _animator.SetFloat(SpeedParam, _speed);
            _animator.speed = gait < 0.06f ? 0f : Mathf.Clamp(gait, 0.35f, 1.8f);

            float breath = _animator.speed > 0f ? 0f : Mathf.Sin(Time.time * 1.7f) * 0.014f;
            _visual.localPosition = _baseVisualPosition + new Vector3(0f, breath, 0f);
            _visual.localRotation = Quaternion.identity;
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

        /// <summary>Albedo. URP usa _BaseMap; shaders legados usam _MainTex.</summary>
        public static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        public static readonly int MainTex = Shader.PropertyToID("_MainTex");
        public static readonly int BumpMap = Shader.PropertyToID("_BumpMap");
        public static readonly int MetallicGlossMap = Shader.PropertyToID("_MetallicGlossMap");
        public static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        public static readonly int Metallic = Shader.PropertyToID("_Metallic");
    }
}
