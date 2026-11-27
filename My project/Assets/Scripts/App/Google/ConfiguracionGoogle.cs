using UnityEngine;

namespace Composta.App.Google
{
    // Credenciales de Google Calendar (opcional: sin esto la app funciona y solo se desactiva "Exportar").
    // La hora y la zona horaria salen del teléfono o PC; no necesitan claves.
    // Crear con: Assets > Create > Composta > Configuración de Google,
    // y guardarlo EXACTAMENTE en Assets/Resources/ConfiguracionGoogle.asset (se carga con Resources.Load).
    // IMPORTANTE: ese archivo está en .gitignore; no subas claves al repositorio.
    [CreateAssetMenu(fileName = "ConfiguracionGoogle", menuName = "Composta/Configuración de Google")]
    public sealed class ConfiguracionGoogle : ScriptableObject
    {
        public const string NombreRecurso = "ConfiguracionGoogle";

        [Header("OAuth 2.0 · cliente tipo Android (para la app en el teléfono)")]
        public string clientIdAndroid = "";
        [Tooltip("Esquema de la URI de retorno. Por defecto, el nombre del paquete (Player Settings > Identification)")]
        public string esquemaRedireccionAndroid = "";

        [Header("OAuth 2.0 · cliente tipo Escritorio (para probar en el Editor / Windows)")]
        public string clientIdEscritorio = "";
        [Tooltip("Google entrega un 'secreto' para clientes de escritorio; no es confidencial en apps instaladas")]
        public string clientSecretEscritorio = "";

        [Header("Calendario")]
        [Tooltip("'primary' = calendario principal de la cuenta conectada")]
        public string idCalendario = "primary";
        [Range(0, 23)] public int horaRecordatorios = 9;

        [Header("Red")]
        [Range(5, 60)] public int segundosTimeout = 15;

        public const string AlcanceCalendario = "https://www.googleapis.com/auth/calendar.events";   // mínimo privilegio

        public bool TieneOAuth
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return !string.IsNullOrWhiteSpace(clientIdAndroid);
#else
                return !string.IsNullOrWhiteSpace(clientIdEscritorio);
#endif
            }
        }

        public string EsquemaAndroid => string.IsNullOrWhiteSpace(esquemaRedireccionAndroid) ? Application.identifier : esquemaRedireccionAndroid.Trim();

        static ConfiguracionGoogle cache;
        public static ConfiguracionGoogle Cargar()
        {
            if (cache == null) cache = Resources.Load<ConfiguracionGoogle>(NombreRecurso);
            return cache;
        }
    }
}
