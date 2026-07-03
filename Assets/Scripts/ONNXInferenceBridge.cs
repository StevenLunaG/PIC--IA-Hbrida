using System;
using Unity.InferenceEngine; // <-- NUEVO NAMESPACE EN UNITY 6
using UnityEngine;

public class ONNXInferenceBridge : MonoBehaviour
{
    [Header("Inyección de Dependencias")]
    public TelemetryManager telemetryManager;

    [Header("Modelo ONNX")]
    public ModelAsset onnxModel;

    // Evento para notificar a la FSM del NPC el perfil detectado
    // 0: Conservador, 1: Agresivo, 2: Caótico, 3: Táctico
    public event Action<int> OnProfileInferred;

    private Worker worker;
    private const int TENSOR_SIZE = 5;

    // Caché de memoria para evitar GC Spikes (Zero Allocation)
    private float[] inputDataCache = new float[TENSOR_SIZE];

    private void Start()
    {
        if (onnxModel == null)
        {
            Debug.LogError("[InferenceEngine] ModelAsset no asignado. Abortando inicialización.");
            return;
        }

        // Carga del modelo y creación del Worker en CPU (Optimizado para tensores pequeños)
        Model runtimeModel = ModelLoader.Load(onnxModel);
        worker = new Worker(runtimeModel, BackendType.CPU);

        // Suscripción al recolector de telemetría
        if (telemetryManager != null)
        {
            telemetryManager.OnWindowCompleted += ExecuteInference;
        }
    }

    private void ExecuteInference(TelemetryTensor data)
    {
        // BYPASS DE INACTIVIDAD (AFK DETECTION)
        // Si no hay APM, ni uso de cobertura, ni evasión, el jugador está estático.
        if (data.APM == 0f && data.IndiceCobertura == 0f && data.IndicePostDano == 0f && data.IET == 0f)
        {
            Debug.Log("[Inferencia Híbrida] Tensor nulo (Jugador Inactivo). Retornando a Patrullaje.");
            OnProfileInferred?.Invoke(-1); // -1 = Patrullaje Errático
            return;
        }

        // 1. Población del array cacheado (0 GC Allocations)
        inputDataCache[0] = data.APM;
        inputDataCache[1] = data.PrecisionRelativa;
        inputDataCache[2] = data.IndiceCobertura;
        inputDataCache[3] = data.IndicePostDano;
        inputDataCache[4] = data.IET;

        // 2. Creación del Tensor tipado 
        using Tensor<float> inputTensor = new Tensor<float>(new TensorShape(1, TENSOR_SIZE), inputDataCache);

        // 3. Ejecución 
        worker.Schedule(inputTensor);

        // 4. Extracción de Resultados
        using Tensor outputTensor = worker.PeekOutput();

        int inferredProfile = -1;

        if (outputTensor is Tensor<int> int32Tensor)
        {
            int[] results = int32Tensor.DownloadToArray();
            inferredProfile = results[0];
        }
        else if (outputTensor is Tensor<long> int64Tensor)
        {
            long[] results = int64Tensor.DownloadToArray();
            inferredProfile = (int)results[0];
        }
        else if (outputTensor is Tensor<float> floatTensor)
        {
            float[] results = floatTensor.DownloadToArray();
            inferredProfile = Mathf.RoundToInt(results[0]);
        }
        else
        {
            Debug.LogError($"[InferenceEngine] Formato desconocido. Tipo: {outputTensor.GetType()}");
            return;
        }

        Debug.Log($"[Inferencia Híbrida] Tensor Inyectado. Perfil Detectado (XGBoost): {inferredProfile}");

        // Disparar evento a la FSM
        OnProfileInferred?.Invoke(inferredProfile);
    }

    private void OnDestroy()
    {
        // Prevención crítica de Memory Leaks
        if (telemetryManager != null)
        {
            telemetryManager.OnWindowCompleted -= ExecuteInference;
        }

        // Destruir el motor de inferencia
        worker?.Dispose();
    }
}