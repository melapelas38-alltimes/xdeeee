using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Composta.App;
using Composta.Core;
using Composta.Core.Seguridad;

// Tablero del modo SIMULACIÓN (acento azul). El modo Control Manual tiene su propio tablero (TableroManual):
// así ninguna vista mezcla datos simulados con datos medidos.
public class PanelUI : MonoBehaviour
{
    public SimuladorComposta simulador;
    public GeneradorGraficas graficas;      // opcional

    Transform raiz;
    Text txtCabecera, txtAlertas, txtPausa, txtVel, txtModo, txtGraf;
    readonly Text[] txtValor = new Text[4], txtReal = new Text[4], txtSensor = new Text[4], txtEstado = new Text[4];
    readonly Image[] franjaCard = new Image[4], fondoEstado = new Image[4];
    readonly RawImage[] imgGrafica = new RawImage[4];
    int tickMostrado = -1;
    bool pausaMostrada, autoMostrado;

    // Botones protegidos por permiso (se habilitan según el rol de la sesión)
    readonly List<(Button boton, Permiso permiso)> botonesProtegidos = new List<(Button, Permiso)>();
    Func<Permiso, bool> permisos;

    // Acciones que resuelve la capa de aplicación (ControlAcceso)
    public event Action PedirAportes;
    public event Action PedirExportar;
    public event Action PedirPrediccion;

    Canvas canvasUI;
    bool tableroVisible = true;

    void Start()
    {
        if (simulador == null) simulador = FindAnyObjectByType<SimuladorComposta>();
        if (graficas == null) graficas = FindAnyObjectByType<GeneradorGraficas>();
        if (simulador == null)
        {
            Debug.LogError("PanelUI: no hay un SimuladorComposta en la escena.");
            enabled = false;
            return;
        }
        if (graficas != null) graficas.mostrarEnPantalla = false;   // la UI las dibuja

        UIFabrica.AsegurarEventSystem();
        Construir();
        simulador.AlCargar += AlCambiarSistema;
        simulador.AlReiniciar += AlCambiarSistema;
    }

    void OnDestroy()
    {
        if (simulador == null) return;
        simulador.AlCargar -= AlCambiarSistema;
        simulador.AlReiniciar -= AlCambiarSistema;
    }

    void AlCambiarSistema(SistemaComposta _) => Invalidar();

    // ---------------- Construcción ----------------
    void Construir()
    {
        raiz = UIFabrica.Canvas("Canvas UI", null, 0);
        canvasUI = raiz.GetComponentInParent<Canvas>();
        canvasUI.enabled = tableroVisible;

        UIFabrica.Panel("Fondo", raiz, Vector2.zero, Vector2.one, Tema.Fondo);

        // Barra superior: pastilla de modo + estado. El centro es de la sesión y la derecha de "Nueva composta".
        var barra = UIFabrica.Panel("Barra", raiz, new Vector2(0.01f, 0.935f), new Vector2(0.99f, 0.99f), Tema.Superficie);
        Responsivo.En(barra, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12, -64), new Vector2(-12, -8));   // móvil: alto fijo
        var pildora = UIFabrica.Pildora(barra, "Modo", new Vector2(0.006f, 0.18f), new Vector2(0.095f, 0.82f), "SIMULACIÓN",
                          Tema.ConAlfa(Tema.AcentoSimulacion, 0.18f), Tema.AcentoSimulacion, 15);
        Responsivo.En(pildora.transform.parent, new Vector2(0.01f, 0.15f), new Vector2(0.32f, 0.85f));
        txtCabecera = UIFabrica.Texto(barra, "Cabecera", new Vector2(0.10f, 0f), new Vector2(0.40f, 1f), 22, TextAnchor.MiddleLeft, Tema.TextoPrimario);
        txtCabecera.resizeTextForBestFit = true; txtCabecera.resizeTextMinSize = 13; txtCabecera.resizeTextMaxSize = 22;
        Responsivo.En(txtCabecera, new Vector2(0.33f, 0f), new Vector2(1f, 1f));
        txtCabecera.text = "Iniciando simulación…";

        // Cuerpo: en escritorio ocupa la pantalla; en móvil se apila y se desplaza con el dedo.
        // Y(y) convierte las fracciones de pantalla del diseño amplio a fracciones del cuerpo.
        var vista = UIFabrica.Desplazable("Cuerpo", raiz, new Vector2(0f, 0.095f), new Vector2(1f, 0.93f), 2200f, out var cuerpo);
        Responsivo.En(vista, Vector2.zero, Vector2.one, new Vector2(0, 156), new Vector2(0, -136));
        float Y(float y) => (y - 0.095f) / 0.835f;

        // 4 indicadores (izquierda en amplio; cuadrícula 2×2 en móvil)
        for (int m = 0; m < 4; m++)
        {
            float top = 0.92f - m * 0.155f;
            var card = UIFabrica.Tarjeta("Card " + m, cuerpo, new Vector2(0.01f, Y(top - 0.142f)), new Vector2(0.33f, Y(top)));
            int fila = m / 2, columna = m % 2;
            float topC = 0.995f - fila * 0.095f;
            Responsivo.En(card, new Vector2(0.02f + columna * 0.485f, topC - 0.088f), new Vector2(0.495f + columna * 0.485f, topC));
            var franja = UIFabrica.Panel("Estado", card, Vector2.zero, new Vector2(0f, 1f), Tema.Exito, 3);
            franja.offsetMax = new Vector2(6f, 0f);
            franjaCard[m] = franja.GetComponent<Image>();

            var mag = (Magnitud)m;
            var nombre = UIFabrica.Texto(card, "Nombre", new Vector2(0.02f, 0.66f), new Vector2(0.6f, 0.98f), 18, TextAnchor.MiddleLeft, Tema.TextoSecundario);
            nombre.text = Reglas.Nombre(mag).ToUpperInvariant();
            nombre.fontStyle = FontStyle.Bold;
            txtEstado[m] = UIFabrica.Pildora(card, "Pildora", new Vector2(0.74f, 0.70f), new Vector2(0.97f, 0.93f), "OK",
                                             Tema.ExitoSuave, Tema.Exito, 13);
            fondoEstado[m] = txtEstado[m].transform.parent.GetComponent<Image>();
            Responsivo.En(txtEstado[m].transform.parent, new Vector2(0.56f, 0.70f), new Vector2(0.97f, 0.93f));
            txtValor[m] = UIFabrica.Texto(card, "Valor", new Vector2(0.02f, 0.26f), new Vector2(0.7f, 0.72f), Tema.TxtKpi, TextAnchor.MiddleLeft, Tema.TextoPrimario);
            txtValor[m].fontStyle = FontStyle.Bold;
            txtReal[m] = UIFabrica.Texto(card, "Real", new Vector2(0.6f, 0.30f), new Vector2(0.98f, 0.62f), 15, TextAnchor.MiddleRight, Tema.TextoTenue);
            Responsivo.OcultarEnCompacto(txtReal[m]);              // no cabe en media pantalla de teléfono
            Responsivo.En(nombre, new Vector2(0.02f, 0.66f), new Vector2(0.56f, 0.98f));
            txtSensor[m] = UIFabrica.Texto(card, "Sensor", new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.30f), 15, TextAnchor.MiddleLeft, Tema.TextoSecundario);
            txtValor[m].text = "—";
        }

        // 4 gráficas (derecha, 2x2)
        for (int m = 0; m < 4; m++)
        {
            int fila = m / 2, col = m % 2;
            float x0 = col == 0 ? 0.34f : 0.67f, x1 = col == 0 ? 0.66f : 0.99f;
            float y1 = fila == 0 ? 0.92f : 0.605f, y0 = y1 - 0.305f;
            var cuadro = UIFabrica.Tarjeta("Grafica " + m, cuerpo, new Vector2(x0, Y(y0)), new Vector2(x1, Y(y1)));
            float topG = 0.80f - m * 0.175f;
            Responsivo.En(cuadro, new Vector2(0.02f, topG - 0.165f), new Vector2(0.98f, topG));

            var mag = (Magnitud)m;
            var titulo = UIFabrica.Texto(cuadro, "Titulo", new Vector2(0.01f, 0.86f), new Vector2(1f, 1f), 17, TextAnchor.MiddleLeft, Tema.TextoSecundario);
            titulo.text = Reglas.Nombre(mag) + (Reglas.De(mag).Unidad != "" ? "  (" + Reglas.De(mag).Unidad + ")" : "") +
                          "   <color=" + Tema.HexDe(Tema.TextoTenue) + ">· zona verde = ideal</color>";

            var go = new GameObject("Img", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(cuadro, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(10, 10); rt.offsetMax = new Vector2(-10, -34);
            imgGrafica[m] = go.GetComponent<RawImage>();
            imgGrafica[m].color = Color.clear;       // oculta hasta que haya textura
        }

        // Alertas
        var panelAl = UIFabrica.Tarjeta("Alertas", cuerpo, new Vector2(0.01f, Y(0.105f)), new Vector2(0.99f, Y(0.285f)));
        Responsivo.En(panelAl, new Vector2(0.02f, 0.005f), new Vector2(0.98f, 0.10f));
        UIFabrica.Texto(panelAl, "Titulo", new Vector2(0.005f, 0.80f), new Vector2(0.5f, 1f), 16, TextAnchor.MiddleLeft, Tema.TextoSecundario)
            .text = "<b>ALERTAS ACTIVAS</b>";
        txtAlertas = UIFabrica.Texto(panelAl, "Texto", new Vector2(0.005f, 0f), new Vector2(1f, 0.82f), 19, TextAnchor.UpperLeft, Tema.TextoPrimario);
        txtAlertas.verticalOverflow = VerticalWrapMode.Truncate;

        // Botones: una fila en escritorio; en móvil, barra fija abajo con 2 filas de toques ≥ 48 dp
        barraBotones = UIFabrica.Panel("BarraBotones", raiz, new Vector2(0.01f, 0.02f), new Vector2(0.99f, 0.088f), new Color(0, 0, 0, 0), 0);
        Responsivo.En(barraBotones, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(8, 8), new Vector2(-8, 148));
        int i = 0;
        Boton("Voltear", i++, ancho, paso, simulador.Voltear, Permiso.OperarComposta, Tema.ConAlfa(Tema.AcentoSimulacion, 0.85f));
        Boton("Regar", i++, ancho, paso, simulador.Regar, Permiso.OperarComposta, Tema.ConAlfa(Tema.AcentoSimulacion, 0.85f));
        txtPausa = Boton("Pausa", i++, ancho, paso, simulador.AlternarPausa);
        txtVel = Boton("Velocidad", i++, ancho, paso, simulador.CambiarVelocidad);
        txtModo = Boton("Modo", i++, ancho, paso, simulador.AlternarAutomatico, Permiso.OperarComposta);
        Boton("Falla sensor", i++, ancho, paso, simulador.InyectarFalla, Permiso.InyectarFallas);
        Boton("Reparar", i++, ancho, paso, simulador.RepararSensores, Permiso.RepararSensores);
        txtGraf = Boton("Gráficas", i++, ancho, paso, () => { if (graficas != null) graficas.CambiarGranularidad(); });
        Boton("Aportes", i++, ancho, paso, () => PedirAportes?.Invoke());
        Boton("Exportar", i++, ancho, paso, () => PedirExportar?.Invoke(), Permiso.Exportar);
        Boton("Predicción", i++, ancho, paso, () => PedirPrediccion?.Invoke(), null, Tema.ConAlfa(Tema.AcentoPrediccion, 0.85f));
        RefrescarPermisos();
    }

    const int TotalBotones = 11;
    const float paso = 1f / TotalBotones, ancho = paso - 0.006f;
    RectTransform barraBotones;

    // Sin sesión, o con un lote de Control Manual abierto, este tablero no se dibuja
    public void MostrarTablero(bool visible)
    {
        tableroVisible = visible;
        if (canvasUI != null) canvasUI.enabled = visible;
    }

    // La llama ControlAcceso al iniciar sesión. Puede llegar antes o después de Start.
    public void AplicarPermisos(Func<Permiso, bool> puede)
    {
        permisos = puede;
        RefrescarPermisos();
    }

    void RefrescarPermisos()
    {
        foreach (var (boton, permiso) in botonesProtegidos)
            if (boton != null) boton.interactable = permisos == null || permisos(permiso);
    }

    Text Boton(string texto, int indice, float ancho, float paso, UnityAction accion, Permiso? permiso = null, Color? color = null)
    {
        float x0 = indice * paso;
        var b = UIFabrica.Boton(barraBotones, texto, new Vector2(x0, 0f), new Vector2(x0 + ancho, 1f), 19, color ?? Tema.BotonNeutro, accion);
        // Móvil: 6 arriba y 5 abajo
        int fila = indice < 6 ? 0 : 1, col = indice < 6 ? indice : indice - 6;
        Responsivo.En(b, new Vector2(col / 6f + 0.004f, fila == 0 ? 0.52f : 0f), new Vector2((col + 1) / 6f - 0.004f, fila == 0 ? 1f : 0.48f));
        var t = UIFabrica.Etiqueta(b);
        t.resizeTextForBestFit = true; t.resizeTextMinSize = 11; t.resizeTextMaxSize = 19;
        if (permiso.HasValue) botonesProtegidos.Add((b, permiso.Value));
        return t;
    }

    // ---------------- Actualización ----------------
    void Update()
    {
        if (simulador == null || txtPausa == null) return;

        txtPausa.text = simulador.pausado ? "Reanudar" : "Pausa";
        txtVel.text = "Velocidad x" + simulador.VelocidadActual;
        txtModo.text = simulador.modoAutomatico ? "Control: auto" : "Control: manual";
        if (graficas != null) txtGraf.text = "Vista: " + graficas.granularidad;

        var r = simulador.Ultimo;
        if (r == null) return;

        // Antes la cabecera solo se actualizaba con cada hora simulada: al pausar nunca mostraba "PAUSADO"
        bool cambioEstado = simulador.pausado != pausaMostrada || simulador.modoAutomatico != autoMostrado;
        if (r.Medida.Tick == tickMostrado && !cambioEstado) return;
        bool tickNuevo = r.Medida.Tick != tickMostrado;
        tickMostrado = r.Medida.Tick;
        pausaMostrada = simulador.pausado;
        autoMostrado = simulador.modoAutomatico;

        ActualizarCabecera(r);
        if (!tickNuevo) return;
        ActualizarTelemetria(r);
        ActualizarAlertas();
        ActualizarGraficas();
    }

    void ActualizarCabecera(ResultadoPaso r)
    {
        string tenue = Tema.HexDe(Tema.TextoTenue);
        txtCabecera.text = $"<b>Día {r.Medida.Dias:F1}</b>  <color={tenue}>·</color>  {NombreEtapa(r.Medida.Etapa)}  <color={tenue}>·</color>  " +
                           (simulador.modoAutomatico ? "control automático" : "control manual") +
                           (simulador.pausado ? $"  <color={Tema.HexDe(Tema.Aviso)}><b>· EN PAUSA</b></color>" : "");
    }

    public static string NombreEtapa(Etapa e)
    {
        switch (e)
        {
            case Etapa.Mesofilica: return "Fase mesofílica";
            case Etapa.Termofilica: return "Fase termofílica (caliente)";
            case Etapa.Enfriamiento: return "Enfriamiento";
            default: return "Maduración";
        }
    }

    // Tras reanudar un lote o crear uno nuevo, forzar el redibujado completo
    public void Invalidar() => tickMostrado = -1;

    void ActualizarTelemetria(ResultadoPaso r)
    {
        for (int m = 0; m < 4; m++)
        {
            var mag = (Magnitud)m;
            var med = r.Mediciones[m];
            string fmt = mag == Magnitud.PH ? "F2" : "F1";
            string u = Reglas.De(mag).Unidad;

            txtValor[m].text = Agregador.Valor(r.Medida, mag).ToString(fmt) + (u != "" ? " " + u : "") + (med.Confiable ? "" : "  ?");
            txtReal[m].text = "valor real del modelo\n" + Agregador.Valor(r.Real, mag).ToString(fmt);

            var nivel = NivelDe(Reglas.Nombre(mag));
            var color = nivel == Nivel.Critico ? Tema.Critico : nivel == Nivel.Aviso ? Tema.Aviso : Tema.Exito;
            franjaCard[m].color = color;
            fondoEstado[m].color = nivel == Nivel.Critico ? Tema.CriticoSuave : nivel == Nivel.Aviso ? Tema.AvisoSuave : Tema.ExitoSuave;
            txtEstado[m].color = color;
            txtEstado[m].text = nivel == Nivel.Critico ? "CRÍTICO" : nivel == Nivel.Aviso ? "AVISO" : "OK";

            if (med.Nivel == Nivel.Ok)
            {
                txtSensor[m].text = "Sensores A y B: OK";
                txtSensor[m].color = Tema.TextoTenue;
            }
            else
            {
                txtSensor[m].text = med.Diagnostico;
                txtSensor[m].color = med.Nivel == Nivel.Critico ? Tema.Critico : Tema.Aviso;
            }
        }
    }

    void ActualizarAlertas()
    {
        var lista = new List<Alerta>(simulador.Monitor.Activas);
        lista.Sort((a, b) => b.Nivel.CompareTo(a.Nivel));

        var sb = new StringBuilder();
        if (lista.Count == 0) sb.Append($"<color={Tema.HexDe(Tema.Exito)}>●</color>  Sin alertas activas: todo dentro de los rangos.");
        foreach (var a in lista)
        {
            string col = Tema.HexDe(a.Nivel == Nivel.Critico ? Tema.Critico : Tema.Aviso);
            string tipo = a.Variable.StartsWith("Sensor", StringComparison.Ordinal) ? "SENSOR"
                        : a.Variable.StartsWith("Predicción", StringComparison.Ordinal) ? "PREDICCIÓN" : "ALERTA";
            sb.Append($"<color={col}>● <b>{tipo}</b></color>  {a.Mensaje}");
            if (!string.IsNullOrEmpty(a.Accion)) sb.Append($"   <color={Tema.HexDe(Tema.TextoTenue)}>→</color>   <b>{a.Accion}</b>");
            sb.AppendLine();
        }
        txtAlertas.text = sb.ToString();
    }

    void ActualizarGraficas()
    {
        if (graficas == null) return;
        for (int m = 0; m < 4; m++)
        {
            var tex = graficas.ObtenerTextura((Magnitud)m);
            imgGrafica[m].texture = tex;
            imgGrafica[m].color = tex == null ? Color.clear : Color.white;
        }
    }

    Nivel NivelDe(string variable)
    {
        var nivel = Nivel.Ok;
        foreach (var a in simulador.Monitor.Activas)
            if (a.Variable == variable && a.Nivel > nivel) nivel = a.Nivel;
        return nivel;
    }
}
