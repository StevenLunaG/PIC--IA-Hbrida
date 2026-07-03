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

    private NavMeshAgent agent;
    private NPCState currentState = NPCState.PatrullajeErratico;

    // Variables para patrullaje estocástico sin allocations
    private float patrolTimer = 0f;
    private int currentPatrolIndex = 0;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.updateRotation = false;
        agent.updateUpAxis = false;
    }

    private void Start()
    {
        if (onnxBridge != null) onnxBridge.OnProfileInferred += TransitionToCounterState;
    }

    private void OnDestroy()
    {
        if (onnxBridge != null) onnxBridge.OnProfileInferred -= TransitionToCounterState;
    }

    private void TransitionToCounterState(int inferredProfile)
    {
        currentState = (NPCState)inferredProfile;
        Debug.Log($"[NPC FSM] Ejecutando Matriz: {currentState}");
    }

    private void Update()
    {
        if (!agent.isOnNavMesh || (playerHealth != null && playerHealth.IsDead)) return;

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
        // Movimiento estocástico por puntos de interés (Waypoints)
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
        // En patrullaje no dispara, es un estado neutro
    }

    private void ExecuteSiege()
    {
        // Táctica: Fuego de supresión constante contra coberturas estáticas
        agent.isStopped = false;
        agent.speed = 1.5f; // Caminata lenta y opresiva
        agent.SetDestination(player.position);
        ShootAtPlayer(); // Dispara en todos los frames (WeaponController limita por fireRate)
    }

    private void ExecuteKiting()
    {
        // Táctica: Retroceder manteniendo distancia máxima + Fuego bajo
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
        agent.isStopped = false;
        agent.speed = 6.0f; // Máxima velocidad
        agent.SetDestination(player.position);
        ShootAtPlayer();
    }

    private void ExecuteFlank()
    {
        // Táctica: Reposicionamiento angular para invalidar cobertura
        if (flankingWaypoints.Length == 0) return;

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