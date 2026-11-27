using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Composta.App.Google
{
    public sealed class RespuestaHttp
    {
        public long Codigo;
        public string Texto;
        public string Error;                    // error de red (sin respuesta del servidor)
        public Dictionary<string, string> Encabezados;
        public bool Exito => Error == null && Codigo >= 200 && Codigo < 300;
        public bool SinRed => Error != null && Codigo == 0;
        public string Encabezado(string nombre)
        {
            if (Encabezados == null) return null;
            foreach (var kv in Encabezados) if (string.Equals(kv.Key, nombre, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return null;
        }
    }

    // UnityWebRequest como Task (funciona igual en Editor, Windows e IL2CPP/Android).
    // Debe usarse desde el hilo principal; las continuaciones vuelven a él gracias al SynchronizationContext de Unity.
    public static class HttpGoogle
    {
        public static Task<RespuestaHttp> Get(string url, string token = null, int timeout = 15)
            => Enviar(UnityWebRequest.Get(url), token, timeout);

        public static Task<RespuestaHttp> Delete(string url, string token, int timeout = 15)
        {
            var req = UnityWebRequest.Delete(url);
            req.downloadHandler = new DownloadHandlerBuffer();
            return Enviar(req, token, timeout);
        }

        public static Task<RespuestaHttp> PostJson(string url, string json, string token, int timeout = 15)
        {
            var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            req.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            return Enviar(req, token, timeout);
        }

        public static Task<RespuestaHttp> PostFormulario(string url, Dictionary<string, string> campos, int timeout = 15)
        {
            var sb = new StringBuilder();
            foreach (var kv in campos)
            {
                if (kv.Value == null) continue;
                if (sb.Length > 0) sb.Append('&');
                sb.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value));
            }
            var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(sb.ToString())),
                downloadHandler = new DownloadHandlerBuffer()
            };
            req.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
            return Enviar(req, null, timeout);
        }

        static Task<RespuestaHttp> Enviar(UnityWebRequest req, string token, int timeout)
        {
            var tcs = new TaskCompletionSource<RespuestaHttp>();
            req.timeout = timeout;
            if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("Authorization", "Bearer " + token);
            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                var r = new RespuestaHttp
                {
                    Codigo = req.responseCode,
                    Texto = req.downloadHandler != null ? req.downloadHandler.text : null,
                    Encabezados = req.GetResponseHeaders()
                };
                if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.DataProcessingError)
                    r.Error = req.error ?? "Error de red";
                req.Dispose();
                tcs.TrySetResult(r);
            };
            return tcs.Task;
        }
    }
}
