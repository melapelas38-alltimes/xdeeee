using System;
using System.Globalization;
using Composta.Core.Seguridad;

namespace Composta.Core.Lotes
{
    public static class ValidadorAporte
    {
        public const float KgMin = 0.1f, KgMax = 5000f;
        public const int NotaMax = 200;

        // Acepta "12,5" y "12.5" (teclados en español), rechaza "1.234,5", exponentes, NaN e infinito
        public static bool IntentarLeerKg(string texto, out float kg, out string error)
        {
            kg = 0f; error = null;
            string t = (texto ?? "").Trim();
            if (t.Length == 0) { error = "Escribe el peso en kg."; return false; }

            int separadores = 0;
            foreach (char c in t) if (c == ',' || c == '.') separadores++;
            if (separadores > 1) { error = "Usa un solo separador decimal (por ejemplo 12,5)."; return false; }
            t = t.Replace(',', '.');

            if (!float.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out float v)
                || float.IsNaN(v) || float.IsInfinity(v))
            {
                error = "El peso debe ser un número positivo (por ejemplo 12,5)."; return false;
            }
            if (v < KgMin) { error = $"El peso mínimo es {KgMin.ToString(CultureInfo.InvariantCulture)} kg."; return false; }
            if (v > KgMax) { error = $"El peso máximo por aporte es {KgMax:F0} kg. Divide el registro si es mayor."; return false; }

            kg = (float)Math.Round(v, 2);
            return true;
        }

        public static string LimpiarNota(string nota) => ValidadorCredenciales.LimpiarTexto(nota, NotaMax);

        public static string Nombre(TipoMaterial m)
        {
            switch (m)
            {
                case TipoMaterial.Verde: return "Verde (nitrógeno)";
                case TipoMaterial.Cafe: return "Café (carbono)";
                default: return "Mixto";
            }
        }
    }
}
