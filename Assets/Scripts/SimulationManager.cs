using UnityEngine;

/// <summary>
/// Orquestador maestro del modo de simulación para generación de dataset.
///
/// Arquitectura (event-driven, sin corrutinas, sin timeScale inter-ventana):
/// ─ Start() fija Time.timeScale UNA sola vez y suscribe HandleWindowCompletedEvent.
/// ─ HandleWindowCompletedEvent es llamado por TelemetryManager al cerrar cada ventana.
///   El handler gestiona conteo, respawn, fin de simulación y elección de clase.
/// ─ IniciarNuevaVentana() ejecuta FullHeal + reset + configuración del bot.
/// ─ La propiedad CurrentTargetClass expone la clase activa para que TelemetryManager
///   la inyecte en el tensor via FindAnyObjectByType.
///
/// REGLA DE TIMESCALE:
/// Time.timeScale = 15f se fija en Start() y permanece constante.
/// Solo se restaura en FinalizarSimulacion(). Nunca se altera entre ventanas.
///
/// TIEMPO ESTIMADO:
/// 20 000 ventanas × 10 s / 15 = ~13 333 s reales (~3.7 horas). Ejecutar en builds.
/// </summary>
public class SimulationManager : MonoBehaviour
{
    // -----------------------------------------------------------------------
    // CONFIGURACIÓN (asignar en Inspector)
    // -----------------------------------------------------------------------
    [Header("Componentes del Sistema")]
    [Tooltip("Motor de telemetría. Se suscribe a su evento OnWindowCompleted.")]
    public TelemetryManager telemetryManager;

    [Tooltip("Referencia al BotInput (Strategy Pattern para el bot simulado).")]
    public BotInput botInput;

    [Tooltip("Referencia al PlayerController para hot-swap de input.")]
    public PlayerController playerController;

    [Tooltip("Referencia al HumanInput (se deshabilita en simulación).")]
    public HumanInput humanInput;

    [Header("Componentes de Salud (Respawn)")]
    [Tooltip("HealthController del jugador/bot.")]
    public HealthController playerHealth;

    [Tooltip("HealthController del NPC enemigo.")]
    public HealthController npcHealth;

    [Header("Parámetros de Simulación")]
    [Tooltip("Número total de ventanas de telemetría a generar.")]
    public int totalVentanasObjetivo = 20000;

    // -----------------------------------------------------------------------
    // ESTADO (solo lectura en Inspector)
    // -----------------------------------------------------------------------
    [Header("Estado (solo lectura)")]
    [SerializeField] private int _ventanasCompletadas = 0;
    [SerializeField] private int _claseActual = -1;
    [SerializeField] private bool _simulacionActiva = false;

    /// <summary>
    /// Clase objetivo actual. Leída por TelemetryManager para inyectarla en el tensor.
    /// </summary>
    public int CurrentTargetClass => _claseActual;

    // -----------------------------------------------------------------------
    // ESTADO INTERNO
    // -----------------------------------------------------------------------
    // Rotación balanceada de clases (0,1,2,3,0,1,2,3,...)
    private int[] _claseRotacion;
    private int _indiceRotacion = 0;

    // -----------------------------------------------------------------------
    // CICLO DE VIDA
    // -----------------------------------------------------------------------

    private void Start()
    {
        // Validar referencias críticas
        if (telemetryManager == null) { Debug.LogError("[SimMgr] telemetryManager no asignado."); enabled = false; return; }
        if (botInput == null) { Debug.LogError("[SimMgr] botInput no asignado."); enabled = false; return; }
        if (playerController == null) { Debug.LogError("[SimMgr] playerController no asignado."); enabled = false; return; }
        if (playerHealth == null) { Debug.LogError("[SimMgr] playerHealth no asignado."); enabled = false; return; }
        if (npcHealth == null) { Debug.LogError("[SimMgr] npcHealth no asignado."); enabled = false; return; }

        // ── Strategy swap: deshabilitar humano, activar bot ──
        if (humanInput != null) humanInput.enabled = false;
        botInput.enabled = true;
        playerController.SetInputProvider(botInput);

        // Inicializar rotación de clases
        _claseRotacion = new int[] { 0, 1, 2, 3 };

        // ── REGLA: TimeScale fijo. No se toca hasta FinalizarSimulacion() ──
        Time.timeScale = 15f;
        // CORRECCIÓN CRÍTICA: fixedDeltaTime debe MULTIPLICARSE por timeScale.
        Time.fixedDeltaTime = 0.02f * 15f;

        // Suscribirse al evento — el TelemetryManager es el director del tiempo.
        telemetryManager.OnWindowCompleted += HandleWindowCompletedEvent;

        _simulacionActiva = true;
        Debug.Log($"[SimMgr] Simulación iniciada. Objetivo: {totalVentanasObjetivo} ventanas. " +
                  $"TimeScale: {Time.timeScale}x. Estimado: " +
                  $"{(totalVentanasObjetivo * 10f / Time.timeScale / 3600f):F1} h reales.");

        // Configurar la primera ventana; el TelemetryManager ya corre desde su Start().
        IniciarNuevaVentana();
    }

    private void OnDestroy()
    {
        if (telemetryManager != null)
            telemetryManager.OnWindowCompleted -= HandleWindowCompletedEvent;
    }

    // -----------------------------------------------------------------------
    // HANDLER DE EVENTO — reemplaza Update() + Coroutine por completo
    // -----------------------------------------------------------------------
    private void HandleWindowCompletedEvent(TelemetryTensor tensor)
    {
        if (!_simulacionActiva) return;

        _ventanasCompletadas++;

        Debug.Log($"[SimMgr] Ventana {_ventanasCompletadas}/{totalVentanasObjetivo} completada | Clase exportada: {tensor.TargetClass}");

        if (_ventanasCompletadas >= totalVentanasObjetivo)
        {
            FinalizarSimulacion();
            return;
        }

        // ── Sin pausas, sin yield, sin timeScale inter-ventana ──
        IniciarNuevaVentana();
    }

    // -----------------------------------------------------------------------
    // GESTIÓN DE VENTANAS
    // -----------------------------------------------------------------------

    /// <summary>
    /// Selecciona la clase siguiente (rotación balanceada + 20 % de ruido),
    /// ejecuta respawn de ambos combatientes y configura el bot.
    /// </summary>
    private void IniciarNuevaVentana()
    {
        // ── RESPAWN: restaurar salud y posición de AMBOS combatientes ──
        // Garantiza que cada ventana inicie como un entorno de combate limpio.
        playerHealth.FullHeal();
        npcHealth.FullHeal();

        // Rotación determinista 0→1→2→3→0→... con ruido del 20 %
        _claseActual = _claseRotacion[_indiceRotacion % 4];
        _indiceRotacion++;

        if (Random.value < 0.2f)
            _claseActual = Random.Range(0, 4);

        // Muestrear parámetros estocásticos e inyectar al bot via IPlayerInput
        BotTargetParams botParams = SimStats.SampleParamsForClass(_claseActual);
        botInput.OnWindowStart(botParams);

        Debug.Log($"[SimMgr] Nueva ventana: clase={_claseActual} | {botParams}");
    }

    // -----------------------------------------------------------------------
    // FIN DE SIMULACIÓN
    // -----------------------------------------------------------------------

    private void FinalizarSimulacion()
    {
        _simulacionActiva = false;
        _claseActual = -1;

        // Desuscribir antes de detener el bot
        if (telemetryManager != null)
            telemetryManager.OnWindowCompleted -= HandleWindowCompletedEvent;

        botInput.StopBot();

        // Restaurar tiempo a escala normal — única vez que se toca post-inicio
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;

        // ── Strategy swap: restaurar humano ──
        if (humanInput != null)
        {
            humanInput.enabled = true;
            playerController.SetInputProvider(humanInput);
        }

        Debug.Log($"[SimMgr] ✓ Simulación completada. " +
                  $"{_ventanasCompletadas} ventanas exportadas. TimeScale restaurado a 1.");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPaused = true;
#endif
    }

    // -----------------------------------------------------------------------
    // API PÚBLICA
    // -----------------------------------------------------------------------

    /// <summary>
    /// Notifica daño recibido por el bot. Delegado al bot via IPlayerInput.OnDamageTaken().
    /// </summary>
    public void NotifyBotDamageTaken()
    {
        if (botInput != null)
            botInput.OnDamageTaken();
    }

    // -----------------------------------------------------------------------
    // DEBUG ON-SCREEN (Editor only)
    // -----------------------------------------------------------------------
#if UNITY_EDITOR
    private void OnGUI()
    {
        if (!_simulacionActiva) return;

        GUI.color = Color.cyan;
        GUI.Label(new Rect(10, 10, 500, 20),
            $"[SIM] Ventanas: {_ventanasCompletadas}/{totalVentanasObjetivo} | " +
            $"Clase: {_claseActual} | Scale: {Time.timeScale}x");
        GUI.Label(new Rect(10, 30, 500, 20),
            $"[SIM] Restante estimado: " +
            $"{((totalVentanasObjetivo - _ventanasCompletadas) * 10f / Time.timeScale / 60f):F0} min");
        GUI.color = Color.white;
    }
#endif
}