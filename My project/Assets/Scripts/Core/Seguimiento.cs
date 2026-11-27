using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Composta.Core
{
    // Convierte "alertas activas" en EVENTOS: entra, cambia de nivel o se resuelve
    public class MonitorAlertas
    {
        const int TicksParaResolver = 3;
        readonly Dictionary<string, Alerta> activas = new Dictionary<string, Alerta>();
        readonly Dictionary<string, int> limpios = new Dictionary<string, int>();

        public IEnumerable<Alerta> Activas => activas.Values;

        // Recibe TODAS las alertas del tick: de reglas, de sensores y de predicción
        public List<EventoAlerta> Actualizar(IEnumerable<Alerta> actuales)
        {
            var eventos = new List<EventoAlerta>();
            var vistas = new HashSet<string>();

            foreach (var a in actuales)
            {
                vistas.Add(a.Variable);
                limpios[a.Variable] = 0;

                if (!activas.TryGetValue(a.Variable, out var previa))
                    eventos.Add(new EventoAlerta { Tipo = TipoEvento.Nueva, Alerta = a, Anterior = Nivel.Ok });
                else if (previa.Nivel != a.Nivel)
                    eventos.Add(new EventoAlerta { Tipo = TipoEvento.CambioNivel, Alerta = a, Anterior = previa.Nivel });

                activas[a.Variable] = a;
            }

            foreach (var par in new List<KeyValuePair<string, Alerta>>(activas))
            {
                if (vistas.Contains(par.Key)) continue;
                limpios.TryGetValue(par.Key, out int n);
                limpios[par.Key] = ++n;
                if (n >= TicksParaResolver)
                {
                    eventos.Add(new EventoAlerta { Tipo = TipoEvento.Resuelta, Alerta = par.Value, Anterior = par.Value.Nivel });
                    activas.Remove(par.Key);
                    limpios.Remove(par.Key);
                }
            }
            return eventos;
        }
    }

    public class Historial
    {
        public readonly List<Lectura> Lecturas = new List<Lectura>();   // lo que MIDEN los sensores
        public readonly List<Lectura> Reales = new List<Lectura>();     // la verdad del modelo
        public int Capacidad = 5000;

        public void Agregar(Lectura medida, Lectura real)
        {
            Lecturas.Add(medida);
            Reales.Add(real);
            if (Lecturas.Count > Capacidad) { Lecturas.RemoveAt(0); Reales.RemoveAt(0); }
        }

        // Reparte una corrección de forma lineal entre dos horas (0 en tickDesde, completa en tickHasta)
        public void AjustarTramo(int tickDesde, int tickHasta, float dTemp, float dHum, float dMasa)
        {
            if (tickHasta <= tickDesde) return;
            float largo = tickHasta - tickDesde;
            for (int i = 0; i < Lecturas.Count; i++)
            {
                int t = Lecturas[i].Tick;
                if (t <= tickDesde || t > tickHasta) continue;
                float f = (t - tickDesde) / largo;
                foreach (var l in new[] { Lecturas[i], Reales[i] })
                {
                    l.Temperatura += dTemp * f;
                    l.Humedad = Math.Max(0f, Math.Min(100f, l.Humedad + dHum * f));
                    l.MasaKg += dMasa * f;
                }
            }
        }

        public string ToCsv()
        {
            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("tick,dias,etapa,temperatura,ph,humedad,oxigeno,real_temperatura,real_ph,real_humedad,real_oxigeno,masa_kg\n");
            for (int i = 0; i < Lecturas.Count; i++)
            {
                var l = Lecturas[i]; var r = Reales[i];
                sb.AppendLine(string.Format(c, "{0},{1:F2},{2},{3:F2},{4:F2},{5:F2},{6:F2},{7:F2},{8:F2},{9:F2},{10:F2},{11:F2}",
                    l.Tick, l.Dias, l.Etapa, l.Temperatura, l.PH, l.Humedad, l.Oxigeno,
                    r.Temperatura, r.PH, r.Humedad, r.Oxigeno, r.MasaKg));
            }
            return sb.ToString();
        }
    }
}