using System;
using System.Collections.Generic;
using Composta.Core.Persistencia;

namespace Composta.Core.Seguridad
{
    public enum CodigoAuth
    {
        Ok, DatosInvalidos, CredencialesInvalidas, CuentaBloqueada, CuentaInactiva,
        SinPermiso, YaExiste, NoEncontrado, NoPermitido, ErrorAlmacenamiento
    }

    public sealed class ResultadoAuth
    {
        public CodigoAuth Codigo { get; private set; }
        public string Mensaje { get; private set; }
        public Usuario Usuario { get; private set; }
        public string Extra { get; private set; }          // p. ej. contraseña temporal generada
        public bool Exito => Codigo == CodigoAuth.Ok;

        public static ResultadoAuth Ok(Usuario u = null, string mensaje = null, string extra = null)
            => new ResultadoAuth { Codigo = CodigoAuth.Ok, Usuario = u, Mensaje = mensaje, Extra = extra };

        public static ResultadoAuth Error(CodigoAuth codigo, string mensaje)
            => new ResultadoAuth { Codigo = codigo, Mensaje = mensaje };
    }

    // Reglas de autenticación y administración de cuentas. No sabe nada de Unity ni de la UI.
    // Es seguro llamarlo desde un hilo de fondo (el hash PBKDF2 tarda ~100 ms y no debe congelar la UI).
    public sealed class ServicioAutenticacion
    {
        public const int MaxIntentos = 5;
        public static readonly TimeSpan DuracionBloqueo = TimeSpan.FromMinutes(5);
        const string MsgCredenciales = "Usuario o contraseña incorrectos.";

        readonly IRepositorioUsuarios repo;
        readonly IHasherContrasena hasher;
        readonly IReloj reloj;
        readonly object candado = new object();
        string hashSenuelo, salSenuelo;

        public ServicioAutenticacion(IRepositorioUsuarios repo, IHasherContrasena hasher, IReloj reloj)
        {
            this.repo = repo ?? throw new ArgumentNullException(nameof(repo));
            this.hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
            this.reloj = reloj ?? throw new ArgumentNullException(nameof(reloj));
        }

        // true en el primer arranque (o si se perdió el último administrador activo).
        // Lanza DatosCorruptosException si el archivo existe pero está dañado.
        public bool RequiereConfiguracionInicial
        {
            get
            {
                lock (candado)
                {
                    foreach (var u in repo.Todos())
                        if (u.Activo && u.EsAdministrador) return false;
                    return true;
                }
            }
        }

        public IReadOnlyList<Usuario> ListarUsuarios(Usuario solicitante)
        {
            lock (candado)
            {
                if (!EsAdminActivo(solicitante)) return new List<Usuario>();
                var lista = new List<Usuario>(repo.Todos());
                lista.Sort((a, b) => string.CompareOrdinal(a.NombreUsuario, b.NombreUsuario));
                return lista;
            }
        }

        // ---------------- Inicio de sesión ----------------
        public ResultadoAuth IniciarSesion(string usuario, string contrasena)
        {
            lock (candado)
            {
                return Proteger(() =>
                {
                    string nombre = ValidadorCredenciales.NormalizarUsuario(usuario);
                    if (nombre.Length == 0 || string.IsNullOrEmpty(contrasena))
                        return ResultadoAuth.Error(CodigoAuth.DatosInvalidos, "Escribe tu usuario y tu contraseña.");
                    if (contrasena.Length > ValidadorCredenciales.ContrasenaMax)
                        return ResultadoAuth.Error(CodigoAuth.CredencialesInvalidas, MsgCredenciales);

                    var ahora = reloj.UtcAhora;
                    var u = repo.BuscarPorNombre(nombre);
                    if (u == null)
                    {
                        VerificarSenuelo(contrasena);   // mismo tiempo de respuesta: no revela si el usuario existe
                        return ResultadoAuth.Error(CodigoAuth.CredencialesInvalidas, MsgCredenciales);
                    }

                    if (u.EstaBloqueado(ahora))
                    {
                        int min = (int)Math.Ceiling(new TimeSpan(u.BloqueadoHastaUtc - ahora.Ticks).TotalMinutes);
                        return ResultadoAuth.Error(CodigoAuth.CuentaBloqueada,
                            $"Demasiados intentos fallidos. Intenta de nuevo en {Math.Max(1, min)} min.");
                    }

                    if (!hasher.Verificar(contrasena, u.Hash, u.Sal, u.Iteraciones))
                    {
                        u.IntentosFallidos++;
                        if (u.IntentosFallidos >= MaxIntentos)
                        {
                            u.IntentosFallidos = 0;
                            u.BloqueadoHastaUtc = (ahora + DuracionBloqueo).Ticks;
                        }
                        repo.Guardar(u);
                        return ResultadoAuth.Error(CodigoAuth.CredencialesInvalidas, MsgCredenciales);
                    }

                    // La contraseña es correcta; recién ahora se informa si la cuenta está desactivada
                    if (!u.Activo)
                        return ResultadoAuth.Error(CodigoAuth.CuentaInactiva, "Esta cuenta está desactivada. Contacta a administración.");

                    u.IntentosFallidos = 0;
                    u.BloqueadoHastaUtc = 0;
                    u.UltimoAccesoUtc = ahora.Ticks;
                    if (u.Iteraciones < hasher.IteracionesActuales)    // migración transparente a más iteraciones
                        AsignarContrasena(u, contrasena);
                    repo.Guardar(u);
                    return ResultadoAuth.Ok(u);
                });
            }
        }

        // ---------------- Primer arranque ----------------
        public ResultadoAuth CrearAdministradorInicial(string usuario, string nombreVisible, string contrasena, string confirmacion)
        {
            lock (candado)
            {
                return Proteger(() =>
                {
                    if (!RequiereConfiguracionInicial)
                        return ResultadoAuth.Error(CodigoAuth.NoPermitido, "Ya existe una cuenta de administración.");
                    var r = CrearInterno(usuario, nombreVisible, contrasena, confirmacion, Rol.Administrador, false);
                    if (!r.Exito) return r;
                    r.Usuario.UltimoAccesoUtc = reloj.UtcAhora.Ticks;
                    string codigo = AsignarCodigoRecuperacion(r.Usuario);
                    repo.Guardar(r.Usuario);
                    return ResultadoAuth.Ok(r.Usuario, r.Mensaje, codigo);     // Extra = código: se muestra UNA vez
                });
            }
        }

        // ---------------- Administración ----------------
        public ResultadoAuth CrearUsuario(Usuario solicitante, string usuario, string nombreVisible, string contrasenaTemporal, Rol rol)
        {
            lock (candado)
            {
                return Proteger(() =>
                {
                    if (!EsAdminActivo(solicitante))
                        return ResultadoAuth.Error(CodigoAuth.SinPermiso, "Solo administración puede crear cuentas.");
                    // La persona deberá cambiar la contraseña en su primer ingreso
                    return CrearInterno(usuario, nombreVisible, contrasenaTemporal, contrasenaTemporal, rol, true);
                });
            }
        }

        public ResultadoAuth RestablecerContrasena(Usuario solicitante, string idObjetivo)
        {
            lock (candado)
            {
                return Proteger(() =>
                {
                    if (!EsAdminActivo(solicitante))
                        return ResultadoAuth.Error(CodigoAuth.SinPermiso, "Solo administración puede restablecer contraseñas.");
                    var u = repo.BuscarPorId(idObjetivo);
                    if (u == null) return ResultadoAuth.Error(CodigoAuth.NoEncontrado, "La cuenta no existe.");

                    string temporal = ValidadorCredenciales.GenerarTemporal();
                    AsignarContrasena(u, temporal);
                    u.DebeCambiarContrasena = u.Id != solicitante.Id;
                    u.IntentosFallidos = 0;
                    u.BloqueadoHastaUtc = 0;             // también desbloquea
                    repo.Guardar(u);
                    return ResultadoAuth.Ok(u, $"Contraseña temporal de {u.NombreUsuario}", temporal);
                });
            }
        }

        public ResultadoAuth CambiarEstado(Usuario solicitante, string idObjetivo, bool activo)
        {
            lock (candado)
            {
                return Proteger(() =>
                {
                    if (!EsAdminActivo(solicitante))
                        return ResultadoAuth.Error(CodigoAuth.SinPermiso, "Solo administración puede activar o desactivar cuentas.");
                    var u = repo.BuscarPorId(idObjetivo);
                    if (u == null) return ResultadoAuth.Error(CodigoAuth.NoEncontrado, "La cuenta no existe.");
                    if (!activo && u.Id == solicitante.Id)
                        return ResultadoAuth.Error(CodigoAuth.NoPermitido, "No puedes desactivar tu propia cuenta.");
                    if (!activo && u.EsAdministrador && ContarAdminsActivos() <= 1)
                        return ResultadoAuth.Error(CodigoAuth.NoPermitido, "Debe quedar al menos una cuenta de administración activa.");

                    u.Activo = activo;
                    repo.Guardar(u);
                    return ResultadoAuth.Ok(u, activo ? "Cuenta activada." : "Cuenta desactivada.");
                });
            }
        }

        // ---------------- Registro abierto ----------------
        // Cualquiera puede crear una cuenta desde el login, pero nace con rol de CONSULTA (solo ver).
        // Un administrador decide si le da más permisos. Así nunca se queda nadie sin poder entrar.
        public ResultadoAuth RegistrarCuenta(string usuario, string nombreVisible, string contrasena, string confirmacion)
        {
            lock (candado)
            {
                return Proteger(() =>
                {
                    if (RequiereConfiguracionInicial)
                        return ResultadoAuth.Error(CodigoAuth.NoPermitido, "Primero hay que crear la cuenta de administración.");
                    var r = CrearInterno(usuario, nombreVisible, contrasena, confirmacion, Rol.Consulta, false);
                    if (!r.Exito) return r;
                    r.Usuario.UltimoAccesoUtc = reloj.UtcAhora.Ticks;
                    string codigo = AsignarCodigoRecuperacion(r.Usuario);
                    repo.Guardar(r.Usuario);
                    return ResultadoAuth.Ok(r.Usuario, "Cuenta creada con permisos de consulta.", codigo);
                });
            }
        }

        // ---------------- Recuperación con código ----------------
        public ResultadoAuth RecuperarConCodigo(string usuario, string codigo, string nueva, string confirmacion)
        {
            lock (candado)
            {
                return Proteger(() =>
                {
                    const string msg = "Usuario o código de recuperación incorrectos.";
                    var ahora = reloj.UtcAhora;
                    var u = repo.BuscarPorNombre(ValidadorCredenciales.NormalizarUsuario(usuario));
                    string cod = ValidadorCredenciales.NormalizarCodigo(codigo);
                    if (u == null || !u.TieneCodigoRecuperacion)
                    {
                        VerificarSenuelo(cod);
                        return ResultadoAuth.Error(CodigoAuth.CredencialesInvalidas, msg);
                    }
                    if (u.EstaBloqueado(ahora))
                        return ResultadoAuth.Error(CodigoAuth.CuentaBloqueada, "Demasiados intentos fallidos. Espera unos minutos.");
                    if (!hasher.Verificar(cod, u.HashRecuperacion, u.SalRecuperacion, u.IteracionesRecuperacion))
                    {
                        u.IntentosFallidos++;
                        if (u.IntentosFallidos >= MaxIntentos) { u.IntentosFallidos = 0; u.BloqueadoHastaUtc = (ahora + DuracionBloqueo).Ticks; }
                        repo.Guardar(u);
                        return ResultadoAuth.Error(CodigoAuth.CredencialesInvalidas, msg);
                    }
                    if (!u.Activo) return ResultadoAuth.Error(CodigoAuth.CuentaInactiva, "Esta cuenta está desactivada. Contacta a administración.");

                    string err = ValidadorCredenciales.ErrorContrasena(nueva, u.NombreUsuario);
                    if (err != null) return ResultadoAuth.Error(CodigoAuth.DatosInvalidos, err);
                    if (nueva != confirmacion) return ResultadoAuth.Error(CodigoAuth.DatosInvalidos, "Las contraseñas no coinciden.");

                    AsignarContrasena(u, nueva);
                    u.DebeCambiarContrasena = false;
                    u.IntentosFallidos = 0; u.BloqueadoHastaUtc = 0;
                    u.UltimoAccesoUtc = ahora.Ticks;
                    string nuevoCodigo = AsignarCodigoRecuperacion(u);      // el usado ya no sirve
                    repo.Guardar(u);
                    return ResultadoAuth.Ok(u, "Contraseña restablecida.", nuevoCodigo);
                });
            }
        }

        // La persona conectada genera un código nuevo (el anterior deja de servir)
        public ResultadoAuth NuevoCodigoRecuperacion(Usuario usuario)
        {
            lock (candado)
            {
                return Proteger(() =>
                {
                    var u = usuario == null ? null : repo.BuscarPorId(usuario.Id);
                    if (u == null || !u.Activo) return ResultadoAuth.Error(CodigoAuth.NoEncontrado, "La sesión ya no es válida.");
                    string codigo = AsignarCodigoRecuperacion(u);
                    repo.Guardar(u);
                    return ResultadoAuth.Ok(u, "Código de recuperación nuevo generado.", codigo);
                });
            }
        }

        // ---------------- Roles ----------------
        public ResultadoAuth CambiarRol(Usuario solicitante, string idObjetivo, Rol nuevo)
        {
            lock (candado)
            {
                return Proteger(() =>
                {
                    if (!EsAdminActivo(solicitante))
                        return ResultadoAuth.Error(CodigoAuth.SinPermiso, "Solo administración puede cambiar roles.");
                    var u = repo.BuscarPorId(idObjetivo);
                    if (u == null) return ResultadoAuth.Error(CodigoAuth.NoEncontrado, "La cuenta no existe.");
                    if (u.EsAdministrador && nuevo != Rol.Administrador && u.Activo && ContarAdminsActivos() <= 1)
                        return ResultadoAuth.Error(CodigoAuth.NoPermitido, "Debe quedar al menos una cuenta de administración activa.");
                    u.Rol = nuevo;
                    repo.Guardar(u);
                    return ResultadoAuth.Ok(u, $"{u.NombreUsuario} ahora tiene rol de {PoliticaAcceso.Nombre(nuevo)}.");
                });
            }
        }

        // ---------------- Cuenta propia ----------------
        public ResultadoAuth CambiarContrasena(Usuario usuario, string actual, string nueva, string confirmacion)
        {
            lock (candado)
            {
                return Proteger(() =>
                {
                    var u = usuario == null ? null : repo.BuscarPorId(usuario.Id);
                    if (u == null || !u.Activo) return ResultadoAuth.Error(CodigoAuth.NoEncontrado, "La sesión ya no es válida.");
                    if (!hasher.Verificar(actual ?? "", u.Hash, u.Sal, u.Iteraciones))
                        return ResultadoAuth.Error(CodigoAuth.CredencialesInvalidas, "La contraseña actual no es correcta.");
                    if (actual == nueva)
                        return ResultadoAuth.Error(CodigoAuth.DatosInvalidos, "La nueva contraseña debe ser distinta de la actual.");
                    return EstablecerNueva(u, nueva, confirmacion);
                });
            }
        }

        // Tras un login correcto con contraseña temporal (no pide la actual: se acaba de verificar)
        public ResultadoAuth CompletarCambioObligatorio(Usuario usuario, string nueva, string confirmacion)
        {
            lock (candado)
            {
                return Proteger(() =>
                {
                    var u = usuario == null ? null : repo.BuscarPorId(usuario.Id);
                    if (u == null || !u.Activo || !u.DebeCambiarContrasena)
                        return ResultadoAuth.Error(CodigoAuth.NoPermitido, "No hay un cambio de contraseña pendiente.");
                    if (hasher.Verificar(nueva ?? "", u.Hash, u.Sal, u.Iteraciones))
                        return ResultadoAuth.Error(CodigoAuth.DatosInvalidos, "Elige una contraseña distinta de la temporal.");
                    return EstablecerNueva(u, nueva, confirmacion);
                });
            }
        }

        // ---------------- Internos ----------------
        ResultadoAuth EstablecerNueva(Usuario u, string nueva, string confirmacion)
        {
            string err = ValidadorCredenciales.ErrorContrasena(nueva, u.NombreUsuario);
            if (err != null) return ResultadoAuth.Error(CodigoAuth.DatosInvalidos, err);
            if (nueva != confirmacion) return ResultadoAuth.Error(CodigoAuth.DatosInvalidos, "Las contraseñas no coinciden.");
            AsignarContrasena(u, nueva);
            u.DebeCambiarContrasena = false;
            repo.Guardar(u);
            return ResultadoAuth.Ok(u, "Contraseña actualizada.");
        }

        ResultadoAuth CrearInterno(string usuario, string nombreVisible, string contrasena, string confirmacion, Rol rol, bool temporal)
        {
            string nombre = ValidadorCredenciales.NormalizarUsuario(usuario);
            string err = ValidadorCredenciales.ErrorUsuario(nombre)
                      ?? ValidadorCredenciales.ErrorNombreVisible(nombreVisible)
                      ?? ValidadorCredenciales.ErrorContrasena(contrasena, nombre);
            if (err != null) return ResultadoAuth.Error(CodigoAuth.DatosInvalidos, err);
            if (contrasena != confirmacion) return ResultadoAuth.Error(CodigoAuth.DatosInvalidos, "Las contraseñas no coinciden.");
            if (repo.BuscarPorNombre(nombre) != null) return ResultadoAuth.Error(CodigoAuth.YaExiste, "Ese nombre de usuario ya está en uso.");

            var u = new Usuario
            {
                Id = Guid.NewGuid().ToString("N"),
                NombreUsuario = nombre,
                NombreVisible = ValidadorCredenciales.LimpiarTexto(nombreVisible, ValidadorCredenciales.NombreMax),
                Rol = rol,
                Activo = true,
                DebeCambiarContrasena = temporal,
                CreadoUtc = reloj.UtcAhora.Ticks
            };
            AsignarContrasena(u, contrasena);
            repo.Guardar(u);
            return ResultadoAuth.Ok(u, $"Cuenta «{u.NombreUsuario}» creada.");
        }

        string AsignarCodigoRecuperacion(Usuario u)
        {
            string codigo = ValidadorCredenciales.GenerarCodigoRecuperacion();
            hasher.Generar(ValidadorCredenciales.NormalizarCodigo(codigo), out var hash, out var sal, out var it);
            u.HashRecuperacion = hash; u.SalRecuperacion = sal; u.IteracionesRecuperacion = it;
            return codigo;
        }

        void AsignarContrasena(Usuario u, string contrasena)
        {
            hasher.Generar(contrasena, out var hash, out var sal, out var it);
            u.Hash = hash; u.Sal = sal; u.Iteraciones = it;
        }

        bool EsAdminActivo(Usuario solicitante)
        {
            if (solicitante == null) return false;
            var u = repo.BuscarPorId(solicitante.Id);     // se relee: el rol pudo cambiar
            return u != null && u.Activo && u.EsAdministrador;
        }

        int ContarAdminsActivos()
        {
            int n = 0;
            foreach (var u in repo.Todos()) if (u.Activo && u.EsAdministrador) n++;
            return n;
        }

        void VerificarSenuelo(string contrasena)
        {
            if (hashSenuelo == null) hasher.Generar("senuelo-" + Guid.NewGuid(), out hashSenuelo, out salSenuelo, out _);
            hasher.Verificar(contrasena, hashSenuelo, salSenuelo, hasher.IteracionesActuales);
        }

        // Cualquier error de disco se convierte en un resultado legible (nunca una excepción en la UI)
        static ResultadoAuth Proteger(Func<ResultadoAuth> accion)
        {
            try { return accion(); }
            catch (DatosCorruptosException e) { return ResultadoAuth.Error(CodigoAuth.ErrorAlmacenamiento, e.Message); }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
            {
                return ResultadoAuth.Error(CodigoAuth.ErrorAlmacenamiento, "No se pudo leer o guardar el archivo de cuentas: " + e.Message);
            }
        }
    }
}
