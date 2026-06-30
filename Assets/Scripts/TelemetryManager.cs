using System;
using UnityEngine;

public class TelemetryManager : MonoBehaviour
{
    [Header("Configuración del Extractor")]
    [Tooltip("Ventana de tiempo en segundos para extraer el tensor")]
    public float timeWindowSeconds = 10f;

    [Header("Referencias (Asignar en Inspector)")]
    public Transform playerTransform;
    public Transform npcTransform;

    // Evento C# estándar para notificar al puente ONNX (desacoplado)
    public event Action<TelemetryTensor> OnWindowCompleted;

    // Acumuladores primitivos (0 GC Allocations)
    private float currentWindowTime;
    private int totalShots;
    private int successfulHits;

    private bool isInCover;
    private float timeInCover;

    private int damageEvents;
    private int coverSoughtAfterDamage; // Veces que se cubrió 2s después de recibir daño
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

        // 2. Muestreo escalonado de distancia (optimización de CPU: 2 veces por segundo)
        distanceSampleTimer += dt;
        if (distanceSampleTimer >= 0.5f)
        {
            accumulatedDistance += Vector2.Distance(playerTransform.position, npcTransform.position);
            distanceSamples++;
            distanceSampleTimer = 0f;
        }

        // 3. Evaluación de Fin de Ventana
        if (currentWindowTime >= timeWindowSeconds)
        {
            PackageAndEmitTensor();
            ResetAccumulators();
        }
    }

    // --- MÉTODOS PÚBLICOS DE INYECCIÓN (Llamados por otros scripts) ---

    public void RegisterShot() => totalShots++;
    public void RegisterHit() => successfulHits++;

    public void SetCoverState(bool state)
    {
        isInCover = state;
        // Si entra en cobertura menos de 2 segundos después de recibir daño, puntúa como táctico/evasivo
        if (state && timeSinceLastDamage <= 2.0f)
        {
            coverSoughtAfterDamage++;
        }
    }

    public void RegisterDamageTaken()
    {
        damageEvents++;
        timeSinceLastDamage = 0f;
    }

    // --- PROCESAMIENTO MATEMÁTICO ---

    private void PackageAndEmitTensor()
    {
        TelemetryTensor tensor = new TelemetryTensor();

        // 1. APM (Acciones por Minuto)
        tensor.APM = (totalShots / timeWindowSeconds) * 60f;

        // 2. Precisión Relativa (Control de división por cero)
        tensor.PrecisionRelativa = totalShots > 0 ? (float)successfulHits / totalShots : 0.0f;

        // 3. Índice de Cobertura (Normalizado 0.0 a 1.0)
        tensor.IndiceCobertura = Mathf.Clamp01(timeInCover / timeWindowSeconds);

        // 4. Índice Post-Daño (Frecuencia de reacción defensiva)
        tensor.IndicePostDano = damageEvents > 0 ? (float)coverSoughtAfterDamage / damageEvents : 0.0f;

        // 5. IET (Índice de Exposición Temeraria)
        float averageDistance = distanceSamples > 0 ? accumulatedDistance / distanceSamples : 0.1f;
        tensor.IET = (tensor.APM * (1.0f - tensor.IndiceCobertura)) / (averageDistance + 1.0f);

        // Notificar a los suscriptores (ej. ONNX Bridge o CSV Exporter)
        OnWindowCompleted?.Invoke(tensor);
        Debug.Log($"APM: {tensor.APM}, IET: {tensor.IET}");
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
        // isInCover NO se resetea, mantiene su estado físico actual
    }
}