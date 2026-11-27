using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Composta.App.Google;
using Composta.Core;
using Composta.Core.Pronostico;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Composta.App
{
    // Ventana dedicada de PREDICCIÓN (acento violeta). Solo muestra el pronóstico: curvas proyectadas, hitos con
    // fecha real (zona horaria de Google) y exportación a Google Calendar. No tiene controles de la simulación
    // en vivo y nunca toca el simulador ni los lotes: recibe un ResultadoPronostico ya calculado en una instancia aislada.
    public sealed class VentanaPrediccion : MonoBehaviour
    {
        static readonly Magnitud[] Curvas = { Magnitud.Temperatura, Magnitud.Humedad, Magnitud.Oxigeno };
        const int AnchoGraf = 640, AltoGraf = 260;

        ResultadoPronostico pronostico;
        string clave, nombreLote;
        InfoTiempo tiempo;
        List<EventoCalendario> eventos = new List<EventoCalendario>();
        bool exportando;

        RectTransform raiz;
        Text txtTitulo, txtSupuestos, txtTiempo, txtHitos, txtGoogle;
        Button btnExportar, btnDesconectar;
        readonly RawImage[] imgCurva = new RawImage[3];
        readonly Texture2D[] texCurva = new Texture2D[3];

        public bool Abierta => raiz != null && raiz.gameObject.activeSelf;

        public void Abrir(ResultadoPronostico p, string claveCalendario, string nombre)
        {
            pronostico = p; clave = claveCalendario; nombreLote = nombre;
            Asegurar();
            raiz.gameObject.SetActive(true);
            GuardiaEntrada.Bloquear(this);

            var c = CultureInfo.InvariantCulture;
            txtTitulo.text = string.Format(c, "<b>Pronóstico</b> · {0} · {1:0.#} kg", p.Perfil.Nombre, p.Perfil.PesoKg);
            txtSupuestos.text = (p.DiaSimuladoInicial > 0.01f ? string.Format(c, "Proyectado desde el día simulado {0:F1}. ", p.DiaSimuladoInicial) : "") +
                                string.Format(c, "Horizonte: {0:F0} días · temperatura máxima prevista {1:F0} °C{2}. ", p.DiasProyectados, p.TempMaxima,
                                              p.AlcanzaFaseCaliente ? "" : " (no alcanza la fase caliente)") + p.Supuestos;
            DibujarCurvas();

            ServicioTiempo.Refrescar();                 // por si cambió la zona del equipo
            tiempo = ServicioTiempo.Actual;             // hora y zona del teléfono o PC: sin cuentas ni claves
            RefrescarHitos();
            RefrescarGoogle();
        }

        public void Cerrar()
        {
            if (raiz == null) return;
            raiz.gameObject.SetActive(false);
            GuardiaEntrada.Liberar(this);
        }

        void OnDestroy()
        {
            GuardiaEntrada.Liberar(this);
            foreach (var t in texCurva) if (t != null) Destroy(t);
        }

        void Update()
        {
            if (!Abierta || Notificador.DialogoAbierto || exportando) return;
            var k = Keyboard.current;                       // en Android, el botón "Atrás" llega como Escape
            if (k != null && k.escapeKey.wasPressedThisFrame) Cerrar();
        }

        // ---------------- Contenido ----------------
        void DibujarCurvas()
        {
            for (int i = 0; i < Curvas.Length; i++)
            {
                var serie = Agregador.Construir(pronostico.Lecturas, Curvas[i], Granularidad.Dia);
                texCurva[i] = DibujanteGrafica.Dibujar(serie, AnchoGraf, AltoGraf, pronostico.Perfil.ZonaIdeal(Curvas[i]), texCurva[i]);
                imgCurva[i].texture = texCurva[i];
                imgCurva[i].color = Color.white;
            }
        }

        void RefrescarHitos()
        {
            var cfg = ConfiguracionGoogle.Cargar();
            int hora = cfg != null ? cfg.horaRecordatorios : 9;
            eventos = PlanCalendario.Generar(pronostico, tiempo.AhoraUtc, tiempo.Zona, nombreLote, hora);
            txtTiempo.text = $"{tiempo.Detalle} · {tiempo.Zona.Etiqueta()}";

            CultureInfo es;
            try { es = CultureInfo.GetCultureInfo("es-ES"); } catch (Exception) { es = CultureInfo.InvariantCulture; }
            var sb = new StringBuilder();
            string tenue = Tema.HexDe(Tema.TextoTenue);
            foreach (var e in eventos)
            {
                string color = Tema.HexDe(ColorHito(e.Tipo));
                sb.Append($"<color={color}>●</color>  <b>{e.InicioLocal.ToString("ddd d MMM", es)}</b>  <color={tenue}>{e.InicioLocal:HH:mm}</color>   ")
                  .Append(e.Titulo.Replace("Composta · ", "")).Append('\n');
            }
            if (eventos.Count == 0) sb.Append("No hay hitos en el horizonte proyectado.");
            txtHitos.text = sb.ToString();
        }

        static Color ColorHito(TipoHito t)
        {
            switch (t)
            {
                case TipoHito.InicioFaseCaliente: case TipoHito.PicoTemperatura: return Tema.Critico;
                case TipoHito.Volteo: return Tema.Aviso;
                case TipoHito.Cosecha: case TipoHito.InicioMaduracion: return Tema.Exito;
                default: return Tema.TextoSecundario;
            }
        }

        void RefrescarGoogle()
        {
            var cfg = ConfiguracionGoogle.Cargar();
            bool configurado = cfg != null && cfg.TieneOAuth;
            bool conectado = configurado && AutenticacionGoogle.Conectado;
            txtGoogle.text = !configurado ? "Google Calendar sin configurar (ver docs/GOOGLE_Y_ANDROID.md)"
                           : conectado ? "Google Calendar conectado"
                           : "Google Calendar: se pedirá permiso al exportar";
            btnExportar.interactable = configurado && !exportando && eventos.Count > 0;
            btnDesconectar.gameObject.SetActive(conectado && !exportando);
        }

        async void Exportar()
        {
            if (exportando || eventos.Count == 0) return;
            exportando = true;
            RefrescarGoogle();
            var res = await ServicioCalendarioGoogle.ExportarAsync(eventos, tiempo.Zona, clave, s => { if (txtGoogle != null) txtGoogle.text = s; });
            exportando = false;
            if (this == null) return;
            RefrescarGoogle();
            if (res.Exito)
                Notificador.Dialogo("Hitos en tu Google Calendar",
                    $"Se crearon {res.Creados} eventos con recordatorio" +
                    (res.Reemplazados > 0 ? $" (se reemplazaron {res.Reemplazados} de una exportación anterior de este lote)." : ".") +
                    $"\n\nZona horaria: {tiempo.Zona.Etiqueta()}");
            else
                Notificador.Mostrar((res.Creados > 0 ? $"Se crearon {res.Creados} eventos, pero hubo errores: " : "No se pudo exportar: ") + res.Error,
                                    TipoMensaje.Error);
        }

        async void Desconectar()
        {
            await AutenticacionGoogle.DesconectarAsync();
            if (this == null) return;
            RefrescarGoogle();
            Notificador.Mostrar("Cuenta de Google desconectada.", TipoMensaje.Info);
        }

        // ---------------- Construcción (amplio + compacto) ----------------
        void Asegurar()
        {
            if (raiz != null) return;
            raiz = UIFabrica.Canvas("Canvas Prediccion", transform, 55);
            UIFabrica.Panel("Velo", raiz, Vector2.zero, Vector2.one, Tema.Velo);
            var w = UIFabrica.Tarjeta("Marco", raiz, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f));
            Responsivo.En(w, Vector2.zero, Vector2.one, new Vector2(6, 6), new Vector2(-6, -6));
            var franja = UIFabrica.Panel("Acento", w, new Vector2(0f, 1f), Vector2.one, Tema.AcentoPrediccion, 3);
            franja.offsetMin = new Vector2(0f, -5f);

            // Pie fijo: estado de Google + botones (lo único interactivo de esta ventana)
            txtGoogle = UIFabrica.Texto(w, "Google", new Vector2(0.015f, 0.02f), new Vector2(0.42f, 0.09f), 16, TextAnchor.MiddleLeft, Tema.TextoSecundario);
            Responsivo.En(txtGoogle, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(14, 152), new Vector2(-14, 192));
            btnDesconectar = UIFabrica.Boton(w, "Desconectar Google", new Vector2(0.43f, 0.02f), new Vector2(0.58f, 0.09f), 16, Tema.BotonNeutro, Desconectar);
            Responsivo.En(btnDesconectar, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(12, 84), new Vector2(-12, 144));
            var cerrar = UIFabrica.Boton(w, "Cerrar", new Vector2(0.60f, 0.02f), new Vector2(0.72f, 0.09f), 20, Tema.BotonNeutro, Cerrar);
            Responsivo.En(cerrar, new Vector2(0f, 0f), new Vector2(0.35f, 0f), new Vector2(12, 12), new Vector2(-6, 76));
            btnExportar = UIFabrica.Boton(w, "Exportar hitos a Google Calendar", new Vector2(0.735f, 0.02f), new Vector2(0.985f, 0.09f), 19,
                                          Tema.AcentoPrediccion, Exportar);
            UIFabrica.Etiqueta(btnExportar).color = Tema.Fondo;
            UIFabrica.Etiqueta(btnExportar).fontStyle = FontStyle.Bold;
            UIFabrica.Etiqueta(btnExportar).resizeTextForBestFit = true;
            UIFabrica.Etiqueta(btnExportar).resizeTextMinSize = 12; UIFabrica.Etiqueta(btnExportar).resizeTextMaxSize = 19;
            Responsivo.En(btnExportar, new Vector2(0.35f, 0f), new Vector2(1f, 0f), new Vector2(6, 12), new Vector2(-12, 76));

            // Cuerpo desplazable (en móvil todo se apila)
            var vista = UIFabrica.Desplazable("Cuerpo", w, new Vector2(0f, 0.10f), new Vector2(1f, 0.985f), 2300f, out var c);
            Responsivo.En(vista, Vector2.zero, Vector2.one, new Vector2(0, 200), new Vector2(0, -8));

            var pild = UIFabrica.Pildora(c, "Modo", new Vector2(0.015f, 0.935f), new Vector2(0.14f, 0.985f), "PREDICCIÓN",
                                         Tema.ConAlfa(Tema.AcentoPrediccion, 0.18f), Tema.AcentoPrediccion, 15);
            Responsivo.En(pild.transform.parent, new Vector2(0.03f, 0.972f), new Vector2(0.45f, 0.995f));
            txtTitulo = UIFabrica.Texto(c, "Titulo", new Vector2(0.15f, 0.925f), new Vector2(0.985f, 0.995f), 26, TextAnchor.MiddleLeft, Tema.TextoPrimario);
            txtTitulo.resizeTextForBestFit = true; txtTitulo.resizeTextMinSize = 14; txtTitulo.resizeTextMaxSize = 26;
            Responsivo.En(txtTitulo, new Vector2(0.02f, 0.935f), new Vector2(0.98f, 0.97f));
            txtSupuestos = UIFabrica.Texto(c, "Supuestos", new Vector2(0.015f, 0.855f), new Vector2(0.985f, 0.925f), 15, TextAnchor.UpperLeft, Tema.TextoSecundario);
            txtSupuestos.resizeTextForBestFit = true; txtSupuestos.resizeTextMinSize = 11; txtSupuestos.resizeTextMaxSize = 15;
            Responsivo.En(txtSupuestos, new Vector2(0.02f, 0.865f), new Vector2(0.98f, 0.933f));

            // Curvas proyectadas (izquierda en amplio, apiladas en compacto)
            string[] titulos = { "Temperatura proyectada (°C)", "Humedad proyectada (%)", "Oxígeno proyectado (%)" };
            for (int i = 0; i < 3; i++)
            {
                float top = 0.845f - i * 0.28f;
                var g = UIFabrica.Panel("Curva " + i, c, new Vector2(0.015f, top - 0.27f), new Vector2(0.60f, top), Tema.SuperficieAlta);
                float topC = 0.855f - i * 0.17f;
                Responsivo.En(g, new Vector2(0.02f, topC - 0.16f), new Vector2(0.98f, topC));
                UIFabrica.Texto(g, "Titulo", new Vector2(0.01f, 0.84f), new Vector2(1f, 1f), 15, TextAnchor.MiddleLeft, Tema.TextoSecundario).text = titulos[i];
                var go = new GameObject("Img", typeof(RectTransform), typeof(RawImage));
                go.transform.SetParent(g, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(8, 8); rt.offsetMax = new Vector2(-8, -28);
                imgCurva[i] = go.GetComponent<RawImage>();
                imgCurva[i].color = Color.clear;
            }

            // Hitos con fecha (derecha en amplio, debajo en compacto)
            var h = UIFabrica.Panel("Hitos", c, new Vector2(0.615f, 0.005f), new Vector2(0.985f, 0.845f), Tema.SuperficieAlta);
            Responsivo.En(h, new Vector2(0.02f, 0.005f), new Vector2(0.98f, 0.34f));
            UIFabrica.Texto(h, "Titulo", new Vector2(0.03f, 0.93f), new Vector2(0.97f, 0.995f), 16, TextAnchor.MiddleLeft, Tema.TextoSecundario)
                .text = "<b>HITOS PREVISTOS</b>";
            txtTiempo = UIFabrica.Texto(h, "Tiempo", new Vector2(0.03f, 0.86f), new Vector2(0.97f, 0.93f), 13, TextAnchor.UpperLeft, Tema.TextoTenue);
            txtTiempo.resizeTextForBestFit = true; txtTiempo.resizeTextMinSize = 10; txtTiempo.resizeTextMaxSize = 13;
            txtHitos = UIFabrica.Texto(h, "Lista", new Vector2(0.03f, 0.01f), new Vector2(0.97f, 0.86f), 16, TextAnchor.UpperLeft, Tema.TextoPrimario);
            txtHitos.resizeTextForBestFit = true; txtHitos.resizeTextMinSize = 10; txtHitos.resizeTextMaxSize = 16;

            raiz.gameObject.SetActive(false);
        }
    }
}
