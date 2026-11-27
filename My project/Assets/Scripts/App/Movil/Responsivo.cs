using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Composta.App
{
    // Dos conjuntos de anclas por elemento: AMPLIO (el que tenía al crearse) y COMPACTO (móvil).
    // Así cada pantalla define su versión móvil sin duplicar la construcción de la UI.
    [RequireComponent(typeof(RectTransform))]
    public sealed class Responsivo : MonoBehaviour
    {
        Vector2 aMinA, aMaxA, oMinA, oMaxA, pivA;
        Vector2 aMinC, aMaxC, oMinC, oMaxC, pivC;
        bool ocultarCompacto, ocultarAmplio, capturado;

        // Anclas compactas (con desplazamientos opcionales en unidades, útil para barras de alto fijo)
        public static Responsivo En(Component c, Vector2 aMin, Vector2 aMax, Vector2? offMin = null, Vector2? offMax = null, Vector2? pivote = null)
        {
            var rt = (RectTransform)c.transform;
            var r = Capturar(rt);
            r.aMinC = aMin; r.aMaxC = aMax;
            r.oMinC = offMin ?? Vector2.zero; r.oMaxC = offMax ?? Vector2.zero;
            r.pivC = pivote ?? rt.pivot;
            r.Aplicar();
            return r;
        }

        public static void OcultarEnCompacto(Component c) { var r = Capturar((RectTransform)c.transform); r.ocultarCompacto = true; r.Aplicar(); }
        public static void OcultarEnAmplio(Component c) { var r = Capturar((RectTransform)c.transform); r.ocultarAmplio = true; r.Aplicar(); }

        static Responsivo Capturar(RectTransform rt)
        {
            var r = rt.GetComponent<Responsivo>();
            if (r == null) r = rt.gameObject.AddComponent<Responsivo>();   // sin "??": Unity usa un null falso en el Editor
            if (!r.capturado)
            {
                r.aMinA = rt.anchorMin; r.aMaxA = rt.anchorMax; r.oMinA = rt.offsetMin; r.oMaxA = rt.offsetMax; r.pivA = rt.pivot;
                r.aMinC = r.aMinA; r.aMaxC = r.aMaxA; r.oMinC = r.oMinA; r.oMaxC = r.oMaxA; r.pivC = r.pivA;
                r.capturado = true;
            }
            return r;
        }

        void OnEnable() { AdaptadorPantalla.AlCambiar += Aplicar; Aplicar(); }
        void OnDisable() => AdaptadorPantalla.AlCambiar -= Aplicar;

        void Aplicar()
        {
            if (!capturado) return;
            var rt = (RectTransform)transform;
            bool c = AdaptadorPantalla.Compacto;
            rt.pivot = c ? pivC : pivA;
            rt.anchorMin = c ? aMinC : aMinA; rt.anchorMax = c ? aMaxC : aMaxA;
            rt.offsetMin = c ? oMinC : oMinA; rt.offsetMax = c ? oMaxC : oMaxA;
            bool oculto = c ? ocultarCompacto : ocultarAmplio;
            rt.localScale = oculto ? Vector3.zero : Vector3.one;    // no se usa SetActive: otros scripts lo controlan
        }
    }

    // Contenido de un ScrollRect vertical: en AMPLIO mide lo mismo que la ventana (sin desplazamiento);
    // en COMPACTO mide "altoCompacto" unidades y se desplaza con el dedo.
    public sealed class ContenidoDesplazable : MonoBehaviour
    {
        public RectTransform contenido;
        public ScrollRect scroll;
        public float altoCompacto = 2000f;
        public float altoAmplio = 0f;                 // 0 = igual a la ventana

        void OnEnable() { AdaptadorPantalla.AlCambiar += Recalcular; Recalcular(); }
        void OnDisable() => AdaptadorPantalla.AlCambiar -= Recalcular;
        void OnRectTransformDimensionsChange() { if (isActiveAndEnabled) Recalcular(); }

        public void Recalcular()
        {
            if (contenido == null) return;
            float visible = ((RectTransform)transform).rect.height;
            float alto = AdaptadorPantalla.Compacto ? Mathf.Max(visible, altoCompacto)
                       : altoAmplio > 0f ? Mathf.Max(visible, altoAmplio) : visible;
            contenido.sizeDelta = new Vector2(0f, alto);
            if (scroll != null) scroll.vertical = alto > visible + 1f;
        }
    }

    // Fila horizontal de tarjetas: en AMPLIO se reparten el ancho; en COMPACTO tienen ancho fijo y se deslizan.
    public sealed class Carrusel : MonoBehaviour
    {
        public RectTransform contenido;
        public ScrollRect scroll;
        public float separacion = 14f;
        public readonly List<RectTransform> Tarjetas = new List<RectTransform>();

        void OnEnable() { AdaptadorPantalla.AlCambiar += Recalcular; Recalcular(); }
        void OnDisable() => AdaptadorPantalla.AlCambiar -= Recalcular;
        void OnRectTransformDimensionsChange() { if (isActiveAndEnabled) Recalcular(); }

        public void Recalcular()
        {
            int n = Tarjetas.Count;
            if (contenido == null || n == 0) return;
            float w = ((RectTransform)transform).rect.width;
            float ancho = AdaptadorPantalla.Compacto
                ? Mathf.Clamp(w / 2.3f, 200f, 300f)                       // se ve "media tarjeta" de más: invita a deslizar
                : (w - separacion * (n - 1)) / n;
            float total = n * ancho + (n - 1) * separacion;
            contenido.anchorMin = new Vector2(0f, 0f); contenido.anchorMax = new Vector2(0f, 1f);
            contenido.pivot = new Vector2(0f, 0.5f);
            contenido.sizeDelta = new Vector2(Mathf.Max(total, w), 0f);
            if (scroll != null) scroll.horizontal = total > w + 1f;
            for (int i = 0; i < n; i++)
            {
                var t = Tarjetas[i];
                t.anchorMin = new Vector2(0f, 0f); t.anchorMax = new Vector2(0f, 1f);
                t.pivot = new Vector2(0f, 0.5f);
                t.sizeDelta = new Vector2(ancho, 0f);
                t.anchoredPosition = new Vector2(i * (ancho + separacion), 0f);
            }
        }
    }

    // Garantiza un área de toque de al menos 48 dp alrededor de botones e íconos pequeños (solo en móvil).
    // Es un hijo transparente que recibe el toque; el evento sube al botón o ícono padre.
    public sealed class AreaTactilMinima : MonoBehaviour
    {
        RectTransform zona;

        public static void Agregar(Component c)
        {
            if (c.GetComponent<AreaTactilMinima>() == null) c.gameObject.AddComponent<AreaTactilMinima>();
        }

        void Awake()
        {
            var go = new GameObject("ZonaTactil", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            go.transform.SetAsFirstSibling();                       // detrás del contenido visible
            zona = (RectTransform)go.transform;
            zona.anchorMin = zona.anchorMax = new Vector2(0.5f, 0.5f);
            var img = go.GetComponent<Image>();
            img.color = new Color(0, 0, 0, 0);
            img.raycastTarget = true;
        }

        void OnEnable() { AdaptadorPantalla.AlCambiar += Recalcular; Recalcular(); }
        void OnDisable() => AdaptadorPantalla.AlCambiar -= Recalcular;
        void OnRectTransformDimensionsChange() { if (isActiveAndEnabled) Recalcular(); }

        void Recalcular()
        {
            if (zona == null) return;
            var r = ((RectTransform)transform).rect;
            float min = AdaptadorPantalla.ToqueMinimoUnidades;
            zona.sizeDelta = new Vector2(Mathf.Max(r.width, min), Mathf.Max(r.height, min));
            zona.gameObject.SetActive(min > 0f && (r.width < min || r.height < min));
        }
    }
}
