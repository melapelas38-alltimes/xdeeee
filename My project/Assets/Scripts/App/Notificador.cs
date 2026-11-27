using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Composta.App
{
    public enum TipoMensaje { Info, Exito, Aviso, Error }

    public readonly struct OpcionDialogo
    {
        public readonly string Texto;
        public readonly Action Accion;
        public readonly bool Primaria;
        public OpcionDialogo(string texto, Action accion = null, bool primaria = false) { Texto = texto; Accion = accion; Primaria = primaria; }
    }

    // Mensajes visibles para quien usa la app. Antes todo iba a Debug.Log,
    // que en una build de escritorio nadie ve (CSV guardado, comparativas, errores...).
    public sealed class Notificador : MonoBehaviour
    {
        const int MaxToasts = 4;
        static Notificador instancia;
        static bool cerrando;

        RectTransform raizToasts, raizDialogo;
        readonly Queue<(string titulo, string cuerpo, OpcionDialogo[] opciones)> colaDialogos =
            new Queue<(string, string, OpcionDialogo[])>();
        readonly List<GameObject> toasts = new List<GameObject>();
        GameObject dialogoActual;

        static Notificador Instancia
        {
            get
            {
                if (instancia == null && !cerrando)
                {
                    var go = new GameObject("[Notificador]");
                    DontDestroyOnLoad(go);
                    instancia = go.AddComponent<Notificador>();
                }
                return instancia;
            }
        }

        public static bool DialogoAbierto => instancia != null && instancia.dialogoActual != null;

        public static void Mostrar(string texto, TipoMensaje tipo = TipoMensaje.Info)
        {
            if (string.IsNullOrEmpty(texto)) return;
            var n = Instancia;
            if (n != null) n.CrearToast(texto, tipo);
        }

        public static void Dialogo(string titulo, string cuerpo, params OpcionDialogo[] opciones)
        {
            var n = Instancia;
            if (n == null) return;
            if (opciones == null || opciones.Length == 0) opciones = new[] { new OpcionDialogo("Aceptar", null, true) };
            n.colaDialogos.Enqueue((titulo, cuerpo, opciones));
            if (n.dialogoActual == null) n.SiguienteDialogo();
        }

        public static void CerrarDialogos()
        {
            if (instancia == null) return;
            instancia.colaDialogos.Clear();
            instancia.CerrarDialogoActual();
        }

        void Awake()
        {
            raizToasts = UIFabrica.Canvas("Canvas Toasts", transform, 300);
            raizToasts.GetComponent<CanvasGroup>().blocksRaycasts = false;     // no tapan clics
            raizDialogo = UIFabrica.Canvas("Canvas Dialogo", transform, 60);
        }

        void OnApplicationQuit() => cerrando = true;

        void OnDestroy()
        {
            if (instancia == this) instancia = null;
            if (dialogoActual != null) GuardiaEntrada.Liberar(this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reiniciar() { instancia = null; cerrando = false; }

        // ---------------- Toasts ----------------
        void CrearToast(string texto, TipoMensaje tipo)
        {
            while (toasts.Count >= MaxToasts) { Destroy(toasts[0]); toasts.RemoveAt(0); }
            for (int i = 0; i < toasts.Count; i++) Reubicar(toasts[i], toasts.Count - i);

            var rt = UIFabrica.Panel("Toast", raizToasts, new Vector2(X0, 0.86f), new Vector2(X1, 0.915f), ColorDe(tipo));
            var cg = rt.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            var t = UIFabrica.Texto(rt, "Texto", Vector2.zero, Vector2.one, 20, TextAnchor.MiddleCenter, Color.white);
            t.text = texto;
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 12; t.resizeTextMaxSize = 20;
            toasts.Add(rt.gameObject);
            StartCoroutine(VidaToast(rt.gameObject, cg, tipo == TipoMensaje.Error ? 7f : 4f));
        }

        static void Reubicar(GameObject toast, int nivel)
        {
            var rt = (RectTransform)toast.transform;
            float y1 = 0.915f - nivel * 0.06f;
            rt.anchorMin = new Vector2(X0, y1 - 0.055f);
            rt.anchorMax = new Vector2(X1, y1);
        }

        IEnumerator VidaToast(GameObject go, CanvasGroup cg, float duracion)
        {
            for (float t = 0f; t < 0.2f && cg != null; t += Time.unscaledDeltaTime) { cg.alpha = t / 0.2f; yield return null; }
            if (cg != null) cg.alpha = 1f;
            yield return new WaitForSecondsRealtime(duracion);
            for (float t = 0f; t < 0.35f && cg != null; t += Time.unscaledDeltaTime) { cg.alpha = 1f - t / 0.35f; yield return null; }
            if (go != null) { toasts.Remove(go); Destroy(go); }
        }

        // En móvil los avisos usan casi todo el ancho
        static float X0 => AdaptadorPantalla.Compacto ? 0.03f : 0.30f;
        static float X1 => AdaptadorPantalla.Compacto ? 0.97f : 0.70f;

        static Color ColorDe(TipoMensaje t)
        {
            switch (t)
            {
                case TipoMensaje.Exito: return new Color(0.16f, 0.36f, 0.22f, 0.96f);
                case TipoMensaje.Aviso: return new Color(0.45f, 0.36f, 0.08f, 0.96f);
                case TipoMensaje.Error: return new Color(0.50f, 0.14f, 0.14f, 0.96f);
                default: return new Color(0.20f, 0.22f, 0.30f, 0.96f);
            }
        }

        // ---------------- Diálogos ----------------
        void SiguienteDialogo()
        {
            if (colaDialogos.Count == 0) return;
            var (titulo, cuerpo, opciones) = colaDialogos.Dequeue();

            var fondo = UIFabrica.Panel("Dialogo", raizDialogo, Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.7f));
            dialogoActual = fondo.gameObject;
            GuardiaEntrada.Bloquear(this);

            var card = UIFabrica.Tarjeta("Card", fondo, new Vector2(0.25f, 0.25f), new Vector2(0.75f, 0.75f), UIFabrica.ColPanel);
            Responsivo.En(card, new Vector2(0.03f, 0.15f), new Vector2(0.97f, 0.85f));
            UIFabrica.Texto(card, "Titulo", new Vector2(0.03f, 0.84f), new Vector2(0.97f, 0.97f), 30, TextAnchor.MiddleLeft, Color.white).text = titulo;
            var c = UIFabrica.Texto(card, "Cuerpo", new Vector2(0.03f, 0.18f), new Vector2(0.97f, 0.83f), 20, TextAnchor.UpperLeft, new Color(1, 1, 1, 0.9f));
            c.text = cuerpo;
            c.resizeTextForBestFit = true; c.resizeTextMinSize = 12; c.resizeTextMaxSize = 22;

            // Botones alineados a la derecha, en el orden recibido
            float ancho = 0.22f, x1 = 0.97f;
            Button primero = null;
            for (int i = opciones.Length - 1; i >= 0; i--)
            {
                var op = opciones[i];
                var b = UIFabrica.Boton(card, op.Texto, new Vector2(x1 - ancho, 0.04f), new Vector2(x1, 0.14f), 22,
                    op.Primaria ? UIFabrica.ColPrimario : UIFabrica.ColBoton, () => Elegir(op));
                var et = UIFabrica.Etiqueta(b); et.resizeTextForBestFit = true; et.resizeTextMinSize = 11; et.resizeTextMaxSize = 22;
                if (op.Primaria || primero == null) primero = b;
                x1 -= ancho + 0.02f;
            }
            UIFabrica.Enfocar(primero);
        }

        void Elegir(OpcionDialogo op)
        {
            CerrarDialogoActual();
            try { op.Accion?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); Mostrar("Ocurrió un error: " + e.Message, TipoMensaje.Error); }
            if (dialogoActual == null) SiguienteDialogo();
        }

        void CerrarDialogoActual()
        {
            if (dialogoActual == null) return;
            Destroy(dialogoActual);
            dialogoActual = null;
            GuardiaEntrada.Liberar(this);
        }
    }
}
