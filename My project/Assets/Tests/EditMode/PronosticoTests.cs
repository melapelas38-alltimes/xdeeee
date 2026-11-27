using System;
using System.Linq;
using Composta.Core;
using Composta.Core.Campo;
using Composta.Core.Lotes;
using Composta.Core.Persistencia;
using Composta.Core.Pronostico;
using NUnit.Framework;

namespace Composta.Tests
{
    public class PronosticoTests
    {
        [Test]
        public void PilaAbierta_TieneFaseCalienteMaduracionYCosecha()
        {
            var r = MotorPronostico.DesdePerfil(PerfilComposta.Crear(TipoComposta.PilaAbierta));
            Assert.IsTrue(r.AlcanzaFaseCaliente);
            Assert.Greater(r.TempMaxima, 45f);
            var tipos = r.Hitos.Select(h => h.Tipo).ToList();
            CollectionAssert.Contains(tipos, TipoHito.InicioFaseCaliente);
            CollectionAssert.Contains(tipos, TipoHito.InicioMaduracion);
            CollectionAssert.Contains(tipos, TipoHito.Cosecha);
            CollectionAssert.Contains(tipos, TipoHito.RevisionSemanal);
            var mad = r.Hitos.First(h => h.Tipo == TipoHito.InicioMaduracion).DiasDesdeAhora;
            var cos = r.Hitos.First(h => h.Tipo == TipoHito.Cosecha).DiasDesdeAhora;
            Assert.AreEqual(MotorPronostico.DiasCosechaTrasMaduracion, cos - mad, 0.05f);
            Assert.LessOrEqual(r.DiasProyectados, MotorPronostico.DiasMaximos + 0.01f);
        }

        [Test]
        public void Hitos_OrdenadosYVolteosLimitados()
        {
            var r = MotorPronostico.DesdePerfil(PerfilComposta.Crear(TipoComposta.Industrial));
            for (int i = 1; i < r.Hitos.Count; i++) Assert.LessOrEqual(r.Hitos[i - 1].DiasDesdeAhora, r.Hitos[i].DiasDesdeAhora);
            var volteos = r.Hitos.Where(h => h.Tipo == TipoHito.Volteo).Select(h => h.DiasDesdeAhora).ToList();
            Assert.LessOrEqual(volteos.Count, MotorPronostico.VolteosMaximos);
            for (int i = 1; i < volteos.Count; i++) Assert.GreaterOrEqual(volteos[i] - volteos[i - 1], MotorPronostico.SeparacionMinimaVolteos - 1e-3f);
        }

        [Test]
        public void Vermicomposta_NoPrometeFaseCaliente()
        {
            var r = MotorPronostico.DesdePerfil(PerfilComposta.Crear(TipoComposta.Vermicomposta));
            Assert.IsFalse(r.AlcanzaFaseCaliente);
            Assert.IsFalse(r.Hitos.Any(h => h.Tipo == TipoHito.InicioFaseCaliente || h.Tipo == TipoHito.PicoTemperatura));
        }

        [Test]
        public void EsDeterminista()
        {
            var a = MotorPronostico.DesdePerfil(PerfilComposta.Crear(TipoComposta.Cerrada));
            var b = MotorPronostico.DesdePerfil(PerfilComposta.Crear(TipoComposta.Cerrada));
            Assert.AreEqual(a.Hitos.Count, b.Hitos.Count);
            Assert.AreEqual(a.TempMaxima, b.TempMaxima);
        }

        // ---------------- Aislamiento ----------------
        [Test]
        public void Pronostico_NoModificaUnLoteManual()
        {
            var inicio = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
            var lote = Lote.Crear(PerfilComposta.Crear(TipoComposta.Domestica), 0, "x", inicio, ModoLote.Manual);
            lote.Informes.Add(new InformeCampo { FechaUtc = inicio.AddDays(7).Ticks, Temperatura = 40, Humedad = 55, PesoKg = 25 });
            lote.Informes.Add(new InformeCampo { FechaUtc = inicio.Ticks, Temperatura = 22, Humedad = 60, PesoKg = 30 });
            var ser = new SerializadorPruebas();
            string antes = ser.Serializar(lote);

            var r = MotorPronostico.DesdeMediciones(lote);

            Assert.AreEqual(antes, ser.Serializar(lote));             // ni un byte cambió (ni siquiera el orden)
            Assert.AreEqual(0, lote.TicksSimulados);
            Assert.AreEqual(25f, r.Perfil.PesoKg);                    // usa la última medición por fecha
            Assert.IsTrue(r.Supuestos.Contains("última medición"));
        }

        [Test]
        public void Pronostico_DesdeLoteSimulado_NoAlteraElLoteNiSuSimulacion()
        {
            var lote = Lote.Crear(PerfilComposta.Crear(TipoComposta.PilaAbierta), 77, "x", DateTime.UtcNow, ModoLote.Simular);
            var vivo = new SistemaComposta(ReproductorLote.CrearPerfil(lote), lote.Semilla);
            for (int t = 0; t < 24 * 5; t++) vivo.Paso();
            lote.TicksSimulados = vivo.Modelo.Ticks;
            lote.Comandos.Add(new Comando { Tick = 3, Tipo = TipoComando.Voltear });
            lote.Comandos.Add(new Comando { Tick = 1, Tipo = TipoComando.Regar, Valor = -1 });   // desordenado a propósito
            var ser = new SerializadorPruebas();
            string antes = ser.Serializar(lote);
            float tempViva = vivo.Modelo.Temperatura;

            var r = MotorPronostico.DesdeLoteSimulado(lote);

            Assert.AreEqual(antes, ser.Serializar(lote));
            Assert.AreEqual(tempViva, vivo.Modelo.Temperatura);
            Assert.AreEqual(5f, r.DiaSimuladoInicial, 0.01f);
            Assert.Greater(r.Lecturas.Count, 0);
        }

        // ---------------- Calendario ----------------
        [Test]
        public void Plan_FechasLocalesYNuncaEnElPasado()
        {
            var r = MotorPronostico.DesdePerfil(PerfilComposta.Crear(TipoComposta.PilaAbierta));
            var ahora = new DateTime(2026, 6, 1, 20, 0, 0, DateTimeKind.Utc);             // 14:00 en UTC−6
            var zona = new ZonaHoraria("America/Mexico_City", TimeSpan.FromHours(-6), "test");
            var eventos = PlanCalendario.Generar(r, ahora, zona, "Lote X");
            Assert.AreEqual(r.Hitos.Count, eventos.Count);
            foreach (var e in eventos)
            {
                Assert.AreEqual(9, e.InicioLocal.Hour);
                Assert.Greater(e.InicioLocal, new DateTime(2026, 6, 1, 14, 0, 0));
            }
        }

        [Test]
        public void JsonEvento_ConZona_YEscapado()
        {
            var e = new EventoCalendario { Tipo = TipoHito.Cosecha, Titulo = "Cosecha \"lista\"", Descripcion = "línea1\nlínea2\\",
                                           InicioLocal = new DateTime(2026, 7, 10, 9, 0, 0), ColorId = "10" };
            var json = PlanCalendario.JsonEvento(e, new ZonaHoraria("Europe/Madrid", TimeSpan.FromHours(2), "t"), "lote-abc");
            StringAssert.Contains("\"summary\":\"Cosecha \\\"lista\\\"\"", json);
            StringAssert.Contains("\"description\":\"línea1\\nlínea2\\\\\"", json);
            StringAssert.Contains("\"start\":{\"dateTime\":\"2026-07-10T09:00:00\",\"timeZone\":\"Europe/Madrid\"}", json);
            StringAssert.Contains("\"end\":{\"dateTime\":\"2026-07-10T09:30:00\"", json);
            StringAssert.Contains("\"compostaPrediccion\":\"lote-abc\"", json);
            StringAssert.Contains("\"compostaHito\":\"Cosecha\"", json);
        }

        [Test]
        public void JsonEvento_SinZona_UsaDesplazamiento()
        {
            var e = new EventoCalendario { Tipo = TipoHito.Volteo, Titulo = "t", Descripcion = "d", InicioLocal = new DateTime(2026, 1, 5, 9, 0, 0) };
            var json = PlanCalendario.JsonEvento(e, new ZonaHoraria(null, TimeSpan.FromHours(-3), "dispositivo"), "k");
            StringAssert.Contains("\"start\":{\"dateTime\":\"2026-01-05T09:00:00-03:00\"}", json);
        }

        [Test]
        public void ClavePrediccion_EstablePorLoteYPorParametros()
        {
            var p = PerfilComposta.Crear(TipoComposta.PilaAbierta);
            Assert.AreEqual("lote-123", PlanCalendario.ClavePrediccion("123", p));
            Assert.AreEqual(PlanCalendario.ClavePrediccion(null, p), PlanCalendario.ClavePrediccion(null, PerfilComposta.Crear(TipoComposta.PilaAbierta)));
            Assert.AreNotEqual(PlanCalendario.ClavePrediccion(null, p), PlanCalendario.ClavePrediccion(null, PerfilComposta.Crear(TipoComposta.Cerrada)));
        }

        // ---------------- OAuth / HTTP ----------------
        [Test]
        public void Pkce_VectorRfc7636()
            => Assert.AreEqual("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", UtilGoogle.DesafioPkce("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

        [Test]
        public void Pkce_VerificadorValido()
        {
            var v = UtilGoogle.NuevoVerificadorPkce();
            Assert.That(v.Length, Is.InRange(43, 128));
            StringAssert.IsMatch("^[A-Za-z0-9_-]+$", v);
        }

        [Test]
        public void ParametrosDeRetorno()
        {
            var d = UtilGoogle.ParametrosConsulta("com.app:/oauth2redirect?state=a%2Bb&code=4%2F0AX&scope=x#frag");
            Assert.AreEqual("4/0AX", d["code"]);
            Assert.AreEqual("a+b", d["state"]);
        }

        [Test]
        public void FechaHttp()
        {
            Assert.IsTrue(UtilGoogle.IntentarLeerFechaHttp("Fri, 27 Nov 2026 18:30:05 GMT", out var utc));
            Assert.AreEqual(new DateTime(2026, 11, 27, 18, 30, 5, DateTimeKind.Utc), utc);
            Assert.AreEqual(DateTimeKind.Utc, utc.Kind);
            Assert.IsFalse(UtilGoogle.IntentarLeerFechaHttp("ayer", out _));
        }

        // ---------------- Zona horaria en PC ----------------
        [TestCase("Central Standard Time (Mexico)", "America/Mexico_City")]
        [TestCase("Romance Standard Time", "Europe/Madrid")]
        [TestCase("SA Pacific Standard Time", "America/Bogota")]
        [TestCase("Europe/Madrid", "Europe/Madrid")]          // macOS/Linux ya usan IANA
        [TestCase("Zona inventada", null)]
        [TestCase("", null)]
        public void ZonasWindows_AIana(string idSistema, string esperado) => Assert.AreEqual(esperado, ZonasWindows.AIana(idSistema));
    }
}
