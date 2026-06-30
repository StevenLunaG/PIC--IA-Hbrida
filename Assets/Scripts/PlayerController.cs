using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Parámetros Físicos")]
    [Tooltip("Velocidad de traslación en unidades de Unity por segundo")]
    public float moveSpeed = 5.0f;

    // Referencia al componente físico (Cacheado en Awake para evitar GetComponents en Update)
    private Rigidbody2D rb;
    // Struct primitivo para almacenar el vector de dirección (0 Allocations)
    private Vector2 movementInput;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private void Update()
    {
        // 1. Polling directo del Input System (Compatible con Unity 6)
        movementInput = Vector2.zero;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) movementInput.y += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) movementInput.y -= 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) movementInput.x -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) movementInput.x += 1f;
        }

        // 2. Normalización vectorial
        // Evita que el movimiento diagonal alcance una magnitud de 1.41 (Raíz de 2)
        if (movementInput.sqrMagnitude > 1f)
        {
            movementInput.Normalize();
        }
    }

    private void FixedUpdate()
    {
        // 3. Resolución Cinemática
        // Se ejecuta sincronizado con el bucle físico, independiente del framerate.
        // Previene el "Stuttering" o saltos visuales en el renderizado.
        rb.MovePosition(rb.position + movementInput * (moveSpeed * Time.fixedDeltaTime));
    }
}