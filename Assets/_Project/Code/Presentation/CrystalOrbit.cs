using UnityEngine;

namespace DestinyTogether.Presentation
{
    /// <summary>
    /// Faz cristais orbitarem a cabeca do personagem de forma continua, independente de qual clipe
    /// o Animator esteja tocando — ou de haver Animator nenhum.
    ///
    /// Vai no ROOT do personagem. Os cristais sao filhos do root e NAO de ossos do esqueleto: o
    /// script escreve a posicao deles em espaco de mundo todo frame, entao ser filho de um osso so
    /// somaria a transformacao do osso por cima e faria o cristal disparar.
    ///
    /// Roda em LateUpdate porque o Animator escreve a pose em Update. Ler a cabeca antes disso
    /// deixaria os cristais um frame atrasados, e um frame de atraso em algo que orbita tremula.
    ///
    /// ---
    ///
    /// A ancoragem e MEDIDA em camadas, e nao presa a um rig Humanoid. O desenho original pedia
    /// `animator.isHuman` e `GetBoneTransform(HumanBodyBones.Head)`, o que nao funciona neste
    /// projeto por dois motivos independentes: o mago chegou em OBJ, que nao carrega esqueleto
    /// nenhum, e mesmo os personagens riggados daqui usam rig **Generic** por decisao registrada
    /// (clipe e malha vem do mesmo esqueleto, entao ligar por caminho de transform casa exato e
    /// nao ha retarget para falhar). Com `isHuman` sempre falso, o script se desligava no Awake e
    /// os cristais nunca giravam — sem sintoma alem de uma linha no console.
    ///
    /// As tres camadas cobrem o presente e o futuro sem escolher um: osso Humanoid se houver,
    /// senao um transform chamado "Head", senao o topo dos bounds da malha. A ultima sempre
    /// responde, entao a orbita nunca depende de o personagem ter chegado riggado.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class CrystalOrbit : MonoBehaviour
    {
        [Header("Referencias")]
        [Tooltip("Deixe vazio para buscar no proprio objeto ou nos filhos.")]
        public Animator Animator;

        [Tooltip("Os cristais, na ordem. Eles sao distribuidos igualmente na orbita.")]
        public Transform[] Crystals;

        [Header("Ancoragem")]
        [Tooltip("Deslocamento a partir da cabeca, em espaco do personagem e em unidades de " +
                 "MODELO — a escala do personagem e aplicada por cima.")]
        public Vector3 HeadOffset = new Vector3(0f, 0.06f, 0f);

        [Tooltip("Fracao da altura da malha em que a cabeca fica, quando nao ha osso para " +
                 "perguntar. 0,86 = um pouco abaixo do topo, que e onde fica a cabeca de um " +
                 "humanoide encapuzado.")]
        [Range(0.5f, 1f)] public float HeadHeightFraction = 0.86f;

        [Header("Orbita")]
        /// <summary>
        /// Raio da orbita, em unidades de MODELO — a escala do personagem entra por cima.
        ///
        /// 0,38 vem de medicao, nao de gosto: no FBX original os tres cristais vinham autorados a
        /// 0,51, 0,51 e 0,58 do eixo do corpo, mas na altura do PEITO, onde o mago e largo por
        /// causa dos bracos. Subindo a orbita para a cabeca, onde a silhueta e estreita, o mesmo
        /// raio deixaria os cristais soltos no ar longe do corpo. 0,38 mantem a proporcao que o
        /// artista escolheu contra a largura que a orbita realmente encontra.
        /// </summary>
        public float Radius = 0.38f;

        [Tooltip("Graus por segundo. Negativo inverte o sentido.")]
        public float OrbitSpeed = 42f;

        [Tooltip("Inclinacao do plano da orbita, para nao ficar perfeitamente horizontal.")]
        public float Tilt = 14f;

        [Tooltip("Cada cristal sobe e desce fora de fase com os outros.")]
        public float BobAmplitude = 0.04f;
        public float BobSpeed = 1.3f;

        [Tooltip("Rotacao do cristal em torno do proprio eixo.")]
        public float SelfSpin = 70f;

        [Header("Inercia")]
        [Tooltip("Quanto maior, mais colado na cabeca. Valores baixos dao a sensacao de que os " +
                 "cristais tem massa e arrastam atras do movimento.")]
        public float FollowSpeed = 11f;

        [Header("Variacao por instancia")]
        [Tooltip("Aleatoriza a fase inicial. Com varios magos em cena eles nao giram em sincronia.")]
        public bool RandomizeStartPhase = true;

        private Transform _head;
        private Vector3 _localHead;
        private bool _headIsBone;

        private Vector3 _pivot;
        private float _phase;
        private bool _ready;

        private void Awake()
        {
            if (Animator == null) Animator = GetComponentInChildren<Animator>();
            ResolveHead();

            if (RandomizeStartPhase) _phase = Random.Range(0f, 360f);
        }

        /// <summary>
        /// Onde fica a cabeca. Tres respostas, da mais precisa para a que sempre existe.
        /// </summary>
        private void ResolveHead()
        {
            if (Animator != null && Animator.isHuman)
            {
                _head = Animator.GetBoneTransform(HumanBodyBones.Head);
                if (_head != null) { _headIsBone = true; return; }
            }

            _head = FindByName(transform);
            if (_head != null) { _headIsBone = true; return; }

            // Malha estatica: a cabeca e uma fracao da altura, medida uma vez. Fica em espaco
            // LOCAL para acompanhar o personagem quando ele anda e gira.
            _headIsBone = false;
            _localHead = new Vector3(0f, 1.5f, 0f);

            if (!TryMeasureBounds(out var bounds)) return;
            _localHead = new Vector3(bounds.center.x,
                                     bounds.min.y + bounds.size.y * HeadHeightFraction,
                                     bounds.center.z);
        }

        private static Transform FindByName(Transform t)
        {
            if (t.name.Equals("Head", System.StringComparison.OrdinalIgnoreCase) ||
                t.name.Equals("head", System.StringComparison.Ordinal)) return t;

            for (int i = 0; i < t.childCount; i++)
            {
                var found = FindByName(t.GetChild(i));
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Bounds da malha em espaco LOCAL, ignorando os proprios cristais.
        ///
        /// Ignorar os cristais nao e detalhe: eles ja estao em orbita quando isto roda, entao
        /// inclui-los inflaria a caixa e a "cabeca" subiria para o topo da orbita — que sobe de
        /// novo no frame seguinte.
        /// </summary>
        private bool TryMeasureBounds(out Bounds bounds)
        {
            bounds = default;
            var toLocal = transform.worldToLocalMatrix;
            bool started = false;

            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (IsCrystal(r.transform)) continue;

                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                             (i & 2) == 0 ? b.min.y : b.max.y,
                                             (i & 4) == 0 ? b.min.z : b.max.z);
                    var local = toLocal.MultiplyPoint3x4(corner);

                    if (!started) { bounds = new Bounds(local, Vector3.zero); started = true; }
                    else bounds.Encapsulate(local);
                }
            }

            return started;
        }

        private bool IsCrystal(Transform t)
        {
            if (Crystals == null) return false;
            for (int i = 0; i < Crystals.Length; i++)
            {
                if (Crystals[i] == null) continue;
                if (t == Crystals[i] || t.IsChildOf(Crystals[i])) return true;
            }
            return false;
        }

        private void LateUpdate()
        {
            if (Crystals == null || Crystals.Length == 0) return;

            // A escala do personagem entra na orbita. O `VisualFitter` normaliza toda arte
            // importada para caber em celulas, entao um raio em unidades de mundo daria uma orbita
            // do tamanho errado assim que alguem trocasse o modelo por outro de escala diferente.
            float scale = Mathf.Abs(transform.lossyScale.y);
            if (scale < 0.0001f) scale = 1f;

            var anchor = _headIsBone && _head != null
                ? _head.position
                : transform.TransformPoint(_localHead);

            var target = anchor + transform.rotation * (HeadOffset * scale);

            if (!_ready) { _pivot = target; _ready = true; }

            // Suavizacao exponencial: independente de framerate, ao contrario de um Lerp com t fixo.
            _pivot = Vector3.Lerp(_pivot, target, 1f - Mathf.Exp(-FollowSpeed * Time.deltaTime));

            // A fase acumula sozinha e nunca reseta. E por isso que a orbita nao da pop quando o
            // Animator troca de estado — e por isso que ela continua girando com o corpo parado.
            _phase += OrbitSpeed * Time.deltaTime;
            if (_phase > 360f) _phase -= 360f;
            else if (_phase < 0f) _phase += 360f;

            // O plano da orbita acompanha o yaw do PERSONAGEM, nao o da cabeca. Se seguisse a
            // cabeca, cada olhada para o lado arrastaria os cristais junto.
            var plane = transform.rotation * Quaternion.Euler(Tilt, 0f, 0f);

            float step = 360f / Crystals.Length;
            float radius = Radius * scale;
            float bobAmplitude = BobAmplitude * scale;

            for (int i = 0; i < Crystals.Length; i++)
            {
                var c = Crystals[i];
                if (c == null) continue;

                float a = (_phase + step * i) * Mathf.Deg2Rad;
                float bob = Mathf.Sin(Time.time * BobSpeed + i * 2.1f) * bobAmplitude;

                var local = new Vector3(Mathf.Cos(a) * radius, bob, Mathf.Sin(a) * radius);

                c.position = _pivot + plane * local;
                c.Rotate(Vector3.up, SelfSpin * Time.deltaTime, Space.Self);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) ResolveHead();

            float scale = Mathf.Abs(transform.lossyScale.y);
            if (scale < 0.0001f) scale = 1f;

            var anchor = _headIsBone && _head != null
                ? _head.position
                : transform.TransformPoint(_localHead);
            var p = anchor + transform.rotation * (HeadOffset * scale);
            var plane = transform.rotation * Quaternion.Euler(Tilt, 0f, 0f);
            float radius = Radius * scale;

            Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.9f);
            var prev = p + plane * new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= 48; i++)
            {
                float ang = i / 48f * Mathf.PI * 2f;
                var cur = p + plane * new Vector3(Mathf.Cos(ang) * radius, 0f,
                                                  Mathf.Sin(ang) * radius);
                Gizmos.DrawLine(prev, cur);
                prev = cur;
            }
        }
#endif
    }
}
