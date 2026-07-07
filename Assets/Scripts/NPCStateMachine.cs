using UnityEngine;
using UnityEngine.AI;

public enum NPCState
{
    PatrullajeErratico = -1, // Estado N/A o Tensor Cero
    Asedio = 0,              // Contra Conservador
    EvasionTactica = 1,      // Contra Agresivo
    Embestida = 2,           // Contra Caótico
    Flanqueo = 3             // Contra Táctico
}

[RequireComponent(typeof(NavMeshAgent))]
public class NPCStateMachine : MonoBehaviour
{
    [Header("Inyección de Dependencias")]
    public ONNXInferenceBridge onnxBridge;
    public Transform player;
    public WeaponController weaponController; // NUEVO: Capacidad letal
    public Transform[] flankingWaypoints;

    public HealthController playerHealth; // Para verificar si el jugador está vivo

    [Header("Límite Espacial")]
    [Tooltip("Distancia mínima al jugador para estados de combate directo (Asedio/Embestida).")]
    [SerializeField] private float minAttackDistance = 2.5f;

    private NavMeshAgent agent;
    private NPCState currentState = NPCState.PatrullajeErratico;

    // Variables para patrullaje estocástico sin allocations
    private float patrolTimer = 0f;
    private int currentPatrolIndex = 0;

    [Header("Comportamiento de Patrullaje (Baseline Activo)")]
    [Tooltip("Distancia al cuadrado para activar el disparo defensivo del NPC.")]
    [SerializeField] private float aggroRadius = 15f;
    [Tooltip("Intervalo base entre intentos de disparo durante el patrullaje.")]
    [SerializeField] private float patrolShootInterval = 1.5f;

    // Temporizador interno para el disparo en patrullaje
    private float _patrolShootTimer = 0f;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.updateRotation = false;
        agent.updateUpAxis = false;
    }

    private void Start()
    {
        if (onnxBridge != null) onnxBridge.OnProfileInferred += TransitionToCounterState;

        // Cachear el propio HealthController para el guard clause post-muerte.
        _npcOwnHealth = GetComponent<HealthController>();
    }

    private void OnDestroy()
    {
        if (onnxBridge != null) onnxBridge.OnProfileInferred -= TransitionToCounterState;
    }

    [Header("Modo Simulación / Recolección de Dataset")]
    [Tooltip("Si es TRUE, ignora el modelo ONNX y fuerza el estado de Patrullaje Errático.")]
    public bool forceNeutralBaseline = false;

    // Flag de simulación activa. Seteado por SimulationManager via SetSimulationMode().
    // Controla parámetros de combate del NPC durante la generación de dataset.
    private bool _isSimulationMode = false;

    // Referencia al propio HealthController para el guard clause post-muerte.
    private HealthController _npcOwnHealth;

    /// <summary>
    /// Activa o desactiva el modo simulación del NPC.
    /// En modo simulación: cadencia de disparo 3x más rápida y aggroRadius efectivo = 999f.
    /// Llamado por SimulationManager al inicio y fin de la sesión de generación de datos.
    /// </summary>
    public void SetSimulationMode(bool active)
    {
        _isSimulationMode = active;
        Debug.Log($"[NPC FSM] Modo simulación: {active}. " +
                  $"Cadencia efectiva: {(active ? 0.5f : patrolShootInterval)}s | " +
                  $"AggroRadius efectivo: {(active ? 999f : aggroRadius)}u");
    }

    // Busca el método existente TransitionToCounterState y modifícalo así:
    private void TransitionToCounterState(int inferredProfile)
    {
        // CORTAFUEGOS DE SIMULACIÓN: Mantiene el entorno neutro para recolectar el Baseline
        if (forceNeutralBaseline)
        {
            currentState = NPCState.PatrullajeErratico;
            return; // Anula cualquier otra transición
        }

        // Lógica normal de producción
        currentState = (NPCState)inferredProfile;
        Debug.Log($"[NPC FSM] Ejecutando Matriz: {currentState}");
    }

    private void Update()
    {
        // Guard clause: detener si el NavMesh no está listo, el jugador murió,
        // o el propio NPC murió (evita el "NPC fantasma" post-muerte en simulación).
        if (!agent.isOnNavMesh
            || (playerHealth != null && playerHealth.IsDead)
            || (_npcOwnHealth != null && _npcOwnHealth.IsDead)) return;

        switch (currentState)
        {
            case NPCState.PatrullajeErratico:
                ExecutePatrol();
                break;
            case NPCState.Asedio:
                ExecuteSiege();
                break;
            case NPCState.EvasionTactica:
                ExecuteKiting();
                break;
            case NPCState.Embestida:
                ExecuteRush();
                break;
            case NPCState.Flanqueo:
                ExecuteFlank();
                break;
        }
    }

    // --- MECÁNICA COMÚN DE DISPARO ---
    private void ShootAtPlayer()
    {
        if (weaponController == null) return;
        // Calcula vector dirección (Zero Allocation)
        Vector2 direction = (player.position - transform.position).normalized;
        weaponController.TryShoot(direction);
    }

    // --- MATRIZ DE CONTRAATAQUE ESTRICTA ---

    private void ExecutePatrol()
    {
        // En modo simulación: cadencia 3x más rápida y aggro universal para garantizar
        // suficientes eventos de daño por ventana y activar IndicePostDano correctamente.
        // En modo interactivo: parámetros originales del Inspector.
        float effectiveShootInterval = _isSimulationMode ? 0.5f : patrolShootInterval;
        float effectiveAggroRadius = _isSimulationMode ? 999f : aggroRadius;

        // 1. Lógica de Movimiento Estocástico (Navegación)
        agent.stoppingDistance = 0f; // Reset: navegación a waypoints exactos
        patrolTimer += Time.deltaTime;
        if (patrolTimer > 3f)
        {
            if (flankingWaypoints.Length > 0)
            {
                currentPatrolIndex = Random.Range(0, flankingWaypoints.Length);
                agent.SetDestination(flankingWaypoints[currentPatrolIndex].position);
            }
            patrolTimer = 0f;
        }
        agent.isStopped = false;
        agent.speed = 2.0f;

        // 2. Lógica de Baseline Activo (Estímulo de Daño)
        _patrolShootTimer -= Time.deltaTime;
        if (_patrolShootTimer <= 0f)
        {
            // Evaluación espacial sin GC Allocations
            float sqrDistance = ((Vector2)player.position - (Vector2)transform.position).sqrMagnitude;

            // LÓGICA HÍBRIDA:
            // A) Si el bot está en el radio de aggro efectivo, dispara por legítima defensa.
            // B) Si el bot está lejos, 40% de probabilidad de "fuego de supresión".
            // En modo simulación, effectiveAggroRadius=999f hace que la condición A siempre sea true.
            if (sqrDistance < (effectiveAggroRadius * effectiveAggroRadius) || Random.value < 0.4f)
            {
                ShootAtPlayer();
            }

            _patrolShootTimer = effectiveShootInterval;
        }
    }

    private void ExecuteSiege()
    {
        // Táctica: Fuego de supresión constante contra coberturas estáticas
        // Límite espacial: se detiene a minAttackDistance para evitar colisión física
        agent.isStopped = false;
        agent.speed = 1f; // Caminata lenta y opresiva
        agent.stoppingDistance = minAttackDistance;
        agent.SetDestination(player.position);
        ShootAtPlayer(); // Dispara en todos los frames (WeaponController limita por fireRate)
    }

    private void ExecuteKiting()
    {
        // Táctica: Retroceder manteniendo distancia máxima + Fuego bajo
        agent.stoppingDistance = 0f; // Reset: la evasión calcula su propia distancia
        float sqrDistance = (transform.position - player.position).sqrMagnitude;
        if (sqrDistance < 100.0f) // 10 metros de distancia de confort
        {
            agent.isStopped = false;
            agent.speed = 4.0f;
            Vector3 fleeDirection = (transform.position - player.position).normalized;
            agent.SetDestination(transform.position + fleeDirection * 4f);
        }
        else
        {
            agent.isStopped = true;
        }

        ShootAtPlayer();
    }

    private void ExecuteRush()
    {
        // Táctica: Presión máxima directa, ignorando coberturas
        // Límite espacial: se detiene a minAttackDistance para evitar colisión física
        agent.isStopped = false;
        agent.speed = 6.0f; // Máxima velocidad
        agent.stoppingDistance = minAttackDistance;
        agent.SetDestination(player.position);
        ShootAtPlayer();
    }

    private void ExecuteFlank()
    {
        // Táctica: Reposicionamiento angular para invalidar cobertura
        if (flankingWaypoints.Length == 0) return;

        agent.stoppingDistance = 0f; // Reset: navega a waypoints exactos
        agent.isStopped = false;
        agent.speed = 4.5f;

        Transform bestWaypoint = flankingWaypoints[0];
        float maxSqrDistance = 0f;

        for (int i = 0; i < flankingWaypoints.Length; i++)
        {
            float sqrDist = (player.position - flankingWaypoints[i].position).sqrMagnitude;
            if (sqrDist > maxSqrDistance)
            {
                maxSqrDistance = sqrDist;
                bestWaypoint = flankingWaypoints[i];
            }
        }

        agent.SetDestination(bestWaypoint.position);
        ShootAtPlayer();
    }
}