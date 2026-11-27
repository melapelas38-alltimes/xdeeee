using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Composta.Core.Campo
{
    // Medición hecha a mano en la pila (sin sensores). Se guarda dentro del lote.
    [Serializable]
    public class InformeCampo
    {
        public string Id;
        public long FechaUtc;
        public int Tick;              // hora simulada en la que se aplicó
        public float Temperatura;     // °C, centro de la pila
        public float Humedad;         // %
        public float PesoKg;
        public string Nota;
        public string Usuario;
    }

    public sealed class ResultadoCalibracion
    {
        public float EstimadoTemp, EstimadoHum, EstimadoMasa;
        public float ErrorTemp, ErrorHum, ErrorMasa;           // medido - estimado
        public float Ganancia;                                  // 0..1: cuánto se confió en la medición
        public float ActividadAntes, ActividadDespues;
        public float EvaporacionAntes, EvaporacionDespues;
        public float DescomposicionAntes, DescomposicionDespues;
        public Etapa EtapaAntes, EtapaDespues;
        public bool AmbienteAjustado;

        public string Resumen()
        {
            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendFormat(c, "Temperatura: medida {0:F1} °C · el modelo estimaba {1:F1} °C\n", EstimadoTemp + ErrorTemp, EstimadoTemp);
            sb.AppendFormat(c, "Humedad: medida {0:F0} % · el modelo estimaba {1:F0} %\n", EstimadoHum + ErrorHum, EstimadoHum);
            sb.AppendFormat(c, "Peso: medido {0:0.#} kg · el modelo estimaba {1:0.#} kg\n\n", EstimadoMasa + ErrorMasa, EstimadoMasa);
            sb.Append("Lo que aprendió el modelo:\n");
            sb.Append("· ").Append(Cambio("Actividad biológica", ActividadAntes, ActividadDespues)).Append('\n');
            sb.Append("· ").Append(Cambio("Evaporación", EvaporacionAntes, EvaporacionDespues)).Append('\n');
            sb.Append("· ").Append(Cambio("Velocidad de descomposición", DescomposicionAntes, DescomposicionDespues));
            if (EtapaAntes != EtapaDespues) sb.Append("\n\nFase actualizada: ").Append(EtapaAntes).Append(" → ").Append(EtapaDespues);
            if (AmbienteAjustado) sb.Append("\n\nLa temperatura medida es menor que la ambiente configurada: se ajustó la ambiente.");
            return sb.ToString();
        }

        static string Cambio(string nombre, float antes, float despues)
        {
            float pct = antes <= 0f ? 0f : (despues / antes - 1f) * 100f;
            if (Math.Abs(pct) < 1f) return nombre + ": sin cambios";
            return string.Format(CultureInfo.InvariantCulture, "{0}: {1}{2:F0} %", nombre, pct > 0 ? "+" : "", pct);
        }
    }

    // Calibración por informes de campo ("asimilación de datos" con un filtro de Kalman simplificado):
    //  1) ajusta las perillas ocultas del modelo según el error de predicción (aprende cómo es ESTA pila);
    //  2) corrige el estado hacia lo medido con una ganancia que crece con el tiempo sin medir;
    //  3) suaviza el historial entre el informe anterior y este, para que las gráficas no den saltos.
    // Es determinista: al reanudar un lote se reaplica igual desde la bitácora.
    public sealed class CalibracionCampo
    {
        public const float GananciaMin = 0.5f, GananciaMax = 0.95f;
        public const float CambioMaxPorInforme = 0.2231436f;   // ln(1.25): como mucho ±25 % por informe

        readonly ModeloComposta m;
        readonly Historial historial;
        readonly List<InformeCampo> aplicados = new List<InformeCampo>();

        public IReadOnlyList<InformeCampo> Aplicados => aplicados;
        public int CantidadInformes => aplicados.Count;
        public int TickUltimoInforme { get; private set; }
        public float MasaUltimoInforme { get; private set; }
        public ResultadoCalibracion UltimoResultado { get; private set; }

        public CalibracionCampo(ModeloComposta modelo, Historial historial)
        {
            m = modelo ?? throw new ArgumentNullException(nameof(modelo));
            this.historial = historial ?? throw new ArgumentNullException(nameof(historial));
            MasaUltimoInforme = modelo.MasaKg;
        }

        // A tiempo (168 h) ≈ 0.7; con 3 semanas de atraso ≈ 0.93; un informe muy temprano pesa menos
        public static float Ganancia(int horasDesdeUltimo)
        {
            double g = 1.0 - 0.3 * Math.Exp(-(horasDesdeUltimo - 168.0) / 336.0);
            return (float)Math.Max(GananciaMin, Math.Min(GananciaMax, g));
        }

        public ResultadoCalibracion Aplicar(InformeCampo inf)
        {
            if (inf == null) throw new ArgumentNullException(nameof(inf));
            int horas = m.Ticks - TickUltimoInforme;
            var r = new ResultadoCalibracion
            {
                EstimadoTemp = m.Temperatura, EstimadoHum = m.Humedad, EstimadoMasa = m.MasaKg,
                ErrorTemp = inf.Temperatura - m.Temperatura,
                ErrorHum = inf.Humedad - m.Humedad,
                ErrorMasa = inf.PesoKg - m.MasaKg,
                Ganancia = Ganancia(horas),
                ActividadAntes = m.FactorActividad,
                EvaporacionAntes = m.FactorEvaporacion,
                DescomposicionAntes = m.FactorDescomposicion,
                EtapaAntes = m.Etapa
            };

            // Si en la pila hace menos que la "ambiente" configurada, la ambiente estaba mal: se corrige
            if (inf.Temperatura < m.TempAmbiente - 0.5f)
            {
                m.EstablecerTempAmbiente(inf.Temperatura);
                r.AmbienteAjustado = true;
            }

            // 1) Perillas: solo con al menos un día de evolución (un informe inmediato no dice nada del ritmo)
            if (horas >= 24)
            {
                // Sensibilidad local: con oxígeno suficiente, más actividad = más calor. Si la pila estuvo
                // ahogada (poco O2), más actividad consume más O2 y la ENFRÍA: el ajuste invierte su signo.
                float o2 = m.FactorO2Promedio;
                float signo = o2 >= 0.75f ? 1f : (o2 <= 0.45f ? -1f : (o2 - 0.6f) / 0.15f);
                if (m.Etapa != Etapa.Maduracion)                       // en maduración el calor ya no informa actividad
                    m.FactorActividad = Ajustar(m.FactorActividad, 0.015f * signo * r.ErrorTemp, 0.5f, 2f);
                m.FactorEvaporacion = Ajustar(m.FactorEvaporacion, -0.02f * r.ErrorHum, 0.3f, 3f);

                float perdidaEstimada = MasaUltimoInforme - m.MasaKg;
                float perdidaMedida = MasaUltimoInforme - inf.PesoKg;
                if (perdidaEstimada > 0.005f * MasaUltimoInforme && perdidaMedida > 0f)
                    m.FactorDescomposicion = Ajustar(m.FactorDescomposicion,
                        0.5f * (float)Math.Log(perdidaMedida / perdidaEstimada), 0.3f, 3f);
            }

            // 2) Estado: mezcla de estimación y medición
            float dT = r.Ganancia * (inf.Temperatura - m.Temperatura);
            float dH = r.Ganancia * (inf.Humedad - m.Humedad);
            float dM = r.Ganancia * (inf.PesoKg - m.MasaKg);
            m.AplicarCorreccion(dT, dH, dM);

            // 3) Gráficas: la corrección se reparte a lo largo del tramo, sin saltos
            historial.AjustarTramo(TickUltimoInforme, m.Ticks, dT, dH, dM);

            r.ActividadDespues = m.FactorActividad;
            r.EvaporacionDespues = m.FactorEvaporacion;
            r.DescomposicionDespues = m.FactorDescomposicion;
            r.EtapaDespues = m.Etapa;

            m.ReiniciarPromedioO2();
            inf.Tick = m.Ticks;
            aplicados.Add(inf);
            TickUltimoInforme = m.Ticks;
            MasaUltimoInforme = m.MasaKg;
            UltimoResultado = r;
            return r;
        }

        static float Ajustar(float factor, float cambioLog, float min, float max)
        {
            float c = Math.Max(-CambioMaxPorInforme, Math.Min(CambioMaxPorInforme, cambioLog));
            float v = factor * (float)Math.Exp(c);
            return v < min ? min : (v > max ? max : v);
        }

        // ---------------- Lectura para la UI ----------------
        public string Confianza(bool informeAtrasado)
        {
            if (aplicados.Count == 0) return "sin calibrar";
            if (informeAtrasado || aplicados.Count == 1) return "baja";
            return aplicados.Count == 2 ? "media" : "alta";
        }

        public string DescribirPila()
        {
            if (aplicados.Count == 0) return "aún sin informes";
            var partes = new List<string>();
            if (m.FactorActividad > 1.15f) partes.Add("más activa de lo esperado");
            else if (m.FactorActividad < 0.87f) partes.Add("más lenta de lo esperado");
            if (m.FactorEvaporacion > 1.15f) partes.Add("se seca más rápido");
            else if (m.FactorEvaporacion < 0.87f) partes.Add("retiene bien la humedad");
            if (m.FactorDescomposicion > 1.15f) partes.Add("se descompone rápido");
            else if (m.FactorDescomposicion < 0.87f) partes.Add("se descompone lento");
            return partes.Count == 0 ? "se comporta como lo esperado" : string.Join(", ", partes);
        }

        // Madurez ≈ cuando queda un 25 % del material degradable. Proyección con la tasa actual (decaimiento exponencial).
        public float? DiasHastaMadurez()
        {
            float objetivo = 0.25f * m.MasaDegradableInicial;
            if (m.MasaDegradable <= objetivo || m.Etapa == Etapa.Maduracion) return 0f;
            if (m.MasaDegradable <= 0f || m.UltimaPerdidaHora <= 1e-7f) return null;
            double k = m.UltimaPerdidaHora / m.MasaDegradable;
            double horas = Math.Log(m.MasaDegradable / objetivo) / k;
            if (double.IsNaN(horas) || horas > 24 * 365 * 2) return null;
            return (float)(horas / 24.0);
        }
    }
}
