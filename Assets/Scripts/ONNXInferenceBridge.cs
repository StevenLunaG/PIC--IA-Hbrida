using System;
using Unity.InferenceEngine;
using UnityEngine;

/// <summary>
/// Puente de inferencia ONNX entre el TelemetryManager y la FSM del NPC.
///
/// Arquitectura:
/// - Tensor de entrada [1, 4] → APM | PrecisionRelativa | IET | VarianzaDistancia
/// - Suavizado temporal (histéresis): Moda de los últimos N perfiles antes de transicionar la FSM.
///   Evita cambios incoherentes por picos de datos en una sola ventana.
/// - Zero Allocation: sin LINQ, sin new() en hot path. Todo pre-reservado en Start().
/// - Log legible: nombre de perfil + métricas clave por ventana.
/// - API de testing (RunInferenceForTesting): ejecuta el modelo sobre un vector arbitrario
///   sin tocar histéresis ni la FSM. Uso exclusivo para validación de paridad
///   (Sentis/Inference Engine vs. referencia onnxruntime). No forma parte del hot path.
/// </summary>
public class ONNXInferenceBridge : MonoBehaviour
{
    [Header("Inyección de Dependencias")]
    public TelemetryManager telemetryManager;

    [Header("Modelo ONNX")]
    public ModelAsset onnxModel;

    [Header("Suavizado Temporal (Histéresis)")]
    [Tooltip("Número de ventanas a promediar antes de transicionar la FSM. Mínimo 1 = sin suavizado.")]
    [Range(1, 7)]
    public int ventanasDeHistéresis = 3;

    // Evento para notificar a la FSM del NPC el perfil detectado.
    // -1 = Patrullaje Errático (AFK o moda no concluyente)
    //  0 = Conservador | 1 = Agresivo | 2 = Caótico | 3 = Táctico
    public event Action<int> OnProfileInferred;

    // ── Contrato de Tensor [1, 4] ──────────────────────────────────────────
    private const int TENSOR_SIZE = 4;
    // Índices: [0]=APM  [1]=PrecisionRelativa  [2]=IET  [3]=VarianzaDistancia

    // Caché de inferencia (Zero Allocation)
    private float[] inputDataCache = new float[TENSOR_SIZE];

    // Motor de inferencia
    private Worker worker;

    // ── Suavizado temporal ────────────────────────────────────────────────────
    // Buffer circular de perfiles recientes. Pre-reservado en Start().
    private int[] _historyBuffer;
    private int _historyIndex = 0;
    private int _historyCount = 0; // Cuántos valores válidos hay en el buffer
    private int _lastEmittedProfile = -1; // Estado anterior: se mantiene en empates

    // Contador de votos por clase (0-3). Reutilizado en cada cálculo de moda.
    private int[] _voteCounts = new int[4];

    // ── Nombres de perfiles ──────────────────────────────────────────
    private static readonly string[] NombresPerfil =
    {
        "Conservador (0)",
        "Agresivo    (1)",
        "Caótico     (2)",
        "Táctico     (3)"
    };

    // ── Ciclo de vida ─────────────────────────────────────────────────────────

    private void Start()
    {
        if (onnxModel == null)
        {
            Debug.LogError("[Bridge] ModelAsset no asignado. Abortando inicialización.");
            return;
        }

        // Pre-reservar buffer de histéresis (Zero Allocation posterior)
        int bufferSize = Mathf.Max(1, ventanasDeHistéresis);
        _historyBuffer = new int[bufferSize];

        // Inicializar buffer con -1 (sin dato)
        for (int i = 0; i < _historyBuffer.Length; i++)
            _historyBuffer[i] = -1;

        // Cargar modelo y crear Worker en CPU
        Model runtimeModel = ModelLoader.Load(onnxModel);
        worker = new Worker(runtimeModel, BackendType.CPU);

        if (telemetryManager != null)
            telemetryManager.OnWindowCompleted += ExecuteInference;
        else
            Debug.LogError("[Bridge] TelemetryManager no asignado.");
    }

    private void OnDestroy()
    {
        if (telemetryManager != null)
            telemetryManager.OnWindowCompleted -= ExecuteInference;

        worker?.Dispose();
    }

    // ── Pipeline de inferencia ────────────────────────────────────────────────

    private void ExecuteInference(TelemetryTensor data)
    {
        // ── BYPASS AFK: APM == 0 → Patrullaje Errático inmediato ──────────────
        // Solo se evalúa APM: es la única señal puramente del jugador. IET es
        // matemáticamente 0 cuando APM=0 (no aporta info nueva). VarianzaDistancia
        // se sacó del gate: no es señal del jugador sino de la distancia relativa
        // Jugador↔NPC, y el propio NPC la contamina al patrullar hacia sus puntos
        // predefinidos durante este mismo estado — eso rompía el bypass y colaba
        // ventanas espurias (APM=0, fuera de distribución) al modelo.
        if (data.APM == 0f)
        {
            // Limpia el historial de histéresis: evita que votos previos al AFK
            // sobrevivan en el buffer circular y contaminen la moda apenas el
            // jugador retome actividad.
            LimpiarHistorial();
            EmitirPerfil(-1, data, forzado: true);
            return;
        }

        // ── 1. Poblar array cacheado (Orden de contrato [1,4]) ─────
        inputDataCache[0] = data.APM;
        inputDataCache[1] = data.PrecisionRelativa;
        inputDataCache[2] = data.IET;
        inputDataCache[3] = data.VarianzaDistancia;

        // ── 2-4. Ejecutar modelo y extraer label crudo (sin probabilidades: hot path) ──
        int rawProfile = EjecutarModeloRaw(inputDataCache, null);

        if (rawProfile == -1)
        {
            // EjecutarModeloRaw ya logueó el error (tipo de salida desconocido).
            // Mismo comportamiento que el original: abortar sin tocar histéresis.
            return;
        }

        // ── 5. Suavizado temporal (histéresis por moda) ───────────────────────
        // Guardar el perfil raw en el buffer circular
        _historyBuffer[_historyIndex] = rawProfile;
        _historyIndex = (_historyIndex + 1) % _historyBuffer.Length;
        _historyCount = Mathf.Min(_historyCount + 1, _historyBuffer.Length);

        // Calcular moda sin LINQ ni allocations
        int smoothedProfile = CalcularModa();

        // ── 6. Emitir perfil suavizado ────────────────────────────────────────
        EmitirPerfil(smoothedProfile, data, forzado: false);
    }

    /// <summary>
    /// Construye el tensor de entrada a partir de inputVector, ejecuta el Worker y extrae
    /// output_label (maneja int32, int64 y float por robustez). Si probabilidadesSalida no es
    /// null, además extrae output_probability (no se pide en el hot path de producción para no
    /// añadir un PeekOutput/Download extra en cada ventana; sí se pide desde
    /// RunInferenceForTesting para la validación de paridad).
    /// </summary>
    private int EjecutarModeloRaw(float[] inputVector, float[] probabilidadesSalida)
    {
        using Tensor<float> inputTensor =
            new Tensor<float>(new TensorShape(1, TENSOR_SIZE), inputVector);

        worker.Schedule(inputTensor);

        int rawProfile = -1;
        using Tensor outputTensor = worker.PeekOutput("output_label");

        if (outputTensor is Tensor<int> int32Tensor)
        {
            rawProfile = int32Tensor.DownloadToArray()[0];
        }
        else if (outputTensor is Tensor<long> int64Tensor)
        {
            rawProfile = (int)int64Tensor.DownloadToArray()[0];
        }
        else if (outputTensor is Tensor<float> floatTensor)
        {
            rawProfile = Mathf.RoundToInt(floatTensor.DownloadToArray()[0]);
        }
        else
        {
            Debug.LogError($"[Bridge] Tipo de salida desconocido: {outputTensor.GetType()}");
            return -1;
        }

        if (probabilidadesSalida != null)
        {
            using Tensor probTensor = worker.PeekOutput("output_probability");

            if (probTensor is Tensor<float> probFloatTensor)
            {
                float[] probs = probFloatTensor.DownloadToArray();
                int n = Mathf.Min(probs.Length, probabilidadesSalida.Length);
                for (int i = 0; i < n; i++)
                    probabilidadesSalida[i] = probs[i];
            }
            else
            {
                Debug.LogWarning($"[Bridge] output_probability con tipo inesperado: {probTensor.GetType()}");
            }
        }

        return rawProfile;
    }

    /// <summary>
    /// Calcula la moda del buffer de histéresis sin LINQ ni allocations.
    /// En caso de empate retorna el último perfil emitido (histéresis conservadora).
    /// </summary>
    private int CalcularModa()
    {
        // Reset de contadores (array ya reservado, solo escribir ceros)
        for (int i = 0; i < _voteCounts.Length; i++)
            _voteCounts[i] = 0;

        // Contar votos de los valores válidos en el buffer
        for (int i = 0; i < _historyCount; i++)
        {
            int val = _historyBuffer[i];
            if (val >= 0 && val <= 3)
                _voteCounts[val]++;
        }

        // Buscar el perfil con más votos
        int maxVotos = 0;
        int modaPerfil = _lastEmittedProfile; // Empate → mantener estado anterior
        bool hayEmpate = false;

        for (int i = 0; i < _voteCounts.Length; i++)
        {
            if (_voteCounts[i] > maxVotos)
            {
                maxVotos = _voteCounts[i];
                modaPerfil = i;
                hayEmpate = false;
            }
            else if (_voteCounts[i] == maxVotos && maxVotos > 0)
            {
                hayEmpate = true;
            }
        }

        // En empate, mantener el perfil anterior (histéresis conservadora)
        return hayEmpate ? _lastEmittedProfile : modaPerfil;
    }

    /// <summary>
    /// Resetea el buffer circular de histéresis (todo a -1) y el contador de
    /// ventanas válidas. Se llama al entrar en AFK para que ningún voto previo
    /// sobreviva y contamine la moda cuando el jugador retome actividad.
    /// </summary>
    private void LimpiarHistorial()
    {
        for (int i = 0; i < _historyBuffer.Length; i++)
            _historyBuffer[i] = -1;

        _historyIndex = 0;
        _historyCount = 0;
    }

    /// <summary>
    /// Emite el perfil a la FSM y registra el log de consola.
    /// </summary>
    private void EmitirPerfil(int perfil, TelemetryTensor data, bool forzado)
    {
        bool huboTransicion = perfil != _lastEmittedProfile;
        _lastEmittedProfile = perfil;

        // ── LOG EN CONSOLA ───────────────────────────────────────────────────────
        string nombrePerfil = (perfil >= 0 && perfil <= 3)
            ? NombresPerfil[perfil]
            : "AFK / Inactivo (-1)";

        string transicionTag = forzado ? "[AFK]"
                             : huboTransicion ? "[NUEVO]"
                             : "[MANTIENE]";

        // Votos del buffer (solo si hay histéresis activa)
        string votosStr = _historyBuffer.Length > 1
            ? $" | Votos [C:{_voteCounts[0]} A:{_voteCounts[1]} X:{_voteCounts[2]} T:{_voteCounts[3]}]"
            : "";

        Debug.Log(
            $"[Bridge] {transicionTag} Perfil: {nombrePerfil}{votosStr}\n" +
            $"         Telemetría → APM:{data.APM,6:F1}  " +
            $"Precisión:{data.PrecisionRelativa,5:F3}  " +
            $"IET:{data.IET,6:F3}  " +
            $"VarDist:{data.VarianzaDistancia,6:F2}"
        );

        // ── EMITIR A LA FSM ───────────────────────────────────────────────────
        OnProfileInferred?.Invoke(perfil);
    }

    // ── API de Testing / Validación de Paridad (no forma parte del hot path) ───

    /// <summary>
    /// Ejecuta el modelo ONNX sobre un vector de features arbitrario, sin tocar el buffer
    /// de histéresis, _lastEmittedProfile ni disparar OnProfileInferred. Pensado
    /// exclusivamente para la validación de paridad end-to-end (Sentis/Inference Engine vs.
    /// referencia onnxruntime) — no debe llamarse desde el hot path de producción.
    /// La asignación del vector local es intencional: este método no está sujeto a las
    /// restricciones de Zero Allocation del pipeline en tiempo real.
    /// </summary>
    /// <param name="apm">APM del vector a evaluar.</param>
    /// <param name="precisionRelativa">PrecisionRelativa del vector a evaluar.</param>
    /// <param name="iet">IET del vector a evaluar.</param>
    /// <param name="varianzaDistancia">VarianzaDistancia del vector a evaluar.</param>
    /// <param name="probabilidades">Array de salida, pre-reservado con al menos 4 elementos,
    /// donde se escribe output_probability por clase.</param>
    /// <returns>Label crudo devuelto por el modelo (0-3), o -1 si la extracción falló.</returns>
    public int RunInferenceForTesting(
        float apm, float precisionRelativa, float iet, float varianzaDistancia,
        float[] probabilidades)
    {
        if (worker == null)
        {
            Debug.LogError("[Bridge] Worker no inicializado (¿se llamó antes de Start()?).");
            return -1;
        }

        float[] inputVector = { apm, precisionRelativa, iet, varianzaDistancia };
        return EjecutarModeloRaw(inputVector, probabilidades);
    }
}