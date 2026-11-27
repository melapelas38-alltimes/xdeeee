using System;
using System.Collections.Generic;

namespace Composta.Core
{
    public struct Limite
    {
        public float AvisoMin, AvisoMax, CriticoMin, CriticoMax;
        public string Unidad, AccionBaja, AccionAlta;
    }

    public static class Reglas
    {
        public static string Nombre(Magnitud m)
        {
            switch (m)
            {
                case Magnitud.Temperatura: return "Temperatura";
                case Magnitud.PH: return "pH";
                case Magnitud.Humedad: return "Humedad";
                default: return "Oxígeno";
            }
        }

        // Límites GENÉRICOS: la base de la que parte cada perfil
        public static Limite Base(Magnitud m)
        {
            switch (m)
            {
                case Magnitud.Temperatura:
                    return new Limite
                    {
                        AvisoMin = 10,
                        AvisoMax = 65,
                        CriticoMin = 0,
                        CriticoMax = 70,
                        Unidad = "°C",
                        AccionBaja = "Agregar material rico en nitrógeno",
                        AccionAlta = "Voltear para disipar calor"
                    };
                case Magnitud.PH:
                    return new Limite
                    {
                        AvisoMin = 6f,
                        AvisoMax = 8f,
                        CriticoMin = 5.5f,
                        CriticoMax = 8.5f,
                        Unidad = "",
                        AccionBaja = "Agregar material alcalino (ceniza o cal)",
                        AccionAlta = "Agregar material seco rico en carbono y voltear"
                    };
                case Magnitud.Humedad:
                    return new Limite
                    {
                        AvisoMin = 40,
                        AvisoMax = 60,
                        CriticoMin = 30,
                        CriticoMax = 70,
                        Unidad = "%",
                        AccionBaja = "Regar",
                        AccionAlta = "Voltear y agregar material seco"
                    };
                default:
                    return new Limite
                    {
                        AvisoMin = 8,
                        AvisoMax = 100,
                        CriticoMin = 5,
                        CriticoMax = 100,
                        Unidad = "%",
                        AccionBaja = "Voltear",
                        AccionAlta = ""
                    };
            }
        }

        // Compatibilidad (la UI solo lo usa para la unidad)
        public static Limite De(Magnitud m) => Base(m);

        public static List<Alerta> Evaluar(Lectura l, PerfilComposta perfil, bool[] confiable = null)
        {
            var lista = new List<Alerta>();
            foreach (Magnitud m in Enum.GetValues(typeof(Magnitud)))
            {
                if (confiable != null && !confiable[(int)m]) continue;
                Revisar(lista, m, Agregador.Valor(l, m), perfil.LimitesDe(m));
            }
            return lista;
        }

        static void Revisar(List<Alerta> lista, Magnitud m, float v, Limite lim)
        {
            string nombre = Nombre(m);
            Nivel nivel = Nivel.Ok;
            if (v < lim.CriticoMin || v > lim.CriticoMax) nivel = Nivel.Critico;
            else if (v < lim.AvisoMin || v > lim.AvisoMax) nivel = Nivel.Aviso;
            if (nivel == Nivel.Ok) return;

            bool bajo = v < lim.AvisoMin;
            lista.Add(new Alerta
            {
                Variable = nombre,
                Nivel = nivel,
                Mensaje = $"{nombre} {(bajo ? "baja" : "alta")}: {v:F1}{lim.Unidad}",
                Accion = bajo ? lim.AccionBaja : lim.AccionAlta
            });
        }
    }
}