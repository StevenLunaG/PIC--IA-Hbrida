using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class SimulatedPlayerController : MonoBehaviour
{
    [Header("Referencias")]
    public WeaponController weaponController;
    public Transform npcTransform;

    [Header("Parámetros de Movimiento")]
    public float baseSpeed = 5f;
    public float waypointArrivalRadius = 0.8f;
    public float kiteDistance = 12f;

    private NavMeshAgent _agent;
    private BotTargetParams _params;
    private int _currentClase = -1;
    private bool _isActive = false;

    private float _shootTimer = 0f;
    private float _repositionTimer = 0f;
    private float _repositionInterval = 3f;


    private Transform[] _coverPoints;
    private Transform _currentCoverTarget;

    private float _postDamageReactionTimer = 0f;
    private const float POST_DAMAGE_WINDOW = 3f;
    // Decisión post-daño evaluada UNA sola vez al recibir el golpe.
    // Evita que UpdateTactico cambie de decisión frame a frame.
    private bool _decidedToSeekCoverOnDamage = false;


    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _agent.updateRotation = false;
        _agent.updateUpAxis = false;

        if (npcTransform == null)
        {
            GameObject npcGO = GameObject.FindWithTag("NPC");
            if (npcGO != null) npcTransform = npcGO.transform;
            else Debug.LogError("[SimBot] No se encontró objeto con Tag 'NPC'.");
        }

        GameObject[] coverGOs = GameObject.FindGameObjectsWithTag("Cover");
        _coverPoints = new Transform[coverGOs.Length];
        for (int i = 0; i < coverGOs.Length; i++) _coverPoints[i] = coverGOs[i].transform;

        _agent.enabled = false;
        _isActive = false;
    }

    public void InitForWindow(BotTargetParams botParams)
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

    public void StopBot()
    {
        _isActive = false;
        _agent.enabled = false;
    }

    public void NotifyDamageTaken()
    {
        _postDamageReactionTimer = POST_DAMAGE_WINDOW;
        // La decisión se toma UNA vez aquí, no frame a frame en UpdateTactico.
        _decidedToSeekCoverOnDamage = Random.value < _params.TargetPostDano;
    }

    private void Update()
    {
        if (!_isActive || npcTransform == null) return;

        float dt = Time.deltaTime;

        if (_postDamageReactionTimer > 0f) _postDamageReactionTimer -= dt;

        switch (_currentClase)
        {
            case 0: UpdateConservador(dt); break;
            case 1: UpdateAgresivo(dt); break;
            case 2: UpdateCaotico(dt); break;
            case 3: UpdateTactico(dt); break;
        }

        UpdateShooting(dt);
    }

    private void UpdateConservador(float dt)
    {
        Transform bestCover = GetNearestCoverAtDistance(_params.TargetDistancia, 5f);
        if (bestCover != null) MoveTo(bestCover.position);
        else MaintainDistanceToNPC(_params.TargetDistancia);
    }

    private void UpdateAgresivo(float dt)
    {
        MoveTo(npcTransform.position);
    }

    private void UpdateCaotico(float dt)
    {
        _repositionTimer -= dt;

        if (_repositionTimer <= 0f || HasArrivedAtDestination())
        {
            if (Random.value < 0.6f)
            {
                MoveTo(npcTransform.position);
            }
            else
            {
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

        // Usar la decisión fijada en NotifyDamageTaken(), no re-evaluar cada frame.
        if (enVentanaPostDano && _decidedToSeekCoverOnDamage)
        {
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
            // OPTIMIZACIÓN: sqrMagnitude en lugar de Distance
            float sqrDistanciaActual = ((Vector2)transform.position - (Vector2)npcTransform.position).sqrMagnitude;
            float targetThreshold = _params.TargetDistancia + 3f;

            if (sqrDistanciaActual > (targetThreshold * targetThreshold))
            {
                MoveTo(npcTransform.position);
            }
            else
            {
                Transform cover = GetNearestCover();
                if (cover != null) MoveTo(cover.position);
            }

            _repositionTimer = Random.Range(2f, _repositionInterval);
        }
    }

    private void UpdateShooting(float dt)
    {
        if (weaponController == null || npcTransform == null) return;

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
        Vector2 inaccurateDirection = new Vector2(
            directionToTarget.x * cos - directionToTarget.y * sin,
            directionToTarget.x * sin + directionToTarget.y * cos
        );

        weaponController.TryShoot(inaccurateDirection);
    }

    private void MoveTo(Vector3 target)
    {
        target.z = 0f;
        if (_agent.isActiveAndEnabled && _agent.isOnNavMesh)
            _agent.SetDestination(target);
    }

    private void MaintainDistanceToNPC(float targetDist)
    {
        // OPTIMIZACIÓN: sqrMagnitude 
        Vector2 toNPC = (Vector2)npcTransform.position - (Vector2)transform.position;
        float sqrDist = toNPC.sqrMagnitude;


        float minTarget = targetDist - 2f;
        float maxTarget = targetDist + 2f;

        if (sqrDist < (minTarget * minTarget))
        {
            Vector3 awayDir = new Vector3(-toNPC.x, -toNPC.y, 0f).normalized;
            MoveTo(transform.position + awayDir * 3f);
        }
        else if (sqrDist > (maxTarget * maxTarget))
        {
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

        foreach (Transform cover in _coverPoints)
        {
            if (cover == null) continue;
            // OPTIMIZACIÓN: sqrMagnitude
            float sqrD = (pos2D - (Vector2)cover.position).sqrMagnitude;
            if (sqrD < minSqrDist)
            {

                minSqrDist = sqrD;

                nearest = cover;

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

        foreach (Transform cover in _coverPoints)
        {
            if (cover == null) continue;
            // Aquí se mantiene Vector2.Distance obligatoriamente para evaluar la tolerancia lineal de error.
            float distToNPC = Vector2.Distance((Vector2)cover.position, npcPos2D);
            float error = Mathf.Abs(distToNPC - targetDistFromNPC);
            if (error < tolerance && error < bestScore)
            {
                bestScore = error;
                best = cover;
            }
        }
        return best;
    }
}