using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Composta.App
{
    // Construcción de UI por código, con el mismo estilo que PanelUI/PanelBuild
    // (anclas relativas + CanvasScaler 1920x1080). Centraliza lo que antes estaba duplicado.
    public static class UIFabrica
    {
        // Alias históricos: ahora apuntan a los tokens del Tema (todo el código existente hereda el rediseño)
        public static readonly Color ColFondo = Tema.Fondo;
        public static readonly Color ColPanel = Tema.Superficie;
        public static readonly Color ColCard = Tema.SuperficieAlta;
        public static readonly Color ColCampo = Tema.SuperficieCampo;
        public static readonly Color ColBoton = Tema.BotonNeutro;
        public static readonly Color ColPrimario = Tema.BotonPrimario;
        public static readonly Color ColPeligro = Tema.BotonPeligro;
        public static readonly Color ColSel = Tema.AcentoSimulacion;
        public static readonly Color ColTextoTenue = Tema.TextoSecundario;
        public static readonly Color ColError = new Color(1f, 0.55f, 0.52f);
        public static readonly Color ColExito = new Color(0.50f, 0.88f, 0.64f);

        static Font fuente;
        public static Font Fuente => fuente != null ? fuente : (fuente = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static void AsegurarEventSystem()
        {
            // Con el Input System nuevo hace falta InputSystemUIInputModule (el StandaloneInputModule da error)
            if (Object.FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        // Devuelve la raíz de contenido del lienzo: un hijo ajustado al ÁREA SEGURA (notch, barra de navegación).
        // El escalado se adapta solo a escritorio o móvil (ver AdaptadorPantalla).
        public static RectTransform Canvas(string nombre, Transform padre, int orden)
        {
            var go = new GameObject(nombre, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            if (padre != null) go.transform.SetParent(padre, false);
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = orden;
            AdaptadorPantalla.Configurar(go.GetComponent<CanvasScaler>());
            go.AddComponent<EscaladorAdaptable>();

            var seguro = new GameObject("AreaSegura", typeof(RectTransform), typeof(CanvasGroup));
            seguro.transform.SetParent(go.transform, false);
            var rt = (RectTransform)seguro.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            // Se agrega AL FINAL: su OnEnable ajusta las anclas a Screen.safeArea (antes se sobrescribían aquí mismo)
            seguro.AddComponent<AreaSegura>();
            return rt;
        }

        // Zona con desplazamiento vertical: en diseño amplio no se desplaza; en compacto mide "altoCompacto".
        // Los hijos se anclan a "contenido" (fracciones de su alto).
        public static RectTransform Desplazable(string nombre, Transform padre, Vector2 aMin, Vector2 aMax, float altoCompacto,
                                                out RectTransform contenido)
        {
            var vista = Panel(nombre, padre, aMin, aMax, new Color(0, 0, 0, 0), 0);
            vista.GetComponent<Image>().raycastTarget = true;           // permite arrastrar desde zonas vacías
            vista.gameObject.AddComponent<RectMask2D>();
            var go = new GameObject("Contenido", typeof(RectTransform));
            go.transform.SetParent(vista, false);
            contenido = (RectTransform)go.transform;
            contenido.anchorMin = new Vector2(0f, 1f); contenido.anchorMax = new Vector2(1f, 1f);
            contenido.pivot = new Vector2(0.5f, 1f);
            contenido.anchoredPosition = Vector2.zero;
            var sr = vista.gameObject.AddComponent<ScrollRect>();
            sr.content = contenido; sr.viewport = vista;
            sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.inertia = true; sr.decelerationRate = 0.12f; sr.scrollSensitivity = 30f;
            var cd = vista.gameObject.AddComponent<ContenidoDesplazable>();
            cd.contenido = contenido; cd.scroll = sr; cd.altoCompacto = altoCompacto;
            cd.Recalcular();
            return vista;
        }

        // Tarjeta modal: en escritorio centrada; en móvil ocupa el ancho y, si no cabe en alto, se desplaza
        public static RectTransform ModalDesplazable(Transform padre, Vector2 aMin, Vector2 aMax, float altoCompacto)
        {
            Desplazable("Desplazable", padre, Vector2.zero, Vector2.one, altoCompacto, out var c);
            var card = Tarjeta("Marco", c, aMin, aMax);
            Responsivo.En(card, new Vector2(0.02f, 0.005f), new Vector2(0.98f, 0.995f));
            return card;
        }

        // Fila horizontal de tarjetas (se reparte el ancho en amplio; carrusel deslizable en compacto)
        public static Carrusel CrearCarrusel(string nombre, Transform padre, Vector2 aMin, Vector2 aMax)
        {
            var vista = Panel(nombre, padre, aMin, aMax, new Color(0, 0, 0, 0), 0);
            vista.GetComponent<Image>().raycastTarget = true;
            vista.gameObject.AddComponent<RectMask2D>();
            var go = new GameObject("Contenido", typeof(RectTransform));
            go.transform.SetParent(vista, false);
            var contenido = (RectTransform)go.transform;
            var sr = vista.gameObject.AddComponent<ScrollRect>();
            sr.content = contenido; sr.viewport = vista;
            sr.horizontal = true; sr.vertical = false;
            sr.movementType = ScrollRect.MovementType.Elastic;
            var c = vista.gameObject.AddComponent<Carrusel>();
            c.contenido = contenido; c.scroll = sr;
            return c;
        }

        // radio: -1 = automático (esquinas redondeadas salvo en fondos de pantalla completa), 0 = recto
        public static RectTransform Panel(string nombre, Transform padre, Vector2 aMin, Vector2 aMax, Color color, int radio = -1)
        {
            var go = new GameObject(nombre, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(padre, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = color.a > 0f;
            bool pantallaCompleta = aMin == Vector2.zero && aMax == Vector2.one;
            if (radio < 0) radio = pantallaCompleta ? 0 : Tema.RadioTarjeta;
            Redondear(img, radio);
            return rt;
        }

        public static void Redondear(Image img, int radio)
        {
            if (radio <= 0) { img.sprite = null; img.type = Image.Type.Simple; return; }
            img.sprite = SpriteRedondeado(radio);
            img.type = Image.Type.Sliced;
            img.fillCenter = true;
        }

        // Tarjeta elevada: esquinas redondeadas + sombra suave + borde sutil
        public static RectTransform Tarjeta(string nombre, Transform padre, Vector2 aMin, Vector2 aMax, Color? color = null)
        {
            var rt = Panel(nombre, padre, aMin, aMax, color ?? Tema.Superficie, Tema.RadioTarjeta);
            var sombra = rt.gameObject.AddComponent<Shadow>();
            sombra.effectColor = new Color(0f, 0f, 0f, 0.45f);
            sombra.effectDistance = new Vector2(0f, -4f);
            return rt;
        }

        // Pastilla (badge) con texto: modo, estado, etiquetas
        public static Text Pildora(Transform padre, string nombre, Vector2 aMin, Vector2 aMax, string texto, Color fondo, Color colorTexto, int tam = 15)
        {
            var rt = Panel(nombre, padre, aMin, aMax, fondo, Tema.RadioPildora);
            rt.GetComponent<Image>().raycastTarget = false;
            var t = Texto(rt, "Texto", Vector2.zero, Vector2.one, tam, TextAnchor.MiddleCenter, colorTexto);
            t.rectTransform.offsetMin = new Vector2(8, 0); t.rectTransform.offsetMax = new Vector2(-8, 0);
            t.fontStyle = FontStyle.Bold;
            t.resizeTextForBestFit = true; t.resizeTextMinSize = Mathf.Max(8, tam - 6); t.resizeTextMaxSize = tam;
            t.text = texto;
            return t;
        }

        // Rectángulo redondeado suavizado, generado en memoria y cacheado por radio (9-slice)
        static readonly System.Collections.Generic.Dictionary<int, Sprite> redondeados = new System.Collections.Generic.Dictionary<int, Sprite>();
        public static Sprite SpriteRedondeado(int radio)
        {
            if (redondeados.TryGetValue(radio, out var s) && s != null) return s;
            int n = radio * 2 + 4;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
            };
            var px = new Color32[n * n];
            float r = radio;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // distancia al rectángulo interior (centros de las esquinas)
                    float cx = Mathf.Clamp(x + 0.5f, r, n - r), cy = Mathf.Clamp(y + 0.5f, r, n - r);
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d + 0.5f) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            float b = radio + 1;
            s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            s.hideFlags = HideFlags.HideAndDontSave;
            redondeados[radio] = s;
            return s;
        }

        public static Text Texto(Transform padre, string nombre, Vector2 aMin, Vector2 aMax, int tam, TextAnchor ancla, Color color)
        {
            var go = new GameObject(nombre, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(padre, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = new Vector2(12, 4); rt.offsetMax = new Vector2(-12, -4);
            var t = go.GetComponent<Text>();
            t.font = Fuente; t.fontSize = tam; t.alignment = ancla; t.color = color;
            t.lineSpacing = 1.05f;
            t.raycastTarget = false;
            t.supportRichText = true;
            return t;
        }

        public static Button Boton(Transform padre, string texto, Vector2 aMin, Vector2 aMax, int tam, Color color, UnityAction accion)
        {
            var rt = Panel("Btn " + texto, padre, aMin, aMax, color, Tema.RadioBoton);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = rt.GetComponent<Image>();
            AplicarEstilo(b);
            AreaTactilMinima.Agregar(b);
            if (accion != null) b.onClick.AddListener(accion);
            var t = Texto(rt, "Etiqueta", Vector2.zero, Vector2.one, tam, TextAnchor.MiddleCenter, Color.white);
            t.text = texto;
            return b;
        }

        // Texto escrito por personas usuarias: evita que "<color=...>" altere el texto enriquecido
        public static string SinRich(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "‹").Replace(">", "›");

        // Estados de interacción coherentes en toda la app (hover más claro, presionado más oscuro)
        public static void AplicarEstilo(Button b)
        {
            var c = b.colors;
            c.normalColor = new Color(0.92f, 0.92f, 0.92f, 1f);
            c.highlightedColor = Color.white;
            c.selectedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
            c.pressedColor = new Color(0.72f, 0.74f, 0.78f, 1f);
            c.disabledColor = new Color(0.5f, 0.5f, 0.52f, 0.45f);
            c.fadeDuration = 0.08f;
            b.colors = c;
        }

        public static Text Etiqueta(Button b) => b.GetComponentInChildren<Text>(true);

        // InputField de una línea, con placeholder y opción de contraseña
        public static InputField Campo(Transform padre, string nombre, Vector2 aMin, Vector2 aMax, string placeholder,
                                       bool contrasena = false, int limite = 64, int tam = 24)
        {
            var rt = Panel(nombre, padre, aMin, aMax, ColCampo, Tema.RadioCampo);
            var borde = rt.gameObject.AddComponent<Outline>();
            borde.effectColor = Tema.Borde;
            borde.effectDistance = new Vector2(1f, -1f);
            var campo = rt.gameObject.AddComponent<InputField>();
            campo.targetGraphic = rt.GetComponent<Image>();

            var texto = Texto(rt, "Texto", Vector2.zero, Vector2.one, tam, TextAnchor.MiddleLeft, Color.white);
            texto.supportRichText = false;           // obligatorio en InputField
            texto.horizontalOverflow = HorizontalWrapMode.Overflow;
            var ph = Texto(rt, "Placeholder", Vector2.zero, Vector2.one, tam, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.35f));
            ph.fontStyle = FontStyle.Italic;
            ph.text = placeholder;

            campo.textComponent = texto;
            campo.placeholder = ph;
            campo.lineType = InputField.LineType.SingleLine;
            campo.characterLimit = limite;
            campo.contentType = contrasena ? InputField.ContentType.Password : InputField.ContentType.Standard;
            campo.asteriskChar = '•';
            campo.caretWidth = 2;
            campo.customCaretColor = true;
            campo.caretColor = Color.white;
            campo.selectionColor = new Color(0.25f, 0.45f, 0.75f, 0.6f);
            return campo;
        }

        // ---------------- Íconos de ayuda ----------------
        public static readonly Color ColIconoAviso = new Color(0.55f, 0.33f, 0.05f);

        // Ícono circular con "?" (información) o "!" (advertencia) que muestra un tooltip al pasar el ratón.
        // ancla/pivote/posición en píxeles de referencia: así el círculo nunca se deforma.
        public static TooltipTrigger IconoInfo(Transform padre, string texto, Vector2 ancla, Vector2 pivote, Vector2 posicion,
                                               string simbolo = "?", float tam = 26f, Color? colorReposo = null)
        {
            var go = new GameObject("Info " + simbolo, typeof(RectTransform), typeof(Image), typeof(TooltipTrigger));
            go.transform.SetParent(padre, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = ancla;
            rt.pivot = pivote;
            rt.sizeDelta = new Vector2(tam, tam);
            rt.anchoredPosition = posicion;

            var img = go.GetComponent<Image>();
            img.sprite = SpriteCirculo;
            img.raycastTarget = true;
            var borde = go.AddComponent<Outline>();
            borde.effectColor = new Color(1f, 1f, 1f, 0.18f);
            borde.effectDistance = new Vector2(1f, -1f);

            var t = Texto(rt, "Simbolo", Vector2.zero, Vector2.one, Mathf.RoundToInt(tam * 0.66f), TextAnchor.MiddleCenter, Color.white);
            t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
            t.fontStyle = FontStyle.Bold;
            t.text = simbolo;

            AreaTactilMinima.Agregar(rt);
            var trig = go.GetComponent<TooltipTrigger>();
            trig.Configurar(texto, img, t, colorReposo ?? (simbolo == "!" ? ColIconoAviso : TooltipTrigger.ColNormal));
            return trig;
        }

        // Coloca el ícono justo a la derecha del texto visible de una etiqueta alineada a la izquierda
        public static TooltipTrigger IconoJuntoATexto(Text etiqueta, string texto, string simbolo = "?")
        {
            var trig = IconoInfo(etiqueta.rectTransform, texto, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, simbolo);
            UbicarTrasTexto((RectTransform)trig.transform, etiqueta);
            return trig;
        }

        public static void UbicarTrasTexto(RectTransform icono, Text etiqueta, float separacion = 10f)
        {
            float ancho = etiqueta.rectTransform.rect.width;
            float x = etiqueta.preferredWidth;
            if (ancho > 0f) x = Mathf.Min(x, ancho);
            icono.anchoredPosition = new Vector2(x + separacion, 0f);
        }

        // Círculo suavizado generado en memoria (en una build no hay sprites "Knob" integrados)
        static Sprite circulo;
        public static Sprite SpriteCirculo
        {
            get
            {
                if (circulo != null) return circulo;
                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                var px = new Color32[n * n];
                float c = (n - 1) / 2f, radio = n / 2f - 1f;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(radio - d + 0.5f) * 255f));
                    }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                circulo = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
                circulo.hideFlags = HideFlags.HideAndDontSave;
                return circulo;
            }
        }

        public static void Enfocar(Selectable s)
        {
            if (s == null || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(s.gameObject);
            if (s is InputField f) f.ActivateInputField();
        }

        public static void Destruir(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Object.Destroy(go); else Object.DestroyImmediate(go);
        }
    }
}
