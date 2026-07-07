using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Implementación de IPlayerInput para el bot simulado.
/// Consume BotTargetParams inyectados por SimulationManager y genera vectores
/// de movimiento via NavMeshAgent (Z=0) y disparo con cadencia/precisión estocástica.
///
/// Zero-allocation: sin Instantiate, sin new en bucles, solo structs y primitivos.
/// Nota: Este componente REEMPLAZA a SimulatedPlayerController.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class BotInput : MonoBehaviour, IPlayerInput
{
    [Header("Referencias")]
    public Transform npcTransform;

    [Header("Parámetros de Movimiento")]
    public float baseSpeed = 5f;
    public float waypointArrivalRadius = 0.8f;

    [Header("Límite Espacial")]
    [Tooltip("Distancia mínima al NPC para estados de persecución directa.")]
    [SerializeField] private float minEngagementDistance = 2.0f;

    // ── Estado interno (tipos de valor) ──
    private NavMeshAgent _agent;
    private BotTargetParams _params;
    private int _currentClase = -1;
    private bool _isActive;

    // Temporizadores
    private float _shootTimer;
    private float _repositionTimer;
    private float _repositionInterval = 3f;

    // Post-daño
    private float _postDamageReactionTimer;
    private const float POST_DAMAGE_WINDOW = 3f;
    private bool _decidedToSeekCoverOnDamage;

    // Cover cache (una sola allocation en Awake)
    private Transform[] _coverPoints;
    private Transform _currentCoverTarget;

    // Salida del frame actual
    private bool _wantsToShoot;
    private Vector2 _aimDirection;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _agent.updateRotation = false;
        _agent.updateUpAxis = false;

        if (npcTransform == null)
        {
            GameObject npcGO = GameObject.FindWithTag("NPC");
            if (npcGO != null) npcTransform = npcGO.transform;
            else Debug.LogError("[BotInput] No se encontró objeto con Tag 'NPC'.");
        }

        // Cache de coberturas — única allocation en vida del objeto
        GameObject[] coverGOs = GameObject.FindGameObjectsWithTag("Cover");
        _coverPoints = new Transform[coverGOs.Length];
        for (int i = 0; i < coverGOs.Length; i++) _coverPoints[i] = coverGOs[i].transform;

        _agent.enabled = false;
        _isActive = false;
    }

    private void Update()
    {
        _wantsToShoot = false;
        _aimDirection = Vector2.right;

        if (!_isActive || npcTransform == null) return;

        float dt = Time.deltaTime;
        if (_postDamageReactionTimer > 0f) _postDamageReactionTimer -= dt;

        // ── Lógica de movimiento por clase ──
        switch (_currentClase)
        {
            case 0: UpdateConservador(); break;
            case 1: UpdateAgresivo(); break;
            case 2: UpdateCaotico(dt); break;
            case 3: UpdateTactico(dt); break;
        }

        // ── Lógica de disparo ──
        UpdateShooting(dt);
    }

    // -----------------------------------------------------------------------
    // IPlayerInput
    // -----------------------------------------------------------------------

    public Vector2 GetMovement()
    {
        // NavMeshAgent maneja el movimiento directamente; no se inyecta al Rigidbody.
        // Retornamos zero para señalar que el bot usa agent, no rb.MovePosition.
        return Vector2.zero;
    }

    public bool IsShooting() => _wantsToShoot;
    public Vector2 GetAimDirection() => _aimDirection;

    public void OnWindowStart(BotTargetParams botParams)
    {
        _params = botParams;
        _currentClase = botParams.Clase;
        _isActive = true;

        _shootTimer = _params.ShootInterval;
        _repositionTimer = 0f;
        _repositionInterval = Random.Range(2.5f, 4.5f);

        _postDamageReactionTimer = 0f;
        _decidedToSeekCoverOnDamage = false;
        _currentCoverTarget = null;

        _agent.enabled = true;
        _agent.speed = baseSpeed;
    }

    public void OnDamageTaken()
    {
        _postDamageReactionTimer = POST_DAMAGE_WINDOW;
        _decidedToSeekCoverOnDamage = Random.value < _params.TargetPostDano;
    }

    /// <summary>
    /// Detiene el bot y desactiva el NavMeshAgent.
    /// </summary>
    public void StopBot()
    {
        _isActive = false;
        if (_agent != null && _agent.isActiveAndEnabled)
            _agent.enabled = false;
    }

    // -----------------------------------------------------------------------
    // ESTRATEGIAS DE MOVIMIENTO (idénticas a SimulatedPlayerController)
    // -----------------------------------------------------------------------

    private void UpdateConservador()
    {
        // Reset: navega al centro exacto de la cobertura
        _agent.stoppingDistance = 0f;
        // Tolerancia ampliada a 12u; fallback a cobertura más cercana sin restricción
        Transform bestCover = GetNearestCoverAtDistance(_params.TargetDistancia, 12f)
            ?? GetNearestCover();

        if (bestCover != null) MoveTo(bestCover.position);
        else MaintainDistanceToNPC(_params.TargetDistancia);
    }

    private void UpdateAgresivo()
    {
        // Límite espacial: se detiene a minEngagementDistance para evitar colisión física
        _agent.stoppingDistance = minEngagementDistance;
        MoveTo(npcTransform.position);
    }

    private void UpdateCaotico(float dt)
    {
        _repositionTimer -= dt;

        if (_repositionTimer <= 0f || HasArrivedAtDestination())
        {
            if (Random.value < 0.6f)
            {
                // Persecución directa: límite espacial activo
                _agent.stoppingDistance = minEngagementDistance;
                MoveTo(npcTransform.position);
            }
            else
            {
                // Exploración aleatoria: reset para alcanzar posición exacta
                _agent.stoppingDistance = 0f;
                Vector3 randomDir = Random.insideUnitSphere * 8f;
                randomDir.z = 0f;
                Vector3 randomTarget = transform.position + randomDir;

                if (NavMesh.SamplePosition(randomTarget, out NavMeshHit hit, 8f, NavMesh.AllAreas))
                    MoveTo(hit.position);
            }
            _repositionTimer = Random.Range(1.5f, _repositionInterval);
        }
    }

    private void UpdateTactico(float dt)
    {
        bool enVentanaPostDano = _postDamageReactionTimer > 0f;

        if (enVentanaPostDano && _decidedToSeekCoverOnDamage)
        {
            // Post-daño → cobertura: reset para alcanzar centro exacto
            _agent.stoppingDistance = 0f;
            Transform cover = GetNearestCover();
            if (cover != null)
            {
                _currentCoverTarget = cover;
                MoveTo(cover.position);
                return;
            }
        }

        _repositionTimer -= dt;
        if (_repositionTimer <= 0f || HasArrivedAtDestination())
        {
            // sqrMagnitude: O(1) sin raíz cuadrada (Regla 4)
            float sqrDistanciaActual = ((Vector2)transform.position - (Vector2)npcTransform.position).sqrMagnitude;
            float targetThreshold = _params.TargetDistancia + 3f;

            if (sqrDistanciaActual > (targetThreshold * targetThreshold))
            {
                // Acercamiento al NPC: límite espacial activo
                _agent.stoppingDistance = minEngagementDistance;
                MoveTo(npcTransform.position);
            }
            else
            {
                // Reposicionamiento táctico → cobertura: reset
                _agent.stoppingDistance = 0f;
                Transform cover = GetNearestCover();
                if (cover != null) MoveTo(cover.position);
            }

            _repositionTimer = Random.Range(2f, _repositionInterval);
        }
    }

    // -----------------------------------------------------------------------
    // DISPARO
    // -----------------------------------------------------------------------

    private void UpdateShooting(float dt)
    {
        if (npcTransform == null) return;

        _shootTimer -= dt;
        if (_shootTimer > 0f) return;

        _shootTimer = _params.ShootInterval;

        Vector2 directionToTarget = (npcTransform.position - transform.position).normalized;
        bool acierta = Random.value < _params.TargetPrecision;

        float maxAngle = _currentClase == 2 ? 45f : 20f;
        float spreadAngle = acierta ? 0f : Random.Range(-maxAngle, maxAngle);

        float rad = spreadAngle * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);

        _aimDirection = new Vector2(
            directionToTarget.x * cos - directionToTarget.y * sin,
            directionToTarget.x * sin + directionToTarget.y * cos
        );

        _wantsToShoot = true;
    }

    // -----------------------------------------------------------------------
    // UTILIDADES DE NAVEGACIÓN
    // -----------------------------------------------------------------------

    private void MoveTo(Vector3 target)
    {
        target.z = 0f;
        if (_agent.isActiveAndEnabled && _agent.isOnNavMesh)
            _agent.SetDestination(target);
    }

    private void MaintainDistanceToNPC(float targetDist)
    {
        Vector2 toNPC = (Vector2)npcTransform.position - (Vector2)transform.position;
        float sqrDist = toNPC.sqrMagnitude;

        float minTarget = targetDist - 2f;
        float maxTarget = targetDist + 2f;

        if (sqrDist < (minTarget * minTarget))
        {
            // Alejarse: reset para alcanzar posición de evasión exacta
            _agent.stoppingDistance = 0f;
            Vector3 awayDir = new Vector3(-toNPC.x, -toNPC.y, 0f).normalized;
            MoveTo(transform.position + awayDir * 3f);
        }
        else if (sqrDist > (maxTarget * maxTarget))
        {
            // Acercarse al NPC: límite espacial activo
            _agent.stoppingDistance = minEngagementDistance;
            MoveTo(npcTransform.position);
        }
    }

    private bool HasArrivedAtDestination()
    {
        if (!_agent.isActiveAndEnabled || !_agent.isOnNavMesh) return false;
        return !_agent.pathPending && _agent.remainingDistance < waypointArrivalRadius;
    }

    private Transform GetNearestCover()
    {
        if (_coverPoints == null || _coverPoints.Length == 0) return null;

        Transform nearest = null;
        float minSqrDist = float.MaxValue;
        Vector2 pos2D = (Vector2)transform.position;

        for (int i = 0; i < _coverPoints.Length; i++)
        {
            if (_coverPoints[i] == null) continue;
            float sqrD = (pos2D - (Vector2)_coverPoints[i].position).sqrMagnitude;
            if (sqrD < minSqrDist)
            {
                minSqrDist = sqrD;
                nearest = _coverPoints[i];
            }
        }
        return nearest;
    }

    private Transform GetNearestCoverAtDistance(float targetDistFromNPC, float tolerance)
    {
        if (_coverPoints == null || _coverPoints.Length == 0) return null;

        Transform best = null;
        float bestScore = float.MaxValue;
        Vector2 npcPos2D = (Vector2)npcTransform.position;

        for (int i = 0; i < _coverPoints.Length; i++)
        {
            if (_coverPoints[i] == null) continue;
            float distToNPC = Vector2.Distance((Vector2)_coverPoints[i].position, npcPos2D);
            float error = Mathf.Abs(distToNPC - targetDistFromNPC);
            if (error < tolerance && error < bestScore)
            {
                bestScore = error;
                best = _coverPoints[i];
            }
        }
        return best;
    }
}
