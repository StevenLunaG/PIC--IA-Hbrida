using UnityEngine;
using UnityEngine.Events;

public class HealthController : MonoBehaviour
{
    [Header("Parámetros Vitales")]
    public float maxHealth = 100f;
    private float currentHealth;

    [Header("Dependencias")]
    [Tooltip("Asignar SOLO en el Jugador. El NPC puede dejarlo vacío.")]
    public TelemetryManager telemetryManager;

    [Header("Modo Simulación (opcional)")]
    [Tooltip("Asignar solo en el jugador/bot. Permite notificar daño al bot. Null en modo humano.")]
    public SimulationManager simulationManager;

    // Eventos para la UI y Efectos Visuales (Desacoplados)
    public UnityEvent OnDeath;
    public UnityEvent<float> OnHealthChanged;

    // Posición de spawn cacheada en Start() — usada para respawn sin allocation
    private Vector3 _spawnPosition;
    private bool _isDead;

    private void Start()
    {
        currentHealth = maxHealth;
        _spawnPosition = transform.position;
        _isDead = false;
    }

    public bool IsDead => _isDead;

    public void TakeDamage(float amount)
    {
        if (_isDead) return;

        currentHealth -= amount;
        OnHealthChanged?.Invoke(currentHealth / maxHealth);

        // Si este script está en el jugador, registrar daño en telemetría
        if (telemetryManager != null)
        {
            telemetryManager.RegisterDamageTaken();
        }

        // Notificar al bot simulado para activar lógica post-daño (buscar cobertura)
        // Null en modo humano → no-op
        if (simulationManager != null)
        {
            simulationManager.NotifyBotDamageTaken();
        }

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        _isDead = true;
        Debug.Log($"[{gameObject.name}] ha sido eliminado.");
        OnDeath?.Invoke();

        // En lugar de SetActive(false), marcamos como muerto.
        // El orquestador se encargará del respawn al inicio de la siguiente ventana.
        // Nota: si NO hay orquestador (modo humano real), se desactiva como fallback.
        if (FindAnyObjectByType<SimulationManager>() == null)
        {
            gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Restaura la salud al máximo y resetea la posición al spawn original.
    /// Llamado por SimulationManager al inicio de cada ventana de telemetría.
    /// Zero-allocation: no crea ni destruye GameObjects.
    /// </summary>
    public void FullHeal()
    {
        currentHealth = maxHealth;
        _isDead = false;
        gameObject.SetActive(true);
        transform.position = _spawnPosition;
        OnHealthChanged?.Invoke(1f);
    }

    /// <summary>
    /// Permite al SimulationManager establecer un spawn personalizado.
    /// </summary>
    public void SetSpawnPosition(Vector3 position)
    {
        _spawnPosition = position;
    }
}