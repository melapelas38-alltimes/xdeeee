using System;
using Composta.Core;
using Composta.Core.Lotes;
using Composta.Core.Persistencia;
using NUnit.Framework;

namespace Composta.Tests
{
    public class ModosTests
    {
        [Test]
        public void RangosManual_ContienenALosDeSimular()
        {
            foreach (Parametro p in Enum.GetValues(typeof(Parametro)))
            {
                var s = RangosParametros.De(ModoLote.Simular, p);
                var m = RangosParametros.De(ModoLote.Manual, p);
                Assert.LessOrEqual(m.Min, s.Min, p.ToString());
                Assert.GreaterOrEqual(m.Max, s.Max, p.ToString());
            }
        }

        [TestCase("12,5", 12.5f)]
        [TestCase("-3.5", -3.5f)]
        [TestCase(" 40 ", 40f)]
        public void IntentarLeer_Valido(string t, float esperado)
        {
            Assert.IsTrue(RangosParametros.IntentarLeer(t, out var v));
            Assert.AreEqual(esperado, v, 1e-4f);
        }

        [TestCase("")]
        [TestCase("abc")]
        [TestCase("1.000,5")]
        [TestCase("NaN")]
        [TestCase("1e9")]
        public void IntentarLeer_Invalido(string t) => Assert.IsFalse(RangosParametros.IntentarLeer(t, out _));

        [Test]
        public void PesoLibre_PermiteValoresExperimentales()
        {
            Assert.AreEqual(50000f, PerfilComposta.Crear(TipoComposta.PilaAbierta, 50000f, true).PesoKg);
            Assert.AreEqual(0.5f, PerfilComposta.Crear(TipoComposta.Domestica, 0.2f, true).PesoKg);
            Assert.AreEqual(1000f, PerfilComposta.Crear(TipoComposta.PilaAbierta, 50000f, false).PesoKg);  // sin libertad: rango recomendado del tipo
        }

        [Test]
        public void LoteGuardadoSinModo_SeLeeComoSimular()
        {
            var almacen = new AlmacenMemoria();
            var repo = new RepositorioLotesJson(almacen, new SerializadorPruebas());
            var l = Lote.Crear(PerfilComposta.Crear(TipoComposta.PilaAbierta), 1, "x", DateTime.UtcNow, ModoLote.Manual);
            repo.Guardar(l);
            // Simula un archivo de una versión anterior: sin el campo "Modo"
            string clave = "lotes/" + l.Id + ".json";
            almacen.Archivos[clave] = almacen.Archivos[clave].Replace("\"Modo\":1,", "");
            Assert.AreEqual(ModoLote.Simular, repo.Cargar(l.Id).Modo);
        }

        [Test]
        public void LoteManual_ConValoresExtremos_NoSeCorrigeAlCargar()
        {
            var p = PerfilComposta.Crear(TipoComposta.Domestica, 30000f, true);
            p.RelacionCN = 90f; p.HumedadInicial = 92f; p.Aislamiento = 4f; p.TempAmbiente = -8f;
            var l = Lote.Crear(p, 7, "x", DateTime.UtcNow, ModoLote.Manual);
            Assert.IsEmpty(ReproductorLote.Sanear(l));
            Assert.AreEqual(30000f, l.PesoKg);
            Assert.AreEqual(-8f, l.TempAmbiente);
        }

        [Test]
        public void AjusteDeTempAmbiente_SeReproduceIgual()
        {
            var lote = Lote.Crear(PerfilComposta.Crear(TipoComposta.PilaAbierta), 99, "x", DateTime.UtcNow, ModoLote.Manual);
            var vivo = new SistemaComposta(ReproductorLote.CrearPerfil(lote), lote.Semilla, true);   // lotes Manual = modelo de campo
            for (int t = 0; t < 24 * 10; t++)
            {
                if (t == 48)
                {
                    var c = new Comando { Tick = vivo.Modelo.Ticks, Tipo = TipoComando.TempAmbiente, Valor = 5f };
                    lote.Comandos.Add(c); ReproductorLote.Aplicar(vivo, c);
                }
                if (t == 90)
                {
                    var c = new Comando { Tick = vivo.Modelo.Ticks, Tipo = TipoComando.Regar, Valor = 37.5f };
                    lote.Comandos.Add(c); ReproductorLote.Aplicar(vivo, c);
                }
                vivo.Paso();
            }
            lote.TicksSimulados = vivo.Modelo.Ticks;

            var copia = ReproductorLote.Reconstruir(lote, out _);
            Assert.AreEqual(5f, copia.Modelo.TempAmbiente);
            Assert.AreEqual(vivo.Modelo.Temperatura, copia.Modelo.Temperatura);
            Assert.AreEqual(vivo.Modelo.Humedad, copia.Modelo.Humedad);
        }

        [Test]
        public void TempAmbiente_IgnoraNaN()
        {
            var m = new ModeloComposta(PerfilComposta.Crear(TipoComposta.PilaAbierta));
            m.EstablecerTempAmbiente(float.NaN);
            Assert.AreEqual(22f, m.TempAmbiente);
        }
    }
}
