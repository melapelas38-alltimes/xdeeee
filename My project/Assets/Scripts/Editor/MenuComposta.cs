using System.IO;
using UnityEditor;
using UnityEngine;

// Herramientas del Editor (menú "Composta" en la barra superior de Unity)
public static class MenuComposta
{
    static string CarpetaDatos => Path.Combine(Application.persistentDataPath, "datos");

    [MenuItem("Composta/Abrir carpeta de datos")]
    static void AbrirCarpeta()
    {
        Directory.CreateDirectory(CarpetaDatos);
        EditorUtility.RevealInFinder(CarpetaDatos);
    }

    // Para cuando se pierde la contraseña de administración y no hay código de recuperación.
    // Borra SOLO las cuentas: lotes, mediciones, aportes y exportaciones se conservan.
    [MenuItem("Composta/Restablecer cuentas (olvidé la contraseña)")]
    static void RestablecerCuentas()
    {
        string archivo = Path.Combine(CarpetaDatos, "usuarios.json");
        if (!File.Exists(archivo) && !File.Exists(archivo + ".bak"))
        {
            EditorUtility.DisplayDialog("Composta", "No hay cuentas guardadas todavía.", "Aceptar");
            return;
        }
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Composta", "Detén el modo Play antes de restablecer las cuentas.", "Aceptar");
            return;
        }
        if (!EditorUtility.DisplayDialog("Restablecer cuentas",
                "Se borrarán TODAS las cuentas de usuario (no los lotes ni las mediciones).\n\n" +
                "Al pulsar Play, la app pedirá crear de nuevo la cuenta de administración.\n\n" +
                "Se guarda una copia por si te equivocas.", "Borrar cuentas", "Cancelar"))
            return;

        string copia = archivo + ".respaldo-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss");
        if (File.Exists(archivo)) File.Move(archivo, copia);
        if (File.Exists(archivo + ".bak")) File.Delete(archivo + ".bak");
        PlayerPrefs.DeleteKey("composta.ultimoUsuario");
        Debug.Log($"Cuentas restablecidas. Copia del archivo anterior: {copia}");
        EditorUtility.DisplayDialog("Composta", "Listo. Pulsa Play y crea de nuevo la cuenta de administración.", "Aceptar");
    }
}
