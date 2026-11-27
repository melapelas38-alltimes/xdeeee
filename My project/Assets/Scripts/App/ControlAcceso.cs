using System;
using System.Threading.Tasks;
using Composta.Core;
using Composta.Core.Campo;
using Composta.Core.Lotes;
using Composta.Core.Persistencia;
using Composta.Core.Seguridad;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Composta.App
{
    // Orquesta el flujo de acceso de la app (presentador): login -> elegir/reanudar lote -> uso -> cierre.
    // Se crea solo al arrancar (no hay que tocar la escena). El símbolo COMPOSTA_SIN_LOGIN lo desactiva
    // SOLO dentro del Editor (para depurar): en una build el login es siempre obligatorio.
    [DefaultExecutionOrder(-500)]
    public sealed class ControlAcceso : MonoBehaviour
    {
        const string PrefUltimoUsuario = "composta.ultimoUsuario";

        [Tooltip("Cierra la sesión tras estos minutos sin teclado ni ratón (0 = nunca)")]
        public float minutosInactividad = 15f;

        public static ControlAcceso Instancia { get; private set; }

        ServiciosApp svc;
        SimuladorComposta sim;
        PanelUI panelUI;
        PanelBuild panelBuild;
        GestorLotes lotes;

        PanelLogin login;
        PanelSesion barra;
        PanelAportes aportes;
        PanelUsuarios usuarios;
        TableroManual tableroManual;           // vista del modo Control Manual (sin simulación)
        PanelRegistroCampo registro;           // formulario de medición de campo
        VentanaPrediccion prediccion;          // pronóstico aislado + Google Calendar
        float proximoRefrescoManual;

        Usuario pendienteDeCambio;
        float ultimaActividad;
        bool pausaAlSalir;

#if !(COMPOSTA_SIN_LOGIN && UNITY_EDITOR)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Arrancar()
        {
            if (Instancia != null) return;
            var go = new GameObject("[ControlAcceso]");
            DontDestroyOnLoad(go);
            go.AddComponent<ControlAcceso>();
        }
#endif

        void Awake()
        {
            if (Instancia != null && Instancia != this) { Destroy(gameObject); return; }
            Instancia = this;
            login = gameObject.AddComponent<PanelLogin>();
            barra = gameObject.AddComponent<PanelSesion>();
            aportes = gameObject.AddComponent<PanelAportes>();
            usuarios = gameObject.AddComponent<PanelUsuarios>();
            tableroManual = gameObject.AddComponent<TableroManual>();
            registro = gameObject.AddComponent<PanelRegistroCampo>();
            prediccion = gameObject.AddComponent<VentanaPrediccion>();
            tableroManual.PedirRegistro += AbrirRegistro;
            tableroManual.PedirAportes += AbrirAportes;
            tableroManual.PedirExportar += Exportar;
            tableroManual.PedirPrediccion += PredecirLoteActual;
            registro.AlRegistrar += () => proximoRefrescoManual = 0f;

            login.EnviarLogin += AlEnviarLogin;
            login.EnviarConfiguracion += AlEnviarConfiguracion;
            login.EnviarCambio += AlEnviarCambio;
            login.EnviarRegistro += AlEnviarRegistro;
            login.EnviarRecuperacion += AlEnviarRecuperacion;
            login.CancelarCambio += () => { pendienteDeCambio = null; MostrarPantallaAcceso(); };
            login.Reintentar += IniciarFlujo;
            barra.PedirSalir += () => CerrarSesion(null);
            barra.PedirCuenta += () => usuarios.Abrir(svc.Autenticacion, svc.Sesion);
        }

        void OnEnable() => SceneManager.sceneLoaded += AlCargarEscena;
        void OnDisable() => SceneManager.sceneLoaded -= AlCargarEscena;

        void OnDestroy()
        {
            if (Instancia == this) Instancia = null;
            lotes?.Dispose();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ReiniciarEstatico() => Instancia = null;

        // sceneLoaded llega después de Awake/OnEnable de la escena y ANTES de los Start:
        // así el simulador queda en pausa y PanelBuild no abre su ventana antes del login.
        void AlCargarEscena(Scene escena, LoadSceneMode modo)
        {
            sim = FindAnyObjectByType<SimuladorComposta>();
            if (sim == null) return;                                  // escena sin simulador: nada que proteger
            panelUI = FindAnyObjectByType<PanelUI>();
            panelBuild = FindAnyObjectByType<PanelBuild>();

            sim.pausado = true;
            MostrarEspacioDeTrabajo(false);          // sin sesión no se dibuja el tablero
            UIFabrica.AsegurarEventSystem();
            Cablear();
            IniciarFlujo();
        }

        void Cablear()
        {
            try { svc = ServiciosApp.Obtener(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                login.MostrarErrorFatal("No se pudo abrir la carpeta de datos de la aplicación:\n" + e.Message);
                return;
            }

            GuardiaEntrada.SesionActiva = () => svc.Sesion.Activa;
            sim.Autorizador = p => svc.Sesion.Puede(p);
            sim.AlMensaje -= Notificador.Mostrar;
            sim.AlMensaje += Notificador.Mostrar;
            sim.AlReporte -= MostrarReporte;
            sim.AlReporte += MostrarReporte;

            lotes?.Dispose();
            lotes = new GestorLotes(sim, svc);
            lotes.AlCambiarLote += _ => ActualizarVista();
            lotes.AlCambiarRegistros += _ => proximoRefrescoManual = 0f;

            if (panelBuild != null)
            {
                panelBuild.PedirLoteManual -= CrearLoteManual;
                panelBuild.PedirLoteManual += CrearLoteManual;
                panelBuild.PedirPrediccion -= PredecirParametros;
                panelBuild.PedirPrediccion += PredecirParametros;
            }

            if (panelUI != null)
            {
                panelUI.PedirAportes -= AbrirAportes;
                panelUI.PedirAportes += AbrirAportes;
                panelUI.PedirExportar -= Exportar;
                panelUI.PedirExportar += Exportar;
                panelUI.PedirPrediccion -= PredecirLoteActual;
                panelUI.PedirPrediccion += PredecirLoteActual;
            }
        }

        static void MostrarReporte(string titulo, string cuerpo) => Notificador.Dialogo(titulo, cuerpo);

        // ---------------- Pantalla de acceso ----------------
        void IniciarFlujo()
        {
            if (svc == null) { Cablear(); if (svc == null) return; }
            MostrarPantallaAcceso();
        }

        void MostrarPantallaAcceso(string mensaje = null)
        {
            bool requiereConfiguracion;
            try { requiereConfiguracion = svc.Autenticacion.RequiereConfiguracionInicial; }
            catch (DatosCorruptosException e)
            {
                // Nunca se trata como "primer arranque": eso permitiría crear un administrador nuevo
                login.MostrarErrorFatal(e.Message + "\n\nRestaura una copia de «usuarios.json» en:\n" + svc.Almacen.Raiz);
                return;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                login.MostrarErrorFatal("No se pudo leer el archivo de cuentas: " + e.Message);
                return;
            }

            if (requiereConfiguracion) login.MostrarConfiguracionInicial();
            else login.MostrarLogin(LeerUltimoUsuario(), mensaje);
        }

        async void AlEnviarLogin(string usuario, string contrasena)
        {
            login.Ocupado(true);
            var r = await EnSegundoPlano(() => svc.Autenticacion.IniciarSesion(usuario, contrasena));
            if (this == null) return;
            login.Ocupado(false);
            login.LimpiarContrasenas();

            if (!r.Exito) { login.MostrarError(r.Mensaje); return; }
            GuardarUltimoUsuario(r.Usuario.NombreUsuario);

            if (r.Usuario.DebeCambiarContrasena)
            {
                pendienteDeCambio = r.Usuario;
                login.MostrarCambioObligatorio(r.Usuario.NombreVisible);
                return;
            }
            Entrar(r.Usuario);
        }

        async void AlEnviarConfiguracion(string usuario, string nombre, string contrasena, string confirmacion)
        {
            login.Ocupado(true);
            var r = await EnSegundoPlano(() => svc.Autenticacion.CrearAdministradorInicial(usuario, nombre, contrasena, confirmacion));
            if (this == null) return;
            login.Ocupado(false);
            login.LimpiarContrasenas();
            if (!r.Exito) { login.MostrarError(r.Mensaje); return; }
            GuardarUltimoUsuario(r.Usuario.NombreUsuario);
            MostrarCodigoRecuperacion(r.Extra, true);
            Entrar(r.Usuario);
        }

        async void AlEnviarRegistro(string usuario, string nombre, string contrasena, string confirmacion)
        {
            login.Ocupado(true);
            var r = await EnSegundoPlano(() => svc.Autenticacion.RegistrarCuenta(usuario, nombre, contrasena, confirmacion));
            if (this == null) return;
            login.Ocupado(false);
            login.LimpiarContrasenas();
            if (!r.Exito) { login.MostrarError(r.Mensaje); return; }
            GuardarUltimoUsuario(r.Usuario.NombreUsuario);
            MostrarCodigoRecuperacion(r.Extra, true);
            Notificador.Mostrar("Cuenta creada con permisos de consulta. Administración puede darte más permisos.", TipoMensaje.Info);
            Entrar(r.Usuario);
        }

        async void AlEnviarRecuperacion(string usuario, string codigo, string nueva, string confirmacion)
        {
            login.Ocupado(true);
            var r = await EnSegundoPlano(() => svc.Autenticacion.RecuperarConCodigo(usuario, codigo, nueva, confirmacion));
            if (this == null) return;
            login.Ocupado(false);
            login.LimpiarContrasenas();
            if (!r.Exito) { login.MostrarError(r.Mensaje); return; }
            GuardarUltimoUsuario(r.Usuario.NombreUsuario);
            Notificador.Mostrar("Contraseña restablecida.", TipoMensaje.Exito);
            MostrarCodigoRecuperacion(r.Extra, false);
            Entrar(r.Usuario);
        }

        // El código solo existe en este momento: en disco se guarda únicamente su hash
        static void MostrarCodigoRecuperacion(string codigo, bool primeraVez)
        {
            if (string.IsNullOrEmpty(codigo)) return;
            Notificador.Dialogo("Guarda tu código de recuperación",
                (primeraVez ? "" : "El código que usaste ya no sirve. Este es el nuevo:\n\n") +
                $"<size=34><b>{codigo}</b></size>\n\n" +
                "Anótalo en papel o guárdalo en un lugar seguro. Si olvidas tu contraseña, lo necesitarás en " +
                "«¿Olvidaste tu contraseña?». No se volverá a mostrar.",
                new OpcionDialogo("Ya lo guardé", null, true));
        }

        async void AlEnviarCambio(string nueva, string confirmacion)
        {
            var u = pendienteDeCambio;
            if (u == null) { MostrarPantallaAcceso(); return; }
            login.Ocupado(true);
            var r = await EnSegundoPlano(() => svc.Autenticacion.CompletarCambioObligatorio(u, nueva, confirmacion));
            if (this == null) return;
            login.Ocupado(false);
            login.LimpiarContrasenas();
            if (!r.Exito) { login.MostrarError(r.Mensaje); return; }
            pendienteDeCambio = null;
            Entrar(r.Usuario);
        }

        static async Task<ResultadoAuth> EnSegundoPlano(Func<ResultadoAuth> op)
        {
            try { return await Task.Run(op); }    // vuelve al hilo principal por el SynchronizationContext de Unity
            catch (Exception e)
            {
                Debug.LogException(e);
                return ResultadoAuth.Error(CodigoAuth.ErrorAlmacenamiento, "Error inesperado: " + e.Message);
            }
        }

        // ---------------- Sesión iniciada ----------------
        void Entrar(Usuario u)
        {
            svc.Sesion.Iniciar(u, svc.Reloj.UtcAhora);
            ultimaActividad = Time.unscaledTime;
            login.Ocultar();
            MostrarEspacioDeTrabajo(true);
            barra.Mostrar(u);
            AplicarPermisos();
            Notificador.Mostrar($"Hola, {u.NombreVisible}.", TipoMensaje.Exito);
            ElegirLote();
        }

        void AplicarPermisos()
        {
            Func<Permiso, bool> puede = svc.Sesion.Puede;
            if (panelUI != null) panelUI.AplicarPermisos(puede);
            if (panelBuild != null) panelBuild.PermitirCrear(puede(Permiso.CrearLote));
        }

        void ElegirLote()
        {
            bool puedeCrear = svc.Sesion.Puede(Permiso.CrearLote);

            // Volvió a entrar sin cerrar la app: se continúa con el lote ya cargado
            if (lotes.LoteActual != null)
            {
                sim.pausado = pausaAlSalir;
                ActualizarVista();
                AvisarRegistroSiCorresponde();
                return;
            }

            Lote ultimo = null;
            try { ultimo = svc.Lotes.UltimoActivo(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                Notificador.Mostrar("No se pudieron leer los lotes guardados: " + e.Message, TipoMensaje.Error);
            }

            if (ultimo != null)
            {
                string cuerpo = $"<b>{UIFabrica.SinRich(ultimo.Nombre)}</b>\n" +
                                (ultimo.Modo == ModoLote.Manual
                                    ? $"Control manual · {ultimo.Informes.Count} mediciones registradas"
                                    : $"Simulación · día simulado {ultimo.DiasSimulados:F1}") +
                                $" · {ultimo.Aportes.Count} aportes ({ultimo.TotalAportadoKg:0.#} kg)\n" +
                                $"Creado por {ultimo.CreadoPor}";
                if (puedeCrear)
                    Notificador.Dialogo("Lote en curso", cuerpo,
                        new OpcionDialogo("Nueva composta", () => { CerrarLoteGuardado(ultimo); AbrirNuevaComposta(); }),
                        new OpcionDialogo("Continuar", () => Reanudar(ultimo), true));
                else
                    Notificador.Dialogo("Lote en curso", cuerpo, new OpcionDialogo("Ver lote", () => Reanudar(ultimo), true));
            }
            else if (puedeCrear) AbrirNuevaComposta();
            else Notificador.Dialogo("Sin lotes activos",
                     "Todavía no hay una composta en curso.\nPide a una persona con rol de Operación que cree un lote.");
        }

        void AbrirNuevaComposta()
        {
            if (panelBuild != null) panelBuild.AbrirFormulario(lotes.LoteActual == null);
        }

        // Elegir "Nueva composta" archiva el lote pendiente (si no, quedaría "Activo" para siempre)
        void CerrarLoteGuardado(Lote lote)
        {
            try
            {
                lote.Estado = EstadoLote.Cerrado;
                lote.ActualizadoUtc = svc.Reloj.UtcAhora.Ticks;
                svc.Lotes.Guardar(lote);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Notificador.Mostrar("No se pudo archivar el lote anterior: " + e.Message, TipoMensaje.Aviso);
            }
        }

        void Reanudar(Lote lote)
        {
            if (lotes.Reanudar(lote, out string error))
            {
                sim.pausado = false;
                Notificador.Mostrar(lote.Modo == ModoLote.Manual ? "Lote de control manual abierto."
                                                                 : $"Simulación reanudada en el día {lote.DiasSimulados:F1}.", TipoMensaje.Exito);
                AvisarRegistroSiCorresponde();
            }
            else
            {
                Notificador.Mostrar("No se pudo reanudar el lote: " + error, TipoMensaje.Error);
                if (svc.Sesion.Puede(Permiso.CrearLote)) AbrirNuevaComposta();
            }
        }

        // ---------------- Vistas: Simulación y Control Manual nunca se mezclan ----------------
        void ActualizarVista()
        {
            bool sesion = svc != null && svc.Sesion.Activa;
            bool manual = sesion && lotes != null && lotes.EsLoteManual;
            if (panelUI != null) panelUI.MostrarTablero(sesion && !manual);
            if (panelBuild != null) panelBuild.MostrarHud(sesion);
            tableroManual.Mostrar(manual);
            proximoRefrescoManual = 0f;
        }

        void CrearLoteManual(PerfilComposta perfil)
        {
            if (lotes == null) return;
            if (lotes.CrearLoteManual(perfil, out string error))
            {
                if (panelBuild != null) panelBuild.Ocultar();
            }
            else Notificador.Mostrar("No se pudo crear el lote: " + error, TipoMensaje.Error);
        }

        void AvisarRegistroSiCorresponde()
        {
            if (lotes == null || !lotes.EsLoteManual) return;
            var s = lotes.Situacion();
            if (s.Estado == EstadoInforme.AlDia) return;
            var tipo = s.Estado == EstadoInforme.Atrasado ? TipoMensaje.Error : TipoMensaje.Aviso;
            Notificador.Mostrar(s.Estado == EstadoInforme.SinRegistros ? "Este lote espera su primera medición." : s.Texto(), tipo);
        }

        void AbrirRegistro()
        {
            if (svc == null || !svc.Sesion.Activa || lotes == null) return;
            if (!lotes.EsLoteManual)
            {
                Notificador.Mostrar("Las mediciones de campo son para lotes de Control Manual.", TipoMensaje.Info);
                return;
            }
            if (!svc.Sesion.Puede(Permiso.RegistrarInformes))
            {
                Notificador.Mostrar("Tu rol solo permite consultar. Pide a una persona de Operación que registre la medición.", TipoMensaje.Info);
                return;
            }
            registro.Abrir(lotes);
        }

        void MostrarEspacioDeTrabajo(bool visible)
        {
            if (visible) { ActualizarVista(); return; }
            if (panelUI != null) panelUI.MostrarTablero(false);
            if (panelBuild != null) panelBuild.MostrarHud(false);
            tableroManual.Mostrar(false);
        }

        void AbrirAportes()
        {
            if (!svc.Sesion.Activa) return;
            aportes.Abrir(lotes, svc.Sesion.Puede(Permiso.RegistrarAportes));
        }

        void Exportar()
        {
            if (lotes.Exportar(out string carpeta, out string error))
                Notificador.Dialogo("Exportación lista",
                    $"Se guardaron los archivos CSV en:\n<b>{carpeta}</b>" +
                    (Application.isMobilePlatform && error == null
                        ? "\n\nÁbrelos con la app Archivos (Descargas › Composta). Desde ahí puedes compartirlos por WhatsApp, correo o Drive."
                        : "") +
                    (error != null ? "\n\n" + error : ""),
                    // En Android no hay explorador de carpetas al que saltar: los archivos quedan en el almacenamiento de la app
                    Application.isMobilePlatform ? new OpcionDialogo("Entendido")
                                                 : new OpcionDialogo("Abrir carpeta", () => Application.OpenURL(new Uri(carpeta).AbsoluteUri)),
                    new OpcionDialogo("Aceptar", null, true));
            else
                Notificador.Mostrar("No se pudo exportar: " + error, TipoMensaje.Error);
        }

        public void CerrarSesion(string motivo)
        {
            if (svc == null || !svc.Sesion.Activa) return;
            lotes?.Guardar();
            pausaAlSalir = sim != null && sim.pausado;
            if (sim != null) sim.pausado = true;
            if (panelBuild != null) panelBuild.Ocultar();
            aportes.Cerrar();
            usuarios.Cerrar();
            registro.Cerrar();
            prediccion.Cerrar();
            MostrarEspacioDeTrabajo(false);
            Notificador.CerrarDialogos();
            svc.Sesion.Cerrar();
            barra.Ocultar();
            MostrarPantallaAcceso(motivo);
        }

        // ---------------- Inactividad y cierre ----------------
        void Update()
        {
            if (svc == null || !svc.Sesion.Activa) return;
            // El tablero manual se redibuja al cambiar los datos y cada 30 s (el estado "atrasado" depende del reloj)
            if (tableroManual.Visible && Time.unscaledTime >= proximoRefrescoManual)
            {
                proximoRefrescoManual = Time.unscaledTime + 30f;
                tableroManual.Refrescar(lotes, svc.Sesion.Puede(Permiso.RegistrarInformes));
            }
            if (HuboActividad()) ultimaActividad = Time.unscaledTime;
            if (minutosInactividad > 0f && Time.unscaledTime - ultimaActividad > minutosInactividad * 60f)
                CerrarSesion($"Sesión cerrada por {minutosInactividad:0} min de inactividad.");
        }

        // ---------------- Predicción (siempre en una instancia aislada del simulador) ----------------
        void PredecirParametros(PerfilComposta perfil)
        {
            if (!svc.Sesion.Activa) return;
            AbrirPrediccion(() => Core.Pronostico.MotorPronostico.DesdePerfil(perfil, sim != null ? sim.semilla : 2024),
                            Core.Pronostico.PlanCalendario.ClavePrediccion(null, perfil),
                            $"Plan: {perfil.Nombre} {perfil.PesoKg:0.#} kg");
        }

        void PredecirLoteActual()
        {
            if (!svc.Sesion.Activa) return;
            var lote = lotes?.LoteActual;
            if (lote == null)
            {
                if (sim != null && sim.Sistema != null) PredecirParametros(sim.Sistema.Perfil);
                return;
            }
            // Lectura de una COPIA: nunca altera el lote ni el simulador en vivo
            AbrirPrediccion(() => lote.Modo == ModoLote.Manual ? Core.Pronostico.MotorPronostico.DesdeMediciones(lote)
                                                               : Core.Pronostico.MotorPronostico.DesdeLoteSimulado(lote),
                            Core.Pronostico.PlanCalendario.ClavePrediccion(lote.Id, null), lote.Nombre);
        }

        void AbrirPrediccion(Func<Core.Pronostico.ResultadoPronostico> calcular, string clave, string nombre)
        {
            Core.Pronostico.ResultadoPronostico r;
            try { r = calcular(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                Notificador.Mostrar("No se pudo calcular la predicción: " + e.Message, TipoMensaje.Error);
                return;
            }
            prediccion.Abrir(r, clave, nombre);
        }

        static bool HuboActividad()
        {
            var t = Touchscreen.current;                // Android: cualquier toque cuenta como actividad
            if (t != null && t.primaryTouch.press.isPressed) return true;
            var k = Keyboard.current;
            if (k != null && k.anyKey.wasPressedThisFrame) return true;
            var m = Mouse.current;
            return m != null && (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame
                                 || m.delta.ReadValue().sqrMagnitude > 0.01f || m.scroll.ReadValue().sqrMagnitude > 0.01f);
        }

        void OnApplicationQuit() => lotes?.Guardar();

        // En escritorio se llama al perder el foco: guardar por si el sistema cierra la app
        void OnApplicationFocus(bool foco) { if (!foco) lotes?.Guardar(); }

        static string LeerUltimoUsuario()
        {
            try { return PlayerPrefs.GetString(PrefUltimoUsuario, ""); } catch { return ""; }
        }

        static void GuardarUltimoUsuario(string u)
        {
            try { PlayerPrefs.SetString(PrefUltimoUsuario, u); PlayerPrefs.Save(); } catch { /* conveniencia: no es crítico */ }
        }
    }
}
