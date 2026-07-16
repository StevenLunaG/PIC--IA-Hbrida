using UnityEngine;

public class WeaponController : MonoBehaviour
{
    [Header("Estadísticas del Arma")]
    public float damage = 20f;
    public float fireRate = 0.2f; // Tiempo entre disparos (Cadencia)
    public float weaponRange = 50f;

    [Header("Configuración Físico/Táctica")]
    [Tooltip("Capas que detendrán el disparo (Player, NPC, Obstacle)")]
    public LayerMask targetMask;
    public Transform firePoint; // Punto desde donde sale el disparo (ej. el cañón)

    [Header("VFX")]
    public LineRenderer bulletTrail;
    public float trailDuration = 0.05f;

    [Header("Dependencias (Opcional)")]
    public TelemetryManager telemetryManager;

    private float nextFireTime = 0f;
    private float _trailTimer = 0f;

    public void TryShoot(Vector2 direction)
    {
        // Validación de cadencia de fuego
        if (Time.time < nextFireTime) return;
        nextFireTime = Time.time + fireRate;

        // Registrar disparo en la telemetría (APM en la nueva fórmula)
        telemetryManager?.RegisterShot();

        // Ejecución del Raycast (Zero GC Allocation: retorna un struct)
        RaycastHit2D hit = Physics2D.Raycast(firePoint.position, direction, weaponRange, targetMask);

        if (hit.collider != null)
        {
            bulletTrail.enabled = true;
            bulletTrail.SetPosition(0, firePoint.position);
            bulletTrail.SetPosition(1, hit.point);

            // Intentar extraer el sistema de salud del objetivo impactado
            HealthController targetHealth = hit.collider.GetComponent<HealthController>();

            if (targetHealth != null)
            {
                targetHealth.TakeDamage(damage);
                // Si el disparo impacta a una entidad viva (no a una pared), registrar acierto
                telemetryManager?.RegisterHit();
            }
        }
        else
        {
            // Disparo fallido (al aire)
            bulletTrail.enabled = true;
            bulletTrail.SetPosition(0, firePoint.position);
            bulletTrail.SetPosition(1, firePoint.position + (Vector3)(direction * weaponRange));
        }

        _trailTimer = trailDuration;
    }

    private void Update()
    {
        if (bulletTrail == null || !bulletTrail.enabled) return;

        _trailTimer -= Time.deltaTime;
        if (_trailTimer <= 0f)
        {
            bulletTrail.enabled = false;
        }
    }
}
