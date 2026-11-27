using System.Collections.Generic;

namespace Composta.Core.Pronostico
{
    // Windows nombra las zonas horarias a su manera ("Central Standard Time (Mexico)"); Google Calendar
    // necesita el identificador IANA ("America/Mexico_City"). Tabla basada en CLDR windowsZones.xml
    // para las zonas más probables de la app (Latinoamérica, España, EE. UU.).
    // Si una zona no está aquí, los eventos se crean con el desfase horario (también válido).
    public static class ZonasWindows
    {
        static readonly Dictionary<string, string> Tabla = new Dictionary<string, string>
        {
            // México y Centroamérica
            { "Central Standard Time (Mexico)", "America/Mexico_City" },
            { "Mountain Standard Time (Mexico)", "America/Mazatlan" },
            { "Pacific Standard Time (Mexico)", "America/Tijuana" },
            { "Eastern Standard Time (Mexico)", "America/Cancun" },
            { "Central America Standard Time", "America/Guatemala" },
            // Sudamérica y Caribe
            { "SA Pacific Standard Time", "America/Bogota" },
            { "SA Western Standard Time", "America/La_Paz" },
            { "SA Eastern Standard Time", "America/Cayenne" },
            { "Argentina Standard Time", "America/Argentina/Buenos_Aires" },
            { "Pacific SA Standard Time", "America/Santiago" },
            { "Venezuela Standard Time", "America/Caracas" },
            { "E. South America Standard Time", "America/Sao_Paulo" },
            { "Paraguay Standard Time", "America/Asuncion" },
            { "Montevideo Standard Time", "America/Montevideo" },
            { "Cuba Standard Time", "America/Havana" },
            { "Atlantic Standard Time", "America/Halifax" },
            // Estados Unidos y Canadá
            { "Eastern Standard Time", "America/New_York" },
            { "Central Standard Time", "America/Chicago" },
            { "Mountain Standard Time", "America/Denver" },
            { "US Mountain Standard Time", "America/Phoenix" },
            { "Pacific Standard Time", "America/Los_Angeles" },
            { "Alaskan Standard Time", "America/Anchorage" },
            { "Hawaiian Standard Time", "Pacific/Honolulu" },
            // Europa
            { "Romance Standard Time", "Europe/Madrid" },
            { "W. Europe Standard Time", "Europe/Berlin" },
            { "GMT Standard Time", "Europe/London" },
            { "UTC", "Etc/UTC" },
        };

        // Devuelve el identificador IANA o null si no se reconoce
        public static string AIana(string idSistema)
        {
            if (string.IsNullOrWhiteSpace(idSistema)) return null;
            if (idSistema.Contains("/")) return idSistema;                 // macOS, Linux y Android ya usan IANA
            return Tabla.TryGetValue(idSistema.Trim(), out var iana) ? iana : null;
        }
    }
}
