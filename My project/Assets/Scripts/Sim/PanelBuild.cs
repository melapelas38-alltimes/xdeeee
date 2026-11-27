using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Composta.App;
using Composta.Core;
using Composta.Core.Seguridad;

// Ventana "Nueva composta". Dos caminos claramente separados:
//  · SIMULACIÓN (azul): parámetros con sliders → crea un lote que el motor simula hora a hora.
//  · CONTROL MANUAL (ámbar): solo tipo + 2 preguntas fáciles → crea un lote vacío, sin simulación,
//    que se llena con mediciones reales (lo gestiona la capa de aplicación vía PedirLoteManual).
public class PanelBuild : MonoBehaviour
{
    public SimuladorComposta simulador;

    [Header("Iconos (opcional). Orden: Doméstica, Cerrada, Pila abierta, Industrial, Vermicomposta")]
    public Sprite[] iconosBuild = new Sprite[5];
    public Sprite iconoPesoExagerado;

    public bool abrirAlIniciar = true;

    // La capa de aplicación crea el lote manual (no pasa por el simulador)
    public event Action<PerfilComposta> PedirLoteManual;
    // Pronóstico aislado con los parámetros del formulario (ventana Predicción)
    public event Action<PerfilComposta> PedirPrediccion;

    // ---- Estado del formulario ----
    TipoComposta tipoSel;
    ModoLote modoSel = ModoLote.Simular;
    float peso, cn, humIni, aisl, amb;
    bool pausaPrevia, permitirCrear = true, hudVisible = true;

    // ---- Vista ----
    Canvas canvasBuild;
    Transform raiz;
    GameObject ventana, insigniaHud, insigniaVentana, seccionSimulacion, seccionManual;
    Button btnCancelar, btnNueva, btnIniciar;
    Text txtResumen, txtDescripcionModo, txtSeccionParametros;
    readonly Image[] fondoModo = new Image[2];
    readonly Text[] textoModo = new Text[2];
    readonly Image[] fondoCards = new Image[5];
    readonly Outline[] bordeCards = new Outline[5];
    readonly GameObject[] marcaSeleccion = new GameObject[5];
    readonly Slider[] sliders = new Slider[5];
    readonly RectTransform[] bandas = new RectTransform[5];
    RectTransform bandaAceptable;                         // solo peso: zona ámbar entre extremos y recomendado
    readonly InputField[] campos = new InputField[5];
    readonly List<(Image fondo, Text texto, float valor)> opcionesMaterial = new List<(Image, Text, float)>();
    readonly List<(Image fondo, Text texto, float valor)> opcionesTapada = new List<(Image, Text, float)>();

    static readonly string[] Etiquetas = { "Peso", "Relación C/N", "Humedad inicial", "Aislamiento", "Temp. ambiente" };
    static readonly string[] Unidades = { "kg", ": 1", "%", "×", "°C" };
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly Color[] ColoresTipo =
    {
        new Color(0.30f, 0.75f, 0.60f), new Color(0.36f, 0.58f, 0.95f), new Color(0.85f, 0.62f, 0.32f),
        new Color(0.62f, 0.48f, 0.90f), new Color(0.90f, 0.45f, 0.60f)
    };

    // Simulación usa los rangos guiados (ver RangosParametros en Core)
    Rango RangoDe(int i) => RangosParametros.De(ModoLote.Simular, (Parametro)i);
    float SliderMin(int i) => i == 0 ? Mathf.Log10(RangoDe(0).Min) : RangoDe(i).Min;   // el peso usa escala logarítmica
    float SliderMax(int i) => i == 0 ? Mathf.Log10(RangoDe(0).Max) : RangoDe(i).Max;
    Color Acento => Tema.Acento(modoSel);

    void Start()
    {
        if (simulador == null) simulador = FindAnyObjectByType<SimuladorComposta>();
        if (simulador == null)
        {
            Debug.LogError("PanelBuild: no hay un SimuladorComposta en la escena.");
            enabled = false;
            return;
        }
        UIFabrica.AsegurarEventSystem();
        ConstruirUI();
        // Con control de acceso, la ventana la abre ControlAcceso después del login
        bool hayLogin = FindAnyObjectByType<ControlAcceso>() != null;
        ventana.SetActive(false);
        if (abrirAlIniciar && !hayLogin) Abrir(true);
        btnNueva.interactable = permitirCrear;
        canvasBuild.enabled = hudVisible;
    }

    void OnDestroy() => GuardiaEntrada.Liberar(this);

    // ---------------- API para la capa de aplicación ----------------
    public bool Abierta => ventana != null && ventana.activeSelf;

    public void MostrarHud(bool visible)
    {
        hudVisible = visible;
        if (canvasBuild != null) canvasBuild.enabled = visible;
    }

    // obligatorio = sin botón Cancelar (no hay lote al que volver)
    public void AbrirFormulario(bool obligatorio)
    {
        if (ventana == null || !permitirCrear) return;
        Abrir(obligatorio);
    }

    public void PermitirCrear(bool valor)
    {
        permitirCrear = valor;
        if (btnNueva != null) btnNueva.interactable = valor;
        if (!valor && Abierta) Ocultar();
    }

    // Cierra sin tocar la pausa (la gestiona quien llama, p. ej. al cerrar sesión)
    public void Ocultar()
    {
        if (ventana == null) return;
        ventana.SetActive(false);
        GuardiaEntrada.Liberar(this);
    }

    void Update()
    {
        if (simulador == null || simulador.Sistema == null || insigniaHud == null) return;
        insigniaHud.SetActive(!ventana.activeSelf && simulador.MotorActivo && simulador.Sistema.Perfil.PesoExagerado);
    }

    // ---------------- Abrir / cerrar / iniciar ----------------
    void Abrir(bool inicio)
    {
        if (Abierta) return;                            // evita perder la pausa previa al abrir dos veces
        GuardiaEntrada.Bloquear(this);                  // los atajos (Espacio, V...) no actúan detrás del modal
        pausaPrevia = simulador.pausado;
        simulador.pausado = true;
        CargarDesdeSimulador();
        btnCancelar.gameObject.SetActive(!inicio);
        ventana.SetActive(true);
        AplicarModo();
    }

    void Cerrar()
    {
        simulador.pausado = pausaPrevia;
        Ocultar();
    }

    void Iniciar()
    {
        if (!simulador.PuedeHacer(Permiso.CrearLote))
        {
            Notificador.Mostrar("Tu rol no permite crear lotes nuevos.", TipoMensaje.Aviso);
            return;
        }

        if (modoSel == ModoLote.Manual)
        {
            if (PedirLoteManual == null) { Notificador.Mostrar("Inicia sesión para crear un lote de control manual.", TipoMensaje.Aviso); return; }
            var perfil = PerfilComposta.Crear(tipoSel);
            perfil.RelacionCN = cn;
            perfil.Aislamiento = aisl;
            PedirLoteManual.Invoke(perfil);
            return;
        }

        if (PerfilDelFormulario().PesoExagerado)
        {
            Notificador.Mostrar("PESO EXAGERADO para este recipiente. Ajusta el peso antes de iniciar la simulación.", TipoMensaje.Aviso);
            return;
        }
        simulador.modoAutomatico = true;                // la simulación arranca con el control automático
        simulador.tipo = tipoSel;
        simulador.pesoKg = peso;
        simulador.relacionCN = cn;
        simulador.humedadInicial = humIni;
        simulador.aislamiento = aisl;
        simulador.tempAmbiente = amb;
        simulador.permitirPesoExagerado = true;
        simulador.Reiniciar();
        simulador.pausado = false;
        Ocultar();
    }

    void CargarDesdeSimulador()
    {
        var d = PerfilComposta.Crear(simulador.tipo);
        tipoSel = simulador.tipo;
        peso = simulador.pesoKg;
        cn = simulador.relacionCN;
        humIni = simulador.humedadInicial >= 0f ? simulador.humedadInicial : d.HumedadInicial;
        aisl = simulador.aislamiento >= 0f ? simulador.aislamiento : d.Aislamiento;
        amb = simulador.tempAmbiente;
        modoSel = simulador.modo;                       // se propone el modo del último lote
    }

    void ElegirBuild(TipoComposta t)
    {
        var d = PerfilComposta.Crear(t);
        tipoSel = t;
        peso = d.PesoDefecto; cn = d.RelacionCN; humIni = d.HumedadInicial; aisl = d.Aislamiento;
        Refrescar();                                  // la temperatura ambiente se conserva
    }

    PerfilComposta PerfilDelFormulario()
    {
        var p = PerfilComposta.Crear(tipoSel, peso, true);   // true = peso libre
        p.RelacionCN = cn; p.HumedadInicial = humIni; p.Aislamiento = aisl; p.TempAmbiente = amb;
        return p;
    }

    // ---------------- Modo ----------------
    void ElegirModo(ModoLote m)
    {
        if (m == modoSel) return;
        modoSel = m;
        AplicarModo();
    }

    void AplicarModo()
    {
        bool manual = modoSel == ModoLote.Manual;
        for (int i = 0; i < 2; i++)
        {
            bool activo = i == (int)modoSel;
            fondoModo[i].color = activo ? Tema.ConAlfa(Tema.Acento((ModoLote)i), 0.22f) : new Color(0, 0, 0, 0);
            textoModo[i].color = activo ? Tema.Acento((ModoLote)i) : Tema.TextoSecundario;
        }
        seccionSimulacion.SetActive(!manual);
        seccionManual.SetActive(manual);
        txtSeccionParametros.text = manual ? "<b>2 · DATOS DEL LOTE</b>" : "<b>2 · PARÁMETROS DE LA SIMULACIÓN</b>";
        txtDescripcionModo.text = manual
            ? "Sin simulación: el tablero empezará vacío y se llenará con las mediciones que registres cada semana."
            : "El motor simulará temperatura, humedad, oxígeno y fases hora a hora a partir de estos parámetros.";

        btnIniciar.GetComponent<Image>().color = Acento;
        var etiqueta = UIFabrica.Etiqueta(btnIniciar);
        etiqueta.text = manual ? "Crear lote de control manual" : "Iniciar simulación";
        etiqueta.color = manual ? Tema.Fondo : Color.white;
        var ayuda = btnIniciar.GetComponentInChildren<TooltipTrigger>(true);
        if (ayuda != null) ayuda.texto = manual ? AyudaBuild.IniciarManual : AyudaBuild.IniciarSimulacion;
        Refrescar();
    }

    // ---------------- Sliders y valores ----------------
    void AlCambiarSlider(int i, float v)
    {
        switch (i)
        {
            case 0: peso = Redondear(Mathf.Pow(10f, v)); break;
            case 1: cn = Mathf.Round(v); break;
            case 2: humIni = Mathf.Round(v); break;
            case 3: aisl = Mathf.Round(v * 20f) / 20f; break;
            default: amb = Mathf.Round(v); break;
        }
        Refrescar();
    }

    void Paso(int i, int dir)
    {
        switch (i)
        {
            case 0: peso = Redondear(peso + dir * PasoPeso(peso)); break;
            case 1: cn += dir; break;
            case 2: humIni += dir; break;
            case 3: aisl += dir * 0.05f; break;
            default: amb += dir; break;
        }
        Refrescar();
    }

    // Valor escrito en la pastilla: acepta coma o punto y se limita al rango de la simulación
    void AlEditarCampo(int i, string texto)
    {
        if (!RangosParametros.IntentarLeer(texto, out float v))
        {
            Notificador.Mostrar($"«{texto}» no es un número válido para {Etiquetas[i]}.", TipoMensaje.Aviso);
            Refrescar();
            return;
        }
        var r = RangoDe(i);
        if (!r.Contiene(v))
        {
            Notificador.Mostrar($"{Etiquetas[i]} se limitó a {r.Min.ToString("0.##", Inv)} – {r.Max.ToString("0.##", Inv)}.", TipoMensaje.Info);
            v = r.Limitar(v);
        }
        switch (i)
        {
            case 0: peso = v; break;
            case 1: cn = v; break;
            case 2: humIni = v; break;
            case 3: aisl = v; break;
            default: amb = v; break;
        }
        Refrescar();
    }

    static float PasoPeso(float p) => p < 10f ? 0.5f : p < 100f ? 1f : p < 1000f ? 5f : p < 10000f ? 50f : 500f;

    static float Redondear(float p)
    {
        if (p < 10f) return Mathf.Round(p * 10f) / 10f;
        if (p < 100f) return Mathf.Round(p);
        if (p < 1000f) return Mathf.Round(p / 5f) * 5f;
        if (p < 10000f) return Mathf.Round(p / 50f) * 50f;
        return Mathf.Round(p / 500f) * 500f;
    }

    void ElegirOpcion(bool material, float valor)
    {
        if (material) cn = valor; else aisl = valor;
        Refrescar();
    }

    void Refrescar()
    {
        if (ventana == null) return;
        peso = RangoDe(0).Limitar(peso);
        cn = RangosParametros.De(ModoLote.Manual, Parametro.RelacionCN).Limitar(cn);
        humIni = RangoDe(2).Limitar(humIni);
        aisl = RangosParametros.De(ModoLote.Manual, Parametro.Aislamiento).Limitar(aisl);
        amb = RangoDe(4).Limitar(amb);
        if (modoSel == ModoLote.Simular) { cn = RangoDe(1).Limitar(cn); aisl = RangoDe(3).Limitar(aisl); }

        var p = PerfilDelFormulario();
        var m = new ModeloComposta(p, 1);

        // Tarjetas de tipo
        var tipos = (TipoComposta[])Enum.GetValues(typeof(TipoComposta));
        for (int i = 0; i < fondoCards.Length; i++)
        {
            bool sel = tipos[i] == tipoSel;
            fondoCards[i].color = sel ? Color.Lerp(Tema.SuperficieAlta, Acento, 0.12f) : Tema.SuperficieAlta;
            bordeCards[i].effectColor = sel ? Acento : new Color(0, 0, 0, 0);
            marcaSeleccion[i].SetActive(sel);
        }

        // Sliders y valores
        float[] valores = { peso, cn, humIni, aisl, amb };
        for (int i = 0; i < 5; i++)
        {
            sliders[i].SetValueWithoutNotify(i == 0 ? Mathf.Log10(peso) : valores[i]);
            if (!campos[i].isFocused) campos[i].SetTextWithoutNotify(Formato(i, valores[i]));
            var rec = RangosParametros.Recomendado(p, (Parametro)i);
            if (rec.HasValue)
            {
                var r = rec.Value;
                if (i == 0) Banda(bandas[0], 0, Mathf.Log10(r.Min), Mathf.Log10(r.Max)); else Banda(bandas[i], i, r.Min, r.Max);
            }
            else Banda(bandas[i], i, 0f, 0f);
        }
        Banda(bandaAceptable, 0, Mathf.Log10(Mathf.Max(p.ExtremoMin, RangoDe(0).Min)), Mathf.Log10(Mathf.Min(p.ExtremoMax, RangoDe(0).Max)));

        // Preguntas del modo manual
        foreach (var (img, txt, v) in opcionesMaterial) Opcion(img, txt, Mathf.Abs(cn - v) < 0.01f);
        foreach (var (img, txt, v) in opcionesTapada) Opcion(img, txt, Mathf.Abs(aisl - v) < 0.01f);

        // Resumen: tres niveles de peso (óptimo / consejo / exagerado)
        bool manual = modoSel == ModoLote.Manual;
        var nivel = p.NivelPesoActual;
        bool exagerado = !manual && nivel == NivelPeso.Exagerado;
        string verde = Tema.HexDe(Tema.Exito), ambar = Tema.HexDe(Tema.Aviso), rojo = Tema.HexDe(Tema.Critico), tenue = Tema.HexDe(Tema.TextoTenue);
        if (manual)
        {
            txtResumen.text = $"<b>{p.Nombre}</b>  <color={tenue}>·</color>  peso recomendado {p.PesoMin:0.#}–{p.PesoMax:0.#} kg  " +
                              $"<color={tenue}>·</color>  el peso real lo registrarás en la primera medición";
        }
        else
        {
            string fase = m.FaseCalientePosible
                ? $"fase caliente ~{m.TempPlateau:F0} °C"
                : $"<color={ambar}>no alcanza fase caliente (máx. ~{m.TempPlateau:F0} °C)</color>";
            string pesoTxt = nivel == NivelPeso.Optimo
                ? $"<color={verde}>● Peso dentro del rango recomendado ({p.PesoMin:0.#}–{p.PesoMax:0.#} kg)</color>"
                : nivel == NivelPeso.FueraDeOptimo
                    ? $"<color={ambar}>● Consejo: funciona, pero el rango ideal es {p.PesoMin:0.#}–{p.PesoMax:0.#} kg</color>"
                    : $"<color={rojo}><b>● PESO EXAGERADO</b>: límites físicos {p.ExtremoMin:0.#}–{p.ExtremoMax:0.#} kg</color>";
            txtResumen.text = $"{pesoTxt}  <color={tenue}>·</color>  {fase}";
        }
        insigniaVentana.SetActive(exagerado);
        btnIniciar.interactable = !exagerado;
    }

    static string Formato(int i, float v)
        => i == 3 ? v.ToString("0.00", Inv) : i == 0 && v < 10f ? v.ToString("0.#", Inv) : v.ToString("0.#", Inv);

    static void Opcion(Image img, Text txt, bool sel)
    {
        img.color = sel ? Tema.ConAlfa(Tema.AcentoManual, 0.22f) : Tema.SuperficieAlta;
        txt.color = sel ? Tema.AcentoManual : Tema.TextoSecundario;
        var borde = img.GetComponent<Outline>();
        if (borde != null) borde.effectColor = sel ? Tema.AcentoManual : new Color(0, 0, 0, 0);
    }

    void Banda(RectTransform banda, int i, float a, float b)
    {
        float t0 = Mathf.InverseLerp(SliderMin(i), SliderMax(i), a);
        float t1 = Mathf.InverseLerp(SliderMin(i), SliderMax(i), b);
        banda.anchorMin = new Vector2(t0, 0f);
        banda.anchorMax = new Vector2(t1, 1f);
    }

    // ---------------- Construcción de la UI ----------------
    void ConstruirUI()
    {
        var rc = UIFabrica.Canvas("Canvas Build", null, 25);     // encima del tablero y de la franja de sesión (20)
        canvasBuild = rc.GetComponentInParent<Canvas>();
        raiz = rc;

        // --- HUD permanente (esquina superior derecha; en móvil, segunda fila bajo la barra) ---
        btnNueva = UIFabrica.Boton(raiz, "+  Nueva composta", new Vector2(0.835f, 0.943f), new Vector2(0.985f, 0.982f), 18,
                                   Tema.BotonNeutro, () => Abrir(false));
        Responsivo.En(btnNueva, new Vector2(0.62f, 1f), new Vector2(1f, 1f), new Vector2(4, -128), new Vector2(-12, -72));
        insigniaHud = Insignia(raiz, new Vector2(0.66f, 0.943f), new Vector2(0.825f, 0.982f));
        Responsivo.OcultarEnCompacto(insigniaHud.transform);   // en móvil el aviso aparece en las alertas
        insigniaHud.SetActive(false);

        // --- Ventana (en móvil ocupa toda el área segura) ---
        ventana = UIFabrica.Panel("Ventana", raiz, Vector2.zero, Vector2.one, Tema.Velo).gameObject;
        var w = UIFabrica.Tarjeta("Marco", ventana.transform, new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.95f));
        Responsivo.En(w, Vector2.zero, Vector2.one, new Vector2(6, 6), new Vector2(-6, -6));

        // Cuerpo desplazable. Y(y): fracción del marco (diseño amplio) → fracción del cuerpo
        var vista = UIFabrica.Desplazable("Cuerpo", w, new Vector2(0f, 0.095f), new Vector2(1f, 0.995f), 2100f, out var c);
        Responsivo.En(vista, Vector2.zero, Vector2.one, new Vector2(0, 96), new Vector2(0, -6));
        float Y(float y) => (y - 0.095f) / 0.90f;

        // Cabecera: título + selector de modo segmentado + insignia de peso
        var titulo = UIFabrica.Texto(c, "Titulo", new Vector2(0.015f, Y(0.905f)), new Vector2(0.30f, Y(0.985f)), Tema.TxtTitulo, TextAnchor.MiddleLeft, Tema.TextoPrimario);
        titulo.text = "<b>Nueva composta</b>";
        Responsivo.En(titulo, new Vector2(0.03f, 0.962f), new Vector2(0.97f, 0.995f));
        var selector = UIFabrica.Panel("Selector", c, new Vector2(0.30f, Y(0.912f)), new Vector2(0.63f, Y(0.978f)), Tema.SuperficieCampo, Tema.RadioBoton);
        Responsivo.En(selector, new Vector2(0.03f, 0.925f), new Vector2(0.86f, 0.958f));
        var modos = new[] { ModoLote.Simular, ModoLote.Manual };
        var nombres = new[] { "Simulación", "Control manual" };
        for (int i = 0; i < 2; i++)
        {
            var m = modos[i];
            var b = UIFabrica.Boton(selector, nombres[i], new Vector2(0.01f + i * 0.495f, 0.08f), new Vector2(0.495f + i * 0.495f, 0.92f), 20,
                                    new Color(0, 0, 0, 0), () => ElegirModo(m));
            fondoModo[i] = b.GetComponent<Image>();
            fondoModo[i].raycastTarget = true;
            textoModo[i] = UIFabrica.Etiqueta(b);
            textoModo[i].fontStyle = FontStyle.Bold;
            textoModo[i].resizeTextForBestFit = true; textoModo[i].resizeTextMinSize = 12; textoModo[i].resizeTextMaxSize = 20;
        }
        var ayudaModo = UIFabrica.IconoInfo(c, AyudaBuild.Modo, new Vector2(0.635f, Y(0.945f)), new Vector2(0f, 0.5f), Vector2.zero);
        Responsivo.En(ayudaModo, new Vector2(0.88f, 0.9415f), new Vector2(0.88f, 0.9415f), new Vector2(0f, -13f), new Vector2(26f, 13f));
        insigniaVentana = Insignia(c, new Vector2(0.72f, Y(0.915f)), new Vector2(0.985f, Y(0.975f)));
        Responsivo.En(insigniaVentana.transform, new Vector2(0.03f, 0.862f), new Vector2(0.75f, 0.884f));
        txtDescripcionModo = UIFabrica.Texto(c, "DescripcionModo", new Vector2(0.015f, Y(0.86f)), new Vector2(0.985f, Y(0.905f)), 17, TextAnchor.MiddleLeft, Tema.TextoSecundario);
        txtDescripcionModo.resizeTextForBestFit = true; txtDescripcionModo.resizeTextMinSize = 11; txtDescripcionModo.resizeTextMaxSize = 17;
        Responsivo.En(txtDescripcionModo, new Vector2(0.03f, 0.887f), new Vector2(0.97f, 0.922f));

        // 1 · Tipo de compostera (fila repartida en escritorio; carrusel deslizable en móvil)
        var s1 = UIFabrica.Texto(c, "Seccion1", new Vector2(0.015f, Y(0.815f)), new Vector2(0.6f, Y(0.855f)), 15, TextAnchor.MiddleLeft, Tema.TextoTenue);
        s1.text = "<b>1 · TIPO DE COMPOSTERA</b>";
        Responsivo.En(s1, new Vector2(0.03f, 0.84f), new Vector2(0.97f, 0.86f));
        var carrusel = UIFabrica.CrearCarrusel("Tipos", c, new Vector2(0.015f, Y(0.575f)), new Vector2(0.985f, Y(0.81f)));
        Responsivo.En(carrusel, new Vector2(0.02f, 0.665f), new Vector2(0.98f, 0.835f));
        var tipos = (TipoComposta[])Enum.GetValues(typeof(TipoComposta));
        for (int i = 0; i < tipos.Length; i++) carrusel.Tarjetas.Add(ConstruirTarjetaTipo(carrusel.contenido, i, tipos[i]));
        carrusel.Recalcular();

        // 2 · Parámetros / datos del lote
        txtSeccionParametros = UIFabrica.Texto(c, "Seccion2", new Vector2(0.015f, Y(0.525f)), new Vector2(0.6f, Y(0.565f)), 15, TextAnchor.MiddleLeft, Tema.TextoTenue);
        Responsivo.En(txtSeccionParametros, new Vector2(0.03f, 0.635f), new Vector2(0.97f, 0.655f));
        var sim = UIFabrica.Panel("Simulacion", c, new Vector2(0f, Y(0.14f)), new Vector2(1f, Y(0.525f)), new Color(0, 0, 0, 0));
        Responsivo.En(sim, new Vector2(0f, 0.075f), new Vector2(1f, 0.63f));
        seccionSimulacion = sim.gameObject;
        for (int i = 0; i < 5; i++) ConstruirFilaParametro(seccionSimulacion.transform, i);
        var man = UIFabrica.Panel("Manual", c, new Vector2(0f, Y(0.14f)), new Vector2(1f, Y(0.525f)), new Color(0, 0, 0, 0));
        Responsivo.En(man, new Vector2(0f, 0.075f), new Vector2(1f, 0.63f));
        seccionManual = man.gameObject;
        ConstruirSeccionManual(seccionManual.transform);

        // Resumen
        txtResumen = UIFabrica.Texto(c, "Resumen", new Vector2(0.015f, Y(0.095f)), new Vector2(0.985f, Y(0.14f)), 17, TextAnchor.MiddleLeft, Tema.TextoPrimario);
        txtResumen.resizeTextForBestFit = true; txtResumen.resizeTextMinSize = 11; txtResumen.resizeTextMaxSize = 17;
        Responsivo.En(txtResumen, new Vector2(0.03f, 0.005f), new Vector2(0.97f, 0.07f));
        UIFabrica.IconoInfo(txtResumen.rectTransform, AyudaBuild.Resumen, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero);

        // Pie fijo: Cancelar / Predicción / Iniciar (en móvil, tres botones de 74 u de alto)
        btnCancelar = UIFabrica.Boton(w, "Cancelar", new Vector2(0.015f, 0.02f), new Vector2(0.20f, 0.085f), 20, Tema.BotonNeutro, Cerrar);
        Responsivo.En(btnCancelar, new Vector2(0f, 0f), new Vector2(0.30f, 0f), new Vector2(10, 10), new Vector2(-5, 84));
        var ayudaCancelar = UIFabrica.IconoInfo(btnCancelar.transform, AyudaBuild.Cancelar, new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f));
        Responsivo.OcultarEnCompacto(ayudaCancelar);

        var btnPrediccion = UIFabrica.Boton(w, "Predicción", new Vector2(0.38f, 0.02f), new Vector2(0.60f, 0.085f), 20,
                                            Tema.ConAlfa(Tema.AcentoPrediccion, 0.9f), PedirPronostico);
        UIFabrica.Etiqueta(btnPrediccion).fontStyle = FontStyle.Bold;
        Responsivo.En(btnPrediccion, new Vector2(0.30f, 0f), new Vector2(0.60f, 0f), new Vector2(5, 10), new Vector2(-5, 84));
        var ayudaPred = UIFabrica.IconoInfo(btnPrediccion.transform, AyudaBuild.Prediccion, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f));
        Responsivo.OcultarEnCompacto(ayudaPred);

        btnIniciar = UIFabrica.Boton(w, "Iniciar simulación", new Vector2(0.62f, 0.02f), new Vector2(0.985f, 0.085f), 22, Tema.AcentoSimulacion, Iniciar);
        var etIniciar = UIFabrica.Etiqueta(btnIniciar);
        etIniciar.fontStyle = FontStyle.Bold;
        etIniciar.resizeTextForBestFit = true; etIniciar.resizeTextMinSize = 12; etIniciar.resizeTextMaxSize = 22;
        Responsivo.En(btnIniciar, new Vector2(0.60f, 0f), new Vector2(1f, 0f), new Vector2(5, 10), new Vector2(-10, 84));
        var ayudaIniciar = UIFabrica.IconoInfo(btnIniciar.transform, AyudaBuild.IniciarSimulacion, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f));
        Responsivo.OcultarEnCompacto(ayudaIniciar);
    }

    // Pronóstico con los parámetros actuales del formulario (no crea ni modifica ningún lote)
    void PedirPronostico()
    {
        if (PedirPrediccion == null) { Notificador.Mostrar("Inicia sesión para ver la predicción.", TipoMensaje.Aviso); return; }
        PerfilComposta p;
        if (modoSel == ModoLote.Manual)
        {
            p = PerfilComposta.Crear(tipoSel);
            p.RelacionCN = cn; p.Aislamiento = aisl;
        }
        else
        {
            p = PerfilDelFormulario();
            if (p.PesoExagerado) { Notificador.Mostrar("PESO EXAGERADO: ajusta el peso antes de pedir una predicción.", TipoMensaje.Aviso); return; }
        }
        PedirPrediccion.Invoke(p);
    }

    // El tamaño y la posición de la tarjeta los decide el Carrusel
    RectTransform ConstruirTarjetaTipo(RectTransform padre, int i, TipoComposta t)
    {
        var d = PerfilComposta.Crear(t);
        var card = UIFabrica.Panel("Tipo " + d.Nombre, padre, Vector2.zero, new Vector2(0f, 1f), Tema.SuperficieAlta, Tema.RadioTarjeta);
        fondoCards[i] = card.GetComponent<Image>();
        bordeCards[i] = card.gameObject.AddComponent<Outline>();
        bordeCards[i].effectDistance = new Vector2(2f, -2f);
        bordeCards[i].effectColor = new Color(0, 0, 0, 0);
        var b = card.gameObject.AddComponent<Button>();
        b.targetGraphic = fondoCards[i];
        UIFabrica.AplicarEstilo(b);
        b.onClick.AddListener(() => ElegirBuild(t));

        // Insignia: ícono propio si se asignó en el Inspector; si no, monograma de color
        var insignia = UIFabrica.Panel("Insignia", card, new Vector2(0f, 1f), new Vector2(0f, 1f), Tema.ConAlfa(ColoresTipo[i], 0.20f), 0);
        insignia.pivot = new Vector2(0f, 1f);
        insignia.sizeDelta = new Vector2(52, 52);
        insignia.anchoredPosition = new Vector2(14, -14);
        var imgInsignia = insignia.GetComponent<Image>();
        imgInsignia.sprite = UIFabrica.SpriteCirculo;
        imgInsignia.raycastTarget = false;
        var sprite = i < iconosBuild.Length ? iconosBuild[i] : null;
        if (sprite != null)
        {
            var ic = UIFabrica.Panel("Icono", insignia, new Vector2(0.15f, 0.15f), new Vector2(0.85f, 0.85f), Color.white, 0).GetComponent<Image>();
            ic.sprite = sprite; ic.preserveAspect = true; ic.raycastTarget = false;
        }
        else
        {
            var mono = UIFabrica.Texto(insignia, "Monograma", Vector2.zero, Vector2.one, 19, TextAnchor.MiddleCenter, ColoresTipo[i]);
            mono.rectTransform.offsetMin = mono.rectTransform.offsetMax = Vector2.zero;
            mono.fontStyle = FontStyle.Bold;
            mono.text = AyudaBuild.Monograma(t);
        }
        UIFabrica.IconoInfo(card, AyudaBuild.Tipo(d), Vector2.one, Vector2.one, new Vector2(-10f, -10f));

        UIFabrica.Texto(card, "Nombre", new Vector2(0.04f, 0.36f), new Vector2(0.98f, 0.58f), 18, TextAnchor.MiddleLeft, Tema.TextoPrimario)
            .text = $"<b>{d.Nombre}</b>";
        UIFabrica.Texto(card, "Rango", new Vector2(0.04f, 0.20f), new Vector2(0.98f, 0.38f), 14, TextAnchor.MiddleLeft, Tema.TextoSecundario)
            .text = $"Recomendado <b>{d.PesoMin:0.#}–{d.PesoMax:0.#} kg</b>";
        UIFabrica.Texto(card, "Limites", new Vector2(0.04f, 0.04f), new Vector2(0.98f, 0.21f), 12, TextAnchor.MiddleLeft, Tema.TextoTenue)
            .text = $"Límites físicos {d.ExtremoMin:0.#}–{d.ExtremoMax:0.#} kg";

        var marca = UIFabrica.Pildora(card, "Seleccionada", new Vector2(0.50f, 0.62f), new Vector2(0.96f, 0.76f), "SELECCIONADA",
                                      Tema.ConAlfa(Tema.TextoPrimario, 0.10f), Tema.TextoPrimario, 11);
        marcaSeleccion[i] = marca.transform.parent.gameObject;
        return card;
    }

    void ConstruirFilaParametro(Transform padre, int i)
    {
        int idx = i;
        float top = 0.98f - i * 0.195f, bot = top - 0.17f;
        // Móvil: cada parámetro en dos líneas (etiqueta arriba; control, valor y −/+ abajo)
        float topC = 0.995f - i * 0.198f, botC = topC - 0.188f, h = topC - botC;
        float yEtq = botC + 0.55f * h, yCtl = botC + 0.45f * h;

        var etiqueta = UIFabrica.Texto(padre, "Etiqueta", new Vector2(0.015f, bot), new Vector2(0.21f, top), 19, TextAnchor.MiddleLeft, Tema.TextoPrimario);
        etiqueta.text = Etiquetas[i];
        UIFabrica.IconoJuntoATexto(etiqueta, AyudaBuild.Parametros[i]);
        Responsivo.En(etiqueta, new Vector2(0.03f, yEtq), new Vector2(0.97f, topC));

        sliders[i] = CrearSlider(padre, new Vector2(0.22f, bot), new Vector2(0.70f, top), SliderMin(i), SliderMax(i),
                                 v => AlCambiarSlider(idx, v), out bandas[i], i == 0 ? (RectTransform r) => bandaAceptable = r : (Action<RectTransform>)null);
        Responsivo.En(sliders[i], new Vector2(0.03f, botC), new Vector2(0.47f, yCtl));

        // Lectura numérica editable (pastilla)
        campos[i] = UIFabrica.Campo(padre, "Valor " + Etiquetas[i], new Vector2(0.72f, bot + 0.02f), new Vector2(0.835f, top - 0.02f), "valor", false, 10, 20);
        campos[i].textComponent.alignment = TextAnchor.MiddleRight;
        campos[i].onEndEdit.AddListener(t => AlEditarCampo(idx, t));
        Responsivo.En(campos[i], new Vector2(0.49f, botC + 0.04f * h), new Vector2(0.68f, botC + 0.43f * h));
        var unidad = UIFabrica.Texto(padre, "Unidad", new Vector2(0.835f, bot), new Vector2(0.88f, top), 17, TextAnchor.MiddleLeft, Tema.TextoSecundario);
        unidad.text = Unidades[i];
        Responsivo.En(unidad, new Vector2(0.68f, botC), new Vector2(0.76f, yCtl));

        var menos = UIFabrica.Boton(padre, "−", new Vector2(0.885f, bot + 0.02f), new Vector2(0.93f, top - 0.02f), 24, Tema.BotonNeutro, () => Paso(idx, -1));
        Responsivo.En(menos, new Vector2(0.765f, botC + 0.03f * h), new Vector2(0.87f, botC + 0.44f * h));
        var mas = UIFabrica.Boton(padre, "+", new Vector2(0.94f, bot + 0.02f), new Vector2(0.985f, top - 0.02f), 24, Tema.BotonNeutro, () => Paso(idx, +1));
        Responsivo.En(mas, new Vector2(0.88f, botC + 0.03f * h), new Vector2(0.985f, botC + 0.44f * h));
    }

    void ConstruirSeccionManual(Transform padre)
    {
        // Preguntas fáciles (izquierda en escritorio; arriba en móvil)
        Pregunta(padre, "¿Qué material predomina?", AyudaBuild.AyudaMaterial, 0.98f, 0.99f, AyudaBuild.OpcionesMaterial(), true, opcionesMaterial);
        Pregunta(padre, "¿Está tapada o cubierta?", AyudaBuild.AyudaTapada, 0.55f, 0.72f, AyudaBuild.OpcionesTapada(), false, opcionesTapada);

        // Cómo funciona (derecha en escritorio; abajo en móvil)
        var info = UIFabrica.Panel("Info", padre, new Vector2(0.60f, 0.04f), new Vector2(0.985f, 0.98f), Tema.SuperficieAlta, Tema.RadioTarjeta);
        Responsivo.En(info, new Vector2(0.03f, 0.02f), new Vector2(0.97f, 0.46f));
        var franja = UIFabrica.Panel("Acento", info, Vector2.zero, new Vector2(0f, 1f), Tema.AcentoManual, 3);
        franja.offsetMax = new Vector2(5f, 0f);
        string a = Tema.HexDe(Tema.AcentoManual);
        var t = UIFabrica.Texto(info, "Texto", new Vector2(0.05f, 0.04f), new Vector2(0.97f, 0.96f), 16, TextAnchor.UpperLeft, Tema.TextoSecundario);
        t.text = $"<color={a}><b>CÓMO FUNCIONA EL CONTROL MANUAL</b></color>\n\n" +
                 "No hay sensores ni simulación. El tablero empezará en <b>\"Esperando primer registro\"</b>.\n\n" +
                 $"<color={a}><b>1</b></color>  Crea el lote.\n" +
                 $"<color={a}><b>2</b></color>  Registra temperatura, humedad y peso medidos en la pila.\n" +
                 $"<color={a}><b>3</b></color>  Repite cada 7 días: verás tendencias, la fase estimada y qué hacer.";
        t.resizeTextForBestFit = true; t.resizeTextMinSize = 11; t.resizeTextMaxSize = 16;
    }

    void Pregunta(Transform padre, string titulo, string ayuda, float top, float topCompacto, (string texto, float valor)[] ops, bool material,
                  List<(Image, Text, float)> destino)
    {
        var lbl = UIFabrica.Texto(padre, "Pregunta", new Vector2(0.015f, top - 0.12f), new Vector2(0.58f, top), 19, TextAnchor.MiddleLeft, Tema.TextoPrimario);
        lbl.text = "<b>" + titulo + "</b>";
        UIFabrica.IconoJuntoATexto(lbl, ayuda);
        Responsivo.En(lbl, new Vector2(0.03f, topCompacto - 0.08f), new Vector2(0.97f, topCompacto));
        float ancho = 0.565f / ops.Length, anchoC = 0.94f / ops.Length;
        for (int k = 0; k < ops.Length; k++)
        {
            float v = ops[k].valor;
            float x0 = 0.015f + k * ancho, x0C = 0.03f + k * anchoC;
            var b = UIFabrica.Boton(padre, ops[k].texto, new Vector2(x0, top - 0.33f), new Vector2(x0 + ancho - 0.01f, top - 0.14f), 17,
                                    Tema.SuperficieAlta, () => ElegirOpcion(material, v));
            Responsivo.En(b, new Vector2(x0C, topCompacto - 0.21f), new Vector2(x0C + anchoC - 0.012f, topCompacto - 0.095f));
            var et = UIFabrica.Etiqueta(b);
            et.resizeTextForBestFit = true; et.resizeTextMinSize = 11; et.resizeTextMaxSize = 17;
            var borde = b.gameObject.AddComponent<Outline>();
            borde.effectDistance = new Vector2(2f, -2f);
            destino.Add((b.GetComponent<Image>(), UIFabrica.Etiqueta(b), v));
        }
    }

    GameObject Insignia(Transform padre, Vector2 aMin, Vector2 aMax)
    {
        var rt = UIFabrica.Panel("Insignia peso exagerado", padre, aMin, aMax, Tema.ConAlfa(Tema.Critico, 0.18f), Tema.RadioPildora);
        var borde = rt.gameObject.AddComponent<Outline>();
        borde.effectColor = Tema.Critico;
        borde.effectDistance = new Vector2(1f, -1f);
        if (iconoPesoExagerado != null)
        {
            var icono = UIFabrica.Panel("Icono", rt, new Vector2(0.02f, 0.08f), new Vector2(0.2f, 0.92f), Color.white, 0).GetComponent<Image>();
            icono.sprite = iconoPesoExagerado; icono.preserveAspect = true; icono.raycastTarget = false;
        }
        var t = UIFabrica.Texto(rt, "Texto", new Vector2(0.05f, 0f), new Vector2(0.86f, 1f), 16, TextAnchor.MiddleCenter, Tema.Critico);
        t.fontStyle = FontStyle.Bold;
        t.text = "PESO EXAGERADO";
        UIFabrica.IconoInfo(rt, AyudaBuild.PesoExagerado, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f),
                            "!", 22f, Tema.BotonNeutro);
        return rt.gameObject;
    }

    // Slider: pista redondeada, zona ámbar opcional (aceptable), zona verde (recomendado) y mango circular
    Slider CrearSlider(Transform padre, Vector2 aMin, Vector2 aMax, float min, float max,
                       UnityAction<float> alCambiar, out RectTransform banda, Action<RectTransform> bandaAceptableCreada)
    {
        var raizS = UIFabrica.Panel("Slider", padre, aMin, aMax, new Color(0, 0, 0, 0), 0);
        var s = raizS.gameObject.AddComponent<Slider>();

        var pista = UIFabrica.Panel("Pista", raizS, new Vector2(0f, 0.40f), new Vector2(1f, 0.60f), Tema.SuperficieCampo, 6);
        pista.offsetMin = new Vector2(12f, pista.offsetMin.y);
        pista.offsetMax = new Vector2(-12f, pista.offsetMax.y);
        if (bandaAceptableCreada != null)
        {
            var aceptable = UIFabrica.Panel("Aceptable", pista, Vector2.zero, new Vector2(0f, 1f), Tema.ConAlfa(Tema.Aviso, 0.35f), 6);
            aceptable.GetComponent<Image>().raycastTarget = false;
            bandaAceptableCreada(aceptable);
        }
        banda = UIFabrica.Panel("Recomendado", pista, Vector2.zero, new Vector2(0f, 1f), Tema.ConAlfa(Tema.Exito, 0.75f), 6);
        banda.GetComponent<Image>().raycastTarget = false;

        var area = UIFabrica.Panel("Area", raizS, Vector2.zero, Vector2.one, new Color(0, 0, 0, 0), 0);
        area.offsetMin = new Vector2(12f, 0f);
        area.offsetMax = new Vector2(-12f, 0f);
        // El Slider de Unity estira el mango a todo el alto del área: por eso el círculo va en un hijo de tamaño fijo
        var mango = UIFabrica.Panel("Mango", area, new Vector2(0f, 0f), new Vector2(0f, 1f), new Color(0, 0, 0, 0), 0);
        mango.sizeDelta = new Vector2(28f, 0f);
        var circulo = UIFabrica.Panel("Circulo", mango, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Tema.TextoPrimario, 0);
        float diametro = AdaptadorPantalla.EsMovil ? 34f : 24f;
        circulo.sizeDelta = new Vector2(diametro, diametro);
        var imgMango = circulo.GetComponent<Image>();
        imgMango.sprite = UIFabrica.SpriteCirculo;
        imgMango.raycastTarget = false;
        var sombra = circulo.gameObject.AddComponent<Shadow>();
        sombra.effectColor = new Color(0, 0, 0, 0.5f);
        sombra.effectDistance = new Vector2(0f, -2f);

        s.handleRect = mango;
        s.targetGraphic = imgMango;
        s.direction = Slider.Direction.LeftToRight;
        s.minValue = min; s.maxValue = max;
        s.onValueChanged.AddListener(alCambiar);
        return s;
    }
}
