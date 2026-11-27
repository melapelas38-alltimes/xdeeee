using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Composta.Core
{
    public enum Accion { Voltear, Regar }

    public class Orden
    {
        public Accion Accion;
        public string Motivo;
    }

    // Control automático con histéresis; los umbrales salen de los rangos del perfil
    public class Controlador
    {
        public bool Activo;
        public float O2Voltear, TempVoltear, HumRegar;
        public int EsperaVoltear = 48;
        public int EsperaRegar = 24;

        int ultimoVoltear = -100000, ultimoRegar = -100000;

        public Controlador(PerfilComposta perfil)
        {
            O2Voltear = perfil.LimitesDe(Magnitud.Oxigeno).AvisoMin + 3f;
            TempVoltear = perfil.LimitesDe(Magnitud.Temperatura).AvisoMax + 1f;
            HumRegar = perfil.LimitesDe(Magnitud.Humedad).AvisoMin + 5f;
        }

        public void Registrar(Accion a, int tick)
        {
            if (a == Accion.Voltear) ultimoVoltear = tick; else ultimoRegar = tick;
        }

        public List<Orden> Decidir(Lectura med, Medicion[] m, int tick)
        {
            var ordenes = new List<Orden>();
            if (!Activo) return ordenes;

            if (tick - ultimoVoltear >= EsperaVoltear)
            {
                if (m[(int)Magnitud.Oxigeno].Confiable && med.Oxigeno < O2Voltear)
                    ordenes.Add(new Orden { Accion = Accion.Voltear, Motivo = $"O2 medido {med.Oxigeno:F1}%" });
                else if (m[(int)Magnitud.Temperatura].Confiable && med.Temperatura > TempVoltear)
                    ordenes.Add(new Orden { Accion = Accion.Voltear, Motivo = $"Temperatura medida {med.Temperatura:F1}°C" });
            }

            if (tick - ultimoRegar >= EsperaRegar && m[(int)Magnitud.Humedad].Confiable && med.Humedad < HumRegar)
                ordenes.Add(new Orden { Accion = Accion.Regar, Motivo = $"Humedad medida {med.Humedad:F1}%" });

            return ordenes;
        }
    }

    // Se evalúa contra la VERDAD del modelo y contra los rangos del perfil
    public class Metricas
    {
        public int Horas;
        public int[] EnRango = new int[4];
        public int HorasCriticas;
        public int Volteos, Riegos;
        public float TempPico;
        public Etapa EtapaFinal;

        readonly PerfilComposta perfil;
        public Metricas(PerfilComposta perfil) { this.perfil = perfil; }

        public void Registrar(Lectura real)
        {
            Horas++;
            for (int m = 0; m < 4; m++)
            {
                var lim = perfil.LimitesDe((Magnitud)m);
                float v = Agregador.Valor(real, (Magnitud)m);
                if (v >= lim.AvisoMin && v <= lim.AvisoMax) EnRango[m]++;
                if (v < lim.CriticoMin || v > lim.CriticoMax) HorasCriticas++;
            }
            if (real.Temperatura > TempPico) TempPico = real.Temperatura;
            EtapaFinal = real.Etapa;
        }

        public float Porcentaje(Magnitud m) => Horas == 0 ? 0f : 100f * EnRango[(int)m] / Horas;

        public static string Tabla(string[] nombres, Metricas[] d)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Escenario | % en rango: Temp | pH | Hum | O2 | Horas críticas | Volteos | Riegos");
            for (int i = 0; i < d.Length; i++)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0} | {1:F0} | {2:F0} | {3:F0} | {4:F0} | {5} | {6} | {7}",
                    nombres[i],
                    d[i].Porcentaje(Magnitud.Temperatura), d[i].Porcentaje(Magnitud.PH),
                    d[i].Porcentaje(Magnitud.Humedad), d[i].Porcentaje(Magnitud.Oxigeno),
                    d[i].HorasCriticas, d[i].Volteos, d[i].Riegos));
            return sb.ToString();
        }

        public static string TablaPerfiles(string[] nombres, float[] pesos, Metricas[] d)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Perfil | Peso (kg) | Temp. pico (°C) | Etapa final | % O2 | % Hum | Volteos | Riegos");
            for (int i = 0; i < d.Length; i++)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0} | {1:F0} | {2:F1} | {3} | {4:F0} | {5:F0} | {6} | {7}",
                    nombres[i], pesos[i], d[i].TempPico, d[i].EtapaFinal,
                    d[i].Porcentaje(Magnitud.Oxigeno), d[i].Porcentaje(Magnitud.Humedad),
                    d[i].Volteos, d[i].Riegos));
            return sb.ToString();
        }
    }
}