using System;
using System.Collections.Generic;
using System.Globalization;
using Composta.Core.Lotes;

namespace Composta.Core.Campo
{
    public enum NivelEstado { Ok, Aviso, Critico, SinDato }

    public sealed class Recomendacion
    {
        public NivelEstado Nivel;
        public string Titulo;
        public string Detalle;
    }

    // Resultado del análisis: SOLO derivado de mediciones reales (nada simulado)
    public sealed class EstadoCampo
    {
        public int Registros;
        public InformeCampo Ultimo, Anterior;
        public float DiasDesdeInicio;              // del primer al último registro
        public Etapa? Fase;                        // null = sin datos suficientes
        public string ExplicacionFase = "";
        public float? DeltaTemp, DeltaHum;          // frente al registro anterior
        public float? PerdidaPesoTotalPct;         // desde el primer registro
        public float? PerdidaSemanalPct;           // ritmo del último tramo, normalizado a 7 días
        public NivelEstado NivelTemp = NivelEstado.SinDato, NivelHum = NivelEstado.SinDato, NivelPeso = NivelEstado.SinDato;
        public readonly List<Recomendacion> Recomendaciones = new List<Recomendacion>();
    }

    // Interpreta los registros de campo con reglas de compostaje conocidas.
    // Separado del simulador a propósito (SRP): el modo Control Manual nunca genera datos sintéticos.
    public static class AnalisisCampo
    {
        static readonly CultureInfo C = CultureInfo.InvariantCulture;

        public static List<InformeCampo> Ordenados(Lote l)
        {
            var lista = new List<InformeCampo>(l?.Informes ?? new List<InformeCampo>());
            lista.RemoveAll(r => r == null);
            lista.Sort((a, b) => a.FechaUtc.CompareTo(b.FechaUtc));
            return lista;
        }

        public static float Dias(InformeCampo desde, InformeCampo hasta)
            => (float)new TimeSpan(hasta.FechaUtc - desde.FechaUtc).TotalDays;

        public static EstadoCampo Analizar(Lote lote)
        {
            var e = new EstadoCampo();
            var regs = Ordenados(lote);
            e.Registros = regs.Count;
            if (regs.Count == 0) return e;

            var perfil = PerfilComposta.Crear(lote.Tipo, lote.PesoKg, true);
            var primero = regs[0];
            e.Ultimo = regs[regs.Count - 1];
            e.Anterior = regs.Count > 1 ? regs[regs.Count - 2] : null;
            e.DiasDesdeInicio = Dias(primero, e.Ultimo);

            if (e.Anterior != null)
            {
                e.DeltaTemp = e.Ultimo.Temperatura - e.Anterior.Temperatura;
                e.DeltaHum = e.Ultimo.Humedad - e.Anterior.Humedad;
                float dias = Math.Max(0.5f, Dias(e.Anterior, e.Ultimo));
                if (e.Anterior.PesoKg > 0f)
                    e.PerdidaSemanalPct = (e.Anterior.PesoKg - e.Ultimo.PesoKg) / e.Anterior.PesoKg * 100f * 7f / dias;
            }
            if (primero.PesoKg > 0f) e.PerdidaPesoTotalPct = (primero.PesoKg - e.Ultimo.PesoKg) / primero.PesoKg * 100f;

            EvaluarNiveles(e, perfil);
            DeducirFase(e, regs, perfil);
            Recomendar(e, perfil, lote);
            return e;
        }

        static void EvaluarNiveles(EstadoCampo e, PerfilComposta p)
        {
            var u = e.Ultimo;
            e.NivelTemp = Nivel(u.Temperatura, p.LimitesDe(Magnitud.Temperatura));
            e.NivelHum = Nivel(u.Humedad, p.LimitesDe(Magnitud.Humedad));
            var np = p.EvaluarPeso(u.PesoKg);
            e.NivelPeso = np == Core.NivelPeso.Exagerado ? NivelEstado.Critico
                        : np == Core.NivelPeso.FueraDeOptimo ? NivelEstado.Aviso : NivelEstado.Ok;
        }

        static NivelEstado Nivel(float v, Limite l)
            => v < l.CriticoMin || v > l.CriticoMax ? NivelEstado.Critico
             : v < l.AvisoMin || v > l.AvisoMax ? NivelEstado.Aviso : NivelEstado.Ok;

        // Fase estimada con reglas: temperatura actual, si ya hubo fase caliente, tiempo y ritmo de pérdida de peso
        static void DeducirFase(EstadoCampo e, List<InformeCampo> regs, PerfilComposta p)
        {
            float t = e.Ultimo.Temperatura;
            bool huboCaliente = false;
            foreach (var r in regs) if (r.Temperatura >= 45f) huboCaliente = true;
            bool perdidaLenta = e.PerdidaSemanalPct.HasValue && e.PerdidaSemanalPct.Value < 1.5f;
            bool vermi = p.Tipo == TipoComposta.Vermicomposta;

            if (!vermi && t >= 45f)
            {
                e.Fase = Etapa.Termofilica;
                e.ExplicacionFase = "Más de 45 °C: los microbios termófilos están trabajando a pleno.";
            }
            else if (regs.Count >= 2 && e.DiasDesdeInicio >= 21f && perdidaLenta && t < 35f)
            {
                e.Fase = Etapa.Maduracion;
                e.ExplicacionFase = "Temperatura baja y el peso casi no cambia: el material se está estabilizando.";
            }
            else if (huboCaliente)
            {
                e.Fase = Etapa.Enfriamiento;
                e.ExplicacionFase = "Ya pasó por la fase caliente y ahora se está enfriando.";
            }
            else
            {
                e.Fase = Etapa.Mesofilica;
                e.ExplicacionFase = vermi
                    ? "Vermicomposta: proceso frío; las lombrices trabajan entre 15 y 25 °C."
                    : regs.Count == 1 ? "Primer registro: se confirmará la fase con la próxima medición."
                                      : "Todavía no supera 45 °C: fase inicial de calentamiento.";
            }
        }

        static void Recomendar(EstadoCampo e, PerfilComposta p, Lote lote)
        {
            var u = e.Ultimo;
            var lh = p.LimitesDe(Magnitud.Humedad);
            var lt = p.LimitesDe(Magnitud.Temperatura);
            void Add(NivelEstado n, string titulo, string detalle) => e.Recomendaciones.Add(new Recomendacion { Nivel = n, Titulo = titulo, Detalle = detalle });

            if (u.Humedad < lh.AvisoMin)
                Add(u.Humedad < lh.CriticoMin ? NivelEstado.Critico : NivelEstado.Aviso, "Regar",
                    string.Format(C, "Humedad {0:F0} % (ideal {1:F0}–{2:F0} %). Sin agua los microbios se detienen.", u.Humedad, lh.AvisoMin, lh.AvisoMax));
            else if (u.Humedad > lh.AvisoMax)
                Add(u.Humedad > lh.CriticoMax ? NivelEstado.Critico : NivelEstado.Aviso, "Voltear y agregar material seco",
                    string.Format(C, "Humedad {0:F0} %: falta oxígeno y aparecen malos olores. Agrega hojas secas o cartón.", u.Humedad));

            if (u.Temperatura > lt.AvisoMax)
                Add(u.Temperatura > lt.CriticoMax ? NivelEstado.Critico : NivelEstado.Aviso, "Voltear para bajar la temperatura",
                    string.Format(C, "{0:F0} °C supera el máximo recomendado ({1:F0} °C) para {2}.", u.Temperatura, lt.AvisoMax, p.Nombre));

            if (p.Tipo != TipoComposta.Vermicomposta && e.Fase == Etapa.Mesofilica && e.Registros >= 2 && e.DiasDesdeInicio >= 10f && u.Temperatura < 35f)
                Add(NivelEstado.Aviso, "La pila no se calienta",
                    "Tras más de 10 días sigue fría. Agrega material verde (restos de cocina, pasto), revisa la humedad y voltea.");

            if (e.PerdidaSemanalPct.HasValue && e.PerdidaSemanalPct.Value < 0.5f && e.Fase != Etapa.Maduracion && u.Temperatura < 40f)
                Add(NivelEstado.Aviso, "El proceso parece estancado",
                    "El peso casi no bajó en la última semana. Voltear airea la pila y reactiva la descomposición.");

            if (e.NivelPeso == NivelEstado.Critico)
                Add(NivelEstado.Critico, "PESO EXAGERADO",
                    string.Format(C, "{0:0.#} kg está fuera de los límites físicos de {1} ({2:0.#}–{3:0.#} kg). Revisa la medición o el tipo de compostera.",
                                  u.PesoKg, p.Nombre, p.ExtremoMin, p.ExtremoMax));
            else if (e.NivelPeso == NivelEstado.Aviso)
                Add(NivelEstado.Ok, "Peso fuera del rango ideal",
                    string.Format(C, "{0:0.#} kg funciona, pero el rango ideal para {1} es {2:0.#}–{3:0.#} kg.", u.PesoKg, p.Nombre, p.PesoMin, p.PesoMax));

            if (e.Fase == Etapa.Maduracion)
                Add(NivelEstado.Ok, "Compost casi listo",
                    "Deja madurar 2–4 semanas más sin voltear. Estará listo cuando huela a tierra y no se distingan los restos.");

            if (e.Recomendaciones.Count == 0)
                Add(NivelEstado.Ok, "Todo en orden", "Los valores medidos están dentro de lo recomendado. Sigue registrando cada semana.");
        }
    }
}
