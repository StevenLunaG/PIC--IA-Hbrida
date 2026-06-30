using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Collider2D))]
public class PlayerTelemetryLink : MonoBehaviour
{
    [Header("Inyección de Dependencias")]
    [Tooltip("Arrastra el objeto __SYSTEMS__ aquí")]
    public TelemetryManager telemetryManager;

    private void Update()
    {
        // Mockup de disparo (Click izquierdo)
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            telemetryManager.RegisterShot();
            // Lógica temporal para simular un acierto con 50% de probabilidad
            if (Random.value > 0.5f) telemetryManager.RegisterHit();
        }

        // Mockup de daño recibido (Barra espaciadora para pruebas manuales)
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            telemetryManager.RegisterDamageTaken();
            Debug.Log("Daño simulado recibido.");
        }
    }


    // Detección de Zonas de Cobertura (Opción A del SDD)
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Cover"))
        {
            telemetryManager.SetCoverState(true);
            Debug.Log("Jugador ENTRO en cobertura.");
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Cover"))
        {
            telemetryManager.SetCoverState(false);
            Debug.Log("Jugador SALIO de cobertura.");
        }
    }
}
