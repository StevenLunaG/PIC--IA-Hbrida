using System;
using UnityEngine;

/// <summary>
/// Motor de telemetría. Es el único dueño del tiempo de ventana.
///
/// Arquitectura:
/// - OnWindowCompleted emite solo el TelemetryTensor.
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

    // ── Evento C# estándar ──
    // La clase supervisada viaja DENTRO del tensor (TargetClass).
    public event Action<TelemetryTensor> OnWindowCompleted;

    // Acumuladores primitivos (0 GC Allocations)
    private float currentWindowTime;
    private int totalShots;
    private int successfulHits;

    private bool isInCover;
    private float timeInCover;

    private int damageEvents;
    private int coverSoughtAfterDamage;
    private float timeSinceLastDamage = 999f;
    // Flag anti-doble-conteo: se habilita al recibir daño, se bloquea al registrar cobertura.
    // Garantiza que coverSoughtAfterDamage se incremente como máximo 1 vez por evento de daño.
    private bool _coverCountedForCurrentDamage = false;

    private float accumulatedDistance;
    private float accumulatedDistanceSquared; // Para varianza: SumXi^2
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
            float sampleDist = Vector2.Distance(playerTransform.position, npcTransform.position);
            accumulatedDistance += sampleDist;
            accumulatedDistanceSquared += sampleDist * sampleDist; // Para Var = E[X^2] - E[X]^2
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
        // Ventana de 8s de tiempo de juego (~0.53s reales a timeScale=15).
        // Flag anti-doble-conteo: solo se registra 1 cobertura por evento de daño.
        if (state && timeSinceLastDamage <= 8.0f && !_coverCountedForCurrentDamage)
        {
            coverSoughtAfterDamage++;
            _coverCountedForCurrentDamage = true;
        }
    }

    public void RegisterDamageTaken()
    {
        damageEvents++;
        timeSinceLastDamage = 0f;
        _coverCountedForCurrentDamage = false; // habilitar conteo para este golpe
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

        // Distancia promedio, varianza e IET (dependen de los acumuladores de distancia)
        float averageDistance = distanceSamples > 0 ? accumulatedDistance / distanceSamples : 0.1f;

        // Varianza: Var = E[X^2] - E[X]^2  (formula de König-Huygens, zero-allocation)
        float meanSquare = distanceSamples > 0 ? accumulatedDistanceSquared / distanceSamples : 0f;
        float varianceDist = Mathf.Max(0f, meanSquare - (averageDistance * averageDistance));

        tensor.IET = (tensor.APM * (1.0f - tensor.IndiceCobertura)) / (averageDistance + 1.0f);
        tensor.DistanciaPromedio = averageDistance;
        tensor.VarianzaDistancia = varianceDist;

        OnWindowCompleted?.Invoke(tensor);

        Debug.Log($"[Telemetry] APM:{tensor.APM:F1} PREC:{tensor.PrecisionRelativa:F3} " +
                  $"COB:{tensor.IndiceCobertura:F3} PDANO:{tensor.IndicePostDano:F3} " +
                  $"IET:{tensor.IET:F3} DIST:{averageDistance:F2} VAR:{varianceDist:F2} " +
                  $"CLASE:{currentTargetClass}");
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
        _coverCountedForCurrentDamage = false;
        accumulatedDistance = 0f;
        accumulatedDistanceSquared = 0f;
        distanceSamples = 0;
        distanceSampleTimer = 0f;
        // isInCover NO se resetea: mantiene su estado físico actual
    }
}