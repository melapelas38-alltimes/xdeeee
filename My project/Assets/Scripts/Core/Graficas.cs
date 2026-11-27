using System.Collections.Generic;

namespace Composta.Core
{
    public enum Magnitud { Temperatura, PH, Humedad, Oxigeno }

    // El valor es cuántos ticks (horas) tiene cada bloque
    public enum Granularidad { Hora = 1, Dia = 24, Semana = 168 }

    public class Punto
    {
        public float X;      // en días simulados
        public float Prom;
        public float Min;
        public float Max;
    }

    public class Serie
    {
        public Magnitud Magnitud;
        public Granularidad Granularidad;
        public List<Punto> Puntos = new List<Punto>();
    }

    public static class Agregador
    {
        public static Serie Construir(IList<Lectura> lecturas, Magnitud m, Granularidad g)
        {
            var s = new Serie { Magnitud = m, Granularidad = g };
            int bloque = (int)g;
            int actual = -1, n = 0;
            float suma = 0, min = 0, max = 0;

            for (int i = 0; i < lecturas.Count; i++)
            {
                var l = lecturas[i];
                int idx = (l.Tick - 1) / bloque;

                if (idx != actual)
                {
                    if (n > 0) s.Puntos.Add(Cerrar(actual, bloque, suma, min, max, n));
                    actual = idx; suma = 0; n = 0;
                }

                float v = Valor(l, m);
                if (n == 0) { min = max = v; }
                else { if (v < min) min = v; if (v > max) max = v; }
                suma += v; n++;
            }
            if (n > 0) s.Puntos.Add(Cerrar(actual, bloque, suma, min, max, n));
            return s;
        }

        static Punto Cerrar(int idx, int bloque, float suma, float min, float max, int n)
            => new Punto { X = idx * bloque / 24f, Prom = suma / n, Min = min, Max = max };

        public static float Valor(Lectura l, Magnitud m)
        {
            switch (m)
            {
                case Magnitud.Temperatura: return l.Temperatura;
                case Magnitud.PH: return l.PH;
                case Magnitud.Humedad: return l.Humedad;
                default: return l.Oxigeno;
            }
        }

        // Escala del eje Y
        public static (float min, float max) Rango(Magnitud m)
        {
            switch (m)
            {
                case Magnitud.Temperatura: return (0f, 80f);
                case Magnitud.PH: return (4f, 10f);
                case Magnitud.Humedad: return (0f, 100f);
                default: return (0f, 22f);
            }
        }

        // Zona sombreada en verde
        public static (float min, float max) ZonaIdeal(Magnitud m)
        {
            switch (m)
            {
                case Magnitud.Temperatura: return (55f, 65f);
                case Magnitud.PH: return (6f, 8f);
                case Magnitud.Humedad: return (40f, 60f);
                default: return (8f, 21f);
            }
        }

        public static string Unidad(Magnitud m)
        {
            switch (m)
            {
                case Magnitud.Temperatura: return "°C";
                case Magnitud.PH: return "";
                default: return "%";
            }
        }
    }
}