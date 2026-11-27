using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Composta.Core.Pronostico;
using UnityEngine;

namespace Composta.App.Google
{
    public sealed class ResultadoExportacion
    {
        public int Creados, Reemplazados, Fallidos;
        public string Error;
        public bool Exito => Error == null && Fallidos == 0;
    }

    // Google Calendar API v3 por REST (UnityWebRequest). Alcance mínimo: calendar.events.
    // Cada evento lleva extendedProperties.private.compostaPrediccion = clave del lote: así se pueden
    // leer, contar y reemplazar los hitos de la app sin tocar el resto del calendario de la persona.
    public static class ServicioCalendarioGoogle
    {
        const string Base = "https://www.googleapis.com/calendar/v3/calendars/";

#pragma warning disable 0649   // DTOs que llena JsonUtility por reflexión
        [Serializable] class ListaDto { public EventoDto[] items; public string nextPageToken; }
        [Serializable] class EventoDto { public string id; public string summary; }
        [Serializable] class ErrorEnvoltura { public ErrorDto error; }
        [Serializable] class ErrorDto { public int code; public string message; public string status; }
#pragma warning restore 0649

        static string UrlEventos(ConfiguracionGoogle cfg) => Base + Uri.EscapeDataString(string.IsNullOrWhiteSpace(cfg.idCalendario) ? "primary" : cfg.idCalendario.Trim()) + "/events";

        // Lectura: eventos que esta app ya creó para la misma predicción
        public static async Task<(List<string> ids, string error)> ListarDePrediccionAsync(string clave, bool interactivo)
        {
            var cfg = ConfiguracionGoogle.Cargar();
            var ids = new List<string>();
            string pagina = null;
            do
            {
                string url = UrlEventos(cfg) + "?maxResults=250&showDeleted=false&privateExtendedProperty=" +
                             Uri.EscapeDataString(PlanCalendario.PropPrediccion + "=" + clave) +
                             (pagina != null ? "&pageToken=" + Uri.EscapeDataString(pagina) : "");
                var (r, error) = await ConToken(t => HttpGoogle.Get(url, t, cfg.segundosTimeout), interactivo);
                if (error != null) return (ids, error);
                ListaDto dto;
                try { dto = JsonUtility.FromJson<ListaDto>(r.Texto); } catch (Exception) { return (ids, "Respuesta inválida de Google Calendar."); }
                if (dto?.items != null) foreach (var e in dto.items) if (!string.IsNullOrEmpty(e.id)) ids.Add(e.id);
                pagina = string.IsNullOrEmpty(dto?.nextPageToken) ? null : dto.nextPageToken;
            } while (pagina != null);
            return (ids, null);
        }

        // Exporta los hitos: borra los de una exportación anterior del mismo lote y crea los nuevos
        public static async Task<ResultadoExportacion> ExportarAsync(List<EventoCalendario> eventos, ZonaHoraria zona, string clave,
                                                                     Action<string> progreso)
        {
            var res = new ResultadoExportacion();
            var cfg = ConfiguracionGoogle.Cargar();
            if (cfg == null || !cfg.TieneOAuth) { res.Error = "Falta configurar Google (ver docs/GOOGLE_Y_ANDROID.md)."; return res; }

            progreso?.Invoke("Conectando con Google…");
            var (_, errToken) = await AutenticacionGoogle.ObtenerTokenAsync(true);
            if (errToken != null) { res.Error = errToken; return res; }

            progreso?.Invoke("Buscando hitos anteriores en tu calendario…");
            var (anteriores, errLista) = await ListarDePrediccionAsync(clave, false);
            if (errLista != null) { res.Error = errLista; return res; }
            foreach (var id in anteriores)
            {
                var (_, e) = await ConToken(t => HttpGoogle.Delete(UrlEventos(cfg) + "/" + Uri.EscapeDataString(id), t, cfg.segundosTimeout), false);
                if (e == null) res.Reemplazados++;
            }

            for (int i = 0; i < eventos.Count; i++)
            {
                progreso?.Invoke($"Creando evento {i + 1} de {eventos.Count}…");
                string json = PlanCalendario.JsonEvento(eventos[i], zona, clave);
                var (_, e) = await ConToken(t => HttpGoogle.PostJson(UrlEventos(cfg), json, t, cfg.segundosTimeout), false);
                if (e == null) res.Creados++;
                else
                {
                    res.Fallidos++;
                    if (res.Fallidos == 1) res.Error = e;            // se informa el primer error
                    if (e.StartsWith("Sin conexión", StringComparison.Ordinal)) break;
                }
            }
            return res;
        }

        // Ejecuta la petición con token; ante 401 renueva el token una vez y reintenta
        static async Task<(RespuestaHttp r, string error)> ConToken(Func<string, Task<RespuestaHttp>> pedir, bool interactivo)
        {
            for (int intento = 0; intento < 2; intento++)
            {
                var (token, err) = await AutenticacionGoogle.ObtenerTokenAsync(interactivo);
                if (err != null) return (null, err);
                var r = await pedir(token);
                if (r.Exito) return (r, null);
                if (r.SinRed) return (r, "Sin conexión con Google Calendar.");
                if (r.Codigo == 401 && intento == 0) { AutenticacionGoogle.InvalidarAcceso(); continue; }
                return (r, Describir(r));
            }
            return (null, "Google rechazó la autorización.");
        }

        static string Describir(RespuestaHttp r)
        {
            string msg = null;
            try { msg = JsonUtility.FromJson<ErrorEnvoltura>(r.Texto)?.error?.message; } catch (Exception) { }
            switch (r.Codigo)
            {
                case 403:
                    if (msg != null && (msg.Contains("has not been used") || msg.Contains("disabled")))
                        return "Activa «Google Calendar API» en tu proyecto de Google Cloud.";
                    return "Google denegó el acceso al calendario (403). " + msg;
                case 404: return "No se encontró el calendario configurado (revisa idCalendario).";
                case 429: return "Demasiadas solicitudes a Google Calendar; intenta en unos minutos.";
                default: return $"Google Calendar respondió {r.Codigo}. {msg}".Trim();
            }
        }
    }
}
