using UnityEngine;

/// <summary>
/// Faz cristais orbitarem a cabeca do personagem de forma continua, independente
/// de qual clip o Animator esteja tocando.
///
/// Coloque este componente no ROOT do personagem.
/// Os cristais devem ser filhos do root, NAO filhos de ossos do esqueleto:
/// o script escreve a posicao deles em espaco de mundo todo frame.
///
/// Roda em LateUpdate porque o Animator escreve a pose em Update. Se voce ler
/// a posicao da cabeca antes disso, os cristais ficam um frame atrasados e tremem.
/// </summary>
[DefaultExecutionOrder(200)]
public class CrystalOrbit : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Deixe vazio para buscar no proprio objeto ou nos filhos.")]
    public Animator animator;
    [Tooltip("Os cristais, na ordem. Eles sao distribuidos igualmente na orbita.")]
    public Transform[] crystals;

    [Header("Ancoragem")]
    [Tooltip("Deslocamento a partir do osso da cabeca, em espaco do personagem. " +
             "Suba um pouco em Y se o capuz for alto.")]
    public Vector3 headOffset = new Vector3(0f, 0.18f, 0f);

    [Header("Orbita")]
    public float radius = 0.45f;
    [Tooltip("Graus por segundo. Negativo inverte o sentido.")]
    public float orbitSpeed = 42f;
    [Tooltip("Inclinacao do plano da orbita, para nao ficar perfeitamente horizontal.")]
    public float tilt = 14f;
    [Tooltip("Cada cristal sobe e desce fora de fase com os outros.")]
    public float bobAmplitude = 0.055f;
    public float bobSpeed = 1.3f;
    [Tooltip("Rotacao do cristal em torno do proprio eixo.")]
    public float selfSpin = 70f;

    [Header("Inercia")]
    [Tooltip("Quanto maior, mais colado na cabeca. Valores baixos dao a sensacao " +
             "de que os cristais tem massa e arrastam atras do movimento.")]
    public float followSpeed = 11f;

    [Header("Variacao por instancia")]
    [Tooltip("Aleatoriza a fase inicial. Util quando ha varios magos em cena " +
             "para eles nao girarem em sincronia.")]
    public bool randomizeStartPhase = true;

    Transform head;
    Vector3 pivot;
    float phase;
    bool ready;

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman)
            head = animator.GetBoneTransform(HumanBodyBones.Head);

        if (head == null)
        {
            Debug.LogError("[CrystalOrbit] Osso da cabeca nao encontrado. " +
                           "O rig precisa estar configurado como Humanoid.", this);
            enabled = false;
            return;
        }

        if (randomizeStartPhase) phase = Random.Range(0f, 360f);
    }

    void LateUpdate()
    {
        if (crystals == null || crystals.Length == 0) return;

        // Alvo: posicao da cabeca com um offset em espaco do personagem.
        Vector3 target = head.position + transform.rotation * headOffset;

        if (!ready) { pivot = target; ready = true; }

        // Suavizacao exponencial: independente de framerate, ao contrario de
        // um Lerp com t fixo.
        pivot = Vector3.Lerp(pivot, target, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));

        // A fase acumula sozinha e nunca reseta. E por isso que a orbita nao
        // da pop quando o Animator troca de estado.
        phase += orbitSpeed * Time.deltaTime;
        if (phase > 360f) phase -= 360f;
        else if (phase < 0f) phase += 360f;

        // O plano da orbita acompanha o yaw do personagem, nao o da cabeca.
        // Se seguisse a cabeca, cada olhada para o lado arrastaria os cristais.
        Quaternion plane = transform.rotation * Quaternion.Euler(tilt, 0f, 0f);

        float step = 360f / crystals.Length;

        for (int i = 0; i < crystals.Length; i++)
        {
            Transform c = crystals[i];
            if (c == null) continue;

            float a = (phase + step * i) * Mathf.Deg2Rad;
            float bob = Mathf.Sin(Time.time * bobSpeed + i * 2.1f) * bobAmplitude;

            Vector3 local = new Vector3(Mathf.Cos(a) * radius, bob, Mathf.Sin(a) * radius);

            c.position = pivot + plane * local;
            c.Rotate(Vector3.up, selfSpin * Time.deltaTime, Space.Self);
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Animator a = animator != null ? animator : GetComponentInChildren<Animator>();
        if (a == null || !a.isHuman) return;
        Transform h = a.GetBoneTransform(HumanBodyBones.Head);
        if (h == null) return;

        Vector3 p = h.position + transform.rotation * headOffset;
        Quaternion plane = transform.rotation * Quaternion.Euler(tilt, 0f, 0f);

        Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.9f);
        Vector3 prev = p + plane * new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= 48; i++)
        {
            float ang = i / 48f * Mathf.PI * 2f;
            Vector3 cur = p + plane * new Vector3(Mathf.Cos(ang) * radius, 0f,
                                                  Mathf.Sin(ang) * radius);
            Gizmos.DrawLine(prev, cur);
            prev = cur;
        }
    }
#endif
}
