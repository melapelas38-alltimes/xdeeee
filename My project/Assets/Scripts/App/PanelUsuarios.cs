using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Composta.Core.Seguridad;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Composta.App
{
    // Administración de cuentas (solo rol Administración) + cambio de la contraseña propia (todos)
    public sealed class PanelUsuarios : MonoBehaviour
    {
        const int FilasMax = 11;

        ServicioAutenticacion auth;
        Sesion sesion;
        Rol rolNuevo = Rol.Operador;
        bool ocupado;

        RectTransform raiz, seccionLista, seccionNueva, contenedorFilas;
        Text txtMensaje;
        InputField campoUsuario, campoNombre, campoTemporal, campoActual, campoNueva, campoConfirmar;
        readonly Image[] fondoRol = new Image[3];
        readonly List<GameObject> filas = new List<GameObject>();

        public bool Abierto => raiz != null && raiz.gameObject.activeSelf;

        public void Abrir(ServicioAutenticacion servicio, Sesion s)
        {
            auth = servicio; sesion = s;
            Asegurar();
            bool admin = sesion.Puede(Permiso.GestionarUsuarios);
            seccionLista.gameObject.SetActive(admin);
            seccionNueva.gameObject.SetActive(admin);
            foreach (var c in new[] { campoUsuario, campoNombre, campoTemporal, campoActual, campoNueva, campoConfirmar }) c.text = "";
            txtMensaje.text = "";
            ElegirRol(Rol.Operador);
            raiz.gameObject.SetActive(true);
            GuardiaEntrada.Bloquear(this);
            if (admin) RefrescarLista();
            UIFabrica.Enfocar(admin ? campoUsuario : campoActual);
        }

        public void Cerrar()
        {
            if (raiz == null) return;
            foreach (var c in new[] { campoTemporal, campoActual, campoNueva, campoConfirmar }) c.text = "";
            raiz.gameObject.SetActive(false);
            GuardiaEntrada.Liberar(this);
        }

        void OnDestroy() => GuardiaEntrada.Liberar(this);

        void Update()
        {
            if (Abierto && !Notificador.DialogoAbierto && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Cerrar();
        }

        // ---------------- Acciones ----------------
        async void Ejecutar(Func<ResultadoAuth> operacion, Action<ResultadoAuth> alExito)
        {
            if (ocupado) return;
            ocupado = true;
            Mensaje("Procesando…", true);
            ResultadoAuth r;
            try { r = await Task.Run(operacion); }      // el hash PBKDF2 no congela la UI
            catch (Exception e) { Debug.LogException(e); r = ResultadoAuth.Error(CodigoAuth.ErrorAlmacenamiento, e.Message); }
            ocupado = false;
            if (this == null || !Abierto) return;     // se cerró mientras tanto

            Mensaje(r.Mensaje ?? (r.Exito ? "Listo." : "No se pudo completar la operación."), r.Exito);
            if (r.Exito) alExito?.Invoke(r);
            if (sesion.Puede(Permiso.GestionarUsuarios)) RefrescarLista();
        }

        void CrearCuenta()
        {
            var solicitante = sesion.Actual;
            string u = campoUsuario.text, n = campoNombre.text, p = campoTemporal.text;
            var rol = rolNuevo;
            Ejecutar(() => auth.CrearUsuario(solicitante, u, n, p, rol), r =>
            {
                campoUsuario.text = campoNombre.text = campoTemporal.text = "";
                Notificador.Dialogo("Cuenta creada",
                    $"Usuario: <b>{r.Usuario.NombreUsuario}</b>\nRol: {PoliticaAcceso.Nombre(r.Usuario.Rol)}\n\n" +
                    "Comunica la contraseña temporal por un canal seguro. Se pedirá cambiarla en el primer ingreso.");
            });
        }

        void Restablecer(Usuario objetivo)
        {
            var solicitante = sesion.Actual;
            void Confirmado() => Ejecutar(() => auth.RestablecerContrasena(solicitante, objetivo.Id), MostrarTemporal);
            Notificador.Dialogo("Restablecer contraseña",
                $"Se generará una contraseña temporal para <b>{objetivo.NombreUsuario}</b> y se desbloqueará la cuenta. ¿Continuar?",
                new OpcionDialogo("Cancelar"),
                new OpcionDialogo("Restablecer", Confirmado, true));
        }

        static void MostrarTemporal(ResultadoAuth r)
        {
            Notificador.Dialogo("Contraseña temporal",
                $"Usuario: <b>{r.Usuario.NombreUsuario}</b>\nContraseña temporal: <b>{r.Extra}</b>\n\n" +
                "Anótala ahora: no se volverá a mostrar. Se pedirá cambiarla en el próximo ingreso.");
        }

        void AlternarActivo(Usuario objetivo)
        {
            var solicitante = sesion.Actual;
            bool activar = !objetivo.Activo;
            Ejecutar(() => auth.CambiarEstado(solicitante, objetivo.Id, activar), null);
        }

        void SiguienteRol(Usuario objetivo)
        {
            var solicitante = sesion.Actual;
            var nuevo = objetivo.Rol == Rol.Consulta ? Rol.Operador : objetivo.Rol == Rol.Operador ? Rol.Administrador : Rol.Consulta;
            Ejecutar(() => auth.CambiarRol(solicitante, objetivo.Id, nuevo), null);
        }

        void NuevoCodigo()
        {
            var yo = sesion.Actual;
            Notificador.Dialogo("Nuevo código de recuperación",
                "Se generará un código nuevo y el anterior dejará de servir. ¿Continuar?",
                new OpcionDialogo("Cancelar"),
                new OpcionDialogo("Generar", () => Ejecutar(() => auth.NuevoCodigoRecuperacion(yo), r =>
                    Notificador.Dialogo("Guarda tu código de recuperación",
                        $"<size=34><b>{r.Extra}</b></size>\n\nAnótalo en un lugar seguro. No se volverá a mostrar.")), true));
        }

        void CambiarPropia()
        {
            var yo = sesion.Actual;
            string a = campoActual.text, n = campoNueva.text, c = campoConfirmar.text;
            campoActual.text = campoNueva.text = campoConfirmar.text = "";
            Ejecutar(() => auth.CambiarContrasena(yo, a, n, c), r => Notificador.Mostrar("Tu contraseña se actualizó.", TipoMensaje.Exito));
        }

        void ElegirRol(Rol r)
        {
            rolNuevo = r;
            for (int i = 0; i < fondoRol.Length; i++) fondoRol[i].color = i == (int)r ? UIFabrica.ColSel : UIFabrica.ColCard;
        }

        void Mensaje(string t, bool ok)
        {
            txtMensaje.text = t;
            txtMensaje.color = ok ? UIFabrica.ColExito : UIFabrica.ColError;
        }

        void RefrescarLista()
        {
            foreach (var f in filas) Destroy(f);
            filas.Clear();

            var ahora = DateTime.UtcNow;
            var lista = auth.ListarUsuarios(sesion.Actual);
            for (int i = 0; i < lista.Count && i < FilasMax; i++)
            {
                var u = lista[i];
                float y1 = 1f - i * (1f / FilasMax), y0 = y1 - (1f / FilasMax) + 0.01f;
                var fila = UIFabrica.Panel("Fila " + u.NombreUsuario, contenedorFilas, new Vector2(0f, y0), new Vector2(1f, y1), UIFabrica.ColCard);
                filas.Add(fila.gameObject);

                string estado = !u.Activo ? "  <color=#ff8a8a>(inactiva)</color>"
                              : u.EstaBloqueado(ahora) ? "  <color=#ffd166>(bloqueada)</color>"
                              : u.DebeCambiarContrasena ? "  <color=#9aa3b5>(temporal)</color>" : "";
                var t = UIFabrica.Texto(fila, "Texto", new Vector2(0f, 0f), new Vector2(0.48f, 1f), 17, TextAnchor.MiddleLeft, Color.white);
                t.text = $"<b>{u.NombreUsuario}</b> · {UIFabrica.SinRich(u.NombreVisible)} · {PoliticaAcceso.Nombre(u.Rol)}{estado}";
                t.resizeTextForBestFit = true; t.resizeTextMinSize = 11; t.resizeTextMaxSize = 17;

                bool esYo = u.Id == sesion.Actual.Id;
                // Rol: cada toque pasa al siguiente (Consulta → Operación → Administración)
                var bRol = UIFabrica.Boton(fila, PoliticaAcceso.Nombre(u.Rol), new Vector2(0.49f, 0.1f), new Vector2(0.65f, 0.9f), 15,
                                           UIFabrica.ColBoton, () => SiguienteRol(u));
                UIFabrica.Etiqueta(bRol).resizeTextForBestFit = true;
                UIFabrica.Etiqueta(bRol).resizeTextMinSize = 10; UIFabrica.Etiqueta(bRol).resizeTextMaxSize = 15;
                var bEstado = UIFabrica.Boton(fila, u.Activo ? "Desactivar" : "Activar", new Vector2(0.66f, 0.1f), new Vector2(0.81f, 0.9f), 15,
                                              u.Activo ? UIFabrica.ColPeligro : UIFabrica.ColPrimario, () => AlternarActivo(u));
                bEstado.interactable = !esYo;
                UIFabrica.Boton(fila, "Restablecer", new Vector2(0.82f, 0.1f), new Vector2(0.99f, 0.9f), 15, UIFabrica.ColBoton, () => Restablecer(u));
            }
            if (lista.Count > FilasMax)
                Mensaje($"Se muestran {FilasMax} de {lista.Count} cuentas.", true);
        }

        // ---------------- Construcción ----------------
        void Asegurar()
        {
            if (raiz != null) return;
            raiz = UIFabrica.Canvas("Canvas Usuarios", transform, 40);
            var fondo = UIFabrica.Panel("Fondo", raiz, Vector2.zero, Vector2.one, new Color(0, 0, 0, 0.75f));
            var w = UIFabrica.ModalDesplazable(fondo, new Vector2(0.08f, 0.06f), new Vector2(0.92f, 0.94f), 1900f);
            UIFabrica.Texto(w, "Titulo", new Vector2(0.02f, 0.91f), new Vector2(0.6f, 0.98f), 30, TextAnchor.MiddleLeft, Color.white).text = "Cuentas y acceso";

            // Lista de cuentas
            seccionLista = UIFabrica.Panel("Lista", w, new Vector2(0.02f, 0.10f), new Vector2(0.57f, 0.90f), new Color(0, 0, 0, 0));
            Responsivo.En(seccionLista, new Vector2(0.02f, 0.52f), new Vector2(0.98f, 0.90f));      // móvil: secciones apiladas
            UIFabrica.Texto(seccionLista, "Sub", new Vector2(0f, 0.93f), new Vector2(1f, 1f), 22, TextAnchor.MiddleLeft, Color.white).text = "Cuentas registradas";
            contenedorFilas = UIFabrica.Panel("Filas", seccionLista, new Vector2(0f, 0f), new Vector2(1f, 0.92f), new Color(0, 0, 0, 0));

            // Nueva cuenta
            seccionNueva = UIFabrica.Panel("Nueva", w, new Vector2(0.60f, 0.45f), new Vector2(0.98f, 0.90f), new Color(0, 0, 0, 0));
            Responsivo.En(seccionNueva, new Vector2(0.02f, 0.29f), new Vector2(0.98f, 0.505f));
            UIFabrica.Texto(seccionNueva, "Sub", new Vector2(0f, 0.86f), new Vector2(1f, 1f), 22, TextAnchor.MiddleLeft, Color.white).text = "Nueva cuenta";
            campoUsuario = UIFabrica.Campo(seccionNueva, "Usuario", new Vector2(0.02f, 0.70f), new Vector2(0.98f, 0.84f), "Usuario (ej.: ana.lopez)", false, ValidadorCredenciales.UsuarioMax, 20);
            campoNombre = UIFabrica.Campo(seccionNueva, "Nombre", new Vector2(0.02f, 0.54f), new Vector2(0.98f, 0.68f), "Nombre completo", false, ValidadorCredenciales.NombreMax, 20);
            campoTemporal = UIFabrica.Campo(seccionNueva, "Temporal", new Vector2(0.02f, 0.38f), new Vector2(0.98f, 0.52f), "Contraseña temporal", true, ValidadorCredenciales.ContrasenaMax, 20);
            var roles = new[] { Rol.Consulta, Rol.Operador, Rol.Administrador };
            for (int i = 0; i < roles.Length; i++)
            {
                var r = roles[i];
                float x0 = 0.02f + i * 0.33f;
                fondoRol[i] = UIFabrica.Boton(seccionNueva, PoliticaAcceso.Nombre(r), new Vector2(x0, 0.21f), new Vector2(x0 + 0.31f, 0.34f), 16,
                                              UIFabrica.ColCard, () => ElegirRol(r)).GetComponent<Image>();
            }
            UIFabrica.Boton(seccionNueva, "Crear cuenta", new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.16f), 22, UIFabrica.ColPrimario, CrearCuenta);

            // Mi contraseña
            var propia = UIFabrica.Panel("Propia", w, new Vector2(0.60f, 0.10f), new Vector2(0.98f, 0.43f), new Color(0, 0, 0, 0));
            Responsivo.En(propia, new Vector2(0.02f, 0.09f), new Vector2(0.98f, 0.275f));
            UIFabrica.Texto(propia, "Sub", new Vector2(0f, 0.80f), new Vector2(1f, 1f), 22, TextAnchor.MiddleLeft, Color.white).text = "Mi contraseña";
            campoActual = UIFabrica.Campo(propia, "Actual", new Vector2(0.02f, 0.60f), new Vector2(0.98f, 0.78f), "Contraseña actual", true, ValidadorCredenciales.ContrasenaMax, 20);
            campoNueva = UIFabrica.Campo(propia, "NuevaC", new Vector2(0.02f, 0.40f), new Vector2(0.98f, 0.58f), "Nueva contraseña", true, ValidadorCredenciales.ContrasenaMax, 20);
            campoConfirmar = UIFabrica.Campo(propia, "Confirmar", new Vector2(0.02f, 0.20f), new Vector2(0.98f, 0.38f), "Confirmar nueva contraseña", true, ValidadorCredenciales.ContrasenaMax, 20);
            UIFabrica.Boton(propia, "Cambiar mi contraseña", new Vector2(0.02f, 0.0f), new Vector2(0.49f, 0.17f), 18, UIFabrica.ColBoton, CambiarPropia);
            UIFabrica.Boton(propia, "Nuevo código de recuperación", new Vector2(0.51f, 0.0f), new Vector2(0.98f, 0.17f), 16, UIFabrica.ColBoton, NuevoCodigo);

            txtMensaje = UIFabrica.Texto(w, "Mensaje", new Vector2(0.02f, 0.01f), new Vector2(0.78f, 0.08f), 18, TextAnchor.MiddleLeft, UIFabrica.ColExito);
            UIFabrica.Boton(w, "Cerrar", new Vector2(0.80f, 0.015f), new Vector2(0.98f, 0.08f), 22, UIFabrica.ColBoton, Cerrar);
            raiz.gameObject.SetActive(false);
        }
    }
}
