using UnityEngine;

public class CrystalIdle : MonoBehaviour
{
    [Header("Rotation")]
    [Tooltip("Vitesse de rotation en degrés par seconde")]
    public float rotationSpeed = 20f;

    [Tooltip("Axe de rotation de base (en local). Laisse (0,1,0) pour tourner autour de Y.")]
    public Vector3 baseRotationAxis = Vector3.up;

    [Tooltip("Randomiser l’axe de rotation par cristal")]
    public bool randomizeRotationAxis = true;

    [Range(0f, 1f), Tooltip("0 = axe de base, 1 = axe totalement aléatoire")]
    public float randomAxisBlend = 0.5f;

    [Header("Lévitation / Sway")]
    [Tooltip("Amplitude du mouvement vertical (en unités)")]
    public float verticalAmplitude = 0.2f;

    [Tooltip("Fréquence du mouvement vertical (oscillations par seconde)")]
    public float verticalFrequency = 1f;

    [Tooltip("Amplitude du sway horizontal (en unités)")]
    public float horizontalAmplitude = 0.05f;

    [Tooltip("Fréquence du sway horizontal (oscillations par seconde)")]
    public float horizontalFrequency = 0.5f;

    [Header("Random par instance")]
    [Tooltip("Décaler la phase du mouvement pour chaque cristal")]
    public bool randomizePhase = true;

    [Tooltip("Randomiser automatiquement à l'Awake")]
    public bool randomizeOnStart = true;

    // Privés
    private Vector3 _baseLocalPosition;
    private Quaternion _baseLocalRotation;
    private Vector3 _finalRotationAxis;
    private float _timeOffset;

    void Awake()
    {
        // On garde la pose de base en local (par rapport au parent)
        _baseLocalPosition = transform.localPosition;
        _baseLocalRotation = transform.localRotation;

        if (randomizeOnStart)
        {
            InitRandom();
        }
        else
        {
            SetupRotationAxis();
            _timeOffset = 0f;
        }
    }

    void InitRandom()
    {
        SetupRotationAxis();

        if (randomizePhase)
        {
            // Décalage aléatoire dans le temps pour éviter que tous les cristaux bougent en synchro
            _timeOffset = Random.Range(0f, 100f);
        }
        else
        {
            _timeOffset = 0f;
        }
    }

    void SetupRotationAxis()
    {
        Vector3 axis = baseRotationAxis;
        if (axis == Vector3.zero)
            axis = Vector3.up;

        axis.Normalize();

        if (randomizeRotationAxis)
        {
            Vector3 randomAxis = Random.onUnitSphere;
            _finalRotationAxis = Vector3.Lerp(axis, randomAxis, randomAxisBlend).normalized;
        }
        else
        {
            _finalRotationAxis = axis;
        }
    }

    void Update()
    {
        float t = Time.time + _timeOffset;

        // --- Rotation ---
        float angle = rotationSpeed * Time.deltaTime;
        Quaternion deltaRot = Quaternion.AngleAxis(angle, _finalRotationAxis);
        transform.localRotation = transform.localRotation * deltaRot;

        // --- Lévitation / Sway ---
        float v = Mathf.Sin(t * Mathf.PI * 2f * verticalFrequency) * verticalAmplitude;
        float h1 = Mathf.Sin(t * Mathf.PI * 2f * horizontalFrequency) * horizontalAmplitude;
        float h2 = Mathf.Cos(t * Mathf.PI * 2f * horizontalFrequency) * horizontalAmplitude;

        Vector3 offset = new Vector3(h1, v, h2);
        transform.localPosition = _baseLocalPosition + offset;
    }
}
