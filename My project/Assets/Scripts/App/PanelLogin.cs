using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Composta.App
{
    // VISTA del acceso: solo dibuja, valida que los campos no estén vacíos y emite eventos.
    // La lógica (verificar, bloquear, crear cuentas) vive en ServicioAutenticacion; el flujo en ControlAcceso.
    public sealed class PanelLogin : MonoBehaviour
    {
        public enum Modo { Login, ConfiguracionInicial, CambioObligatorio, ErrorFatal, Registro, Recuperacion }

        public event Action<string, string> EnviarLogin;                       // usuario, contraseña
        public event Action<string, string, string, string> EnviarConfiguracion; // usuario, nombre, contraseña, confirmación
        public event Action<string, string> EnviarCambio;                      // nueva, confirmación
        public event Action CancelarCambio;                                    // también "Volver" desde registro/recuperación
        public event Action<string, string, string, string> EnviarRegistro;     // usuario, nombre, contraseña, confirmación
        public event Action<string, string, string, string> EnviarRecuperacion; // usuario, código, nueva, confirmación

        public Modo ModoActual { get; private set; }
        public bool Visible => raiz != null && raiz.gameObject.activeSelf;

        RectTransform raiz, card;
        CanvasGroup grupo;
        Text txtTitulo, txtSubtitulo, txtError, txtPie;
        InputField campoNombre, campoUsuario, campoCodigo, campoContrasena, campoConfirmar;
        Text lblNombre, lblUsuario, lblCodigo, lblContrasena, lblConfirmar;
        Button btnPrincipal, btnMostrar, btnSecundario, btnOlvide, btnRegistrarse;
        bool ocupado, mostrandoContrasena;
        Coroutine fade;

        // ---------------- API pública ----------------
        public void MostrarLogin(string ultimoUsuario = null, string mensaje = null)
        {
            Preparar(Modo.Login, "Ingresar", "Accede con tu cuenta para registrar y operar los lotes de composta.");
            campoUsuario.text = ultimoUsuario ?? "";
            Mostrar(string.IsNullOrEmpty(campoUsuario.text) ? campoUsuario : campoContrasena);
            if (!string.IsNullOrEmpty(mensaje)) MostrarError(mensaje, false);
        }

        // Siempre disponible desde el login: la cuenta nace con rol de Consulta
        public void MostrarRegistro()
        {
            Preparar(Modo.Registro, "Crear mi cuenta",
                     "Crea tu cuenta para consultar los lotes. Una persona de administración puede darte más permisos.");
            Mostrar(campoNombre);
        }

        public void MostrarRecuperacion(string usuario = null)
        {
            Preparar(Modo.Recuperacion, "Restablecer contraseña",
                     "Escribe tu usuario y el código de recuperación que guardaste al crear la cuenta. " +
                     "Si no lo tienes, pide a administración que restablezca tu contraseña.");
            campoUsuario.text = usuario ?? "";
            Mostrar(string.IsNullOrEmpty(campoUsuario.text) ? campoUsuario : campoCodigo);
        }

        public void MostrarConfiguracionInicial()
        {
            Preparar(Modo.ConfiguracionInicial, "Crear cuenta de administración",
                     "Primer uso: crea la cuenta que administrará a las demás personas usuarias.");
            Mostrar(campoNombre);
        }

        public void MostrarCambioObligatorio(string nombreVisible)
        {
            Preparar(Modo.CambioObligatorio, "Guardar contraseña",
                     $"Hola, {nombreVisible}. Tu contraseña es temporal: elige una nueva para continuar.");
            Mostrar(campoContrasena);
        }

        public void MostrarErrorFatal(string mensaje)
        {
            Preparar(Modo.ErrorFatal, "Reintentar", "No se pueden cargar los datos de acceso.");
            Mostrar(null);
            MostrarError(mensaje, true);
        }

        public event Action Reintentar;

        public void MostrarError(string mensaje, bool esError = true)
        {
            Asegurar();
            txtError.text = mensaje ?? "";
            txtError.color = esError ? UIFabrica.ColError : UIFabrica.ColTextoTenue;
        }

        public void Ocupado(bool valor)
        {
            ocupado = valor;
            if (btnPrincipal == null) return;
            btnPrincipal.interactable = !valor;
            UIFabrica.Etiqueta(btnPrincipal).text = valor ? "Verificando…" : TextoPrincipal();
            foreach (var c in Campos()) c.interactable = !valor;
        }

        public void Ocultar()
        {
            if (raiz == null || !raiz.gameObject.activeSelf) return;
            LimpiarContrasenas();
            GuardiaEntrada.Liberar(this);
            if (fade != null) StopCoroutine(fade);
            fade = StartCoroutine(Fundido(1f, 0f, () => raiz.gameObject.SetActive(false)));
        }

        // ---------------- Teclado: Tab / Shift+Tab / Enter ----------------
        void Update()
        {
            if (!Visible || ocupado) return;
            var k = Keyboard.current;
            if (k == null) return;

            if (k.tabKey.wasPressedThisFrame)
            {
                var visibles = new List<InputField>();
                foreach (var c in Campos()) if (c.gameObject.activeSelf) visibles.Add(c);
                if (visibles.Count == 0) return;
                int i = visibles.FindIndex(c => c.isFocused);
                bool atras = k.shiftKey.isPressed;
                int j = i < 0 ? 0 : (i + (atras ? -1 : 1) + visibles.Count) % visibles.Count;
                UIFabrica.Enfocar(visibles[j]);
            }
            else if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame)
            {
                Enviar();
            }
        }

        // ---------------- Internos ----------------
        void Preparar(Modo modo, string textoBoton, string subtitulo)
        {
            Asegurar();
            ModoActual = modo;
            txtSubtitulo.text = subtitulo;
            txtError.text = "";
            LimpiarContrasenas();
            if (modo != Modo.Login) campoUsuario.text = "";
            campoNombre.text = "";

            bool login = modo == Modo.Login, conf = modo == Modo.ConfiguracionInicial, cambio = modo == Modo.CambioObligatorio;
            bool registro = modo == Modo.Registro, recuperar = modo == Modo.Recuperacion;
            var filas = new List<(Text lbl, InputField campo)>();
            Fila(filas, lblNombre, campoNombre, conf || registro);
            Fila(filas, lblUsuario, campoUsuario, login || conf || registro || recuperar);
            Fila(filas, lblCodigo, campoCodigo, recuperar);
            Fila(filas, lblContrasena, campoContrasena, login || conf || cambio || registro || recuperar);
            Fila(filas, lblConfirmar, campoConfirmar, conf || cambio || registro || recuperar);
            lblContrasena.text = cambio || recuperar ? "Nueva contraseña" : "Contraseña";
            btnOlvide.gameObject.SetActive(login);
            btnRegistrarse.gameObject.SetActive(login);
            UIFabrica.Etiqueta(btnSecundario).text = cambio ? "Cancelar" : "Volver";

            // Coloca las filas visibles de arriba hacia abajo
            float top = 0.76f;
            foreach (var (lbl, campo) in filas)
            {
                Anclar(lbl.rectTransform, new Vector2(0.07f, top - 0.04f), new Vector2(0.93f, top));
                Anclar((RectTransform)campo.transform, new Vector2(0.07f, top - 0.105f), new Vector2(0.93f, top - 0.045f));
                top -= 0.125f;
            }

            btnMostrar.gameObject.SetActive(modo != Modo.ErrorFatal);
            btnSecundario.gameObject.SetActive(cambio || registro || recuperar);
            UIFabrica.Etiqueta(btnPrincipal).text = textoBoton;
            txtPie.text = conf || registro || recuperar
                ? $"La contraseña necesita {Core.Seguridad.ValidadorCredenciales.ContrasenaMin}+ caracteres con letras y números."
                : "Tras 5 intentos fallidos la cuenta se bloquea 5 minutos.";
            Ocupado(false);
        }

        static void Fila(List<(Text, InputField)> filas, Text lbl, InputField campo, bool visible)
        {
            lbl.gameObject.SetActive(visible);
            campo.gameObject.SetActive(visible);
            if (visible) filas.Add((lbl, campo));
        }

        void Mostrar(InputField foco)
        {
            raiz.gameObject.SetActive(true);
            raiz.SetAsLastSibling();
            GuardiaEntrada.Bloquear(this);
            if (fade != null) StopCoroutine(fade);
            fade = StartCoroutine(Fundido(grupo.alpha, 1f, null));
            if (foco != null) UIFabrica.Enfocar(foco);
        }

        void Enviar()
        {
            if (ocupado) return;
            switch (ModoActual)
            {
                case Modo.Login:
                    if (campoUsuario.text.Trim().Length == 0) { MostrarError("Escribe tu usuario."); UIFabrica.Enfocar(campoUsuario); return; }
                    if (campoContrasena.text.Length == 0) { MostrarError("Escribe tu contraseña."); UIFabrica.Enfocar(campoContrasena); return; }
                    EnviarLogin?.Invoke(campoUsuario.text, campoContrasena.text);
                    break;
                case Modo.ConfiguracionInicial:
                    EnviarConfiguracion?.Invoke(campoUsuario.text, campoNombre.text, campoContrasena.text, campoConfirmar.text);
                    break;
                case Modo.CambioObligatorio:
                    EnviarCambio?.Invoke(campoContrasena.text, campoConfirmar.text);
                    break;
                case Modo.ErrorFatal:
                    Reintentar?.Invoke();
                    break;
                case Modo.Registro:
                    EnviarRegistro?.Invoke(campoUsuario.text, campoNombre.text, campoContrasena.text, campoConfirmar.text);
                    break;
                case Modo.Recuperacion:
                    if (campoCodigo.text.Trim().Length == 0) { MostrarError("Escribe tu código de recuperación."); UIFabrica.Enfocar(campoCodigo); return; }
                    EnviarRecuperacion?.Invoke(campoUsuario.text, campoCodigo.text, campoContrasena.text, campoConfirmar.text);
                    break;
            }
        }

        // Tras cada intento la contraseña se borra del campo (no queda en memoria de la UI)
        public void LimpiarContrasenas()
        {
            if (campoContrasena == null) return;
            campoContrasena.text = "";
            campoConfirmar.text = "";
            campoCodigo.text = "";
        }

        void AlternarMostrar()
        {
            mostrandoContrasena = !mostrandoContrasena;
            var tipo = mostrandoContrasena ? InputField.ContentType.Standard : InputField.ContentType.Password;
            foreach (var c in new[] { campoContrasena, campoConfirmar })
            {
                c.contentType = tipo;
                c.ForceLabelUpdate();
            }
            UIFabrica.Etiqueta(btnMostrar).text = mostrandoContrasena ? "Ocultar contraseña" : "Mostrar contraseña";
        }

        string TextoPrincipal()
        {
            switch (ModoActual)
            {
                case Modo.ConfiguracionInicial: return "Crear cuenta de administración";
                case Modo.CambioObligatorio: return "Guardar contraseña";
                case Modo.ErrorFatal: return "Reintentar";
                case Modo.Registro: return "Crear mi cuenta";
                case Modo.Recuperacion: return "Restablecer contraseña";
                default: return "Ingresar";
            }
        }

        IEnumerable<InputField> Campos()
        {
            yield return campoNombre; yield return campoUsuario; yield return campoCodigo; yield return campoContrasena; yield return campoConfirmar;
        }

        IEnumerator Fundido(float desde, float hasta, Action alTerminar)
        {
            const float dur = 0.25f;
            grupo.blocksRaycasts = true;
            for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
            {
                grupo.alpha = Mathf.Lerp(desde, hasta, t / dur);
                yield return null;
            }
            grupo.alpha = hasta;
            grupo.blocksRaycasts = hasta > 0.5f;
            fade = null;
            alTerminar?.Invoke();
        }

        static void Anclar(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = min; rt.anchorMax = max;
        }

        // ---------------- Construcción ----------------
        void Asegurar()
        {
            if (raiz != null) return;
            raiz = UIFabrica.Canvas("Canvas Login", transform, 100);     // por encima de todo salvo los toasts
            grupo = raiz.GetComponent<CanvasGroup>();
            grupo.alpha = 0f;

            // Fondo opaco: no se ven datos de los lotes sin iniciar sesión
            var fondo = UIFabrica.Panel("Fondo", raiz, Vector2.zero, Vector2.one, UIFabrica.ColFondo);
            var marca = UIFabrica.Texto(fondo, "Marca", new Vector2(0.02f, 0.92f), new Vector2(0.5f, 0.99f), 22, TextAnchor.MiddleLeft, UIFabrica.ColTextoTenue);
            marca.text = "Compostaje comunitario · monitoreo de lotes";
            Responsivo.OcultarEnCompacto(marca);

            // En móvil la tarjeta ocupa el ancho y se desplaza si el teclado o la pantalla horizontal la recortan
            UIFabrica.Desplazable("Cuerpo", fondo, Vector2.zero, Vector2.one, 1150f, out var cuerpo);
            card = UIFabrica.Tarjeta("Card", cuerpo, new Vector2(0.34f, 0.12f), new Vector2(0.66f, 0.88f), UIFabrica.ColPanel);
            Responsivo.En(card, new Vector2(0.03f, 0.01f), new Vector2(0.97f, 0.99f));
            UIFabrica.Panel("Acento", card, new Vector2(0f, 0.985f), Vector2.one, UIFabrica.ColPrimario);
            txtTitulo = UIFabrica.Texto(card, "Titulo", new Vector2(0.05f, 0.88f), new Vector2(0.95f, 0.97f), 36, TextAnchor.MiddleLeft, Color.white);
            txtTitulo.text = "Compostaje comunitario";
            txtSubtitulo = UIFabrica.Texto(card, "Subtitulo", new Vector2(0.05f, 0.78f), new Vector2(0.95f, 0.88f), 19, TextAnchor.UpperLeft, UIFabrica.ColTextoTenue);

            lblNombre = UIFabrica.Texto(card, "LblNombre", Vector2.zero, Vector2.zero, 20, TextAnchor.LowerLeft, Color.white);
            lblNombre.text = "Nombre completo";
            campoNombre = UIFabrica.Campo(card, "CampoNombre", Vector2.zero, Vector2.zero, "Ej.: Ana López", false, Core.Seguridad.ValidadorCredenciales.NombreMax);

            lblUsuario = UIFabrica.Texto(card, "LblUsuario", Vector2.zero, Vector2.zero, 20, TextAnchor.LowerLeft, Color.white);
            lblUsuario.text = "Usuario";
            campoUsuario = UIFabrica.Campo(card, "CampoUsuario", Vector2.zero, Vector2.zero, "usuario", false, Core.Seguridad.ValidadorCredenciales.UsuarioMax);

            lblCodigo = UIFabrica.Texto(card, "LblCodigo", Vector2.zero, Vector2.zero, 20, TextAnchor.LowerLeft, Color.white);
            lblCodigo.text = "Código de recuperación";
            campoCodigo = UIFabrica.Campo(card, "CampoCodigo", Vector2.zero, Vector2.zero, "XXXX-XXXX-XXXX-XXXX", false, 24);

            lblContrasena = UIFabrica.Texto(card, "LblContrasena", Vector2.zero, Vector2.zero, 20, TextAnchor.LowerLeft, Color.white);
            lblContrasena.text = "Contraseña";
            campoContrasena = UIFabrica.Campo(card, "CampoContrasena", Vector2.zero, Vector2.zero, "••••••••", true, Core.Seguridad.ValidadorCredenciales.ContrasenaMax);

            lblConfirmar = UIFabrica.Texto(card, "LblConfirmar", Vector2.zero, Vector2.zero, 20, TextAnchor.LowerLeft, Color.white);
            lblConfirmar.text = "Confirmar contraseña";
            campoConfirmar = UIFabrica.Campo(card, "CampoConfirmar", Vector2.zero, Vector2.zero, "••••••••", true, Core.Seguridad.ValidadorCredenciales.ContrasenaMax);

            btnMostrar = UIFabrica.Boton(card, "Mostrar contraseña", new Vector2(0.07f, 0.215f), new Vector2(0.45f, 0.255f), 17, UIFabrica.ColCard, AlternarMostrar);
            txtError = UIFabrica.Texto(card, "Error", new Vector2(0.05f, 0.13f), new Vector2(0.95f, 0.21f), 19, TextAnchor.MiddleLeft, UIFabrica.ColError);
            txtError.resizeTextForBestFit = true; txtError.resizeTextMinSize = 12; txtError.resizeTextMaxSize = 19;

            btnPrincipal = UIFabrica.Boton(card, "Ingresar", new Vector2(0.07f, 0.045f), new Vector2(0.93f, 0.125f), 26, UIFabrica.ColPrimario, Enviar);
            btnSecundario = UIFabrica.Boton(card, "Cancelar", new Vector2(0.55f, 0.215f), new Vector2(0.93f, 0.255f), 17, UIFabrica.ColCard,
                                            () => CancelarCambio?.Invoke());
            // Enlaces del login: siempre hay una salida si se olvidó la contraseña o no se tiene cuenta
            btnOlvide = UIFabrica.Boton(card, "¿Olvidaste tu contraseña?", new Vector2(0.07f, 0.40f), new Vector2(0.93f, 0.45f), 18,
                                        new Color(0, 0, 0, 0), () => MostrarRecuperacion(campoUsuario.text));
            UIFabrica.Etiqueta(btnOlvide).color = Tema.AcentoSimulacion;
            btnOlvide.GetComponent<Image>().raycastTarget = true;
            btnRegistrarse = UIFabrica.Boton(card, "¿No tienes cuenta? Crear cuenta nueva", new Vector2(0.07f, 0.33f), new Vector2(0.93f, 0.38f), 18,
                                             new Color(0, 0, 0, 0), MostrarRegistro);
            UIFabrica.Etiqueta(btnRegistrarse).color = Tema.AcentoSimulacion;
            btnRegistrarse.GetComponent<Image>().raycastTarget = true;

            txtPie = UIFabrica.Texto(card, "Pie", new Vector2(0.05f, 0f), new Vector2(0.95f, 0.045f), 14, TextAnchor.MiddleCenter, UIFabrica.ColTextoTenue);

            raiz.gameObject.SetActive(false);
        }

        void OnDisable() => GuardiaEntrada.Liberar(this);
    }
}
