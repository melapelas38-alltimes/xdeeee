using System;

namespace Composta.Core
{
    public enum TipoFalla { Ninguna = 0, Congelado = 1, Desconectado = 2, Deriva = 3, Picos = 4 }

    // Un sensor físico simulado: mide la "verdad" con ruido y puede fallar
    public class Sensor
    {
        public readonly string Nombre;
        public readonly Magnitud Magnitud;
        public TipoFalla Falla { get; private set; } = TipoFalla.Ninguna;

        readonly System.Random rnd;
        readonly float escala, ruido;
        int horasRestantes;                       // -1 = hasta que se repare a mano
        float sesgo, deriva, ultimo = float.NaN;

        public Sensor(string nombre, Magnitud magnitud, System.Random rnd)
        {
            Nombre = nombre; Magnitud = magnitud; this.rnd = rnd;
            var (min, max) = Agregador.Rango(magnitud);
            escala = max - min;
            ruido = escala * 0.005f;
        }

        public void Fallar(TipoFalla tipo, int horas = -1)
        {
            Falla = tipo;
            horasRestantes = horas;
            sesgo = 0f;
            if (tipo == TipoFalla.Deriva)
                deriva = (rnd.Next(2) == 0 ? 1f : -1f) * escala * 0.002f;   // 0.2% del rango por hora
        }

        public void Reparar() { Falla = TipoFalla.Ninguna; sesgo = 0f; horasRestantes = 0; }

        // null = sin señal
        public float? Leer(float real)
        {
            if (horasRestantes > 0 && --horasRestantes == 0) Reparar();

            float r = ((float)rnd.NextDouble() * 2f - 1f) * ruido;
            switch (Falla)
            {
                case TipoFalla.Desconectado:
                    return null;
                case TipoFalla.Congelado:
                    if (float.IsNaN(ultimo)) ultimo = real + r;
                    return ultimo;
                case TipoFalla.Deriva:
                    sesgo += deriva;
                    break;
                case TipoFalla.Picos:
                    if (rnd.NextDouble() < 0.25)
                        r += (rnd.Next(2) == 0 ? 1f : -1f) * escala * 0.2f;
                    break;
            }
            ultimo = real + sesgo + r;
            return ultimo;
        }
    }

    // Lo que el sistema "cree" de una variable después de fusionar los sensores
    public class Medicion
    {
        public Magnitud Magnitud;
        public float Valor;
        public bool Confiable;
        public Nivel Nivel;
        public string Diagnostico = "";
    }

    // 2 sensores por variable (A y B): detecta fallas y fusiona las lecturas
    public class PanelSensores
    {
        public float ProbFalla;                   // probabilidad por sensor y por hora

        const int HorasCongelado = 6;             // lecturas idénticas seguidas = congelado
        const float Tolerancia = 0.03f;           // % del rango permitido entre A y B

        readonly Sensor[,] sensores = new Sensor[4, 2];
        readonly float[] ultimoValido = new float[4];
        readonly float[,] previo = new float[4, 2];
        readonly int[,] iguales = new int[4, 2];
        readonly System.Random rnd;
        bool inicializado;

        public PanelSensores(int semilla)
        {
            rnd = new System.Random(semilla);
            for (int m = 0; m < 4; m++)
                for (int i = 0; i < 2; i++)
                    sensores[m, i] = new Sensor(i == 0 ? "A" : "B", (Magnitud)m, rnd);
        }

        public Sensor Obtener(Magnitud m, int i) => sensores[(int)m, i];

        public string InyectarAleatoria()
        {
            var m = (Magnitud)rnd.Next(4);
            int i = rnd.Next(2);
            var tipo = (TipoFalla)rnd.Next(1, 5);
            sensores[(int)m, i].Fallar(tipo, -1);
            return $"Falla inyectada: {tipo} en el sensor {sensores[(int)m, i].Nombre} de {Reglas.Nombre(m)}";
        }

        public void RepararTodo()
        {
            foreach (var s in sensores) s.Reparar();
        }

        public Medicion[] Medir(Lectura real)
        {
            if (!inicializado)
            {
                for (int m = 0; m < 4; m++) ultimoValido[m] = Agregador.Valor(real, (Magnitud)m);
                inicializado = true;              // calibración de fábrica
            }

            var resultado = new Medicion[4];
            for (int m = 0; m < 4; m++)
            {
                var mag = (Magnitud)m;
                float verdadero = Agregador.Valor(real, mag);
                var (rMin, rMax) = Agregador.Rango(mag);
                float escala = rMax - rMin;
                var val = new float[2];
                var ok = new bool[2];
                var motivo = new string[2];

                for (int i = 0; i < 2; i++)
                {
                    var sen = sensores[m, i];
                    if (ProbFalla > 0f && sen.Falla == TipoFalla.Ninguna && rnd.NextDouble() < ProbFalla)
                        sen.Fallar((TipoFalla)rnd.Next(1, 5), rnd.Next(24, 97));

                    float? lectura = sen.Leer(verdadero);
                    if (!lectura.HasValue) { motivo[i] = "desconectado"; iguales[m, i] = 0; continue; }

                    float v = lectura.Value;
                    iguales[m, i] = (v == previo[m, i]) ? iguales[m, i] + 1 : 0;
                    previo[m, i] = v;

                    if (iguales[m, i] >= HorasCongelado) { motivo[i] = "congelado"; continue; }
                    if (v < rMin - 0.1f * escala || v > rMax + 0.1f * escala) { motivo[i] = "fuera de rango"; continue; }

                    val[i] = v; ok[i] = true;
                }
                resultado[m] = Fusionar(m, val, ok, motivo, escala);
            }
            return resultado;
        }

        Medicion Fusionar(int m, float[] val, bool[] ok, string[] motivo, float escala)
        {
            var med = new Medicion { Magnitud = (Magnitud)m };
            int validos = (ok[0] ? 1 : 0) + (ok[1] ? 1 : 0);

            if (validos == 2)
            {
                if (Math.Abs(val[0] - val[1]) <= escala * Tolerancia)
                {
                    med.Valor = (val[0] + val[1]) / 2f;
                    med.Confiable = true;
                    med.Nivel = Nivel.Ok;
                    ultimoValido[m] = med.Valor;
                }
                else
                {
                    // Con 2 sensores se detecta el desacuerdo, pero no cuál tiene la razón
                    med.Valor = ultimoValido[m];
                    med.Confiable = false;
                    med.Nivel = Nivel.Aviso;
                    med.Diagnostico = $"Discrepancia entre sensores ({val[0]:F1} vs {val[1]:F1})";
                }
            }
            else if (validos == 1)
            {
                int bueno = ok[0] ? 0 : 1, malo = 1 - bueno;
                med.Valor = val[bueno];
                med.Confiable = true;
                med.Nivel = Nivel.Aviso;
                med.Diagnostico = $"Sensor {sensores[m, malo].Nombre} {motivo[malo]} (operando con el sensor {sensores[m, bueno].Nombre})";
                ultimoValido[m] = med.Valor;
            }
            else
            {
                med.Valor = ultimoValido[m];
                med.Confiable = false;
                med.Nivel = Nivel.Critico;
                med.Diagnostico = $"Sin datos válidos (A: {motivo[0]}, B: {motivo[1]})";
            }
            return med;
        }
    }
}