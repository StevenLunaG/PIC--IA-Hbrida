using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

public class TelemetryCSVExporter : MonoBehaviour
{
    [Header("Inyección de Dependencias")]
    [Tooltip("Arrastra el objeto __SYSTEMS__ (TelemetryManager) aquí")]
    public TelemetryManager telemetryManager;

    [Header("Configuración de Salida")]
    public string fileName = "dataset_simulacion.csv";

    // Ruta en cache
    private string filePath;

    private void Start()
    {
        // Application.dataPath apunta a la carpeta "Assets" del proyecto. 
        // Solo puede ser consultado de forma segura desde el Main Thread.
        filePath = Path.Combine(Application.dataPath, fileName);

        // Escribir cabeceras si el archivo es nuevo
        if (!File.Exists(filePath))
        {
            string header = "APM,PrecisionRelativa,IndiceCobertura,IndicePostDano,IET\n";
            File.WriteAllText(filePath, header);
            Debug.Log($"[Exportador] Archivo CSV inicializado en: {filePath}");
        }

        // Suscripción al patrón Observador del TelemetryManager
        if (telemetryManager != null)
        {
            telemetryManager.OnWindowCompleted += HandleWindowCompleted;
        }
        else
        {
            Debug.LogError("[Exportador] Faltan dependencias. Asigna el TelemetryManager.");
        }
    }

    private void OnDestroy()
    {
        // Desuscripción obligatoria para evitar Memory Leaks
        if (telemetryManager != null)
        {
            telemetryManager.OnWindowCompleted -= HandleWindowCompleted;
        }
    }

    private void HandleWindowCompleted(TelemetryTensor tensor)
    {
        // Formateo estricto. CultureInfo.InvariantCulture asegura que se usen puntos (.) 
        // para los decimales en lugar de comas (,), evitando corromper el CSV para Python/Pandas.
        string dataRow = string.Format(CultureInfo.InvariantCulture,
            "{0:F2},{1:F4},{2:F4},{3:F4},{4:F4}\n",
            tensor.APM, tensor.PrecisionRelativa, tensor.IndiceCobertura, tensor.IndicePostDano, tensor.IET);

        // Derivar la escritura a disco a un hilo secundario
        _ = WriteToFileAsync(dataRow);
    }

    private async Task WriteToFileAsync(string line)
    {
        await Task.Run(() =>
        {
            try
            {
                // AppendAllText es altamente eficiente para ventanas de 10 segundos.
                // Si la cadencia fuera por milisegundos, requeriríamos un FileStream persistente.
                File.AppendAllText(filePath, line);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Exportador] Fallo crítico de I/O: {e.Message}");
            }
        });
    }
}