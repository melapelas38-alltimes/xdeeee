using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Composta.Core;
using Composta.Core.Campo;
using Composta.Core.Lotes;
using UnityEngine;
using UnityEngine.UI;

namespace Composta.App
{
    // Tablero del modo CONTROL MANUAL (acento ámbar). Muestra SOLO datos medidos a mano:
    // sin simulación, sin curvas inventadas. Antes del primer registro muestra un estado vacío guiado.
    public sealed class TableroManual : MonoBehaviour
    {
        public event Action PedirRegistro;
        public event Action PedirAportes;
        public event Action PedirExportar;
        public event Action PedirPrediccion;

        static readonly CultureInfo C = CultureInfo.InvariantCulture;
        const int AnchoGraf = 640, AltoGraf = 300;

        RectTransform raiz, contenido, vacio;
        Text txtLote, txtEstadoBanner, txtFase, txtFaseDetalle, txtRecs, txtHistorial, txtVacioPie;
        Image fondoBanner;
        Button btnRegistrarBanner, btnRegistrarVacio;
        readonly Text[] kpiValor = new Text[3], kpiDelta = new Text[3], kpiEstado = new Text[3];
        readonly Image[] kpiFranja = new Image[3], kpiFondoEstado = new Image[3];
        readonly RawImage[] imgGraf = new RawImage[3];
        readonly Texture2D[] texGraf = new Texture2D[3];
        readonly Text[] txtGrafInfo = new Text[3];

        public bool Visible => raiz != null && raiz.gameObject.activeSelf;

        public void Mostrar(bool visible)
        {
            if (visible) Asegurar();
            if (raiz != null) raiz.gameObject.SetActive(visible);
        }

        void OnDestroy()
        {
            foreach (var t in texGraf) if (t != null) Destroy(t);
        }

        // ---------------- Datos ----------------
        public void Refrescar(GestorLotes gestor, bool puedeRegistrar)
        {
            if (!Visible || gestor == null || !gestor.EsLoteManual) return;
            var lote = gestor.LoteActual;
            var s = gestor.Situacion();
            var e = gestor.Analisis();
            var perfil = PerfilComposta.Crear(lote.Tipo, lote.PesoKg, true);

            txtLote.text = $"<b>{UIFabrica.SinRich(perfil.Nombre)}</b>  <color={Tema.HexDe(Tema.TextoTenue)}>·</color>  " +
                           $"{e.Registros} {(e.Registros == 1 ? "medición" : "mediciones")}";
            ActualizarBanner(s, puedeRegistrar);

            bool sinDatos = e.Registros == 0;
            vacio.gameObject.SetActive(sinDatos);
            contenido.gameObject.SetActive(!sinDatos);
            btnRegistrarVacio.interactable = puedeRegistrar;
            txtVacioPie.text = puedeRegistrar ? "Tarda menos de 2 minutos."
                                              : "Tu rol es de consulta: pide a una persona de Operación que registre la medición.";
            if (sinDatos) return;

            // Fase estimada (siempre con su explicación: es una deducción, no una medición)
            txtFase.text = e.Fase.HasValue ? PanelUINombreEtapa(e.Fase.Value) : "Sin datos";
            txtFaseDetalle.text = e.ExplicacionFase +
                (e.PerdidaPesoTotalPct.HasValue && e.Registros > 1
                    ? string.Format(C, "\nPeso perdido desde el inicio: <b>{0:F1} %</b>", e.PerdidaPesoTotalPct.Value) : "");

            var u = e.Ultimo;
            Kpi(0, string.Format(C, "{0:F1} °C", u.Temperatura), e.DeltaTemp, "°C", e.NivelTemp);
            Kpi(1, string.Format(C, "{0:F0} %", u.Humedad), e.DeltaHum, " pts", e.NivelHum);
            float? deltaPeso = e.Anterior != null ? u.PesoKg - e.Anterior.PesoKg : (float?)null;
            Kpi(2, string.Format(C, "{0:0.#} kg", u.PesoKg), deltaPeso, " kg", e.NivelPeso);

            Graficas(lote, perfil);
            Recomendaciones(e);
            Historial(lote);
        }

        void ActualizarBanner(SituacionInforme s, bool puedeRegistrar)
        {
            Color c = s.Estado == EstadoInforme.Atrasado ? Tema.Critico
                    : s.Estado == EstadoInforme.Pendiente || s.Estado == EstadoInforme.SinRegistros ? Tema.Aviso : Tema.Exito;
            fondoBanner.color = Color.Lerp(Tema.Superficie, c, 0.14f);
            txtEstadoBanner.text = $"<color={Tema.HexDe(c)}>●</color>  {s.Texto()}";
            btnRegistrarBanner.interactable = puedeRegistrar;
        }

        void Kpi(int i, string valor, float? delta, string unidad, NivelEstado nivel)
        {
            kpiValor[i].text = valor;
            kpiDelta[i].text = delta.HasValue
                ? string.Format(C, "{0} {1:0.#}{2} desde la medición anterior", delta.Value > 0.05f ? "▲" : delta.Value < -0.05f ? "▼" : "=", Math.Abs(delta.Value), unidad)
                : "Primera medición";
            var color = Tema.Estado(nivel);
            kpiFranja[i].color = color;
            kpiFondoEstado[i].color = Tema.EstadoSuave(nivel);
            kpiEstado[i].color = color;
            kpiEstado[i].text = nivel == NivelEstado.Critico ? "CRÍTICO" : nivel == NivelEstado.Aviso ? "AVISO" : nivel == NivelEstado.Ok ? "OK" : "—";
        }

        void Graficas(Lote lote, PerfilComposta perfil)
        {
            var regs = AnalisisCampo.Ordenados(lote);
            var t = new List<(float, float)>(); var h = new List<(float, float)>(); var p = new List<(float, float)>();
            float pesoMax = 1f;
            foreach (var r in regs)
            {
                float dia = AnalisisCampo.Dias(regs[0], r);
                t.Add((dia, r.Temperatura)); h.Add((dia, r.Humedad)); p.Add((dia, r.PesoKg));
                pesoMax = Math.Max(pesoMax, r.PesoKg);
            }
            var zt = perfil.ZonaIdeal(Magnitud.Temperatura);
            var zh = perfil.ZonaIdeal(Magnitud.Humedad);
            texGraf[0] = DibujanteGrafica.DibujarPuntos(t, 0f, 80f, zt, DibujanteGrafica.ColorDe(Magnitud.Temperatura), AnchoGraf, AltoGraf, texGraf[0]);
            texGraf[1] = DibujanteGrafica.DibujarPuntos(h, 0f, 100f, zh, DibujanteGrafica.ColorDe(Magnitud.Humedad), AnchoGraf, AltoGraf, texGraf[1]);
            texGraf[2] = DibujanteGrafica.DibujarPuntos(p, 0f, pesoMax * 1.2f, (perfil.PesoMin, perfil.PesoMax),
                                                        new Color32(240, 162, 74, 255), AnchoGraf, AltoGraf, texGraf[2]);
            for (int i = 0; i < 3; i++) { imgGraf[i].texture = texGraf[i]; imgGraf[i].color = Color.white; }

            float dias = regs.Count > 1 ? AnalisisCampo.Dias(regs[0], regs[regs.Count - 1]) : 0f;
            string rango = regs.Count > 1 ? string.Format(C, "{0} mediciones en {1:F0} días", regs.Count, dias) : "1 medición";
            txtGrafInfo[0].text = string.Format(C, "ideal {0:F0}–{1:F0} °C · {2}", zt.min, zt.max, rango);
            txtGrafInfo[1].text = string.Format(C, "ideal {0:F0}–{1:F0} % · {2}", zh.min, zh.max, rango);
            txtGrafInfo[2].text = string.Format(C, "recomendado {0:0.#}–{1:0.#} kg · {2}", perfil.PesoMin, perfil.PesoMax, rango);
        }

        void Recomendaciones(EstadoCampo e)
        {
            var sb = new StringBuilder();
            foreach (var r in e.Recomendaciones)
            {
                sb.Append($"<color={Tema.HexDe(Tema.Estado(r.Nivel))}>●</color>  <b>{r.Titulo}</b>\n");
                sb.Append($"<color={Tema.HexDe(Tema.TextoSecundario)}>{r.Detalle}</color>\n\n");
            }
            txtRecs.text = sb.ToString().TrimEnd();
        }

        void Historial(Lote lote)
        {
            var regs = AnalisisCampo.Ordenados(lote);
            var sb = new StringBuilder();
            string tenue = Tema.HexDe(Tema.TextoTenue);
            if (AdaptadorPantalla.Compacto)
            {
                // Móvil: una "ficha" de dos líneas por medición en lugar de columnas
                for (int i = regs.Count - 1, n = 0; i >= 0 && n < 4; i--, n++)
                {
                    var r = regs[i];
                    var f = new DateTime(r.FechaUtc, DateTimeKind.Utc).ToLocalTime();
                    sb.AppendFormat(C, "<b>{0:dd/MM HH:mm}</b>   {1:F1} °C · {2:F0} % · {3:0.#} kg\n", f, r.Temperatura, r.Humedad, r.PesoKg);
                    sb.Append($"<color={tenue}>{UIFabrica.SinRich(r.Usuario)}" +
                              (string.IsNullOrEmpty(r.Nota) ? "" : " · " + UIFabrica.SinRich(r.Nota)) + "</color>\n");
                }
                if (regs.Count > 4) sb.Append($"<color={tenue}>… y {regs.Count - 4} anteriores (exporta para ver todas)</color>");
                txtHistorial.text = sb.ToString();
                return;
            }
            sb.Append($"<color={tenue}>FECHA              TEMPERATURA     HUMEDAD     PESO            REGISTRÓ          NOTA</color>\n");
            for (int i = regs.Count - 1, n = 0; i >= 0 && n < 5; i--, n++)
            {
                var r = regs[i];
                var f = new DateTime(r.FechaUtc, DateTimeKind.Utc).ToLocalTime();
                sb.AppendFormat(C, "{0:dd/MM HH:mm}        {1,6:F1} °C          {2,4:F0} %         {3,8:0.#} kg      {4,-14}   <color={5}>{6}</color>\n",
                                f, r.Temperatura, r.Humedad, r.PesoKg, UIFabrica.SinRich(r.Usuario), tenue, UIFabrica.SinRich(r.Nota));
            }
            if (regs.Count > 5) sb.Append($"<color={tenue}>… y {regs.Count - 5} mediciones anteriores (exporta para ver todas)</color>");
            txtHistorial.text = sb.ToString();
        }

        static string PanelUINombreEtapa(Etapa e) => PanelUI.NombreEtapa(e);

        // ---------------- Construcción ----------------
        void Asegurar()
        {
            if (raiz != null) return;
            raiz = UIFabrica.Canvas("Canvas Tablero Manual", transform, 1);   // debajo de sesión (20) y "Nueva composta" (10)
            UIFabrica.Panel("Fondo", raiz, Vector2.zero, Vector2.one, Tema.Fondo);

            // Barra superior con la pastilla de modo (ámbar = medido a mano)
            var barra = UIFabrica.Panel("Barra", raiz, new Vector2(0.01f, 0.935f), new Vector2(0.99f, 0.99f), Tema.Superficie);
            Responsivo.En(barra, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12, -64), new Vector2(-12, -8));
            var pild = UIFabrica.Pildora(barra, "Modo", new Vector2(0.006f, 0.18f), new Vector2(0.125f, 0.82f), "CONTROL MANUAL",
                              Tema.ConAlfa(Tema.AcentoManual, 0.18f), Tema.AcentoManual, 15);
            Responsivo.En(pild.transform.parent, new Vector2(0.01f, 0.15f), new Vector2(0.42f, 0.85f));
            txtLote = UIFabrica.Texto(barra, "Lote", new Vector2(0.13f, 0f), new Vector2(0.40f, 1f), 20, TextAnchor.MiddleLeft, Tema.TextoPrimario);
            txtLote.resizeTextForBestFit = true; txtLote.resizeTextMinSize = 11; txtLote.resizeTextMaxSize = 20;
            Responsivo.En(txtLote, new Vector2(0.43f, 0f), new Vector2(1f, 1f));

            // Franja: sensores no conectados + estado de la medición semanal + Predicción + CTA (alto fijo en móvil)
            var banner = UIFabrica.Tarjeta("Banner", raiz, new Vector2(0.01f, 0.868f), new Vector2(0.99f, 0.925f));
            Responsivo.En(banner, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12, -360), new Vector2(-12, -140));
            fondoBanner = banner.GetComponent<Image>();
            var icono = UIFabrica.IconoInfo(banner, "Este lote no tiene sensores físicos ni simulación. Todo lo que ves sale de las mediciones " +
                                        "que registras a mano (temperatura, humedad y peso). Registra una medición cada 7 días.",
                                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), "!", 26f, UIFabrica.ColIconoAviso);
            // Móvil: arriba a la derecha (con anclas puntuales, los desplazamientos definen posición y tamaño 26×26)
            Responsivo.En(icono, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-38f, -38f), new Vector2(-12f, -12f), new Vector2(1f, 1f));
            var aviso = UIFabrica.Texto(banner, "Aviso", new Vector2(0.025f, 0f), new Vector2(0.43f, 1f), 18, TextAnchor.MiddleLeft, Tema.AcentoManual);
            aviso.text = "<b>Sensores físicos no conectados</b> · Modo de registro manual activo";
            aviso.resizeTextForBestFit = true; aviso.resizeTextMinSize = 12; aviso.resizeTextMaxSize = 18;
            Responsivo.En(aviso, new Vector2(0.02f, 0.68f), new Vector2(0.88f, 0.98f));
            txtEstadoBanner = UIFabrica.Texto(banner, "Estado", new Vector2(0.43f, 0f), new Vector2(0.64f, 1f), 17, TextAnchor.MiddleLeft, Tema.TextoPrimario);
            txtEstadoBanner.resizeTextForBestFit = true; txtEstadoBanner.resizeTextMinSize = 11; txtEstadoBanner.resizeTextMaxSize = 17;
            Responsivo.En(txtEstadoBanner, new Vector2(0.02f, 0.40f), new Vector2(0.98f, 0.68f));
            var prediccion = UIFabrica.Boton(banner, "Predicción", new Vector2(0.65f, 0.14f), new Vector2(0.78f, 0.86f), 18,
                                             Tema.ConAlfa(Tema.AcentoPrediccion, 0.85f), () => PedirPrediccion?.Invoke());
            Responsivo.En(prediccion, new Vector2(0.02f, 0.04f), new Vector2(0.36f, 0.37f));
            btnRegistrarBanner = UIFabrica.Boton(banner, "Registrar medición semanal", new Vector2(0.79f, 0.14f), new Vector2(0.995f, 0.86f), 18,
                                                 Tema.AcentoManual, () => PedirRegistro?.Invoke());
            Responsivo.En(btnRegistrarBanner, new Vector2(0.38f, 0.04f), new Vector2(0.98f, 0.37f));
            UIFabrica.Etiqueta(btnRegistrarBanner).color = Tema.Fondo;
            UIFabrica.Etiqueta(btnRegistrarBanner).fontStyle = FontStyle.Bold;

            ConstruirVacio();
            ConstruirContenido();
            raiz.gameObject.SetActive(false);
        }

        // Estado vacío: explica qué medir y lleva a la acción principal
        void ConstruirVacio()
        {
            var vistaVacio = UIFabrica.Desplazable("Vacio", raiz, new Vector2(0f, 0f), new Vector2(1f, 0.86f), 1000f, out var cVacio);
            Responsivo.En(vistaVacio, Vector2.zero, Vector2.one, new Vector2(0, 8), new Vector2(0, -368));
            vacio = vistaVacio;
            var card = UIFabrica.Tarjeta("Card", cVacio, new Vector2(0.29f, 0.12f), new Vector2(0.71f, 0.92f));
            Responsivo.En(card, new Vector2(0.03f, 0.01f), new Vector2(0.97f, 0.99f));
            var circulo = UIFabrica.Panel("Icono", card, new Vector2(0.5f, 0.80f), new Vector2(0.5f, 0.80f), Tema.ConAlfa(Tema.AcentoManual, 0.16f), 0);
            circulo.sizeDelta = new Vector2(96, 96);
            circulo.GetComponent<Image>().sprite = UIFabrica.SpriteCirculo;
            var signo = UIFabrica.Texto(circulo, "Signo", Vector2.zero, Vector2.one, 56, TextAnchor.MiddleCenter, Tema.AcentoManual);
            signo.rectTransform.offsetMin = signo.rectTransform.offsetMax = Vector2.zero;
            signo.fontStyle = FontStyle.Bold;
            signo.text = "+";

            UIFabrica.Texto(card, "Titulo", new Vector2(0.06f, 0.62f), new Vector2(0.94f, 0.71f), Tema.TxtTitulo, TextAnchor.MiddleCenter, Tema.TextoPrimario)
                .text = "<b>Esperando primer registro</b>";
            var cuerpo = UIFabrica.Texto(card, "Cuerpo", new Vector2(0.08f, 0.24f), new Vector2(0.92f, 0.62f), Tema.TxtCuerpo, TextAnchor.UpperLeft, Tema.TextoSecundario);
            string acento = Tema.HexDe(Tema.AcentoManual);
            cuerpo.text =
                "Este lote <b>no usa simulación</b>: el tablero mostrará solo lo que midas en la pila.\n\n" +
                $"<color={acento}><b>1</b></color>   Temperatura en el centro de la pila (termómetro de sonda).\n" +
                $"<color={acento}><b>2</b></color>   Humedad (higrómetro o prueba del puño).\n" +
                $"<color={acento}><b>3</b></color>   Peso de la pila o del contenedor.\n\n" +
                "Repite cada 7 días. Con dos o más mediciones verás tendencias, la fase estimada y recomendaciones.";
            btnRegistrarVacio = UIFabrica.Boton(card, "Registrar primera medición", new Vector2(0.22f, 0.09f), new Vector2(0.78f, 0.19f), 22,
                                                Tema.AcentoManual, () => PedirRegistro?.Invoke());
            UIFabrica.Etiqueta(btnRegistrarVacio).color = Tema.Fondo;
            UIFabrica.Etiqueta(btnRegistrarVacio).fontStyle = FontStyle.Bold;
            txtVacioPie = UIFabrica.Texto(card, "Pie", new Vector2(0.06f, 0.02f), new Vector2(0.94f, 0.08f), Tema.TxtPequeno, TextAnchor.MiddleCenter, Tema.TextoTenue);
        }

        void ConstruirContenido()
        {
            var vista = UIFabrica.Desplazable("Contenido", raiz, new Vector2(0f, 0f), new Vector2(1f, 0.86f), 3000f, out var cuerpo);
            Responsivo.En(vista, Vector2.zero, Vector2.one, new Vector2(0, 8), new Vector2(0, -368));
            contenido = vista;
            // Coordenadas relativas al cuerpo (en escritorio = 86 % de la pantalla; en móvil, 3000 u desplazables)

            // Fase estimada
            var fase = UIFabrica.Tarjeta("Fase", cuerpo, new Vector2(0.01f, 0.835f), new Vector2(0.30f, 0.995f));
            Responsivo.En(fase, new Vector2(0.02f, 0.92f), new Vector2(0.98f, 0.995f));
            var franjaF = UIFabrica.Panel("Acento", fase, Vector2.zero, new Vector2(0f, 1f), Tema.AcentoManual, 3);
            franjaF.offsetMax = new Vector2(6f, 0f);
            UIFabrica.Texto(fase, "Lbl", new Vector2(0.03f, 0.70f), new Vector2(0.6f, 0.97f), 15, TextAnchor.MiddleLeft, Tema.TextoSecundario).text = "<b>FASE ESTIMADA</b>";
            UIFabrica.Pildora(fase, "Origen", new Vector2(0.55f, 0.72f), new Vector2(0.97f, 0.94f), "según tus mediciones", Tema.SuperficieAlta, Tema.TextoTenue, 12);
            txtFase = UIFabrica.Texto(fase, "Fase", new Vector2(0.03f, 0.40f), new Vector2(0.97f, 0.72f), 26, TextAnchor.MiddleLeft, Tema.TextoPrimario);
            txtFase.fontStyle = FontStyle.Bold;
            txtFaseDetalle = UIFabrica.Texto(fase, "Detalle", new Vector2(0.03f, 0.02f), new Vector2(0.97f, 0.42f), 14, TextAnchor.UpperLeft, Tema.TextoSecundario);
            txtFaseDetalle.resizeTextForBestFit = true; txtFaseDetalle.resizeTextMinSize = 10; txtFaseDetalle.resizeTextMaxSize = 14;

            // KPIs: última medición de cada variable
            string[] nombres = { "TEMPERATURA", "HUMEDAD", "PESO" };
            for (int i = 0; i < 3; i++)
            {
                float top = 0.82f - i * 0.18f;
                var card = UIFabrica.Tarjeta("Kpi " + i, cuerpo, new Vector2(0.01f, top - 0.165f), new Vector2(0.30f, top));
                float topC = 0.91f - i * 0.072f;
                Responsivo.En(card, new Vector2(0.02f, topC - 0.068f), new Vector2(0.98f, topC));
                var franja = UIFabrica.Panel("Estado", card, Vector2.zero, new Vector2(0f, 1f), Tema.Exito, 3);
                franja.offsetMax = new Vector2(6f, 0f);
                kpiFranja[i] = franja.GetComponent<Image>();
                UIFabrica.Texto(card, "Nombre", new Vector2(0.03f, 0.68f), new Vector2(0.6f, 0.97f), 15, TextAnchor.MiddleLeft, Tema.TextoSecundario)
                    .text = $"<b>{nombres[i]}</b>  <color={Tema.HexDe(Tema.TextoTenue)}>· última medición</color>";
                kpiEstado[i] = UIFabrica.Pildora(card, "Pildora", new Vector2(0.74f, 0.70f), new Vector2(0.97f, 0.93f), "OK", Tema.ExitoSuave, Tema.Exito, 13);
                kpiFondoEstado[i] = kpiEstado[i].transform.parent.GetComponent<Image>();
                kpiValor[i] = UIFabrica.Texto(card, "Valor", new Vector2(0.03f, 0.25f), new Vector2(0.97f, 0.70f), 40, TextAnchor.MiddleLeft, Tema.TextoPrimario);
                kpiValor[i].fontStyle = FontStyle.Bold;
                kpiDelta[i] = UIFabrica.Texto(card, "Delta", new Vector2(0.03f, 0.02f), new Vector2(0.97f, 0.28f), 14, TextAnchor.MiddleLeft, Tema.TextoSecundario);
            }

            // Gráficas de puntos medidos
            string[] titulos = { "Temperatura (°C)", "Humedad (%)", "Peso (kg)" };
            var rects = new[]
            {
                (new Vector2(0.31f, 0.60f), new Vector2(0.65f, 0.995f)),
                (new Vector2(0.66f, 0.60f), new Vector2(0.99f, 0.995f)),
                (new Vector2(0.31f, 0.27f), new Vector2(0.65f, 0.585f)),
            };
            for (int i = 0; i < 3; i++)
            {
                var g = UIFabrica.Tarjeta("Grafica " + i, cuerpo, rects[i].Item1, rects[i].Item2);
                float topG = 0.69f - i * 0.135f;
                Responsivo.En(g, new Vector2(0.02f, topG - 0.13f), new Vector2(0.98f, topG));
                UIFabrica.Texto(g, "Titulo", new Vector2(0.01f, 0.86f), new Vector2(0.5f, 1f), 17, TextAnchor.MiddleLeft, Tema.TextoSecundario).text = titulos[i];
                txtGrafInfo[i] = UIFabrica.Texto(g, "Info", new Vector2(0.45f, 0.86f), new Vector2(0.99f, 1f), 13, TextAnchor.MiddleRight, Tema.TextoTenue);
                var go = new GameObject("Img", typeof(RectTransform), typeof(RawImage));
                go.transform.SetParent(g, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(10, 10); rt.offsetMax = new Vector2(-10, -34);
                imgGraf[i] = go.GetComponent<RawImage>();
                imgGraf[i].color = Color.clear;
            }

            // Recomendaciones
            var recs = UIFabrica.Tarjeta("Recomendaciones", cuerpo, new Vector2(0.66f, 0.27f), new Vector2(0.99f, 0.585f));
            Responsivo.En(recs, new Vector2(0.02f, 0.15f), new Vector2(0.98f, 0.285f));
            UIFabrica.Texto(recs, "Titulo", new Vector2(0.02f, 0.86f), new Vector2(0.98f, 1f), 17, TextAnchor.MiddleLeft, Tema.TextoSecundario)
                .text = "<b>QUÉ HACER AHORA</b>";
            txtRecs = UIFabrica.Texto(recs, "Texto", new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.86f), 16, TextAnchor.UpperLeft, Tema.TextoPrimario);
            txtRecs.resizeTextForBestFit = true; txtRecs.resizeTextMinSize = 11; txtRecs.resizeTextMaxSize = 16;

            // Historial de mediciones + acciones secundarias
            var hist = UIFabrica.Tarjeta("Historial", cuerpo, new Vector2(0.01f, 0.015f), new Vector2(0.99f, 0.255f));
            Responsivo.En(hist, new Vector2(0.02f, 0.005f), new Vector2(0.98f, 0.145f));
            var tituloHist = UIFabrica.Texto(hist, "Titulo", new Vector2(0.01f, 0.80f), new Vector2(0.6f, 1f), 17, TextAnchor.MiddleLeft, Tema.TextoSecundario);
            tituloHist.text = "<b>MEDICIONES REGISTRADAS</b>";
            tituloHist.resizeTextForBestFit = true; tituloHist.resizeTextMinSize = 11; tituloHist.resizeTextMaxSize = 17;
            Responsivo.En(tituloHist, new Vector2(0.01f, 0.84f), new Vector2(0.45f, 0.99f));
            var bAportes = UIFabrica.Boton(hist, "Aportes", new Vector2(0.80f, 0.80f), new Vector2(0.89f, 0.97f), 16, Tema.BotonNeutro, () => PedirAportes?.Invoke());
            Responsivo.En(bAportes, new Vector2(0.46f, 0.84f), new Vector2(0.72f, 0.99f));
            var bExportar = UIFabrica.Boton(hist, "Exportar", new Vector2(0.90f, 0.80f), new Vector2(0.995f, 0.97f), 16, Tema.BotonNeutro, () => PedirExportar?.Invoke());
            Responsivo.En(bExportar, new Vector2(0.73f, 0.84f), new Vector2(0.99f, 0.99f));
            txtHistorial = UIFabrica.Texto(hist, "Tabla", new Vector2(0.01f, 0.02f), new Vector2(0.99f, 0.80f), 16, TextAnchor.UpperLeft, Tema.TextoPrimario);
            Responsivo.En(txtHistorial, new Vector2(0.01f, 0.02f), new Vector2(0.99f, 0.83f));
            txtHistorial.verticalOverflow = VerticalWrapMode.Truncate;
        }
    }
}
