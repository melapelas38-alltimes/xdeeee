using System;
using System.Collections.Generic;
using System.Globalization;
using Composta.Core;
using Composta.Core.Campo;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Composta.App
{
    // Formulario de medición de campo (Control Manual): solo las 3 variables medibles en la práctica.
    // No hay estimaciones de un modelo: se compara con la medición anterior, si existe.
    public sealed class PanelRegistroCampo : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        GestorLotes gestor;
        RectTransform raiz;
        Text txtTitulo, txtContexto, txtMensaje;
        readonly Text[] txtAnterior = new Text[3];
        InputField campoTemp, campoHum, campoPeso, campoNota;
        bool enviando;

        public bool Abierto => raiz != null && raiz.gameObject.activeSelf;
        public event Action AlRegistrar;

        public void Abrir(GestorLotes g)
        {
            gestor = g;
            Asegurar();
            foreach (var c in new[] { campoTemp, campoHum, campoPeso, campoNota }) c.text = "";
            txtMensaje.text = "";

            var e = gestor.Analisis();
            var perfil = PerfilComposta.Crear(gestor.LoteActual.Tipo);
            bool primera = e.Registros == 0;
            txtTitulo.text = primera ? "Primera medición" : "Medición semanal";
            txtContexto.text = string.Format(Inv, "{0} · peso recomendado {1:0.#}–{2:0.#} kg · límites físicos {3:0.#}–{4:0.#} kg",
                                             perfil.Nombre, perfil.PesoMin, perfil.PesoMax, perfil.ExtremoMin, perfil.ExtremoMax);
            var u = e.Ultimo;
            txtAnterior[0].text = u == null ? "" : string.Format(Inv, "Anterior: {0:F1} °C", u.Temperatura);
            txtAnterior[1].text = u == null ? "" : string.Format(Inv, "Anterior: {0:F0} %", u.Humedad);
            txtAnterior[2].text = u == null ? "" : string.Format(Inv, "Anterior: {0:0.#} kg", u.PesoKg);

            raiz.gameObject.SetActive(true);
            GuardiaEntrada.Bloquear(this);
            UIFabrica.Enfocar(campoTemp);
        }

        public void Cerrar()
        {
            if (raiz == null) return;
            raiz.gameObject.SetActive(false);
            GuardiaEntrada.Liberar(this);
        }

        void OnDestroy() => GuardiaEntrada.Liberar(this);

        void Update()
        {
            if (!Abierto || Notificador.DialogoAbierto) return;
            var k = Keyboard.current;
            if (k == null) return;
            if (k.escapeKey.wasPressedThisFrame) Cerrar();
            else if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame) Enviar();
        }

        void Enviar()
        {
            if (enviando || gestor == null || !gestor.EsLoteManual) return;
            var advertencias = new List<string>();
            var perfil = PerfilComposta.Crear(gestor.LoteActual.Tipo);
            if (!ValidadorInforme.Validar(campoTemp.text, campoHum.text, campoPeso.text, perfil, gestor.Analisis().Ultimo,
                                          out float t, out float h, out float p, out string error, advertencias))
            {
                Mensaje(error, false);
                return;
            }
            string nota = campoNota.text;
            if (advertencias.Count == 0) { Registrar(t, h, p, nota); return; }

            // Un dato raro puede ser real: se pide confirmación en vez de bloquear
            Notificador.Dialogo("Revisa los datos antes de guardar", "· " + string.Join("\n· ", advertencias),
                new OpcionDialogo("Corregir"),
                new OpcionDialogo("Guardar igual", () => Registrar(t, h, p, nota), true));
        }

        void Registrar(float t, float h, float p, string nota)
        {
            enviando = true;
            bool ok = gestor.RegistrarMedicion(t, h, p, nota, out string error);
            enviando = false;
            if (!ok) { Mensaje(error, false); return; }
            Cerrar();
            Notificador.Mostrar("Medición guardada. El tablero se actualizó con tus datos.", TipoMensaje.Exito);
            AlRegistrar?.Invoke();
        }

        void Mensaje(string texto, bool ok)
        {
            txtMensaje.text = texto;
            txtMensaje.color = ok ? UIFabrica.ColExito : UIFabrica.ColError;
        }

        void Asegurar()
        {
            if (raiz != null) return;
            raiz = UIFabrica.Canvas("Canvas Registro", transform, 40);
            UIFabrica.Panel("Velo", raiz, Vector2.zero, Vector2.one, Tema.Velo);
            var w = UIFabrica.ModalDesplazable(raiz, new Vector2(0.29f, 0.09f), new Vector2(0.71f, 0.91f), 1250f);
            var franja = UIFabrica.Panel("Acento", w, new Vector2(0f, 1f), Vector2.one, Tema.AcentoManual, 3);
            franja.offsetMin = new Vector2(0f, -5f);

            UIFabrica.Pildora(w, "Modo", new Vector2(0.05f, 0.925f), new Vector2(0.32f, 0.965f), "CONTROL MANUAL",
                              Tema.ConAlfa(Tema.AcentoManual, 0.18f), Tema.AcentoManual, 13);
            txtTitulo = UIFabrica.Texto(w, "Titulo", new Vector2(0.04f, 0.85f), new Vector2(0.96f, 0.925f), Tema.TxtTitulo, TextAnchor.MiddleLeft, Tema.TextoPrimario);
            txtTitulo.fontStyle = FontStyle.Bold;
            txtContexto = UIFabrica.Texto(w, "Contexto", new Vector2(0.04f, 0.80f), new Vector2(0.96f, 0.85f), 15, TextAnchor.MiddleLeft, Tema.TextoSecundario);
            txtContexto.resizeTextForBestFit = true; txtContexto.resizeTextMinSize = 11; txtContexto.resizeTextMaxSize = 15;

            campoTemp = Fila(w, 0, 0.78f, "Temperatura", "Ej.: 55", "°C",
                "Clava un termómetro de compost (sonda larga) en el centro de la pila, a 30-50 cm de profundidad. " +
                "Espera 1-2 minutos. Si puedes, mide en 2-3 puntos y anota el promedio.");
            campoHum = Fila(w, 1, 0.615f, "Humedad", "Ej.: 55", "%",
                "Con higrómetro de suelo, mide en el centro. Sin instrumento, prueba del puño: si escurren gotas, más de 65 %; " +
                "si queda compacto y húmedo sin gotear, 50-60 %; si se desmorona, menos de 40 %.");
            campoPeso = Fila(w, 2, 0.45f, "Peso", "Ej.: 20", "kg",
                "Pesa el contenedor (restando su peso vacío) o, en pilas grandes, estima: volumen (m³) × ~500 kg/m³. " +
                "Si agregaste material esta semana, regístralo también como aporte.");

            UIFabrica.Texto(w, "LblNota", new Vector2(0.04f, 0.245f), new Vector2(0.96f, 0.29f), 17, TextAnchor.LowerLeft, Tema.TextoSecundario).text = "Nota (opcional)";
            campoNota = UIFabrica.Campo(w, "Nota", new Vector2(0.05f, 0.18f), new Vector2(0.95f, 0.24f), "Olor, color, si se volteó, clima…", false, 200, 18);

            txtMensaje = UIFabrica.Texto(w, "Mensaje", new Vector2(0.04f, 0.11f), new Vector2(0.96f, 0.175f), 16, TextAnchor.MiddleLeft, UIFabrica.ColError);
            UIFabrica.Boton(w, "Cancelar", new Vector2(0.05f, 0.03f), new Vector2(0.33f, 0.10f), 20, Tema.BotonNeutro, Cerrar);
            var guardar = UIFabrica.Boton(w, "Guardar medición", new Vector2(0.38f, 0.03f), new Vector2(0.95f, 0.10f), 22, Tema.AcentoManual, Enviar);
            UIFabrica.Etiqueta(guardar).color = Tema.Fondo;
            UIFabrica.Etiqueta(guardar).fontStyle = FontStyle.Bold;
            raiz.gameObject.SetActive(false);
        }

        InputField Fila(Transform w, int i, float top, string etiqueta, string ejemplo, string unidad, string ayuda)
        {
            var lbl = UIFabrica.Texto(w, "Lbl" + i, new Vector2(0.04f, top - 0.045f), new Vector2(0.60f, top), 19, TextAnchor.LowerLeft, Tema.TextoPrimario);
            lbl.text = "<b>" + etiqueta + "</b>";
            UIFabrica.IconoJuntoATexto(lbl, ayuda);
            var campo = UIFabrica.Campo(w, "Campo" + i, new Vector2(0.05f, top - 0.125f), new Vector2(0.42f, top - 0.05f), ejemplo, false, 10, 26);
            UIFabrica.Texto(w, "Unidad" + i, new Vector2(0.42f, top - 0.125f), new Vector2(0.52f, top - 0.05f), 20, TextAnchor.MiddleLeft, Tema.TextoSecundario).text = unidad;
            txtAnterior[i] = UIFabrica.Texto(w, "Ant" + i, new Vector2(0.53f, top - 0.125f), new Vector2(0.96f, top - 0.05f), 16, TextAnchor.MiddleLeft, Tema.TextoTenue);
            return campo;
        }
    }
}
