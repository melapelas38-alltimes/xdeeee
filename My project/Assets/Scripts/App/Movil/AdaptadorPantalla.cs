using System;
using UnityEngine;
using UnityEngine.UI;

namespace Composta.App
{
    // Decide cómo se dibuja la interfaz según el dispositivo y la orientación:
    //  · Escritorio: CanvasScaler "Scale With Screen Size" 1920×1080 → diseño AMPLIO (el de siempre).
    //  · Móvil: escala física (1 unidad ≈ 0,78 dp, texto de 18 u ≈ 14 sp). Si el lienzo resultante es
    //    estrecho o bajo (teléfono vertical u horizontal), se usa el diseño COMPACTO: tarjetas apiladas,
    //    desplazamiento vertical y carruseles. Una tablet en horizontal sigue en diseño AMPLIO.
    public sealed class AdaptadorPantalla : MonoBehaviour
    {
        public const float DpPorUnidad = 0.78f;
        public const float UnidadesPorDp = 1f / DpPorUnidad;
        public const float ToqueMinimoDp = 48f;                     // pauta de Material Design / Android
        public static float ToqueMinimoUnidades => EsMovil ? ToqueMinimoDp * UnidadesPorDp : 0f;
        const float AnchoMinimoAmplio = 1400f, AltoMinimoAmplio = 800f;

        public static event Action AlCambiar;
        public static bool EsMovil { get; private set; }
        public static bool Compacto { get; private set; }
        public static float EscalaMovil { get; private set; } = 1f;
        public static Vector2 TamanoLienzo { get; private set; } = new Vector2(1920, 1080);

        // Para probar el diseño compacto en el Editor: F9 alterna (o Device Simulator, que simula móvil)
        public static bool? Forzado;

        static AdaptadorPantalla instancia;
        int ancho, alto;
        Camera camaraPintada;

        // Fuera del área segura (barra de estado, notch) se ve el fondo de la cámara: debe ser el del tema
        void PintarCamara(Camera cam)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Tema.Fondo;
            camaraPintada = cam;
        }
        Rect seguro;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Arrancar()
        {
            if (instancia != null) return;
            var go = new GameObject("[AdaptadorPantalla]");
            DontDestroyOnLoad(go);
            instancia = go.AddComponent<AdaptadorPantalla>();
            instancia.Recalcular(true);

            if (Application.isMobilePlatform)
            {
                // Vertical y horizontal (sin "boca abajo"); el SO decide según el sensor
                Screen.autorotateToPortrait = true;
                Screen.autorotateToLandscapeLeft = true;
                Screen.autorotateToLandscapeRight = true;
                Screen.autorotateToPortraitUpsideDown = false;
                Screen.orientation = ScreenOrientation.AutoRotation;
                Application.targetFrameRate = 60;
                Screen.sleepTimeout = SleepTimeout.SystemSetting;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ReiniciarEstatico() { AlCambiar = null; instancia = null; Forzado = null; }

        void Update()
        {
#if UNITY_EDITOR
            var k = UnityEngine.InputSystem.Keyboard.current;
            if (k != null && k.f9Key.wasPressedThisFrame) { Forzado = !(Forzado ?? Compacto); Recalcular(true); return; }
#endif
            if (Screen.width != ancho || Screen.height != alto || Screen.safeArea != seguro) Recalcular(false);
            // La cámara de la escena aparece después del arranque: se pinta con el fondo del tema una sola vez
            var cam = Camera.main;
            if (cam != null && cam != camaraPintada) PintarCamara(cam);
        }

        void Recalcular(bool forzarEvento)
        {
            ancho = Screen.width; alto = Screen.height; seguro = Screen.safeArea;
            EsMovil = Application.isMobilePlatform || Forzado == true;

            float dpi = Screen.dpi > 1f ? Screen.dpi : 160f * Mathf.Max(1f, Mathf.Min(ancho, alto) / 540f);
            EscalaMovil = Mathf.Clamp(dpi / 160f * DpPorUnidad, 0.75f, 5f);
            if (Forzado == true && !Application.isMobilePlatform) EscalaMovil = Mathf.Max(1f, Mathf.Min(ancho, alto) / 600f);

            if (EsMovil)
            {
                TamanoLienzo = new Vector2(ancho / EscalaMovil, alto / EscalaMovil);
                Compacto = Forzado ?? (TamanoLienzo.x < AnchoMinimoAmplio || TamanoLienzo.y < AltoMinimoAmplio);
            }
            else
            {
                TamanoLienzo = new Vector2(1920, 1080);
                Compacto = Forzado ?? false;
            }

            // El fondo de la cámara cubre las franjas fuera del área segura (notch, barra de navegación)
            var cam = Camera.main;
            if (cam != null) PintarCamara(cam);

            AlCambiar?.Invoke();       // cualquier cambio de tamaño, orientación o área segura
        }

        // Configura un CanvasScaler según el dispositivo (lo llama EscaladorAdaptable)
        public static void Configurar(CanvasScaler sc)
        {
            if (EsMovil)
            {
                sc.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                sc.scaleFactor = EscalaMovil;
            }
            else
            {
                sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                sc.referenceResolution = new Vector2(1920, 1080);
                sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                sc.matchWidthOrHeight = 0.5f;
            }
        }
    }

    [RequireComponent(typeof(CanvasScaler))]
    public sealed class EscaladorAdaptable : MonoBehaviour
    {
        CanvasScaler sc;
        void Awake() => sc = GetComponent<CanvasScaler>();
        void OnEnable() { AdaptadorPantalla.AlCambiar += Aplicar; Aplicar(); }
        void OnDisable() => AdaptadorPantalla.AlCambiar -= Aplicar;
        void Aplicar() { if (sc != null) AdaptadorPantalla.Configurar(sc); }
    }

    // Ajusta un RectTransform a Screen.safeArea (notch / perforación de cámara / barra de navegación)
    [RequireComponent(typeof(RectTransform))]
    public sealed class AreaSegura : MonoBehaviour
    {
        void OnEnable() { AdaptadorPantalla.AlCambiar += Aplicar; Aplicar(); }
        void OnDisable() => AdaptadorPantalla.AlCambiar -= Aplicar;

        void Aplicar()
        {
            var rt = (RectTransform)transform;
            var s = Screen.safeArea;
            if (Screen.width <= 0 || Screen.height <= 0) return;
            rt.anchorMin = new Vector2(s.xMin / Screen.width, s.yMin / Screen.height);
            rt.anchorMax = new Vector2(s.xMax / Screen.width, s.yMax / Screen.height);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
