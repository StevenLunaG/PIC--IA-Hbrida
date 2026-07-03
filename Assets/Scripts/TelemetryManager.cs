using System;
using UnityEngine;

/// <summary>
/// Motor de telemetría. Es el único dueño del tiempo de ventana.
///
/// Cambios de arquitectura:
/// - OnWindowCompleted emite solo el TelemetryTensor (firma de un solo parámetro).
/// - La clase supervisada (TargetClass) se inyecta en el tensor leyéndola del
///   SimulationManager via FindObjectOfType en PackageAndEmitTensor().
/// </summary>
public class TelemetryManager : MonoBehaviour
{
    [Header("Configuración del Extractor")]
    [Tooltip("Ventana de tiempo en segundos para extraer el tensor")]
    public float timeWindowSeconds = 10f;

    [Header("Referencias (Asignar en Inspector)")]
    public Transform playerTransform;
    public Transform npcTransform;

    // ── Evento C# estándar — firma original de un solo parámetro ──
    // La clase supervisada viaja DENTRO del tensor (TargetClass).
    public event Action<TelemetryTensor> OnWindowCompleted;

    // Acumuladores primitivos (0 GC Allocations)
    private float currentWindowTime;
    private int totalShots;
    private int successfulHits;

    private bool isInCover;
    private float timeInCover;

    private int damageEvents;
    private int coverSoughtAfterDamage; // Veces que se cubrió ≤2 s después de recibir daño
    private float timeSinceLastDamage = 999f;

    private float accumulatedDistance;
    private int distanceSamples;
    private float distanceSampleTimer;



    private void Start()
    {
        ResetAccumulators();
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        currentWindowTime += dt;
        timeSinceLastDamage += dt;

        // 1. Acumulación de Tiempo en Cobertura
        if (isInCover) timeInCover += dt;

        // 2. Muestreo escalonado de distancia (2 veces por segundo)
        distanceSampleTimer += dt;
        if (distanceSampleTimer >= 0.5f)
        {
            accumulatedDistance += Vector2.Distance(playerTransform.position, npcTransform.position);
            distanceSamples++;
            distanceSampleTimer = 0f;
        }

        // 3. Evaluación de Fin de Ventana — solo el TelemetryManager decide cuándo acaba
        if (currentWindowTime >= timeWindowSeconds)
        {
            PackageAndEmitTensor();
            ResetAccumulators();
        }
    }

    // -----------------------------------------------------------------------
    // API PÚBLICA — Inyección de estado externo
    // -----------------------------------------------------------------------



    public void RegisterShot() => totalShots++;
    public void RegisterHit() => successfulHits++;

    public void SetCoverState(bool state)
    {
        isInCover = state;
        if (state && timeSinceLastDamage <= 2.0f)
            coverSoughtAfterDamage++;
    }

    public void RegisterDamageTaken()
    {
        damageEvents++;
        timeSinceLastDamage = 0f;
    }

    // -----------------------------------------------------------------------
    // PROCESAMIENTO MATEMÁTICO
    // -----------------------------------------------------------------------

    private void PackageAndEmitTensor()
    {
        // Inyección de la etiqueta supervisada desde el orquestador
        int currentTargetClass = -1;
        SimulationManager simManager = FindAnyObjectByType<SimulationManager>();
        if (simManager != null)
        {
            currentTargetClass = simManager.CurrentTargetClass;
        }

        TelemetryTensor tensor = new TelemetryTensor
        {
            APM = (totalShots / timeWindowSeconds) * 60f,
            PrecisionRelativa = totalShots > 0 ? (float)successfulHits / totalShots : 0.0f,
            IndiceCobertura = Mathf.Clamp01(timeInCover / timeWindowSeconds),
            IndicePostDano = damageEvents > 0 ? (float)coverSoughtAfterDamage / damageEvents : 0.0f,
            IET = 0f, // Se calcula abajo
            TargetClass = currentTargetClass // Inyección de la etiqueta supervisada
        };

        // 5. IET (depende de APM e IndiceCobertura ya calculados)
        float averageDistance = distanceSamples > 0 ? accumulatedDistance / distanceSamples : 0.1f;
        tensor.IET = (tensor.APM * (1.0f - tensor.IndiceCobertura)) / (averageDistance + 1.0f);

        OnWindowCompleted?.Invoke(tensor);

        Debug.Log($"[Telemetry] APM:{tensor.APM:F1} PREC:{tensor.PrecisionRelativa:F3} " +
                  $"COB:{tensor.IndiceCobertura:F3} PDANO:{tensor.IndicePostDano:F3} " +
                  $"IET:{tensor.IET:F3} CLASE:{currentTargetClass}");
    }

    private void ResetAccumulators()
    {
        currentWindowTime = 0f;
        totalShots = 0;
        successfulHits = 0;
        timeInCover = 0f;
        damageEvents = 0;
        coverSoughtAfterDamage = 0;
        timeSinceLastDamage = 999f;
        accumulatedDistance = 0f;
        distanceSamples = 0;
        distanceSampleTimer = 0f;
        // isInCover NO se resetea: mantiene su estado físico actual
    }
}