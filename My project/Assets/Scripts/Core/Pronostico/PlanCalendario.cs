using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Composta.Core.Pronostico
{
    // Zona horaria resuelta (Google Time Zone API o, sin conexión, la del dispositivo)
    public readonly struct ZonaHoraria
    {
        public readonly string Id;                 // IANA, p. ej. "America/Mexico_City"; null si no se conoce
        public readonly TimeSpan Desplazamiento;   // rawOffset + dstOffset en el momento de la consulta
        public readonly string Fuente;             // texto para la UI
        public ZonaHoraria(string id, TimeSpan desplazamiento, string fuente) { Id = id; Desplazamiento = desplazamiento; Fuente = fuente; }

        public string Etiqueta()
        {
            var d = Desplazamiento;
            string signo = d < TimeSpan.Zero ? "−" : "+";
            d = d.Duration();
            return $"{(string.IsNullOrEmpty(Id) ? "zona local" : Id)} (UTC{signo}{d.Hours:00}:{d.Minutes:00})";
        }
    }

    public sealed class EventoCalendario
    {
        public TipoHito Tipo;
        public string Titulo, Descripcion;
        public DateTime InicioLocal;              // hora "de pared" en la zona del evento
        public int DuracionMinutos = 30;
        public int RecordatorioMinutos = 60;
        public string ColorId;                    // paleta de Google Calendar ("1".."11")
    }

    // Convierte un pronóstico en eventos de calendario y en el JSON de la Calendar API v3. Sin Unity: testeable.
    public static class PlanCalendario
    {
        public const string PropPrediccion = "compostaPrediccion";
        public const string PropHito = "compostaHito";
        static readonly CultureInfo C = CultureInfo.InvariantCulture;

        public static List<EventoCalendario> Generar(ResultadoPronostico p, DateTime ahoraUtc, ZonaHoraria zona,
                                                     string nombreLote, int horaLocal = 9)
        {
            var lista = new List<EventoCalendario>();
            DateTime ahoraLocal = DateTime.SpecifyKind(ahoraUtc + zona.Desplazamiento, DateTimeKind.Unspecified);
            foreach (var h in p.Hitos)
            {
                var fecha = ahoraLocal.Date.AddDays(Math.Round(h.DiasDesdeAhora)).AddHours(horaLocal);
                if (fecha <= ahoraLocal) fecha = fecha.AddDays(1);           // nunca un recordatorio en el pasado
                lista.Add(new EventoCalendario
                {
                    Tipo = h.Tipo,
                    Titulo = "Composta · " + h.Titulo,
                    Descripcion = h.Descripcion + "\n\nLote: " + nombreLote + "\n" + p.Supuestos +
                                  "\nGenerado por la app de compostaje comunitario.",
                    InicioLocal = fecha,
                    RecordatorioMinutos = h.Tipo == TipoHito.Cosecha || h.Tipo == TipoHito.InicioFaseCaliente ? 24 * 60 : 60,
                    ColorId = Color(h.Tipo)
                });
            }
            return lista;
        }

        static string Color(TipoHito t)
        {
            switch (t)
            {
                case TipoHito.InicioFaseCaliente: case TipoHito.PicoTemperatura: return "11";   // rojo
                case TipoHito.Volteo: return "5";                                                // amarillo
                case TipoHito.Cosecha: case TipoHito.InicioMaduracion: return "10";              // verde
                default: return "8";                                                             // gris
            }
        }

        // Clave estable por lote: al re-exportar se reemplazan los eventos anteriores del mismo lote (idempotente)
        public static string ClavePrediccion(string idLote, PerfilComposta perfil)
        {
            if (!string.IsNullOrEmpty(idLote)) return "lote-" + idLote;
            string firma = string.Format(C, "{0}|{1:F1}|{2:F1}|{3:F1}|{4:F2}|{5:F1}", perfil.Tipo, perfil.PesoKg, perfil.RelacionCN,
                                         perfil.HumedadInicial, perfil.Aislamiento, perfil.TempAmbiente);
            using (var sha = SHA256.Create())
            {
                var b = sha.ComputeHash(Encoding.UTF8.GetBytes(firma));
                return "plan-" + BitConverter.ToString(b, 0, 8).Replace("-", "").ToLowerInvariant();
            }
        }

        // Cuerpo JSON para events.insert. Con Id de zona, dateTime va sin desplazamiento + timeZone (Google aplica
        // el horario de verano correcto en cada fecha); sin Id, se escribe el desplazamiento explícito.
        public static string JsonEvento(EventoCalendario e, ZonaHoraria zona, string clavePrediccion)
        {
            var fin = e.InicioLocal.AddMinutes(e.DuracionMinutos);
            var sb = new StringBuilder(512);
            sb.Append('{');
            sb.Append("\"summary\":").Append(Json(e.Titulo)).Append(',');
            sb.Append("\"description\":").Append(Json(e.Descripcion)).Append(',');
            sb.Append("\"start\":").Append(Momento(e.InicioLocal, zona)).Append(',');
            sb.Append("\"end\":").Append(Momento(fin, zona)).Append(',');
            if (!string.IsNullOrEmpty(e.ColorId)) sb.Append("\"colorId\":").Append(Json(e.ColorId)).Append(',');
            sb.Append("\"reminders\":{\"useDefault\":false,\"overrides\":[{\"method\":\"popup\",\"minutes\":")
              .Append(e.RecordatorioMinutos.ToString(C)).Append("}]},");
            sb.Append("\"extendedProperties\":{\"private\":{")
              .Append('"').Append(PropPrediccion).Append("\":").Append(Json(clavePrediccion)).Append(',')
              .Append('"').Append(PropHito).Append("\":").Append(Json(e.Tipo.ToString()))
              .Append("}}");
            sb.Append('}');
            return sb.ToString();
        }

        static string Momento(DateTime local, ZonaHoraria zona)
        {
            if (!string.IsNullOrEmpty(zona.Id))
                return "{\"dateTime\":" + Json(local.ToString("yyyy-MM-dd'T'HH:mm:ss", C)) + ",\"timeZone\":" + Json(zona.Id) + "}";
            var d = zona.Desplazamiento;
            string off = (d < TimeSpan.Zero ? "-" : "+") + d.Duration().ToString(@"hh\:mm", C);
            return "{\"dateTime\":" + Json(local.ToString("yyyy-MM-dd'T'HH:mm:ss", C) + off) + "}";
        }

        // Escapado JSON (RFC 8259): comillas, barra invertida y caracteres de control
        public static string Json(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4", C));
                        else sb.Append(ch);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }

    // Utilidades de OAuth 2.0 / HTTP sin dependencias de Unity
    public static class UtilGoogle
    {
        // PKCE (RFC 7636): verificador aleatorio y desafío S256
        public static string NuevoVerificadorPkce()
        {
            var bytes = new byte[48];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return Base64Url(bytes);                         // 64 caracteres del alfabeto permitido
        }

        public static string DesafioPkce(string verificador)
        {
            using (var sha = SHA256.Create()) return Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verificador)));
        }

        public static string NuevoEstado()
        {
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return Base64Url(bytes);
        }

        public static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static string UrlAutorizacion(string clientId, string redirectUri, string scope, string desafio, string estado)
            => "https://accounts.google.com/o/oauth2/v2/auth" +
               "?client_id=" + Uri.EscapeDataString(clientId) +
               "&redirect_uri=" + Uri.EscapeDataString(redirectUri) +
               "&response_type=code" +
               "&scope=" + Uri.EscapeDataString(scope) +
               "&code_challenge=" + desafio +
               "&code_challenge_method=S256" +
               "&state=" + Uri.EscapeDataString(estado) +
               "&access_type=offline&prompt=consent";

        // Extrae parámetros de la URL de retorno (?code=...&state=... o error=...)
        public static Dictionary<string, string> ParametrosConsulta(string url)
        {
            var d = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(url)) return d;
            int q = url.IndexOf('?');
            if (q < 0) return d;
            string consulta = url.Substring(q + 1);
            int hash = consulta.IndexOf('#');
            if (hash >= 0) consulta = consulta.Substring(0, hash);
            foreach (var par in consulta.Split('&'))
            {
                if (par.Length == 0) continue;
                int i = par.IndexOf('=');
                string k = Uri.UnescapeDataString((i < 0 ? par : par.Substring(0, i)).Replace('+', ' '));
                string v = i < 0 ? "" : Uri.UnescapeDataString(par.Substring(i + 1).Replace('+', ' '));
                d[k] = v;
            }
            return d;
        }

        // Encabezado HTTP "Date" (RFC 1123): hora real de los servidores de Google, independiente del reloj del teléfono
        public static bool IntentarLeerFechaHttp(string valor, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrWhiteSpace(valor)) return false;
            if (!DateTime.TryParseExact(valor.Trim(), "r", CultureInfo.InvariantCulture,
                                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc)) return false;
            utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return true;
        }
    }
}
