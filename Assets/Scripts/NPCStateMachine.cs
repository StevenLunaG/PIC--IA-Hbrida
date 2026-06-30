using UnityEngine;
using UnityEngine.AI;

public enum NPCState
{
    PatrullajeErratico = -1,
    Asedio = 0,
    EvasionTactica = 1,
    Embestida = 2,
    Flanqueo = 3
}

[RequireComponent(typeof(NavMeshAgent))]
public class NPCStateMachine : MonoBehaviour
{
    [Header("Inyección de Dependencias")]
    public ONNXInferenceBridge onnxBridge;
    public Transform player;
    public Transform[] flankingWaypoints;

    private NavMeshAgent agent;
    private NPCState currentState = NPCState.PatrullajeErratico;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();

        // Desacoplamiento de ejes 3D para movimiento en 2D puro
        agent.updateRotation = false;
        agent.updateUpAxis = false;
    }

    private void Start()
    {
        if (onnxBridge != null)
        {
            onnxBridge.OnProfileInferred += TransitionToCounterState;
        }
    }

    private void OnDestroy()
    {
        if (onnxBridge != null)
        {
            onnxBridge.OnProfileInferred -= TransitionToCounterState;
        }
    }

    private void TransitionToCounterState(int inferredProfile)
    {
        currentState = (NPCState)inferredProfile;
        Debug.Log($"[NPC FSM] Transición de Estado a: {currentState}");
    }

    private void Update()
    {
        // Válvula de seguridad: Abortar FSM si el agente se cae del NavMesh
        // Evita excepciones de asignación y protege el Main Thread
        if (!agent.isOnNavMesh)
        {
            Debug.LogWarning("[NPC FSM] Agente fuera del NavMesh. Abortando cálculo de ruta.");
            return;
        }

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

    private void ExecutePatrol()
    {
        agent.isStopped = true;
    }

    private void ExecuteSiege()
    {
        agent.isStopped = false;
        agent.speed = 2.5f;
        agent.SetDestination(player.position);
    }

    private void ExecuteKiting()
    {
        // Optimización: Uso de sqrMagnitude es computacionalmente más barato que Distance()
        float sqrDistance = (transform.position - player.position).sqrMagnitude;
        if (sqrDistance < 64.0f) // Equivalente a distance < 8f (8^2)
        {
            agent.isStopped = false;
            Vector3 fleeDirection = (transform.position - player.position).normalized;
            agent.SetDestination(transform.position + fleeDirection * 4f);
        }
        else
        {
            agent.isStopped = true;
        }
    }

    private void ExecuteRush()
    {
        agent.isStopped = false;
        agent.speed = 6.0f;
        agent.SetDestination(player.position);
    }

    private void ExecuteFlank()
    {
        // 1. Evitar ejecución si el array no existe o está vacío
        if (flankingWaypoints == null || flankingWaypoints.Length == 0) return;

        agent.isStopped = false;
        agent.speed = 4.0f;

        Transform bestWaypoint = null;
        float maxSqrDistance = -1f;

        for (int i = 0; i < flankingWaypoints.Length; i++)
        {
            // 2. Saltar iteración si el slot del array está vacío (Null Reference Bypass)
            if (flankingWaypoints[i] == null) continue;

            float sqrDist = (player.position - flankingWaypoints[i].position).sqrMagnitude;
            if (sqrDist > maxSqrDistance)
            {
                maxSqrDistance = sqrDist;
                bestWaypoint = flankingWaypoints[i];
            }
        }

        // 3. Solo enviar la orden al NavMesh si encontramos un waypoint válido
        if (bestWaypoint != null)
        {
            agent.SetDestination(bestWaypoint.position);
        }
        else
        {
            // Fallback táctico: si no hay waypoints válidos, emular Asedio
            agent.SetDestination(player.position);
        }
    }
}