using System;
using System.Collections.Generic;
using Composta.Core;
using Composta.Core.Campo;
using Composta.Core.Lotes;
using Composta.Core.Persistencia;
using NUnit.Framework;

namespace Composta.Tests
{
    public class CampoTests
    {
        // Valores del motor ANTES de agregar el modelo de campo: el modo Simular no debe cambiar.
        // (Tolerancia pequeña: Mono y .NET pueden diferir en el último bit de Math.Pow/Exp)
        [TestCase(TipoComposta.Domestica, 24.905235f, 51.610497f, 16.719925f, Etapa.Maduracion, 6, 5)]
        [TestCase(TipoComposta.PilaAbierta, 24.990349f, 53.106163f, 13.319912f, Etapa.Maduracion, 7, 4)]
        [TestCase(TipoComposta.Vermicomposta, 24.893705f, 72.08528f, 18.039955f, Etapa.Maduracion, 5, 5)]
        public void ModoSimular_DaLosMismosResultadosQueAntes(TipoComposta t, float temp, float hum, float o2, Etapa etapa, int volteos, int riegos)
        {
            var s = new SistemaComposta(PerfilComposta.Crear(t), 4321);
            s.Controlador.Activo = true;
            s.Sensores.ProbFalla = 0.01f;
            for (int i = 0; i < 24 * 40; i++) { if (i == 100) s.Regar(); if (i == 300) s.Voltear(); s.Paso(); }
            Assert.AreEqual(temp, s.Modelo.Temperatura, 1e-3f);
            Assert.AreEqual(hum, s.Modelo.Humedad, 1e-3f);
            Assert.AreEqual(o2, s.Modelo.Oxigeno, 1e-3f);
            Assert.AreEqual(etapa, s.Modelo.Etapa);
            Assert.AreEqual(volteos, s.Metricas.Volteos);
            Assert.AreEqual(riegos, s.Metricas.Riegos);
        }

        [Test]
        public void ModeloCampo_PierdeMasaYNoUsaSensores()
        {
            var s = new SistemaComposta(PerfilComposta.Crear(TipoComposta.PilaAbierta), 1, true);
            Assert.IsTrue(s.SinSensores);
            ResultadoPaso r = null;
            for (int i = 0; i < 24 * 14; i++) r = s.Paso();
            Assert.Less(s.Modelo.MasaKg, 500f);
            Assert.Greater(s.Modelo.MasaKg, 250f);                       // como mucho se pierde la fracción degradable
            Assert.AreEqual(r.Real.Temperatura, r.Medida.Temperatura);    // "lo medido" es la estimación
            foreach (var m in r.Mediciones) Assert.IsTrue(m.Confiable);
        }

        [Test]
        public void Ganancia_CreceConElTiempoSinMedir()
        {
            Assert.AreEqual(0.7f, CalibracionCampo.Ganancia(168), 0.01f);
            Assert.Greater(CalibracionCampo.Ganancia(672), 0.9f);
            Assert.Less(CalibracionCampo.Ganancia(0), 0.55f);                 // informe muy temprano: pesa menos
            Assert.GreaterOrEqual(CalibracionCampo.Ganancia(0), CalibracionCampo.GananciaMin);
            Assert.LessOrEqual(CalibracionCampo.Ganancia(100000), CalibracionCampo.GananciaMax);
        }

        [Test]
        public void Informe_AcercaElEstadoALoMedido_YAjustaPerillasConTope()
        {
            var s = new SistemaComposta(PerfilComposta.Crear(TipoComposta.PilaAbierta), 3, true);
            for (int i = 0; i < 168; i++) s.Paso();
            var m = s.Modelo;
            float tAntes = m.Temperatura, hAntes = m.Humedad;
            var r = s.Calibracion.Aplicar(new InformeCampo { Temperatura = tAntes - 20f, Humedad = hAntes - 15f, PesoKg = m.MasaKg * 0.9f });

            Assert.AreEqual(tAntes - 20f * r.Ganancia, m.Temperatura, 0.6f);   // pudo cambiar la ambiente/limitar
            Assert.Less(m.Humedad, hAntes);
            Assert.Greater(m.FactorEvaporacion, 1f);                          // se secó más de lo esperado
            Assert.LessOrEqual(m.FactorEvaporacion, 1.25f + 1e-4f);           // tope ±25 % por informe
            Assert.GreaterOrEqual(m.FactorActividad, 0.8f - 1e-4f);
            Assert.AreEqual(1, s.Calibracion.CantidadInformes);
        }

        [Test]
        public void Informe_SuavizaElHistorialSinSaltos()
        {
            var s = new SistemaComposta(PerfilComposta.Crear(TipoComposta.PilaAbierta), 3, true);
            for (int i = 0; i < 168; i++) s.Paso();
            float tFinal = s.Modelo.Temperatura;
            s.Calibracion.Aplicar(new InformeCampo { Temperatura = tFinal + 10f, Humedad = s.Modelo.Humedad, PesoKg = s.Modelo.MasaKg });
            var h = s.Historial.Lecturas;
            Assert.AreEqual(s.Modelo.Temperatura, h[h.Count - 1].Temperatura, 0.01f);   // la última lectura ya coincide
            Assert.Less(Math.Abs(h[83].Temperatura - h[84].Temperatura), 3.5f);         // sin escalón a mitad de tramo
        }

        [Test]
        public void Calibracion_ConvergeHaciaUnaPilaConParametrosDistintos()
        {
            var perfil = PerfilComposta.Crear(TipoComposta.PilaAbierta);
            var real = new SistemaComposta(perfil, 99, true);
            real.Modelo.FactorEvaporacion = 1.6f; real.Modelo.FactorDescomposicion = 1.5f; real.Modelo.FactorActividad = 1.4f;
            var mod = new SistemaComposta(perfil, 7, true);
            var sin = new SistemaComposta(perfil, 7, true);
            float errCal = 0, errSin = 0;
            for (int sem = 1; sem <= 8; sem++)
            {
                for (int h = 0; h < 168; h++)
                {
                    if (h > 0 && h % 48 == 0) { real.Voltear(); mod.Voltear(); sin.Voltear(); }
                    real.Paso(); mod.Paso(); sin.Paso();
                }
                if (sem >= 6)
                {
                    errCal += Math.Abs(mod.Modelo.Humedad - real.Modelo.Humedad) + Math.Abs(mod.Modelo.Temperatura - real.Modelo.Temperatura);
                    errSin += Math.Abs(sin.Modelo.Humedad - real.Modelo.Humedad) + Math.Abs(sin.Modelo.Temperatura - real.Modelo.Temperatura);
                }
                var R = real.Modelo;
                mod.Calibracion.Aplicar(new InformeCampo { Temperatura = R.Temperatura, Humedad = R.Humedad, PesoKg = R.MasaKg });
            }
            Assert.Less(errCal, errSin * 0.5f, $"calibrado {errCal:F1} vs sin calibrar {errSin:F1}");
            Assert.Greater(mod.Modelo.FactorEvaporacion, 1.2f);
        }

        [Test]
        public void LoteManual_ConInformes_SeReproduceIgual()
        {
            var lote = Lote.Crear(PerfilComposta.Crear(TipoComposta.PilaAbierta), 11, "x", DateTime.UtcNow, ModoLote.Manual);
            var vivo = new SistemaComposta(ReproductorLote.CrearPerfil(lote), lote.Semilla, true);
            for (int t = 0; t < 24 * 21; t++)
            {
                if (t > 0 && t % 168 == 0)
                {
                    var inf = new InformeCampo { Temperatura = vivo.Modelo.Temperatura + 5f, Humedad = vivo.Modelo.Humedad - 6f, PesoKg = vivo.Modelo.MasaKg * 0.95f };
                    lote.Informes.Add(inf);
                    var c = new Comando { Tick = vivo.Modelo.Ticks, Tipo = TipoComando.Informe, Valor = lote.Informes.Count - 1 };
                    lote.Comandos.Add(c);
                    ReproductorLote.Aplicar(vivo, c, lote);
                }
                vivo.Paso();
            }
            lote.TicksSimulados = vivo.Modelo.Ticks;

            // Ida y vuelta por JSON, como al guardar y reabrir
            var repo = new RepositorioLotesJson(new AlmacenMemoria(), new SerializadorPruebas());
            repo.Guardar(lote);
            var cargado = repo.Cargar(lote.Id);
            Assert.IsEmpty(ReproductorLote.Sanear(cargado));
            var copia = ReproductorLote.Reconstruir(cargado, out _);

            Assert.AreEqual(vivo.Modelo.Temperatura, copia.Modelo.Temperatura);
            Assert.AreEqual(vivo.Modelo.MasaKg, copia.Modelo.MasaKg);
            Assert.AreEqual(vivo.Modelo.FactorEvaporacion, copia.Modelo.FactorEvaporacion);
            Assert.AreEqual(2, copia.Calibracion.CantidadInformes);
        }

        [Test]
        public void Sanear_DescartaInformesQueNoExisten()
        {
            var l = Lote.Crear(PerfilComposta.Crear(TipoComposta.PilaAbierta), 1, "x", DateTime.UtcNow, ModoLote.Manual);
            l.TicksSimulados = 10;
            l.Comandos.Add(new Comando { Tick = 5, Tipo = TipoComando.Informe, Valor = 3 });
            ReproductorLote.Sanear(l);
            Assert.IsEmpty(l.Comandos);
        }

        [Test]
        public void Plan_SinRegistros_AlDia_Pendiente_Atrasado()
        {
            var ahora = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            var l = Lote.Crear(PerfilComposta.Crear(TipoComposta.PilaAbierta), 1, "x", ahora, ModoLote.Manual);
            Assert.AreEqual(EstadoInforme.SinRegistros, PlanInformes.Evaluar(l, ahora.AddDays(30)).Estado);

            l.Informes.Add(new InformeCampo { FechaUtc = ahora.Ticks });
            Assert.AreEqual(EstadoInforme.AlDia, PlanInformes.Evaluar(l, ahora.AddDays(3)).Estado);
            Assert.AreEqual(EstadoInforme.Pendiente, PlanInformes.Evaluar(l, ahora.AddDays(6.5)).Estado);
            Assert.AreEqual(EstadoInforme.Atrasado, PlanInformes.Evaluar(l, ahora.AddDays(10)).Estado);
        }

        [Test]
        public void Validador_ErroresYAdvertencias()
        {
            var perfil = PerfilComposta.Crear(TipoComposta.Domestica);
            var adv = new List<string>();
            Assert.IsFalse(ValidadorInforme.Validar("abc", "50", "20", perfil, null, out _, out _, out _, out var e1, adv));
            Assert.IsNotNull(e1);
            Assert.IsFalse(ValidadorInforme.Validar("50", "120", "20", perfil, null, out _, out _, out _, out _, adv));

            Assert.IsTrue(ValidadorInforme.Validar("40,5", "55", "20", perfil, null, out var t, out _, out _, out _, adv));
            Assert.AreEqual(40.5f, t, 1e-4f);
            Assert.IsEmpty(adv);                                    // 20 kg en una compostera doméstica es normal

            Assert.IsTrue(ValidadorInforme.Validar("40", "55", "300", perfil, null, out _, out _, out _, out _, adv));
            Assert.IsTrue(adv.Exists(a => a.StartsWith("PESO EXAGERADO")));

            adv.Clear();
            var anterior = new InformeCampo { Temperatura = 30, Humedad = 55, PesoKg = 20 };
            Assert.IsTrue(ValidadorInforme.Validar("31", "55", "25", perfil, anterior, out _, out _, out _, out _, adv));
            Assert.IsTrue(adv.Exists(a => a.Contains("subió")));
        }

        // ---------------- Pesos realistas ----------------
        [TestCase(TipoComposta.Domestica, 20f, NivelPeso.Optimo)]
        [TestCase(TipoComposta.Domestica, 10f, NivelPeso.FueraDeOptimo)]
        [TestCase(TipoComposta.Domestica, 1f, NivelPeso.Exagerado)]
        [TestCase(TipoComposta.Domestica, 300f, NivelPeso.Exagerado)]
        [TestCase(TipoComposta.Vermicomposta, 30f, NivelPeso.Optimo)]
        [TestCase(TipoComposta.PilaAbierta, 1500f, NivelPeso.FueraDeOptimo)]
        [TestCase(TipoComposta.Industrial, 200f, NivelPeso.Exagerado)]
        public void NivelesDePeso(TipoComposta tipo, float kg, NivelPeso esperado)
            => Assert.AreEqual(esperado, PerfilComposta.Crear(tipo).EvaluarPeso(kg));

        [Test]
        public void RangosDePeso_SonCoherentes()
        {
            foreach (TipoComposta t in Enum.GetValues(typeof(TipoComposta)))
            {
                var p = PerfilComposta.Crear(t);
                Assert.Less(p.ExtremoMin, p.PesoMin, t.ToString());
                Assert.Less(p.PesoMin, p.PesoMax, t.ToString());
                Assert.Less(p.PesoMax, p.ExtremoMax, t.ToString());
                Assert.AreEqual(NivelPeso.Optimo, p.NivelPesoActual, t + ": el peso por defecto debe ser óptimo");
            }
        }

        // ---------------- Análisis de registros reales (sin simulación) ----------------
        static Lote LoteManual(TipoComposta tipo, params (float dia, float t, float h, float kg)[] regs)
        {
            var inicio = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
            var l = Lote.Crear(PerfilComposta.Crear(tipo), 1, "x", inicio, ModoLote.Manual);
            foreach (var (dia, t, h, kg) in regs)
                l.Informes.Add(new InformeCampo { FechaUtc = inicio.AddDays(dia).Ticks, Temperatura = t, Humedad = h, PesoKg = kg });
            return l;
        }

        [Test]
        public void Analisis_SinRegistros_NoInventaNada()
        {
            var e = AnalisisCampo.Analizar(LoteManual(TipoComposta.PilaAbierta));
            Assert.AreEqual(0, e.Registros);
            Assert.IsNull(e.Fase);
            Assert.IsEmpty(e.Recomendaciones);
        }

        [Test]
        public void Analisis_FasesSegunRegistros()
        {
            Assert.AreEqual(Etapa.Mesofilica, AnalisisCampo.Analizar(LoteManual(TipoComposta.PilaAbierta, (0, 25, 55, 500))).Fase);
            Assert.AreEqual(Etapa.Termofilica, AnalisisCampo.Analizar(LoteManual(TipoComposta.PilaAbierta, (0, 25, 55, 500), (7, 58, 52, 470))).Fase);
            Assert.AreEqual(Etapa.Enfriamiento, AnalisisCampo.Analizar(LoteManual(TipoComposta.PilaAbierta,
                (0, 25, 55, 500), (7, 58, 52, 470), (14, 40, 50, 440))).Fase);
            Assert.AreEqual(Etapa.Maduracion, AnalisisCampo.Analizar(LoteManual(TipoComposta.PilaAbierta,
                (0, 25, 55, 500), (7, 58, 52, 470), (14, 40, 50, 440), (21, 30, 48, 400), (28, 26, 47, 398))).Fase);
        }

        [Test]
        public void Analisis_TendenciasYRecomendaciones()
        {
            var e = AnalisisCampo.Analizar(LoteManual(TipoComposta.PilaAbierta, (0, 25, 55, 500), (7, 50, 25, 450)));
            Assert.AreEqual(25f, e.DeltaTemp.Value, 1e-4f);
            Assert.AreEqual(10f, e.PerdidaSemanalPct.Value, 1e-3f);
            Assert.AreEqual(10f, e.PerdidaPesoTotalPct.Value, 1e-3f);
            Assert.AreEqual(NivelEstado.Critico, e.NivelHum);                  // 25 % < mínimo crítico (30 %)
            Assert.IsTrue(e.Recomendaciones.Exists(r => r.Titulo == "Regar"));
        }

        [Test]
        public void Analisis_PesoExagerado_GeneraAlertaCritica()
        {
            var e = AnalisisCampo.Analizar(LoteManual(TipoComposta.Domestica, (0, 25, 55, 300)));
            Assert.AreEqual(NivelEstado.Critico, e.NivelPeso);
            Assert.IsTrue(e.Recomendaciones.Exists(r => r.Titulo == "PESO EXAGERADO"));
        }

        [Test]
        public void Analisis_OrdenaPorFecha_AunqueSeGuardenDesordenados()
        {
            var l = LoteManual(TipoComposta.PilaAbierta, (7, 58, 52, 470), (0, 25, 55, 500));
            var e = AnalisisCampo.Analizar(l);
            Assert.AreEqual(58f, e.Ultimo.Temperatura);
            Assert.AreEqual(33f, e.DeltaTemp.Value, 1e-4f);
        }
    }
}
