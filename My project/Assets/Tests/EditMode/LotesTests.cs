using System;
using System.IO;
using Composta.Core;
using Composta.Core.Lotes;
using Composta.Core.Persistencia;
using NUnit.Framework;

namespace Composta.Tests
{
    public class ReproductorLoteTests
    {
        static Lote LoteDePrueba()
        {
            var perfil = PerfilComposta.Crear(TipoComposta.PilaAbierta);
            return Lote.Crear(perfil, 4321, "tester", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }

        // Ejecuta "en vivo" registrando comandos igual que GestorLotes, y compara con la reconstrucción
        [Test]
        public void Reconstruir_ReproduceExactamenteElEstado()
        {
            var lote = LoteDePrueba();
            var vivo = new SistemaComposta(ReproductorLote.CrearPerfil(lote), lote.Semilla);

            void Cmd(TipoComando t, float v)
            {
                lote.Comandos.Add(new Comando { Tick = vivo.Modelo.Ticks, Tipo = t, Valor = v });
                ReproductorLote.Aplicar(vivo, lote.Comandos[lote.Comandos.Count - 1]);
            }

            Cmd(TipoComando.Automatico, 0f);
            Cmd(TipoComando.ProbFalla, 0f);
            for (int t = 0; t < 24 * 20; t++)
            {
                if (t == 30) Cmd(TipoComando.Voltear, 0f);
                if (t == 50) Cmd(TipoComando.Regar, -1f);
                if (t == 70) Cmd(TipoComando.Automatico, 1f);
                if (t == 100) Cmd(TipoComando.InyectarFalla, 0f);
                if (t == 160) Cmd(TipoComando.RepararSensores, 0f);
                if (t == 200) { Cmd(TipoComando.ProbFalla, 0.01f); Cmd(TipoComando.Regar, 12.5f); }
                vivo.Paso();
            }
            lote.TicksSimulados = vivo.Modelo.Ticks;

            var copia = ReproductorLote.Reconstruir(lote, out var ultimo);

            Assert.AreEqual(vivo.Modelo.Ticks, copia.Modelo.Ticks);
            Assert.AreEqual(vivo.Modelo.Temperatura, copia.Modelo.Temperatura);
            Assert.AreEqual(vivo.Modelo.Humedad, copia.Modelo.Humedad);
            Assert.AreEqual(vivo.Modelo.Oxigeno, copia.Modelo.Oxigeno);
            Assert.AreEqual(vivo.Modelo.PH, copia.Modelo.PH);
            Assert.AreEqual(vivo.Metricas.Volteos, copia.Metricas.Volteos);
            Assert.AreEqual(vivo.Metricas.Riegos, copia.Metricas.Riegos);
            Assert.AreEqual(vivo.Historial.Lecturas.Count, copia.Historial.Lecturas.Count);
            Assert.IsNotNull(ultimo);
            Assert.AreEqual(lote.TicksSimulados, ultimo.Medida.Tick);
        }

        [Test]
        public void Reconstruir_LoteSinHoras_NoAvanza()
        {
            var s = ReproductorLote.Reconstruir(LoteDePrueba(), out var ultimo);
            Assert.AreEqual(0, s.Modelo.Ticks);
            Assert.IsNull(ultimo);
        }

        [Test]
        public void Sanear_CorrigeValoresImposibles()
        {
            var l = LoteDePrueba();
            l.PesoKg = float.NaN;
            l.TicksSimulados = 10;
            l.Comandos.Add(new Comando { Tick = 99, Tipo = TipoComando.Voltear });     // más allá de lo simulado
            l.Comandos.Add(new Comando { Tick = 5, Tipo = (TipoComando)77 });          // tipo desconocido
            l.Comandos.Add(new Comando { Tick = 3, Tipo = TipoComando.Regar, Valor = float.PositiveInfinity });
            l.Comandos.Add(new Comando { Tick = 8, Tipo = TipoComando.Voltear });
            l.Comandos.Add(new Comando { Tick = 2, Tipo = TipoComando.Regar, Valor = -1f });
            l.Aportes = null;

            var avisos = ReproductorLote.Sanear(l);

            Assert.IsNotEmpty(avisos);
            Assert.AreEqual(500f, l.PesoKg);
            Assert.AreEqual(2, l.Comandos.Count);
            Assert.AreEqual(2, l.Comandos[0].Tick);     // quedó ordenado
            Assert.IsNotNull(l.Aportes);
        }

        [Test]
        public void Repositorio_GuardaYCargaElLoteCompleto()
        {
            var repo = new RepositorioLotesJson(new AlmacenMemoria(), new SerializadorPruebas());
            var l = LoteDePrueba();
            l.TicksSimulados = 48;
            l.Comandos.Add(new Comando { Tick = 5, Tipo = TipoComando.Regar, Valor = 12.5f, Usuario = "ana" });
            l.Aportes.Add(new Aporte { Id = "x", Kg = 3.5f, Material = TipoMaterial.Cafe, Nota = "hojas" });
            repo.Guardar(l);

            var c = repo.Cargar(l.Id);
            Assert.AreEqual(l.Nombre, c.Nombre);
            Assert.AreEqual(TipoComando.Regar, c.Comandos[0].Tipo);
            Assert.AreEqual(12.5f, c.Comandos[0].Valor);
            Assert.AreEqual(TipoMaterial.Cafe, c.Aportes[0].Material);
            Assert.AreEqual(l.Id, repo.UltimoActivo().Id);
        }

        [Test]
        public void Repositorio_IgnoraLotesDanados_SinRomperLosDemas()
        {
            var almacen = new AlmacenMemoria();
            var repo = new RepositorioLotesJson(almacen, new SerializadorPruebas());
            var bueno = LoteDePrueba();
            repo.Guardar(bueno);
            almacen.Archivos["lotes/" + new string('a', 32) + ".json"] = "{ roto";

            Assert.AreEqual(1, repo.Listar().Count);
            Assert.AreEqual(1, repo.ErroresDeLectura.Count);
            Assert.IsNull(repo.Cargar("../../usuarios"));
        }
    }

    public class ValidadorAporteTests
    {
        [TestCase("12,5", 12.5f)]
        [TestCase(" 12.5 ", 12.5f)]
        [TestCase("3", 3f)]
        [TestCase("0,333", 0.33f)]
        public void KgValidos(string texto, float esperado)
        {
            Assert.IsTrue(ValidadorAporte.IntentarLeerKg(texto, out var kg, out var err), err);
            Assert.AreEqual(esperado, kg, 0.0001f);
        }

        [TestCase("")]
        [TestCase("abc")]
        [TestCase("-3")]
        [TestCase("1.234,5")]
        [TestCase("1e3")]
        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("0")]
        [TestCase("6000")]
        public void KgInvalidos(string texto)
        {
            Assert.IsFalse(ValidadorAporte.IntentarLeerKg(texto, out _, out var err));
            Assert.IsNotEmpty(err);
        }

        [Test]
        public void Nota_SeLimpiaYRecorta()
        {
            Assert.AreEqual("a b", ValidadorAporte.LimpiarNota(" a\nb "));
            Assert.AreEqual(ValidadorAporte.NotaMax, ValidadorAporte.LimpiarNota(new string('x', 500)).Length);
        }
    }

    public class ExportadorCsvTests
    {
        [TestCase("hojas secas", "hojas secas")]
        [TestCase("a,b", "\"a,b\"")]
        [TestCase("dijo \"hola\"", "\"dijo \"\"hola\"\"\"")]
        [TestCase("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
        [TestCase("+1", "'+1")]
        [TestCase(null, "")]
        public void Campo(string entrada, string esperado) => Assert.AreEqual(esperado, ExportadorCsv.Campo(entrada));
    }

    public class AlmacenArchivosTests
    {
        string dir;

        [SetUp] public void Preparar() => dir = Path.Combine(Path.GetTempPath(), "composta_tests_" + Guid.NewGuid().ToString("N"));
        [TearDown] public void Limpiar() { if (Directory.Exists(dir)) Directory.Delete(dir, true); }

        [Test]
        public void Escritura_EsAtomica_YDejaRespaldo()
        {
            var a = new AlmacenArchivos(dir);
            a.Escribir("lotes/x.json", "uno");
            a.Escribir("lotes/x.json", "dos");
            Assert.AreEqual("dos", a.Leer("lotes/x.json"));
            Assert.AreEqual("uno", a.LeerRespaldo("lotes/x.json"));
            Assert.IsFalse(File.Exists(a.RutaAbsoluta("lotes/x.json") + ".tmp"));
            CollectionAssert.AreEqual(new[] { "lotes/x.json" }, a.Listar("lotes", ".json"));
        }

        [TestCase("../fuera.json")]
        [TestCase("/etc/passwd.json")]
        [TestCase("lotes/../../x.json")]
        [TestCase("Lotes/X.json")]
        [TestCase("lotes/x.exe")]
        public void ClavesPeligrosas_SeRechazan(string clave)
        {
            var a = new AlmacenArchivos(dir);
            Assert.Throws<ArgumentException>(() => a.Escribir(clave, "x"));
        }
    }
}
