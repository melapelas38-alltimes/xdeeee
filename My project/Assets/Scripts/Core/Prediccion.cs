using System;
using System.Collections.Generic;

namespace Composta.Core
{
    // Ajusta una recta a las últimas horas y estima cuándo se cruzará un límite de aviso
    public class Predictor
    {
        public int Ventana = 24;
        public float HorizonteHoras = 72f;
        public int Confirmaciones = 4;

        struct Vigilancia { public Magnitud M; public bool Sube; }

        // Solo variables de comportamiento casi lineal
        static readonly Vigilancia[] Vigiladas =
        {
            new Vigilancia { M = Magnitud.Oxigeno, Sube = false },
            new Vigilancia { M = Magnitud.Humedad, Sube = false },
            new Vigilancia { M = Magnitud.Humedad, Sube = true },
        };

        readonly PerfilComposta perfil;
        readonly List<float>[] buf = new List<float>[4];
        readonly int[] racha = new int[Vigiladas.Length];

        public Predictor(PerfilComposta perfil)
        {
            this.perfil = perfil;
            for (int i = 0; i < 4; i++) buf[i] = new List<float>();
        }

        public void Observar(Medicion[] med)
        {
            for (int m = 0; m < 4; m++)
            {
                if (!med[m].Confiable) { buf[m].Clear(); continue; }
                buf[m].Add(med[m].Valor);
                if (buf[m].Count > Ventana) buf[m].RemoveAt(0);
            }
        }

        public void Reiniciar()
        {
            for (int i = 0; i < 4; i++) buf[i].Clear();
            for (int k = 0; k < racha.Length; k++) racha[k] = 0;
        }

        public List<Alerta> Evaluar()
        {
            var lista = new List<Alerta>();
            for (int k = 0; k < Vigiladas.Length; k++)
            {
                var v = Vigiladas[k];
                var b = buf[(int)v.M];
                var lim = perfil.LimitesDe(v.M);
                bool acierto = false;
                float eta = 0f, limite = 0f;

                if (b.Count >= Ventana)
                {
                    float pend = Pendiente(b, out float ajustado);
                    var (rMin, rMax) = Agregador.Rango(v.M);
                    bool direccion = v.Sube ? pend > 0f : pend < 0f;

                    if (direccion && Math.Abs(pend) >= (rMax - rMin) * 0.0001f
                        && ajustado >= lim.AvisoMin && ajustado <= lim.AvisoMax)
                    {
                        limite = v.Sube ? lim.AvisoMax : lim.AvisoMin;
                        eta = (limite - ajustado) / pend;
                        acierto = eta > 0f && eta <= HorizonteHoras;
                    }
                }

                racha[k] = acierto ? racha[k] + 1 : 0;
                if (acierto && racha[k] >= Confirmaciones)
                {
                    string nombre = Reglas.Nombre(v.M);
                    lista.Add(new Alerta
                    {
                        Variable = $"Predicción {nombre} ({(v.Sube ? "sube" : "baja")})",
                        Nivel = Nivel.Aviso,
                        Mensaje = $"{nombre} llegaría a {limite:F0}{lim.Unidad} en ~{eta:F0} h",
                        Accion = v.Sube ? lim.AccionAlta : lim.AccionBaja
                    });
                }
            }
            return lista;
        }

        static float Pendiente(List<float> y, out float ultimoAjustado)
        {
            int n = y.Count;
            float sx = 0, sy = 0, sxx = 0, sxy = 0;
            for (int i = 0; i < n; i++)
            {
                sx += i; sy += y[i]; sxx += i * (float)i; sxy += i * y[i];
            }
            float den = n * sxx - sx * sx;
            float pend = den == 0f ? 0f : (n * sxy - sx * sy) / den;
            float inter = (sy - pend * sx) / n;
            ultimoAjustado = inter + pend * (n - 1);
            return pend;
        }
    }
}