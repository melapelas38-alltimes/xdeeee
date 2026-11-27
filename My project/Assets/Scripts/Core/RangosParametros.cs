using System;
using System.Globalization;

namespace Composta.Core
{
    // Cómo se configura y opera un lote. El valor numérico se guarda en disco: no reordenar.
    public enum ModoLote
    {
        Simular = 0,   // guiado: rangos recomendados, avisos que bloquean, control automático por defecto
        Manual = 1     // variables libres: rangos amplios, avisos informativos, control a cargo de la persona
    }

    public enum Parametro { Peso = 0, RelacionCN = 1, HumedadInicial = 2, Aislamiento = 3, TempAmbiente = 4 }

    public readonly struct Rango
    {
        public readonly float Min, Max;
        public Rango(float min, float max) { Min = min; Max = max; }
        public bool Contiene(float v) => !float.IsNaN(v) && v >= Min && v <= Max;
        public float Limitar(float v) => float.IsNaN(v) ? Min : (v < Min ? Min : (v > Max ? Max : v));
    }

    // Única fuente de verdad de los límites de cada parámetro según el modo.
    // La UI, el perfil y la validación de lotes guardados leen de aquí (antes eran constantes sueltas).
    public static class RangosParametros
    {
        // Límites físicos del modelo: ni siquiera el modo Manual puede salirse de aquí
        public const float PesoAbsMin = 0.5f, PesoAbsMax = 100000f;

        public static Rango De(ModoLote modo, Parametro p)
        {
            if (modo == ModoLote.Manual)
            {
                switch (p)
                {
                    case Parametro.Peso: return new Rango(PesoAbsMin, PesoAbsMax);
                    case Parametro.RelacionCN: return new Rango(5f, 100f);
                    case Parametro.HumedadInicial: return new Rango(5f, 95f);
                    case Parametro.Aislamiento: return new Rango(0.1f, 5f);
                    default: return new Rango(-10f, 50f);
                }
            }
            switch (p)
            {
                case Parametro.Peso: return new Rango(1f, 20000f);
                case Parametro.RelacionCN: return new Rango(10f, 60f);
                case Parametro.HumedadInicial: return new Rango(20f, 90f);
                case Parametro.Aislamiento: return new Rango(0.5f, 2f);
                default: return new Rango(0f, 40f);
            }
        }

        // Rango recomendado (la "barra verde"). null = no hay recomendación para ese parámetro.
        public static Rango? Recomendado(PerfilComposta perfil, Parametro p)
        {
            switch (p)
            {
                case Parametro.Peso: return new Rango(perfil.PesoMin, perfil.PesoMax);
                case Parametro.RelacionCN: return new Rango(25f, 30f);
                case Parametro.HumedadInicial:
                    var z = perfil.ZonaIdeal(Magnitud.Humedad);
                    return new Rango(z.min, z.max);
                default: return null;
            }
        }

        // Lee "12,5" o "12.5" (también negativos, para la temperatura). Rechaza NaN, infinito y exponentes.
        public static bool IntentarLeer(string texto, out float valor)
        {
            valor = 0f;
            string t = (texto ?? "").Trim();
            if (t.Length == 0) return false;
            int separadores = 0;
            foreach (char c in t) if (c == ',' || c == '.') separadores++;
            if (separadores > 1) return false;
            t = t.Replace(',', '.');
            return float.TryParse(t, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                                  CultureInfo.InvariantCulture, out valor)
                   && !float.IsNaN(valor) && !float.IsInfinity(valor);
        }

        public static string Nombre(ModoLote m) => m == ModoLote.Manual ? "Manual" : "Simulación";
    }
}
