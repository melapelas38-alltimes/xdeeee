using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Composta.App
{
    // Tarjeta flotante única (singleton) que muestra la ayuda del ícono bajo el puntero.
    // Vive en su propio Canvas por encima de todo y nunca intercepta clics.
    public sealed class TooltipPanel : MonoBehaviour
    {
        const float AnchoMax = 440f;     // en unidades de referencia (1920x1080)
        const float Relleno = 14f;
        const float Separacion = 16f;    // distancia al ícono
        const float Margen = 12f;        // distancia mínima a los bordes de la pantalla
        const float Retardo = 0.15f;     // evita parpadeos al pasar el ratón de largo
        const float DuracionFade = 0.12f;

        static readonly Color ColFondo = new Color32(0x1E, 0x22, 0x29, 0xF5);
        static readonly Color ColBorde = new Color32(0x3A, 0x42, 0x50, 0xFF);
        static readonly Color ColTexto = new Color32(0xE6, 0xEA, 0xF0, 0xFF);

        static TooltipPanel instancia;
        static bool cerrando;

        RectTransform canvasRT, card;
        Text texto;
        CanvasGroup grupo;
        UnityEngine.Object dueno;
        Coroutine animacion;

        static TooltipPanel Instancia
        {
            get
            {
                if (instancia == null && !cerrando)
                {
                    var go = new GameObject("[Tooltips]");
                    DontDestroyOnLoad(go);
                    instancia = go.AddComponent<TooltipPanel>();
                }
                return instancia;
            }
        }

        public static void Mostrar(RectTransform ancla, string contenido, UnityEngine.Object quien, bool porToque = false)
        {
            if (ancla == null || string.IsNullOrEmpty(contenido)) return;
            var t = Instancia;
            if (t == null) return;
            t.MostrarInterno(ancla, contenido, quien);
            t.porToque = porToque;
            t.ocultarEn = porToque ? Time.unscaledTime + SegundosToque : float.MaxValue;
        }

        public static bool VisiblePara(UnityEngine.Object quien) => instancia != null && instancia.dueno == quien && instancia.card.gameObject.activeSelf;

        // En pantallas táctiles no hay "hover": un toque abre el tooltip y se cierra al tocar en otro lugar o tras unos segundos
        const float SegundosToque = 7f;
        bool porToque;
        float ocultarEn = float.MaxValue;

        void Update()
        {
            if (dueno == null || !card.gameObject.activeSelf) return;
            bool nuevoToque = Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame;
            if ((porToque && nuevoToque) || Time.unscaledTime > ocultarEn) OcultarInterno();
        }

        // Solo oculta si el tooltip visible pertenece a quien lo pide (evita que un ícono apague el de otro)
        public static void Ocultar(UnityEngine.Object quien)
        {
            if (instancia != null && instancia.dueno == quien) instancia.OcultarInterno();
        }

        void Awake()
        {
            canvasRT = UIFabrica.Canvas("Canvas Tooltips", transform, 500);
            var cgCanvas = canvasRT.GetComponent<CanvasGroup>();
            cgCanvas.blocksRaycasts = false;     // si la tarjeta tapara el ícono, provocaría enter/exit en bucle
            cgCanvas.interactable = false;

            card = UIFabrica.Panel("Tooltip", canvasRT, Vector2.zero, Vector2.zero, ColFondo);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);    // posición = coordenadas locales del canvas
            card.pivot = new Vector2(0f, 1f);
            var borde = card.gameObject.AddComponent<Outline>();
            borde.effectColor = ColBorde;
            borde.effectDistance = new Vector2(1f, -1f);

            var acento = UIFabrica.Panel("Acento", card, Vector2.zero, new Vector2(0f, 1f), UIFabrica.ColSel);
            acento.offsetMax = new Vector2(3f, 0f);

            texto = UIFabrica.Texto(card, "Texto", Vector2.zero, Vector2.one, 18, TextAnchor.UpperLeft, ColTexto);
            texto.rectTransform.offsetMin = new Vector2(Relleno + 3f, Relleno * 0.8f);
            texto.rectTransform.offsetMax = new Vector2(-Relleno, -Relleno * 0.8f);
            texto.horizontalOverflow = HorizontalWrapMode.Wrap;
            texto.verticalOverflow = VerticalWrapMode.Overflow;
            texto.lineSpacing = 1.1f;

            grupo = card.gameObject.AddComponent<CanvasGroup>();
            grupo.alpha = 0f;
            card.gameObject.SetActive(false);
        }

        void OnApplicationQuit() => cerrando = true;
        void OnDestroy() { if (instancia == this) instancia = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reiniciar() { instancia = null; cerrando = false; }

        void MostrarInterno(RectTransform ancla, string contenido, UnityEngine.Object quien)
        {
            dueno = quien;
            texto.text = contenido;
            card.gameObject.SetActive(true);
            Dimensionar();
            Posicionar(ancla);
            Animar(grupo.alpha, 1f, Retardo);
        }

        void OcultarInterno()
        {
            dueno = null;
            if (!card.gameObject.activeSelf) return;
            Animar(grupo.alpha, 0f, 0f, () => card.gameObject.SetActive(false));
        }

        // Ancho = la línea más larga (hasta AnchoMax); alto = lo que ocupe el texto ya envuelto
        void Dimensionar()
        {
            float rellenoH = Relleno * 2f + 3f, rellenoV = Relleno * 1.6f;
            // Nunca más ancho que la pantalla (teléfonos en vertical)
            float maxPantalla = Mathf.Max(160f, canvasRT.rect.width - 2f * Margen);
            float ancho = Mathf.Min(Mathf.Min(AnchoMax, maxPantalla), texto.preferredWidth + rellenoH + 2f);
            card.sizeDelta = new Vector2(ancho, 10f);
            card.sizeDelta = new Vector2(ancho, texto.preferredHeight + rellenoV);
        }

        // Abajo-derecha del ícono; si no cabe, se voltea a la izquierda y/o arriba; al final se limita a la pantalla
        void Posicionar(RectTransform ancla)
        {
            Vector2 pantalla = RectTransformUtility.WorldToScreenPoint(null, ancla.TransformPoint(ancla.rect.center));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, pantalla, null, out Vector2 p);

            Rect r = canvasRT.rect;
            Vector2 tam = card.sizeDelta;
            float x = p.x + Separacion;
            if (x + tam.x > r.xMax - Margen) x = p.x - Separacion - tam.x;
            float y = p.y - Separacion;
            if (y - tam.y < r.yMin + Margen) y = p.y + Separacion + tam.y;

            x = Mathf.Clamp(x, r.xMin + Margen, Mathf.Max(r.xMin + Margen, r.xMax - Margen - tam.x));
            y = Mathf.Clamp(y, Mathf.Min(r.yMax - Margen, r.yMin + Margen + tam.y), r.yMax - Margen);
            card.anchoredPosition = new Vector2(Mathf.Round(x), Mathf.Round(y));
        }

        void Animar(float desde, float hasta, float retardo, Action alTerminar = null)
        {
            if (animacion != null) StopCoroutine(animacion);
            animacion = StartCoroutine(Fundido(desde, hasta, retardo, alTerminar));
        }

        IEnumerator Fundido(float desde, float hasta, float retardo, Action alTerminar)
        {
            if (retardo > 0f && desde < 0.01f) yield return new WaitForSecondsRealtime(retardo);
            for (float t = 0f; t < DuracionFade; t += Time.unscaledDeltaTime)
            {
                grupo.alpha = Mathf.Lerp(desde, hasta, t / DuracionFade);
                yield return null;
            }
            grupo.alpha = hasta;
            animacion = null;
            alTerminar?.Invoke();
        }
    }

    // Ícono circular "?" o "!" que muestra un tooltip al pasar el ratón.
    // También consume el clic, para que pulsar el ícono no active el botón o la tarjeta que lo contiene.
    public sealed class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public static readonly Color ColNormal = new Color32(0x3D, 0x47, 0x59, 0xFF);
        static readonly Color ColSimbolo = new Color32(0xC9, 0xD3, 0xE3, 0xFF);

        [TextArea] public string texto;
        public Func<string> textoDinamico;      // opcional: si el texto depende del estado

        Image fondo;
        Text simbolo;
        Color colorBase = ColNormal;

        public string Texto => textoDinamico != null ? textoDinamico() : texto;

        public void Configurar(string contenido, Image imagen, Text etiqueta, Color colorReposo)
        {
            texto = contenido; fondo = imagen; simbolo = etiqueta; colorBase = colorReposo;
            Resaltar(false);
        }

        static bool EsToque(PointerEventData e)
            => (e is ExtendedPointerEventData x && x.pointerType == UIPointerType.Touch) || Application.isMobilePlatform;

        public void OnPointerEnter(PointerEventData e)
        {
            if (EsToque(e)) return;                     // con el dedo, "entrar" no significa pasar por encima
            Resaltar(true);
            TooltipPanel.Mostrar((RectTransform)transform, Texto, this);
        }

        public void OnPointerExit(PointerEventData e)
        {
            Resaltar(false);
            if (EsToque(e)) return;                     // al levantar el dedo el tooltip sigue abierto
            TooltipPanel.Ocultar(this);
        }

        // Toque/clic: abre (o cierra si ya estaba abierto). También consume el clic para no activar el botón padre.
        public void OnPointerClick(PointerEventData e)
        {
            if (TooltipPanel.VisiblePara(this) && !EsToque(e)) { TooltipPanel.Ocultar(this); return; }
            TooltipPanel.Mostrar((RectTransform)transform, Texto, this, EsToque(e));
        }

        // Si la ventana se cierra con el ratón encima, no debe quedar un tooltip huérfano
        void OnDisable()
        {
            Resaltar(false);
            TooltipPanel.Ocultar(this);
        }

        void Resaltar(bool activo)
        {
            if (fondo != null) fondo.color = activo ? UIFabrica.ColSel : colorBase;
            if (simbolo != null) simbolo.color = activo ? Color.white : ColSimbolo;
        }
    }
}
