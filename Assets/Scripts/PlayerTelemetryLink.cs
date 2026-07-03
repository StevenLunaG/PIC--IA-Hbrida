using UnityEngine;

/// <summary>
/// Enlace entre el jugador y el sistema de telemetría.
/// Ahora solo gestiona detección de cobertura (triggers).
/// El disparo se delega completamente a PlayerController via IPlayerInput.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class PlayerTelemetryLink : MonoBehaviour
{
    public TelemetryManager telemetryManager;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Cover")) telemetryManager.SetCoverState(true);
    }
    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Cover")) telemetryManager.SetCoverState(false);
    }
}