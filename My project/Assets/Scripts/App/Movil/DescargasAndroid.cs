using System;
using UnityEngine;

namespace Composta.App
{
    // Guarda archivos en la carpeta pública "Descargas" de Android, visible en la app Archivos
    // y fácil de compartir. La carpeta interna de la app (Android/data/...) no es accesible desde
    // el teléfono a partir de Android 11.
    //  · Android 10+ (API 29+): MediaStore.Downloads, sin pedir ningún permiso.
    //  · Android 8–9 (API 26–28): escritura directa en Download/; requiere el permiso de almacenamiento
    //    (Player Settings > Android > Other Settings > Write Permission = External (SDCard)).
    public static class DescargasAndroid
    {
        public static bool Disponible => Application.platform == RuntimePlatform.Android;

        public static bool Guardar(string subcarpeta, string nombre, string tipoMime, byte[] datos, out string error)
        {
            error = null;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                int sdk;
                using (var version = new AndroidJavaClass("android.os.Build$VERSION")) sdk = version.GetStatic<int>("SDK_INT");
                return sdk >= 29 ? GuardarMediaStore(subcarpeta, nombre, tipoMime, datos) : GuardarDirecto(subcarpeta, nombre, datos, out error);
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogException(e);
                return false;
            }
#else
            error = "Solo disponible en Android";
            return false;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static bool GuardarMediaStore(string subcarpeta, string nombre, string tipoMime, byte[] datos)
        {
            using (var jugador = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var actividad = jugador.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var resolver = actividad.Call<AndroidJavaObject>("getContentResolver"))
            using (var valores = new AndroidJavaObject("android.content.ContentValues"))
            using (var descargas = new AndroidJavaClass("android.provider.MediaStore$Downloads"))
            using (var destino = descargas.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI"))
            {
                valores.Call("put", "_display_name", nombre);
                valores.Call("put", "mime_type", tipoMime);
                valores.Call("put", "relative_path", "Download/" + subcarpeta);
                using (var uri = resolver.Call<AndroidJavaObject>("insert", destino, valores))
                {
                    if (uri == null) return false;
                    using (var salida = resolver.Call<AndroidJavaObject>("openOutputStream", uri))
                    {
                        salida.Call("write", datos);
                        salida.Call("flush");
                        salida.Call("close");
                    }
                }
            }
            return true;
        }

        static bool GuardarDirecto(string subcarpeta, string nombre, byte[] datos, out string error)
        {
            error = null;
            const string permiso = "android.permission.WRITE_EXTERNAL_STORAGE";
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(permiso))
            {
                UnityEngine.Android.Permission.RequestUserPermission(permiso);
                error = "Concede el permiso de almacenamiento y vuelve a exportar.";
                return false;
            }
            string raiz;
            using (var entorno = new AndroidJavaClass("android.os.Environment"))
            using (var dir = entorno.CallStatic<AndroidJavaObject>("getExternalStoragePublicDirectory",
                                                                     entorno.GetStatic<string>("DIRECTORY_DOWNLOADS")))
                raiz = dir.Call<string>("getAbsolutePath");
            string carpeta = System.IO.Path.Combine(raiz, subcarpeta);
            System.IO.Directory.CreateDirectory(carpeta);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(carpeta, nombre), datos);
            return true;
        }
#endif
    }
}
