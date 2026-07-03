using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Implementación de IPlayerInput para el jugador humano.
/// Usa polling directo de UnityEngine.InputSystem (compatible con Unity 6).
/// Zero-allocation: solo tipos de valor.
/// </summary>
public class HumanInput : MonoBehaviour, IPlayerInput
{
    private Camera _mainCam;
    private Vector2 _movementInput;
    private Vector2 _aimDirection;
    private bool _isShooting;

    private void Awake()
    {
        _mainCam = Camera.main;
    }

    private void Update()
    {
        // ── Movimiento: polling directo WASD + flechas ──
        _movementInput = Vector2.zero;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) _movementInput.y += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) _movementInput.y -= 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) _movementInput.x -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) _movementInput.x += 1f;
        }

        if (_movementInput.sqrMagnitude > 1f) _movementInput.Normalize();

        // ── Apuntado: posición del ratón en mundo ──
        if (Mouse.current != null && _mainCam != null)
        {
            Vector3 mouseWorld = _mainCam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            _aimDirection = ((Vector2)mouseWorld - (Vector2)transform.position).normalized;

            _isShooting = Mouse.current.leftButton.wasPressedThisFrame;
        }
        else
        {
            _aimDirection = Vector2.right;
            _isShooting = false;
        }
    }

    // ── IPlayerInput ──

    public Vector2 GetMovement() => _movementInput;
    public bool IsShooting() => _isShooting;
    public Vector2 GetAimDirection() => _aimDirection;

    /// <summary>No-op para humano: los parámetros de bot no aplican.</summary>
    public void OnWindowStart(BotTargetParams botParams) { }

    /// <summary>No-op para humano: la reacción post-daño es decisión del jugador.</summary>
    public void OnDamageTaken() { }
}
