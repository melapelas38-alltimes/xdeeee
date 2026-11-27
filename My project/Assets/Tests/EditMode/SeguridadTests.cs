using System;
using System.Text;
using Composta.Core.Persistencia;
using Composta.Core.Seguridad;
using NUnit.Framework;

namespace Composta.Tests
{
    public class HasherTests
    {
        [Test]
        public void Pbkdf2_CoincideConVectorRfc7914()
        {
            // RFC 7914 §11: PBKDF2-HMAC-SHA256, P="passwd", S="salt", c=1, dkLen=64
            var r = HasherPbkdf2.Pbkdf2(Encoding.ASCII.GetBytes("passwd"), Encoding.ASCII.GetBytes("salt"), 1, 64);
            Assert.AreEqual("55ac046e56e3089fec1691c22544b605f94185216dde0465e68b9d57c20dacbc" +
                            "49ca9cccf179b645991664b39d77ef317c71b845b1e30bd509112041d3a19783",
                            BitConverter.ToString(r).Replace("-", "").ToLowerInvariant());
        }

        [Test]
        public void Verificar_AceptaCorrectaYRechazaIncorrecta()
        {
            var h = new HasherPbkdf2(1000);
            h.Generar("composta2026", out var hash, out var sal, out var it);
            Assert.IsTrue(h.Verificar("composta2026", hash, sal, it));
            Assert.IsFalse(h.Verificar("Composta2026", hash, sal, it));
            Assert.IsFalse(h.Verificar("composta2026", "no-es-base64!!", sal, it));
            Assert.IsFalse(h.Verificar("composta2026", hash, sal, 0));
        }

        [Test]
        public void Verificar_NormalizaUnicode()
        {
            var h = new HasherPbkdf2(1000);
            h.Generar("café123", out var hash, out var sal, out var it);   // é precompuesta
            Assert.IsTrue(h.Verificar("café123", hash, sal, it));        // e + acento combinable
        }

        [Test]
        public void MismaContrasena_GeneraSalesDistintas()
        {
            var h = new HasherPbkdf2(1000);
            h.Generar("composta2026", out var h1, out var s1, out _);
            h.Generar("composta2026", out var h2, out var s2, out _);
            Assert.AreNotEqual(s1, s2);
            Assert.AreNotEqual(h1, h2);
        }
    }

    public class ValidadorCredencialesTests
    {
        [TestCase("ana", true)]
        [TestCase("  Ana.Lopez ", true)]
        [TestCase("ab", false)]
        [TestCase("_ana", false)]
        [TestCase("josé", false)]
        [TestCase("ana lopez", false)]
        public void Usuario(string u, bool valido) => Assert.AreEqual(valido, ValidadorCredenciales.ErrorUsuario(u) == null);

        [TestCase("composta1", true)]
        [TestCase("corta1", false)]
        [TestCase("sololetras", false)]
        [TestCase("12345678", false)]
        public void Contrasena(string p, bool valida) => Assert.AreEqual(valida, ValidadorCredenciales.ErrorContrasena(p) == null);

        [Test]
        public void Contrasena_NoPuedeSerIgualAlUsuario()
            => Assert.IsNotNull(ValidadorCredenciales.ErrorContrasena("operador1", "Operador1"));

        [Test]
        public void Temporal_CumpleLaPolitica()
        {
            for (int i = 0; i < 50; i++)
                Assert.IsNull(ValidadorCredenciales.ErrorContrasena(ValidadorCredenciales.GenerarTemporal()));
        }
    }

    public class ServicioAutenticacionTests
    {
        AlmacenMemoria almacen;
        RelojFalso reloj;
        ServicioAutenticacion auth;

        [SetUp]
        public void Preparar()
        {
            almacen = new AlmacenMemoria();
            reloj = new RelojFalso();
            auth = Crear();
        }

        ServicioAutenticacion Crear()
            => new ServicioAutenticacion(new RepositorioUsuariosJson(almacen, new SerializadorPruebas()), new HasherPbkdf2(1000), reloj);

        Usuario Admin()
        {
            var r = auth.CrearAdministradorInicial("admin", "Admin", "composta1", "composta1");
            Assert.IsTrue(r.Exito, r.Mensaje);
            return r.Usuario;
        }

        [Test]
        public void PrimerArranque_RequiereConfiguracion_YLuegoNo()
        {
            Assert.IsTrue(auth.RequiereConfiguracionInicial);
            Admin();
            Assert.IsFalse(auth.RequiereConfiguracionInicial);
            Assert.AreEqual(CodigoAuth.NoPermitido, auth.CrearAdministradorInicial("otro", "Otro", "composta1", "composta1").Codigo);
        }

        [Test]
        public void Login_Correcto_ConUsuarioEnMayusculasYEspacios()
        {
            Admin();
            var r = auth.IniciarSesion("  ADMIN ", "composta1");
            Assert.IsTrue(r.Exito, r.Mensaje);
            Assert.AreEqual(Rol.Administrador, r.Usuario.Rol);
        }

        [Test]
        public void Login_MensajeGenerico_ParaUsuarioInexistenteYContrasenaMala()
        {
            Admin();
            var a = auth.IniciarSesion("nadie", "composta1");
            var b = auth.IniciarSesion("admin", "equivocada1");
            Assert.AreEqual(CodigoAuth.CredencialesInvalidas, a.Codigo);
            Assert.AreEqual(a.Mensaje, b.Mensaje);
        }

        [Test]
        public void Login_BloqueaTrasCincoFallos_YDesbloqueaConElTiempo()
        {
            Admin();
            for (int i = 0; i < ServicioAutenticacion.MaxIntentos; i++) auth.IniciarSesion("admin", "mala12345");
            Assert.AreEqual(CodigoAuth.CuentaBloqueada, auth.IniciarSesion("admin", "composta1").Codigo);

            reloj.Avanzar(ServicioAutenticacion.DuracionBloqueo + TimeSpan.FromSeconds(1));
            Assert.IsTrue(auth.IniciarSesion("admin", "composta1").Exito);
        }

        [Test]
        public void Bloqueo_SePersiste_EntreReinicios()
        {
            Admin();
            for (int i = 0; i < ServicioAutenticacion.MaxIntentos; i++) auth.IniciarSesion("admin", "mala12345");
            var nuevo = Crear();   // simula cerrar y abrir la app
            Assert.AreEqual(CodigoAuth.CuentaBloqueada, nuevo.IniciarSesion("admin", "composta1").Codigo);
        }

        [Test]
        public void UsuarioCreadoPorAdmin_DebeCambiarContrasena()
        {
            var admin = Admin();
            var c = auth.CrearUsuario(admin, "operador", "Operador Uno", "temporal1", Rol.Operador);
            Assert.IsTrue(c.Exito, c.Mensaje);

            var login = auth.IniciarSesion("operador", "temporal1");
            Assert.IsTrue(login.Exito);
            Assert.IsTrue(login.Usuario.DebeCambiarContrasena);

            Assert.IsFalse(auth.CompletarCambioObligatorio(login.Usuario, "temporal1", "temporal1").Exito);
            Assert.IsTrue(auth.CompletarCambioObligatorio(login.Usuario, "definitiva9", "definitiva9").Exito);
            Assert.IsTrue(auth.IniciarSesion("operador", "definitiva9").Exito);
            Assert.IsFalse(auth.IniciarSesion("operador", "temporal1").Exito);
        }

        [Test]
        public void NoAdmin_NoPuedeCrearUsuarios()
        {
            var admin = Admin();
            var op = auth.CrearUsuario(admin, "operador", "Op", "temporal1", Rol.Operador).Usuario;
            Assert.AreEqual(CodigoAuth.SinPermiso, auth.CrearUsuario(op, "intruso", "X", "temporal1", Rol.Administrador).Codigo);
            Assert.AreEqual(CodigoAuth.SinPermiso, auth.CrearUsuario(null, "intruso", "X", "temporal1", Rol.Administrador).Codigo);
        }

        [Test]
        public void UsuarioDuplicado_SeRechaza()
        {
            var admin = Admin();
            Assert.AreEqual(CodigoAuth.YaExiste, auth.CrearUsuario(admin, "ADMIN", "Dup", "temporal1", Rol.Consulta).Codigo);
        }

        [Test]
        public void NoSePuedeDesactivarElUltimoAdmin_NiASiMismo()
        {
            var admin = Admin();
            Assert.AreEqual(CodigoAuth.NoPermitido, auth.CambiarEstado(admin, admin.Id, false).Codigo);
        }

        [Test]
        public void CuentaDesactivada_NoInicia()
        {
            var admin = Admin();
            var op = auth.CrearUsuario(admin, "operador", "Op", "temporal1", Rol.Operador).Usuario;
            Assert.IsTrue(auth.CambiarEstado(admin, op.Id, false).Exito);
            Assert.AreEqual(CodigoAuth.CuentaInactiva, auth.IniciarSesion("operador", "temporal1").Codigo);
        }

        [Test]
        public void Restablecer_DevuelveTemporalQueFuncionaYDesbloquea()
        {
            var admin = Admin();
            var op = auth.CrearUsuario(admin, "operador", "Op", "temporal1", Rol.Operador).Usuario;
            for (int i = 0; i < ServicioAutenticacion.MaxIntentos; i++) auth.IniciarSesion("operador", "mala12345");

            var r = auth.RestablecerContrasena(admin, op.Id);
            Assert.IsTrue(r.Exito);
            var login = auth.IniciarSesion("operador", r.Extra);
            Assert.IsTrue(login.Exito, login.Mensaje);
            Assert.IsTrue(login.Usuario.DebeCambiarContrasena);
        }

        [Test]
        public void ArchivoDanado_UsaRespaldo()
        {
            Admin();
            auth.IniciarSesion("admin", "composta1");                 // segunda escritura: genera .bak
            almacen.Archivos[RepositorioUsuariosJson.Clave] = "{ basura";
            Assert.IsTrue(Crear().IniciarSesion("admin", "composta1").Exito);
        }

        [Test]
        public void ArchivoDanadoSinRespaldo_NoPermiteCrearAdminNuevo()
        {
            almacen.Archivos[RepositorioUsuariosJson.Clave] = "{ basura";
            var a = Crear();
            Assert.Throws<DatosCorruptosException>(() => { var _ = a.RequiereConfiguracionInicial; });
            Assert.IsFalse(a.CrearAdministradorInicial("hacker", "H", "composta1", "composta1").Exito);
        }

        [Test]
        public void Politica_DeRoles()
        {
            Assert.IsTrue(PoliticaAcceso.Tiene(Rol.Consulta, Permiso.VerPanel));
            Assert.IsFalse(PoliticaAcceso.Tiene(Rol.Consulta, Permiso.OperarComposta));
            Assert.IsTrue(PoliticaAcceso.Tiene(Rol.Operador, Permiso.RegistrarAportes));
            Assert.IsFalse(PoliticaAcceso.Tiene(Rol.Operador, Permiso.GestionarUsuarios));
            Assert.IsTrue(PoliticaAcceso.Tiene(Rol.Administrador, Permiso.InyectarFallas));
        }

    public class RecuperacionYRegistroTests
    {
        AlmacenMemoria almacen;
        ServicioAutenticacion auth;

        [SetUp]
        public void Preparar()
        {
            almacen = new AlmacenMemoria();
            auth = new ServicioAutenticacion(new RepositorioUsuariosJson(almacen, new SerializadorPruebas()), new HasherPbkdf2(1000), new RelojFalso());
        }

        [Test]
        public void AdminInicial_RecibeCodigo_YRecuperaConEl()
        {
            var r = auth.CrearAdministradorInicial("admin", "Admin", "composta1", "composta1");
            StringAssert.IsMatch("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", r.Extra);

            var rec = auth.RecuperarConCodigo("ADMIN", r.Extra.ToLowerInvariant().Replace("-", " "), "nueva2026", "nueva2026");
            Assert.IsTrue(rec.Exito, rec.Mensaje);
            Assert.IsTrue(auth.IniciarSesion("admin", "nueva2026").Exito);
            Assert.IsFalse(auth.IniciarSesion("admin", "composta1").Exito);

            // El código usado deja de servir; el nuevo sí
            Assert.IsFalse(auth.RecuperarConCodigo("admin", r.Extra, "otra20266", "otra20266").Exito);
            Assert.IsTrue(auth.RecuperarConCodigo("admin", rec.Extra, "otra20266", "otra20266").Exito);
        }

        [Test]
        public void CodigoIncorrecto_MensajeGenerico_YBloqueo()
        {
            auth.CrearAdministradorInicial("admin", "Admin", "composta1", "composta1");
            var a = auth.RecuperarConCodigo("admin", "AAAA-BBBB-CCCC-DDDD", "nueva2026", "nueva2026");
            var b = auth.RecuperarConCodigo("nadie", "AAAA-BBBB-CCCC-DDDD", "nueva2026", "nueva2026");
            Assert.AreEqual(a.Mensaje, b.Mensaje);
            for (int i = 0; i < ServicioAutenticacion.MaxIntentos; i++) auth.RecuperarConCodigo("admin", "XXXX", "nueva2026", "nueva2026");
            Assert.AreEqual(CodigoAuth.CuentaBloqueada, auth.RecuperarConCodigo("admin", "XXXX", "nueva2026", "nueva2026").Codigo);
        }

        [Test]
        public void Registro_CreaCuentaDeConsulta_ConCodigo()
        {
            auth.CrearAdministradorInicial("admin", "Admin", "composta1", "composta1");
            var r = auth.RegistrarCuenta("vecina", "Vecina", "huerto2026", "huerto2026");
            Assert.IsTrue(r.Exito, r.Mensaje);
            Assert.AreEqual(Rol.Consulta, r.Usuario.Rol);
            Assert.IsNotNull(r.Extra);
            Assert.IsTrue(auth.IniciarSesion("vecina", "huerto2026").Exito);
            Assert.AreEqual(CodigoAuth.YaExiste, auth.RegistrarCuenta("VECINA", "Otra", "huerto2026", "huerto2026").Codigo);
        }

        [Test]
        public void Registro_NoReemplazaLaConfiguracionInicial()
            => Assert.AreEqual(CodigoAuth.NoPermitido, auth.RegistrarCuenta("x123", "X", "huerto2026", "huerto2026").Codigo);

        [Test]
        public void CambiarRol_SoloAdmin_YSinQuedarseSinAdmin()
        {
            var admin = auth.CrearAdministradorInicial("admin", "Admin", "composta1", "composta1").Usuario;
            var v = auth.RegistrarCuenta("vecina", "Vecina", "huerto2026", "huerto2026").Usuario;
            Assert.AreEqual(CodigoAuth.SinPermiso, auth.CambiarRol(v, v.Id, Rol.Administrador).Codigo);
            Assert.IsTrue(auth.CambiarRol(admin, v.Id, Rol.Operador).Exito);
            Assert.AreEqual(Rol.Operador, auth.IniciarSesion("vecina", "huerto2026").Usuario.Rol);
            Assert.AreEqual(CodigoAuth.NoPermitido, auth.CambiarRol(admin, admin.Id, Rol.Consulta).Codigo);
        }

        [Test]
        public void ElCodigoNoSeGuardaEnTextoPlano()
        {
            var r = auth.CrearAdministradorInicial("admin", "Admin", "composta1", "composta1");
            string archivo = almacen.Archivos[RepositorioUsuariosJson.Clave];
            StringAssert.DoesNotContain(r.Extra.Replace("-", ""), archivo);
            StringAssert.DoesNotContain("composta1", archivo);
        }
    }
    }
}
