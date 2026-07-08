using UnityEngine;

// Contrato de datos estricto [1, 7]
// Indices del tensor de inferencia:
// 0: APM  1: PrecisionRelativa  2: IndiceCobertura
// 3: IndicePostDano  4: IET  5: DistanciaPromedio  6: VarianzaDistancia
public struct TelemetryTensor
{
    public float APM;
    public float PrecisionRelativa;
    public float IndiceCobertura;
    public float IndicePostDano;
    public float IET;
    /// <summary>Distancia euclidiana promedio al NPC durante la ventana.</summary>
    public float DistanciaPromedio;
    /// <summary>
    /// Varianza de la distancia al NPC durante la ventana.
    /// Alta en Tactico (ciclos avance/retirada), baja en Conservador (posicion estatica).
    /// Calculada como: (SumXi2 / N) - media^2
    /// </summary>
    public float VarianzaDistancia;
    public int TargetClass;
}
