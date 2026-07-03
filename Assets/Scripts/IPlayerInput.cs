using UnityEngine;

/// <summary>
/// Contrato Strategy para abstracción de input.
/// Permite intercambiar entre humano y bot sin SetActive(false).
/// Todos los métodos retornan tipos de valor (zero-allocation).
/// </summary>
public interface IPlayerInput
{
    /// <summary>
    /// Vector de movimiento normalizado en el plano XY.
    /// </summary>
    Vector2 GetMovement();

    /// <summary>
    /// True si el agente desea disparar en este frame.
    /// </summary>
    bool IsShooting();

    /// <summary>
    /// Dirección de apuntado normalizada (del agente hacia el objetivo).
    /// </summary>
    Vector2 GetAimDirection();

    /// <summary>
    /// Llamado una vez al inicio de cada ventana de telemetría para inyectar parámetros.
    /// HumanInput puede ignorar la llamada; BotInput la usa para configurar targets.
    /// </summary>
    void OnWindowStart(BotTargetParams botParams);

    /// <summary>
    /// Notifica al input que el agente recibió daño. BotInput usa esto para
    /// lógica post-daño (buscar cobertura). HumanInput lo ignora.
    /// </summary>
    void OnDamageTaken();
}
