using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Composta.Core.Pronostico;
using UnityEngine;

namespace Composta.App.Google
{
    // Recibe la redirección de OAuth con el código de autorización
    public interface IReceptorRedireccion : IDisposable
    {
        string RedirectUri { get; }
        Task<string> EsperarAsync(TimeSpan limite);     // devuelve la URL completa de retorno (o null si venció)
    }

    // OAuth 2.0 para apps instaladas con PKCE (sin bibliotecas de Google: solo UnityWebRequest).
    //  · Android: cliente OAuth tipo "Android" + esquema de URI propio (deep link) → Application.deepLinkActivated.
    //  · Editor / Windows / macOS: cliente tipo "Escritorio" + redirección a 127.0.0.1 (bucle local).
    // El token de actualización se guarda en PlayerPrefs (almacenamiento privado de la app en Android).
    public static class AutenticacionGoogle
    {
        const string ClavePrefs = "composta.google.token.v1";
        const string UrlToken = "https://oauth2.googleapis.com/token";
        const string UrlRevocar = "https://oauth2.googleapis.com/revoke";
        static readonly TimeSpan LimiteConsentimiento = TimeSpan.FromMinutes(5);

#pragma warning disable 0649   // DTOs que llena JsonUtility por reflexión
        [Serializable] class TokenDto { public string access_token; public int expires_in; public string refresh_token; public string error; public string error_description; }
        [Serializable] class TokenGuardado { public string acceso; public string actualizacion; public long expiraUtc; public string clientId; }
#pragma warning restore 0649

        static Task<(string, string)> autorizacionEnCurso;

        public static bool Conectado
        {
            get { var t = Cargar(); return t != null && !string.IsNullOrEmpty(t.actualizacion) && t.clientId == ClientId(); }
        }

        // Devuelve (token, error). Si interactivo = true y no hay sesión, abre el navegador para pedir permiso.
        public static async Task<(string token, string error)> ObtenerTokenAsync(bool interactivo)
        {
            var cfg = ConfiguracionGoogle.Cargar();
            if (cfg == null || !cfg.TieneOAuth)
                return (null, "Falta configurar el cliente OAuth de Google (ver docs/GOOGLE_Y_ANDROID.md).");

            var t = Cargar();
            if (t != null && t.clientId == ClientId())
            {
                if (!string.IsNullOrEmpty(t.acceso) && t.expiraUtc > DateTime.UtcNow.AddSeconds(60).Ticks) return (t.acceso, null);
                if (!string.IsNullOrEmpty(t.actualizacion))
                {
                    var (ok, err) = await Refrescar(t, cfg);
                    if (ok) return (t.acceso, null);
                    if (!interactivo) return (null, err);
                }
            }
            if (!interactivo) return (null, "No hay una cuenta de Google conectada.");
            if (autorizacionEnCurso != null && !autorizacionEnCurso.IsCompleted) return await autorizacionEnCurso;
            autorizacionEnCurso = Autorizar(cfg);
            return await autorizacionEnCurso;
        }

        // Tras un 401: se descarta el token de acceso para forzar su renovación
        public static void InvalidarAcceso()
        {
            var t = Cargar();
            if (t == null) return;
            t.acceso = null; t.expiraUtc = 0;
            Guardar(t);
        }

        public static async Task DesconectarAsync()
        {
            var t = Cargar();
            PlayerPrefs.DeleteKey(ClavePrefs);
            PlayerPrefs.Save();
            string token = t?.actualizacion ?? t?.acceso;
            if (!string.IsNullOrEmpty(token))
                await HttpGoogle.PostFormulario(UrlRevocar, new Dictionary<string, string> { { "token", token } });   // revoca en Google
        }

        // ---------------- Flujo de autorización ----------------
        static async Task<(string, string)> Autorizar(ConfiguracionGoogle cfg)
        {
            string verificador = UtilGoogle.NuevoVerificadorPkce();
            string estado = UtilGoogle.NuevoEstado();
            using (var receptor = CrearReceptor(cfg))
            {
                string url = UtilGoogle.UrlAutorizacion(ClientId(), receptor.RedirectUri, ConfiguracionGoogle.AlcanceCalendario,
                                                        UtilGoogle.DesafioPkce(verificador), estado);
                Application.OpenURL(url);                    // navegador del sistema (Google no permite WebViews embebidos)
                string retorno = await receptor.EsperarAsync(LimiteConsentimiento);
                if (retorno == null) return (null, "No se completó el inicio de sesión con Google a tiempo.");

                var p = UtilGoogle.ParametrosConsulta(retorno);
                if (p.TryGetValue("error", out var e)) return (null, e == "access_denied" ? "Permiso denegado en Google." : "Google devolvió: " + e);
                if (!p.TryGetValue("state", out var st) || st != estado) return (null, "Respuesta de Google no válida (estado distinto).");
                if (!p.TryGetValue("code", out var codigo)) return (null, "Google no devolvió el código de autorización.");

                var campos = new Dictionary<string, string>
                {
                    { "client_id", ClientId() }, { "code", codigo }, { "code_verifier", verificador },
                    { "grant_type", "authorization_code" }, { "redirect_uri", receptor.RedirectUri }
                };
                string secreto = SecretoCliente(cfg);
                if (!string.IsNullOrEmpty(secreto)) campos["client_secret"] = secreto;

                var r = await HttpGoogle.PostFormulario(UrlToken, campos, cfg.segundosTimeout);
                var dto = Leer(r);
                if (dto == null || string.IsNullOrEmpty(dto.access_token))
                    return (null, "No se pudo obtener el token: " + (dto?.error_description ?? dto?.error ?? r.Error ?? r.Codigo.ToString()));

                var t = new TokenGuardado
                {
                    acceso = dto.access_token, actualizacion = dto.refresh_token, clientId = ClientId(),
                    expiraUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, dto.expires_in)).Ticks
                };
                Guardar(t);
                return (t.acceso, null);
            }
        }

        static async Task<(bool, string)> Refrescar(TokenGuardado t, ConfiguracionGoogle cfg)
        {
            var campos = new Dictionary<string, string>
            {
                { "client_id", ClientId() }, { "refresh_token", t.actualizacion }, { "grant_type", "refresh_token" }
            };
            string secreto = SecretoCliente(cfg);
            if (!string.IsNullOrEmpty(secreto)) campos["client_secret"] = secreto;
            var r = await HttpGoogle.PostFormulario(UrlToken, campos, cfg.segundosTimeout);
            if (r.SinRed) return (false, "Sin conexión con Google.");
            var dto = Leer(r);
            if (dto == null || string.IsNullOrEmpty(dto.access_token))
            {
                if (dto?.error == "invalid_grant") { PlayerPrefs.DeleteKey(ClavePrefs); PlayerPrefs.Save(); }   // revocado o vencido
                return (false, "La sesión de Google venció; vuelve a conectar la cuenta.");
            }
            t.acceso = dto.access_token;
            t.expiraUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, dto.expires_in)).Ticks;
            if (!string.IsNullOrEmpty(dto.refresh_token)) t.actualizacion = dto.refresh_token;
            Guardar(t);
            return (true, null);
        }

        static IReceptorRedireccion CrearReceptor(ConfiguracionGoogle cfg)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new ReceptorEnlaceProfundo(cfg.EsquemaAndroid);
#else
            return new ReceptorBucleLocal();
#endif
        }

        static string ClientId()
        {
            var cfg = ConfiguracionGoogle.Cargar();
            if (cfg == null) return "";
#if UNITY_ANDROID && !UNITY_EDITOR
            return cfg.clientIdAndroid.Trim();
#else
            return cfg.clientIdEscritorio.Trim();
#endif
        }

        static string SecretoCliente(ConfiguracionGoogle cfg)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return null;                                     // los clientes Android no usan secreto (PKCE)
#else
            return string.IsNullOrWhiteSpace(cfg.clientSecretEscritorio) ? null : cfg.clientSecretEscritorio.Trim();
#endif
        }

        static TokenDto Leer(RespuestaHttp r)
        {
            if (string.IsNullOrEmpty(r?.Texto)) return null;
            try { return JsonUtility.FromJson<TokenDto>(r.Texto); } catch (Exception) { return null; }
        }

        static TokenGuardado Cargar()
        {
            string s = PlayerPrefs.GetString(ClavePrefs, "");
            if (string.IsNullOrEmpty(s)) return null;
            try { return JsonUtility.FromJson<TokenGuardado>(s); } catch (Exception) { return null; }
        }

        static void Guardar(TokenGuardado t)
        {
            PlayerPrefs.SetString(ClavePrefs, JsonUtility.ToJson(t));
            PlayerPrefs.Save();
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    // Android: Google redirige a "<paquete>:/oauth2redirect?code=..." y Android abre la app (intent-filter del manifiesto)
    sealed class ReceptorEnlaceProfundo : IReceptorRedireccion
    {
        readonly string esquema;
        readonly TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();
        public string RedirectUri => esquema + ":/oauth2redirect";

        public ReceptorEnlaceProfundo(string esquema)
        {
            this.esquema = esquema;
            Application.deepLinkActivated += AlEnlace;
        }

        void AlEnlace(string url)
        {
            if (url != null && url.StartsWith(esquema + ":", StringComparison.OrdinalIgnoreCase)) tcs.TrySetResult(url);
        }

        public async Task<string> EsperarAsync(TimeSpan limite)
        {
            var gana = await Task.WhenAny(tcs.Task, Task.Delay(limite));
            return gana == tcs.Task ? tcs.Task.Result : null;
        }

        public void Dispose() => Application.deepLinkActivated -= AlEnlace;
    }
#else
    // Editor y escritorio: servidor HTTP mínimo en 127.0.0.1 con puerto libre (flujo "loopback" de Google)
    sealed class ReceptorBucleLocal : IReceptorRedireccion
    {
        readonly System.Net.HttpListener listener = new System.Net.HttpListener();
        public string RedirectUri { get; }

        public ReceptorBucleLocal()
        {
            var tcp = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            tcp.Start();
            int puerto = ((System.Net.IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            RedirectUri = $"http://127.0.0.1:{puerto}/";
            listener.Prefixes.Add(RedirectUri);
            listener.Start();
        }

        public async Task<string> EsperarAsync(TimeSpan limite)
        {
            var contexto = Task.Run(() => listener.GetContext());
            if (await Task.WhenAny(contexto, Task.Delay(limite)) != contexto) return null;
            var ctx = contexto.Result;
            string url = ctx.Request.Url.ToString();
            var html = System.Text.Encoding.UTF8.GetBytes(
                "<html><body style='font-family:sans-serif;background:#0E1116;color:#E8ECF2;text-align:center;padding-top:15%'>" +
                "<h2>Cuenta de Google conectada</h2><p>Ya puedes cerrar esta pestaña y volver a la app de compostaje.</p></body></html>");
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = html.Length;
            ctx.Response.OutputStream.Write(html, 0, html.Length);
            ctx.Response.OutputStream.Close();
            return url;
        }

        public void Dispose()
        {
            try { listener.Stop(); listener.Close(); } catch (Exception) { }
        }
    }
#endif
}
