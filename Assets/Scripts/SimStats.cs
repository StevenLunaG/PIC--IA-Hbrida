using UnityEngine;

/// <summary>
/// Utilidades estadísticas para la generación de comportamiento del bot simulado.
/// Implementa Box-Muller para distribución normal y correlaciones vía
/// Combinaciones Lineales de Variables Normales Estándar (O(1), sin Cholesky).
///
/// Zero-allocation: todos los métodos son estáticos y trabajan con tipos de valor.
///
/// Modelo de correlación:
/// Para dos variables con correlación ρ, se genera una variable latente compartida
/// Z_shared y una independiente Z_ind. La muestra correlacionada se construye:
///   X = μ + σ × (ρ × Z_shared + √(1 - ρ²) × Z_ind)
/// Esto garantiza Var(X) = σ² y Corr(X,Y) ≈ ρ cuando ambas comparten Z_shared.
/// </summary>
public static class SimStats
{
    // -----------------------------------------------------------------------
    // TRANSFORMADA DE BOX-MULLER
    // Genera una muestra de N(0,1) a partir de dos uniformes U(0,1).
    // Se usa la forma estándar; evitamos ln(0) con 1-U.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Retorna una muestra de N(0,1) — distribución normal estándar.
    /// </summary>
    public static float SampleStdNormal()
    {
        // Garantizar U1, U2 en (0, 1] para evitar ln(0)
        float u1 = 1f - Random.value;
        float u2 = 1f - Random.value;
        return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
    }

    /// <summary>
    /// Retorna una muestra de distribución normal N(mu, sigma).
    /// </summary>
    public static float SampleNormal(float mu, float sigma)
    {
        return mu + sigma * SampleStdNormal();
    }

    /// <summary>
    /// Retorna una muestra clampada al rango [min, max].
    /// </summary>
    public static float SampleNormalClamped(float mu, float sigma, float min, float max)
    {
        return Mathf.Clamp(SampleNormal(mu, sigma), min, max);
    }

    // -----------------------------------------------------------------------
    // CORRELACIÓN VÍA VARIABLE LATENTE COMPARTIDA
    //
    // Dado Z_shared ~ N(0,1) y Z_ind ~ N(0,1) independientes:
    //   X = μ + σ × (ρ × Z_shared + √(1 - ρ²) × Z_ind)
    //
    // Propiedad: E[X] = μ, Var(X) = σ², y si Y usa el mismo Z_shared
    // con su propio ρ, Corr(X,Y) ≈ ρ_x × ρ_y (aproximación O(1)).
    //
    // Para correlaciones moderadas (|ρ| ≤ 0.5) el error es despreciable.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Genera una muestra correlacionada dado un Z compartido y una correlación ρ.
    /// </summary>
    private static float SampleCorrelated(float mu, float sigma, float rho,
                                           float zShared, float min, float max)
    {
        float zInd = SampleStdNormal();
        float rhoAbs = Mathf.Abs(rho);
        float rhoSign = rho >= 0f ? 1f : -1f;
        float orthWeight = Mathf.Sqrt(1f - rhoAbs * rhoAbs);
        float z = rhoSign * rhoAbs * zShared + orthWeight * zInd;
        return Mathf.Clamp(mu + sigma * z, min, max);
    }

    // -----------------------------------------------------------------------
    // PARÁMETROS POR PERFIL
    // Corresponden a los vectores μ y σ definidos en el generador multivariado
    // de Python (generador_dataset.ipynb, distribución normal multivariada).
    // -----------------------------------------------------------------------

    /// <summary>
    /// Genera los parámetros operativos del bot para una clase dada.
    /// Respeta las correlaciones definidas en la matriz Σ del dataset.
    /// </summary>
    /// <param name="clase">0=Conservador, 1=Agresivo, 2=Caótico, 3=Táctico</param>
    /// <returns>Struct con todos los targets del bot para esta ventana</returns>
    public static BotTargetParams SampleParamsForClass(int clase)
    {
        switch (clase)
        {
            case 0: return SampleConservador();
            case 1: return SampleAgresivo();
            case 2: return SampleCaotico();
            case 3: return SampleTactico();
            default:
                Debug.LogWarning($"[SimStats] Clase desconocida: {clase}. Usando Conservador por defecto.");
                return SampleConservador();
        }
    }

    // -----------------------------------------------------------------------
    // CONSERVADOR
    // μ = [37, 0.75, 0.50]  σ = [√12, 0.10, 0.12]
    // Correlación APM↔Precisión: +0.2
    // Correlación APM↔PostDaño:  +0.1
    // -----------------------------------------------------------------------
    private static BotTargetParams SampleConservador()
    {
        float zShared = SampleStdNormal();

        float sigmaAPM = Mathf.Sqrt(12f);
        float apm = SampleCorrelated(37f, sigmaAPM, 0.2f, zShared, 5f, 70f);
        float precision = SampleCorrelated(0.75f, 0.10f, 0.2f, zShared, 0.5f, 1.0f);
        float postDano = SampleCorrelated(0.50f, 0.12f, 0.1f, zShared, 0.2f, 0.8f);

        // Variables independientes (sin correlación significativa con APM)
        float distancia = SampleNormalClamped(31f, 8f, 12f, 50f);
        float cobertura = SampleNormalClamped(0.70f, 0.12f, 0.4f, 1.0f);

        return new BotTargetParams(clase: 0, apm, precision, cobertura, postDano, distancia);
    }

    // -----------------------------------------------------------------------
    // AGRESIVO
    // μ = [110, 0.52, 0.30]  σ = [22, 0.13, 0.13]
    // Correlación APM↔Precisión: +0.3  APM↔PostDaño: -0.2
    // -----------------------------------------------------------------------
    private static BotTargetParams SampleAgresivo()
    {
        float zShared = SampleStdNormal();

        float apm = SampleCorrelated(110f, 22f, 0.3f, zShared, 40f, 170f);
        float precision = SampleCorrelated(0.52f, 0.13f, 0.3f, zShared, 0.2f, 0.85f);
        float postDano = SampleCorrelated(0.30f, 0.13f, -0.2f, zShared, 0.05f, 0.7f);

        float distancia = SampleNormalClamped(15f, 6f, 0.5f, 30f);
        float cobertura = SampleNormalClamped(0.35f, 0.12f, 0.05f, 0.65f);

        return new BotTargetParams(clase: 1, apm, precision, cobertura, postDano, distancia);
    }

    // -----------------------------------------------------------------------
    // CAÓTICO
    // μ = [130, 0.22, 0.25]  σ = [35, 0.12, 0.12]
    // Correlaciones ≈ 0: máxima varianza, sin patrón entre variables.
    // Es el único perfil donde NO se aplica correlación explícita.
    // -----------------------------------------------------------------------
    private static BotTargetParams SampleCaotico()
    {
        float apm = SampleNormalClamped(130f, 35f, 80f, 200f);
        float precision = SampleNormalClamped(0.22f, 0.12f, 0.05f, 0.35f);
        float postDano = SampleNormalClamped(0.25f, 0.12f, 0.05f, 0.40f);
        float distancia = SampleNormalClamped(22f, 14f, 0.5f, 45f);
        float cobertura = SampleNormalClamped(0.50f, 0.28f, 0.05f, 1.0f);

        return new BotTargetParams(clase: 2, apm, precision, cobertura, postDano, distancia);
    }

    // -----------------------------------------------------------------------
    // TÁCTICO
    // μ = [65, 0.82, 0.87]  σ = [15, 0.08, 0.07]
    // Correlación APM↔Precisión: +0.4
    // Correlación APM↔PostDaño:  -0.4
    // Correlación Precisión↔PostDaño: +0.5 (la más fuerte del sistema)
    //
    // Se usan DOS variables latentes:
    //   Z1 → acopla APM y Precisión
    //   Z2 → acopla Precisión y PostDaño
    // -----------------------------------------------------------------------
    private static BotTargetParams SampleTactico()
    {
        float zLatent1 = SampleStdNormal(); // Latente APM↔Precisión
        float zLatent2 = SampleStdNormal(); // Latente Precisión↔PostDaño

        // APM: solo correlacionada via zLatent1
        float apm = SampleCorrelated(65f, 15f, 0.4f, zLatent1, 30f, 100f);

        // Precisión: correlacionada con APM via zLatent1 Y con PostDaño via zLatent2
        // Composición: ρ1 = 0.4 (de APM), ρ2 = 0.5 (de PostDaño)
        // Peso total: w1 × zLatent1 + w2 × zLatent2 + w3 × zInd
        // Con w1=0.4, w2=0.5, w3=√(1 - 0.4² - 0.5²) ≈ 0.7681
        float precision;
        {
            float w1 = 0.4f;
            float w2 = 0.5f;
            float w3Sq = 1f - w1 * w1 - w2 * w2;
            float w3 = w3Sq > 0f ? Mathf.Sqrt(w3Sq) : 0f;
            float zInd = SampleStdNormal();
            float z = w1 * zLatent1 + w2 * zLatent2 + w3 * zInd;
            precision = Mathf.Clamp(0.82f + 0.08f * z, 0.65f, 1.0f);
        }

        // PostDaño: correlacionada con Precisión via zLatent2, anti-correlacionada con APM via zLatent1
        float postDano;
        {
            float w1 = -0.4f; // anti-correlación con APM
            float w2 = 0.5f; // correlación con Precisión
            float w3Sq = 1f - 0.4f * 0.4f - 0.5f * 0.5f;
            float w3 = w3Sq > 0f ? Mathf.Sqrt(w3Sq) : 0f;
            float zInd = SampleStdNormal();
            float z = w1 * zLatent1 + w2 * zLatent2 + w3 * zInd;
            postDano = Mathf.Clamp(0.87f + 0.07f * z, 0.75f, 1.0f);
        }

        float distancia = SampleNormalClamped(21f, 8f, 2f, 40f);
        float cobertura = SampleNormalClamped(0.60f, 0.18f, 0.2f, 1.0f);

        return new BotTargetParams(clase: 3, apm, precision, cobertura, postDano, distancia);
    }
}

// -----------------------------------------------------------------------
// STRUCT DE PARÁMETROS: Zero-allocation, tipo de valor puro.
// Contiene todos los targets operativos que el bot usará durante la ventana.
// -----------------------------------------------------------------------

/// <summary>
/// Parámetros operativos del bot para una ventana de telemetría.
/// Struct (tipo de valor) para garantizar zero-allocation en el heap.
/// </summary>
public struct BotTargetParams
{
    public readonly int Clase;
    /// <summary>Acciones por minuto objetivo. Controla la cadencia de disparo.</summary>
    public readonly float TargetAPM;
    /// <summary>Probabilidad [0,1] de que un disparo acierte. Se evalúa en TryShoot.</summary>
    public readonly float TargetPrecision;
    /// <summary>Proporción de tiempo objetivo en zona de cobertura [0,1].</summary>
    public readonly float TargetCobertura;
    /// <summary>Probabilidad [0,1] de buscar cobertura tras recibir daño.</summary>
    public readonly float TargetPostDano;
    /// <summary>Distancia euclidiana objetivo al NPC en unidades de Unity.</summary>
    public readonly float TargetDistancia;

    /// <summary>Intervalo en segundos entre disparos derivado del APM.</summary>
    public float ShootInterval => TargetAPM > 0f ? 60f / TargetAPM : float.MaxValue;

    public BotTargetParams(int clase, float apm, float precision,
                           float cobertura, float postDano, float distancia)
    {
        Clase = clase;
        TargetAPM = apm;
        TargetPrecision = precision;
        TargetCobertura = cobertura;
        TargetPostDano = postDano;
        TargetDistancia = distancia;
    }

    public override string ToString() =>
        $"[Clase={Clase} APM={TargetAPM:F1} Prec={TargetPrecision:F3} " +
        $"Cob={TargetCobertura:F3} PostD={TargetPostDano:F3} Dist={TargetDistancia:F2}]";
}
