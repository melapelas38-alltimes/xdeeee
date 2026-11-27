using System.Globalization;
using System.Text;
using Composta.Core.Lotes;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Composta.App
{
    // Bitácora de residuos que entran al lote (peso + tipo de material + nota)
    public sealed class PanelAportes : MonoBehaviour
    {
        const int UltimosVisibles = 10;

        GestorLotes gestor;
        bool puedeRegistrar;
        TipoMaterial material = TipoMaterial.Verde;

        RectTransform raiz;
        Text txtTitulo, txtLista, txtMensaje, txtTotal;
        InputField campoKg, campoNota;
        Button btnRegistrar;
        readonly Image[] fondoMaterial = new Image[3];

        public bool Abierto => raiz != null && raiz.gameObject.activeSelf;

        public void Abrir(GestorLotes g, bool permitirRegistro)
        {
            Asegurar();
            if (gestor != null) gestor.AlCambiarAportes -= Refrescar;
            gestor = g;
            gestor.AlCambiarAportes += Refrescar;
            puedeRegistrar = permitirRegistro;

            campoKg.text = ""; campoNota.text = ""; txtMensaje.text = "";
            foreach (var c in new[] { campoKg, campoNota }) c.interactable = puedeRegistrar;
            btnRegistrar.interactable = puedeRegistrar;
            if (!puedeRegistrar) Mensaje("Tu rol solo permite consultar los aportes.", false);

            raiz.gameObject.SetActive(true);
            GuardiaEntrada.Bloquear(this);
            ElegirMaterial(material);
            Refrescar(gestor.LoteActual);
            if (puedeRegistrar) UIFabrica.Enfocar(campoKg);
        }

        public void Cerrar()
        {
            if (raiz == null) return;
            raiz.gameObject.SetActive(false);
            GuardiaEntrada.Liberar(this);
        }

        void OnDestroy()
        {
            if (gestor != null) gestor.AlCambiarAportes -= Refrescar;
            GuardiaEntrada.Liberar(this);
        }

        void Update()
        {
            if (!Abierto || Notificador.DialogoAbierto) return;
            var k = Keyboard.current;
            if (k == null) return;
            if (k.escapeKey.wasPressedThisFrame) Cerrar();
            else if (puedeRegistrar && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame)) Registrar();
        }

        void Registrar()
        {
            if (gestor == null) return;
            if (gestor.RegistrarAporte(campoKg.text, material, campoNota.text, out string msg))
            {
                Mensaje(msg, true);
                campoKg.text = ""; campoNota.text = "";
                UIFabrica.Enfocar(campoKg);
            }
            else Mensaje(msg, false);
        }

        void Mensaje(string texto, bool exito)
        {
            txtMensaje.text = texto;
            txtMensaje.color = exito ? UIFabrica.ColExito : UIFabrica.ColError;
        }

        void ElegirMaterial(TipoMaterial m)
        {
            material = m;
            for (int i = 0; i < fondoMaterial.Length; i++)
                fondoMaterial[i].color = i == (int)m ? UIFabrica.ColSel : UIFabrica.ColCard;
        }

        void Refrescar(Lote l)
        {
            if (raiz == null) return;
            var c = CultureInfo.InvariantCulture;
            if (l == null)
            {
                txtTitulo.text = "Aportes de residuos";
                txtLista.text = "No hay un lote activo.";
                txtTotal.text = "";
                return;
            }
            txtTitulo.text = "Aportes · " + l.Nombre;
            txtTotal.text = string.Format(c, "Total aportado: <b>{0:0.##} kg</b> en {1} registros", l.TotalAportadoKg, l.Aportes.Count);

            var sb = new StringBuilder();
            if (l.Aportes.Count == 0) sb.Append("Todavía no hay aportes registrados en este lote.");
            for (int i = l.Aportes.Count - 1, n = 0; i >= 0 && n < UltimosVisibles; i--, n++)
            {
                var a = l.Aportes[i];
                var fecha = new System.DateTime(a.FechaUtc, System.DateTimeKind.Utc).ToLocalTime();
                sb.AppendFormat(c, "{0:dd/MM HH:mm}  ·  <b>{1:0.##} kg</b>  ·  {2}  ·  {3}", fecha, a.Kg, ValidadorAporte.Nombre(a.Material), a.Usuario);
                if (!string.IsNullOrEmpty(a.Nota)) sb.Append("  ·  <i>").Append(UIFabrica.SinRich(a.Nota)).Append("</i>");
                sb.AppendLine();
            }
            txtLista.text = sb.ToString();
        }

        void Asegurar()
        {
            if (raiz != null) return;
            raiz = UIFabrica.Canvas("Canvas Aportes", transform, 40);
            var fondo = UIFabrica.Panel("Fondo", raiz, Vector2.zero, Vector2.one, new Color(0, 0, 0, 0.75f));
            var w = UIFabrica.ModalDesplazable(fondo, new Vector2(0.15f, 0.10f), new Vector2(0.85f, 0.90f), 1500f);

            // En móvil: formulario arriba (una columna) y lista de aportes debajo
            txtTitulo = UIFabrica.Texto(w, "Titulo", new Vector2(0.02f, 0.90f), new Vector2(0.98f, 0.98f), 30, TextAnchor.MiddleLeft, Color.white);
            txtTitulo.resizeTextForBestFit = true; txtTitulo.resizeTextMinSize = 14; txtTitulo.resizeTextMaxSize = 30;
            Responsivo.En(txtTitulo, new Vector2(0.03f, 0.945f), new Vector2(0.97f, 0.995f));

            // Formulario (izquierda)
            var lblKg = UIFabrica.Texto(w, "LblKg", new Vector2(0.02f, 0.82f), new Vector2(0.45f, 0.87f), 20, TextAnchor.LowerLeft, Color.white);
            lblKg.text = "Peso (kg)";
            Responsivo.En(lblKg, new Vector2(0.03f, 0.90f), new Vector2(0.97f, 0.94f));
            campoKg = UIFabrica.Campo(w, "CampoKg", new Vector2(0.03f, 0.74f), new Vector2(0.44f, 0.815f), "Ej.: 12,5", false, 10);
            Responsivo.En(campoKg, new Vector2(0.03f, 0.845f), new Vector2(0.97f, 0.895f));
            // Sin CharacterValidation.Decimal: según la versión de uGUI rechaza la coma decimal.
            // La validación real (coma o punto, rango, NaN) la hace ValidadorAporte.

            var lblMat = UIFabrica.Texto(w, "LblMaterial", new Vector2(0.02f, 0.66f), new Vector2(0.45f, 0.71f), 20, TextAnchor.LowerLeft, Color.white);
            lblMat.text = "Tipo de material";
            Responsivo.En(lblMat, new Vector2(0.03f, 0.795f), new Vector2(0.97f, 0.835f));
            var tipos = new[] { TipoMaterial.Verde, TipoMaterial.Cafe, TipoMaterial.Mixto };
            for (int i = 0; i < tipos.Length; i++)
            {
                var t = tipos[i];
                float x0 = 0.03f + i * 0.14f;
                var b = UIFabrica.Boton(w, ValidadorAporte.Nombre(t), new Vector2(x0, 0.58f), new Vector2(x0 + 0.13f, 0.655f), 16, UIFabrica.ColCard, () => ElegirMaterial(t));
                Responsivo.En(b, new Vector2(0.03f + i * 0.315f, 0.725f), new Vector2(0.03f + i * 0.315f + 0.30f, 0.79f));
                var et = UIFabrica.Etiqueta(b); et.resizeTextForBestFit = true; et.resizeTextMinSize = 11; et.resizeTextMaxSize = 16;
                fondoMaterial[i] = b.GetComponent<Image>();
            }

            var lblNota = UIFabrica.Texto(w, "LblNota", new Vector2(0.02f, 0.50f), new Vector2(0.45f, 0.55f), 20, TextAnchor.LowerLeft, Color.white);
            lblNota.text = "Nota (opcional)";
            Responsivo.En(lblNota, new Vector2(0.03f, 0.675f), new Vector2(0.97f, 0.715f));
            campoNota = UIFabrica.Campo(w, "CampoNota", new Vector2(0.03f, 0.42f), new Vector2(0.44f, 0.495f), "Origen, estado del material…", false, ValidadorAporte.NotaMax, 20);
            Responsivo.En(campoNota, new Vector2(0.03f, 0.62f), new Vector2(0.97f, 0.67f));

            btnRegistrar = UIFabrica.Boton(w, "Registrar aporte", new Vector2(0.03f, 0.31f), new Vector2(0.44f, 0.39f), 24, UIFabrica.ColPrimario, Registrar);
            Responsivo.En(btnRegistrar, new Vector2(0.03f, 0.555f), new Vector2(0.97f, 0.61f));
            txtMensaje = UIFabrica.Texto(w, "Mensaje", new Vector2(0.02f, 0.20f), new Vector2(0.45f, 0.30f), 18, TextAnchor.UpperLeft, UIFabrica.ColError);
            Responsivo.En(txtMensaje, new Vector2(0.03f, 0.505f), new Vector2(0.97f, 0.55f));

            // Historial (derecha)
            var lista = UIFabrica.Panel("Lista", w, new Vector2(0.48f, 0.12f), new Vector2(0.98f, 0.87f), UIFabrica.ColCampo);
            Responsivo.En(lista, new Vector2(0.03f, 0.085f), new Vector2(0.97f, 0.495f));
            txtTotal = UIFabrica.Texto(lista, "Total", new Vector2(0f, 0.92f), new Vector2(1f, 1f), 19, TextAnchor.MiddleLeft, Color.white);
            txtLista = UIFabrica.Texto(lista, "Registros", new Vector2(0f, 0f), new Vector2(1f, 0.91f), 16, TextAnchor.UpperLeft, new Color(1, 1, 1, 0.85f));
            txtLista.verticalOverflow = VerticalWrapMode.Truncate;

            var cerrar = UIFabrica.Boton(w, "Cerrar", new Vector2(0.80f, 0.02f), new Vector2(0.98f, 0.095f), 22, UIFabrica.ColBoton, Cerrar);
            Responsivo.En(cerrar, new Vector2(0.55f, 0.01f), new Vector2(0.97f, 0.07f));
            raiz.gameObject.SetActive(false);
        }
    }
}
