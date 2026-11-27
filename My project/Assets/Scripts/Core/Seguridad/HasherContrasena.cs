using System;
using System.Security.Cryptography;
using System.Text;

namespace Composta.Core.Seguridad
{
    public interface IHasherContrasena
    {
        int IteracionesActuales { get; }
        void Generar(string contrasena, out string hashB64, out string salB64, out int iteraciones);
        bool Verificar(string contrasena, string hashB64, string salB64, int iteraciones);
    }

    // PBKDF2-HMAC-SHA256 implementado sobre HMACSHA256 (disponible en Mono, IL2CPP y .NET),
    // así no dependemos de sobrecargas de Rfc2898DeriveBytes que no todos los runtimes traen.
    public sealed class HasherPbkdf2 : IHasherContrasena
    {
        public const int IteracionesPorDefecto = 100_000;
        const int TamSal = 16, TamHash = 32;
        const int IteracionesMin = 1_000, IteracionesMax = 10_000_000;

        public int IteracionesActuales { get; }

        public HasherPbkdf2(int iteraciones = IteracionesPorDefecto)
        {
            if (iteraciones < IteracionesMin || iteraciones > IteracionesMax)
                throw new ArgumentOutOfRangeException(nameof(iteraciones));
            IteracionesActuales = iteraciones;
        }

        public void Generar(string contrasena, out string hashB64, out string salB64, out int iteraciones)
        {
            if (contrasena == null) throw new ArgumentNullException(nameof(contrasena));
            var sal = new byte[TamSal];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(sal);
            iteraciones = IteracionesActuales;
            hashB64 = Convert.ToBase64String(Derivar(contrasena, sal, iteraciones, TamHash));
            salB64 = Convert.ToBase64String(sal);
        }

        public bool Verificar(string contrasena, string hashB64, string salB64, int iteraciones)
        {
            if (contrasena == null || string.IsNullOrEmpty(hashB64) || string.IsNullOrEmpty(salB64)) return false;
            if (iteraciones < IteracionesMin || iteraciones > IteracionesMax) return false;

            byte[] esperado, sal;
            try
            {
                esperado = Convert.FromBase64String(hashB64);
                sal = Convert.FromBase64String(salB64);
            }
            catch (FormatException) { return false; }   // archivo manipulado o dañado

            if (esperado.Length == 0) return false;
            var calculado = Derivar(contrasena, sal, iteraciones, esperado.Length);
            return IgualesTiempoConstante(esperado, calculado);
        }

        // Normaliza Unicode (FormC) para que "ñ"/"á" escritas con distintas combinaciones de teclas coincidan
        static byte[] Derivar(string contrasena, byte[] sal, int iteraciones, int largo)
        {
            var clave = Encoding.UTF8.GetBytes(contrasena.Normalize(NormalizationForm.FormC));
            try { return Pbkdf2(clave, sal, iteraciones, largo); }
            finally { Array.Clear(clave, 0, clave.Length); }
        }

        // RFC 8018, sección 5.2
        public static byte[] Pbkdf2(byte[] clave, byte[] sal, int iteraciones, int largo)
        {
            using (var hmac = new HMACSHA256(clave))
            {
                int tamBloque = hmac.HashSize / 8;
                int bloques = (largo + tamBloque - 1) / tamBloque;
                var resultado = new byte[largo];
                var entrada = new byte[sal.Length + 4];
                Buffer.BlockCopy(sal, 0, entrada, 0, sal.Length);

                for (int i = 1; i <= bloques; i++)
                {
                    entrada[sal.Length] = (byte)(i >> 24);
                    entrada[sal.Length + 1] = (byte)(i >> 16);
                    entrada[sal.Length + 2] = (byte)(i >> 8);
                    entrada[sal.Length + 3] = (byte)i;

                    byte[] u = hmac.ComputeHash(entrada);
                    var t = (byte[])u.Clone();
                    for (int j = 1; j < iteraciones; j++)
                    {
                        u = hmac.ComputeHash(u);
                        for (int k = 0; k < t.Length; k++) t[k] ^= u[k];
                    }
                    int offset = (i - 1) * tamBloque;
                    Buffer.BlockCopy(t, 0, resultado, offset, Math.Min(tamBloque, largo - offset));
                }
                return resultado;
            }
        }

        // Evita filtrar por tiempo cuántos bytes coinciden
        static bool IgualesTiempoConstante(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int dif = 0;
            for (int i = 0; i < a.Length; i++) dif |= a[i] ^ b[i];
            return dif == 0;
        }
    }
}
