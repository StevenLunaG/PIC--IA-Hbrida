using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Exporta cada ventana de telemetría completada a un archivo CSV.
///
/// Arquitectura (desacoplada):
/// ─ Solo depende de TelemetryManager; NO referencia a SimulationManager.
/// ─ La clase objetivo llega dentro del tensor (TargetClass),
///   eliminando cualquier acoplamiento con el orquestador.
/// </summary>
public class TelemetryCSVExporter : MonoBehaviour
{
    [Header("Inyección de Dependencias")]
    [Tooltip("El motor de telemetría (Obligatorio)")]
    public TelemetryManager telemetryManager;

    [Header("Configuración de Salida")]
    public string fileName = "dataset_simulacion.csv";

    // Ruta en caché
    private string filePath;

    private void Start()
    {
        filePath = Path.Combine(Application.dataPath, fileName);

        if (!File.Exists(filePath))
        {
            // El contrato SDD exige 6 columnas
            string header = "APM,PrecisionRelativa,IndiceCobertura,IndicePostDano,IET,Clase\n";
            File.WriteAllText(filePath, header);
            Debug.Log($"[Exportador] Archivo CSV inicializado en: {filePath}");
        }

        if (telemetryManager != null)
        {
            telemetryManager.OnWindowCompleted += HandleWindowCompleted;
        }
        else
        {
            Debug.LogError("[Exportador] Falta TelemetryManager. Asígnalo en el Inspector.");
        }
    }

    private void OnDestroy()
    {
        if (telemetryManager != null)
            telemetryManager.OnWindowCompleted -= HandleWindowCompleted;
    }

    /// <summary>
    /// Recibe el tensor directamente del evento — la clase viaja dentro de tensor.TargetClass.
    /// Si TargetClass == -1 (modo humano sin simulación), la columna Clase queda vacía.
    /// </summary>
    private void HandleWindowCompleted(TelemetryTensor tensor)
    {
        string classColumn = tensor.TargetClass >= 0 ? tensor.TargetClass.ToString() : "";

        string dataRow = string.Format(CultureInfo.InvariantCulture,
            "{0:F2},{1:F4},{2:F4},{3:F4},{4:F4},{5}\n",
            tensor.APM, tensor.PrecisionRelativa, tensor.IndiceCobertura,
            tensor.IndicePostDano, tensor.IET, classColumn);

        _ = WriteToFileAsync(dataRow);
    }

    private async Task WriteToFileAsync(string line)
    {
        await Task.Run(() =>
        {
            try
            {
                File.AppendAllText(filePath, line);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Exportador] Fallo crítico de I/O: {e.Message}");
            }
        });
    }
}