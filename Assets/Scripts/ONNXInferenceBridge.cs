using UnityEngine;
using Unity.InferenceEngine; // <-- NUEVO NAMESPACE EN UNITY 6
using System;

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
        // 1. Población del array cacheado (0 GC Allocations)
        inputDataCache[0] = data.APM;
        inputDataCache[1] = data.PrecisionRelativa;
        inputDataCache[2] = data.IndiceCobertura;
        inputDataCache[3] = data.IndicePostDano;
        inputDataCache[4] = data.IET;

        // 2. Creación del Tensor tipado (Uso de 'using' para forzar Dispose automático en memoria no administrada)
        using Tensor<float> inputTensor = new Tensor<float>(new TensorShape(1, TENSOR_SIZE), inputDataCache);

        // 3. Ejecución (Schedule reemplaza a Execute en la versión 2.x)
        worker.Schedule(inputTensor);

        // 4. Extracción de Resultados
        // PeekOutput devuelve la clase base abstracta 'Tensor'
        using Tensor outputTensor = worker.PeekOutput();

        int inferredProfile = -1;

        // Casteo de patrones para manejar las salidas de ONNX (Int32, Int64 o Float32)
        if (outputTensor is Tensor<int> int32Tensor)
        {
            // Hummingbird suele exportar las clases como Enteros de 32 bits
            int[] results = int32Tensor.DownloadToArray();
            inferredProfile = results[0];
        }
        else if (outputTensor is Tensor<long> int64Tensor)
        {
            // XGBoost nativo suele exportar las clases como Enteros de 64 bits
            long[] results = int64Tensor.DownloadToArray();
            inferredProfile = (int)results[0];
        }
        else if (outputTensor is Tensor<float> floatTensor)
        {
            // Redes neuronales estándar suelen devolver probabilidades en Float
            float[] results = floatTensor.DownloadToArray();
            inferredProfile = Mathf.RoundToInt(results[0]);
        }
        else
        {
            // Diagnóstico profundo si falla
            Debug.LogError($"[InferenceEngine] Formato desconocido. Clase C#: {outputTensor.GetType()} | ONNX DataType: {outputTensor.dataType}");
            return;
        }

        Debug.Log($"[Inferencia Híbrida] Tensor Inyectado. Perfil Detectado: {inferredProfile}");
        
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