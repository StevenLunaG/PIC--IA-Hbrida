using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Validador de paridad ONNX: corre el modelo en vivo (Sentis / Inference Engine, vía
/// ONNXInferenceBridge.RunInferenceForTesting) sobre el lote de referencia generado en Python
/// (onnxruntime) y compara los resultados.
///
/// No usa Unity Test Framework. Es un MonoBehaviour normal que se ejecuta manualmente en
/// Play Mode desde el menú contextual del componente en el Inspector — no necesita asmdef
/// especial ni toca ONNXInferenceBridge.cs ni ningún otro script del proyecto.
/// </summary>
public class OnnxParityValidator : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("El mismo ONNXInferenceBridge que ya está en la escena, con el ModelAsset asignado.")]
    public ONNXInferenceBridge bridge;

    [Tooltip("Arrastra aquí onnx_referencia_paridad.json (Unity lo importa como TextAsset).")]
    public TextAsset referenciaJson;

    [Tooltip("Tolerancia para comparar probabilidades (float32, Sentis vs onnxruntime).")]
    public float epsilon = 1e-3f;

    [Serializable]
    private class ReferenciaItem
    {
        public int id;
        public float apm;
        public float precisionRelativa;
        public float iet;
        public float varianzaDistancia;
        public int claseOriginalDataset;
        public int expectedLabel;
        public float[] expectedProbabilities;
        public bool esCasoFrontera;
    }

    // JsonUtility no soporta un array en la raíz del JSON, por eso se envuelve en un objeto
    // con un solo campo "items" antes de parsear (ver EjecutarValidacion).
    [Serializable]
    private class ReferenciaWrapper
    {
        public List<ReferenciaItem> items;
    }

    [ContextMenu("Ejecutar Validación de Paridad")]
    public void EjecutarValidacion()
    {
        if (bridge == null || referenciaJson == null)
        {
            Debug.LogError("[ParityValidator] Falta asignar 'Bridge' o 'Referencia Json' en el Inspector.");
            return;
        }

        string wrapped = "{\"items\":" + referenciaJson.text + "}";
        ReferenciaWrapper data = JsonUtility.FromJson<ReferenciaWrapper>(wrapped);

        if (data == null || data.items == null || data.items.Count == 0)
        {
            Debug.LogError(
                "[ParityValidator] No se pudo parsear el JSON o está vacío. Revisa que los " +
                "nombres de campo del archivo coincidan EXACTAMENTE (mayúsculas incluidas) con " +
                "ReferenciaItem: id, apm, precisionRelativa, iet, varianzaDistancia, " +
                "claseOriginalDataset, expectedLabel, expectedProbabilities, esCasoFrontera.");
            return;
        }

        int total = data.items.Count;
        int labelOk = 0, labelOkFrontera = 0, totalFrontera = 0;
        float[] probsBuffer = new float[4];

        foreach (ReferenciaItem item in data.items)
        {
            int label = bridge.RunInferenceForTesting(
                item.apm, item.precisionRelativa, item.iet, item.varianzaDistancia, probsBuffer);

            bool labelMatch = label == item.expectedLabel;
            bool probsMatch = true;
            string probDetalle = "";

            if (item.expectedProbabilities != null)
            {
                int n = Mathf.Min(4, item.expectedProbabilities.Length);
                for (int i = 0; i < n; i++)
                {
                    float diff = Mathf.Abs(probsBuffer[i] - item.expectedProbabilities[i]);
                    if (diff > epsilon)
                    {
                        probsMatch = false;
                        probDetalle += $" [clase {i}: Sentis={probsBuffer[i]:F4} vs Ref={item.expectedProbabilities[i]:F4}]";
                    }
                }
            }

            if (labelMatch) labelOk++;
            if (item.esCasoFrontera)
            {
                totalFrontera++;
                if (labelMatch) labelOkFrontera++;
            }

            if (!labelMatch || !probsMatch)
            {
                Debug.LogWarning(
                    $"[ParityValidator] MISMATCH id={item.id} (frontera={item.esCasoFrontera}) " +
                    $"→ label Sentis={label} vs esperado={item.expectedLabel}.{probDetalle}");
            }
        }

        string resumenFrontera = totalFrontera > 0
            ? $"{labelOkFrontera}/{totalFrontera} ({(100f * labelOkFrontera / totalFrontera):F1}%)"
            : "(sin casos marcados como frontera)";

        Debug.Log(
            $"[ParityValidator] ── Resumen ──\n" +
            $"Total muestras: {total} | Label OK: {labelOk}/{total} ({(100f * labelOk / total):F1}%)\n" +
            $"Frontera Táctico↔Conservador: {resumenFrontera}");
    }
}
