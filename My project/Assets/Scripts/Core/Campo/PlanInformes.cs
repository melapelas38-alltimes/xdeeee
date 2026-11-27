using System;
using System.Collections.Generic;
using System.Globalization;
using Composta.Core.Lotes;

namespace Composta.Core.Campo
{
    public enum EstadoInforme { SinRegistros, AlDia, Pendiente, Atrasado }

    public readonly struct SituacionInforme
    {
        public readonly EstadoInforme Estado;
        public readonly double HorasRestantes;     // negativo = atraso
        public readonly int Registros;

        public SituacionInforme(EstadoInforme estado, double horasRestantes, int registros)
        { Estado = estado; HorasRestantes = horasRestantes; Registros = registros; }

        public string Texto()
        {
            var c = CultureInfo.InvariantCulture;
            double h = Math.Abs(HorasRestantes);
            string tiempo = h >= 48 ? string.Format(c, "{0:F0} días", h / 24) : string.Format(c, "{0:F0} h", h);
            switch (Estado)
            {
                case EstadoInforme.SinRegistros: return "Esperando primer registro";
                case EstadoInforme.Atrasado: return $"Medición semanal atrasada {tiempo}";
                case EstadoInforme.Pendiente: return $"Medición semanal pendiente · vence en {tiempo}";
                default: return $"Registros al día · próxima medición en {tiempo}";
            }
        }
    }

    // Calendario de mediciones de un lote de Control Manual: una cada 7 días de calendario real.
    // No hay simulación: el reloj es el del mundo real.
    public static class PlanInformes
    {
        public const int HorasPeriodo = 168;
        public const int HorasAvisoPrevio = 24;

        public static SituacionInforme Evaluar(Lote l, DateTime ahoraUtc)
        {
            int n = l?.Informes?.Count ?? 0;
            if (l == null || l.Modo != ModoLote.Manual) return new SituacionInforme(EstadoInforme.AlDia, HorasPeriodo, n);
            if (n == 0) return new SituacionInforme(EstadoInforme.SinRegistros, 0, 0);

            long ultimo = 0;
            foreach (var r in l.Informes) if (r.FechaUtc > ultimo) ultimo = r.FechaUtc;
            double restantes = HorasPeriodo - (ahoraUtc - new DateTime(ultimo, DateTimeKind.Utc)).TotalHours;
            var estado = restantes < 0 ? EstadoInforme.Atrasado
                       : restantes <= HorasAvisoPrevio ? EstadoInforme.Pendiente : EstadoInforme.AlDia;
            return new SituacionInforme(estado, restantes, n);
        }
    }

    public static class ValidadorInforme
    {
        public const float TempMin = -10f, TempMax = 85f;

        // Errores bloquean; advertencias piden confirmación (un dato raro puede ser real)
        public static bool Validar(string temp, string hum, string peso, PerfilComposta perfil, InformeCampo anterior,
                                   out float t, out float h, out float p, out string error, List<string> advertencias)
        {
            t = h = p = 0f; error = null;
            if (!RangosParametros.IntentarLeer(temp, out t) || t < TempMin || t > TempMax)
            { error = $"Temperatura: escribe un número entre {TempMin:F0} y {TempMax:F0} °C."; return false; }
            if (!RangosParametros.IntentarLeer(hum, out h) || h < 0f || h > 100f)
            { error = "Humedad: escribe un porcentaje entre 0 y 100."; return false; }
            var rp = RangosParametros.De(ModoLote.Manual, Parametro.Peso);
            if (!RangosParametros.IntentarLeer(peso, out p) || !rp.Contiene(p))
            { error = $"Peso: escribe un número entre {rp.Min} y {rp.Max:F0} kg."; return false; }

            if (advertencias == null) return true;
            var c = CultureInfo.InvariantCulture;
            if (perfil != null && perfil.EvaluarPeso(p) == NivelPeso.Exagerado)
                advertencias.Add(string.Format(c, "PESO EXAGERADO: {0:0.#} kg es absurdo para {1} (límites físicos {2:0.#}–{3:0.#} kg).",
                                               p, perfil.Nombre, perfil.ExtremoMin, perfil.ExtremoMax));
            if (perfil != null && perfil.Tipo == TipoComposta.Vermicomposta && t > 35f)
                advertencias.Add("Más de 35 °C es letal para las lombrices. ¿La medición es correcta?");
            if (anterior != null)
            {
                if (Math.Abs(t - anterior.Temperatura) > 30f)
                    advertencias.Add(string.Format(c, "La temperatura cambió {0:F0} °C desde el registro anterior.", Math.Abs(t - anterior.Temperatura)));
                if (Math.Abs(h - anterior.Humedad) > 35f)
                    advertencias.Add(string.Format(c, "La humedad cambió {0:F0} puntos desde el registro anterior.", Math.Abs(h - anterior.Humedad)));
                if (p > anterior.PesoKg * 1.15f)
                    advertencias.Add("El peso subió más de 15 %. Si agregaste material, regístralo también como aporte.");
                if (p < anterior.PesoKg * 0.6f)
                    advertencias.Add("El peso bajó más de 40 % desde el registro anterior. ¿Se retiró material de la pila?");
            }
            return true;
        }
    }
}
