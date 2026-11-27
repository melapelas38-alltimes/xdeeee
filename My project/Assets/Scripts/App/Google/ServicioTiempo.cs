using System;
using Composta.Core.Pronostico;
using UnityEngine;

namespace Composta.App.Google
{
    // Instantánea de tiempo: hora UTC y zona horaria del teléfono
    public sealed class InfoTiempo
    {
        public DateTime UtcAlConsultar;
        public ZonaHoraria Zona;
        public string Detalle = "";

        public DateTime AhoraUtc => DateTime.UtcNow;
        public DateTime AhoraLocal => DateTime.SpecifyKind(AhoraUtc + Zona.Desplazamiento, DateTimeKind.Unspecified);
    }

    // Hora y zona horaria DEL DISPOSITIVO: sin cuentas, sin claves de API, sin permiso de ubicación.
    // Android sincroniza solo la hora y la zona con la red del operador o con internet.
    //  · Android: zona IANA del sistema (java.util.TimeZone), p. ej. "America/Mexico_City" → Google Calendar
    //    aplica el horario de verano correcto en cada fecha futura.
    //  · PC: macOS/Linux ya dan IANA; Windows se traduce con ZonasWindows; si no se reconoce, se usa el desfase.
    public static class ServicioTiempo
    {
        public static InfoTiempo Actual
        {
            get
            {
                var ahora = DateTime.UtcNow;
                var desfase = TimeZoneInfo.Local.GetUtcOffset(ahora);
                string id = ZonaIana();
                return new InfoTiempo
                {
                    UtcAlConsultar = ahora,
                    Zona = new ZonaHoraria(id, desfase, "dispositivo"),
                    Detalle = Application.isMobilePlatform ? "Hora y zona horaria del teléfono" : "Hora y zona horaria de este equipo"
                };
            }
        }

        static string zonaCache;

        static string ZonaIana()
        {
            if (zonaCache != null) return zonaCache.Length == 0 ? null : zonaCache;
            string id = null;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var tz = new AndroidJavaClass("java.util.TimeZone"))
                using (var def = tz.CallStatic<AndroidJavaObject>("getDefault"))
                    id = def.Call<string>("getID");
            }
            catch (Exception e) { Debug.LogWarning("No se pudo leer la zona horaria de Android: " + e.Message); }
#endif
            if (string.IsNullOrEmpty(id))
            {
                try { id = ZonasWindows.AIana(TimeZoneInfo.Local.Id); }
                catch (Exception) { id = null; }
            }
            zonaCache = id ?? "";
            return id;
        }

        // Si el usuario cambia la zona del teléfono mientras la app está abierta
        public static void Refrescar() => zonaCache = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reiniciar() => zonaCache = null;
    }
}
