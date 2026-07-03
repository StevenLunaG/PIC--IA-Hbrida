using UnityEngine;

/// <summary>
/// Controlador cinemático del jugador. Ahora consume IPlayerInput (Strategy Pattern)
/// para desacoplar la fuente de input de la lógica de movimiento y disparo.
///
/// En modo humano: se asigna HumanInput como inputProvider.
/// En modo simulación: se asigna BotInput como inputProvider.
///
/// El cambio se realiza sin SetActive/Destroy — solo se reasigna la referencia.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Parámetros Físicos")]
    [Tooltip("Velocidad de traslación en unidades de Unity por segundo")]
    public float moveSpeed = 5.0f;

    [Header("Dependencias")]
    [Tooltip("Componente que implementa IPlayerInput (HumanInput o BotInput)")]
    public MonoBehaviour inputProviderComponent;

    [Tooltip("Arma del jugador (para disparar via IPlayerInput)")]
    public WeaponController weaponController;

    // Referencia cacheada a la interfaz
    private IPlayerInput _inputProvider;
    private Rigidbody2D _rb;

    // Struct primitivo para almacenar el vector de dirección (0 Allocations)
    private Vector2 _movementInput;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        CacheInputProvider();
    }

    /// <summary>
    /// Hot-swap de input provider en runtime. Zero-allocation.
    /// </summary>
    public void SetInputProvider(MonoBehaviour provider)
    {
        inputProviderComponent = provider;
        CacheInputProvider();
    }

    private void CacheInputProvider()
    {
        // Si hay asignación manual en Inspector, usarla
        if (inputProviderComponent != null)
        {
            _inputProvider = inputProviderComponent as IPlayerInput;
            if (_inputProvider == null)
                Debug.LogError($"[PlayerController] {inputProviderComponent.name} no implementa IPlayerInput.");
            return;
        }

        // Auto-descubrimiento: buscar cualquier IPlayerInput habilitado en este GameObject.
        // Prioridad: HumanInput > BotInput (para modo humano por defecto).
        MonoBehaviour[] components = GetComponents<MonoBehaviour>();
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] != null && components[i].enabled && components[i] is IPlayerInput found)
            {
                _inputProvider = found;
                inputProviderComponent = components[i];
                Debug.Log($"[PlayerController] Auto-descubierto: {components[i].GetType().Name}");
                return;
            }
        }

        Debug.LogWarning("[PlayerController] No se encontró IPlayerInput. Asigna HumanInput o BotInput.");
    }

    private void Update()
    {
        if (_inputProvider == null) return;

        _movementInput = _inputProvider.GetMovement();

        // Normalización vectorial — evita magnitud diagonal > 1.0
        if (_movementInput.sqrMagnitude > 1f)
            _movementInput.Normalize();

        // Disparo via IPlayerInput
        if (_inputProvider.IsShooting() && weaponController != null)
        {
            weaponController.TryShoot(_inputProvider.GetAimDirection());
        }
    }

    private void FixedUpdate()
    {
        // Resolución cinemática sincronizada con el bucle físico.
        // Para BotInput (NavMeshAgent), GetMovement() retorna zero — el agente maneja su posición.
        _rb.MovePosition(_rb.position + _movementInput * (moveSpeed * Time.fixedDeltaTime));
    }
}