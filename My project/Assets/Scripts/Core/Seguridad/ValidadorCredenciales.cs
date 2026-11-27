using System;
using System.Security.Cryptography;
using System.Text;

namespace Composta.Core.Seguridad
{
    public static class ValidadorCredenciales
    {
        public const int UsuarioMin = 3, UsuarioMax = 32;
        public const int ContrasenaMin = 8, ContrasenaMax = 128;
        public const int NombreMax = 60;

        public static string NormalizarUsuario(string usuario)
            => (usuario ?? "").Trim().ToLowerInvariant();

        // Devuelve null si es válido; si no, el mensaje de error para la UI
        public static string ErrorUsuario(string usuario)
        {
            string u = NormalizarUsuario(usuario);
            if (u.Length < UsuarioMin || u.Length > UsuarioMax)
                return $"El usuario debe tener entre {UsuarioMin} y {UsuarioMax} caracteres.";
            if (!char.IsLetterOrDigit(u[0]))
                return "El usuario debe empezar con una letra o un número.";
            foreach (char c in u)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
                if (!ok) return "El usuario solo admite letras sin acento, números, punto, guion y guion bajo.";
            }
            return null;
        }

        public static string ErrorContrasena(string contrasena, string usuario = null)
        {
            if (string.IsNullOrEmpty(contrasena)) return "Escribe una contraseña.";
            if (contrasena.Length < ContrasenaMin) return $"La contraseña debe tener al menos {ContrasenaMin} caracteres.";
            if (contrasena.Length > ContrasenaMax) return $"La contraseña no puede superar {ContrasenaMax} caracteres.";

            bool letra = false, digito = false;
            foreach (char c in contrasena)
            {
                if (char.IsControl(c)) return "La contraseña contiene caracteres no válidos.";
                if (char.IsLetter(c)) letra = true;
                else if (char.IsDigit(c)) digito = true;
            }
            if (!letra || !digito) return "La contraseña debe combinar letras y números.";
            if (usuario != null && string.Equals(contrasena.Trim(), NormalizarUsuario(usuario), StringComparison.OrdinalIgnoreCase))
                return "La contraseña no puede ser igual al usuario.";
            return null;
        }

        public static string ErrorNombreVisible(string nombre)
        {
            string n = LimpiarTexto(nombre, NombreMax + 1);
            if (n.Length == 0) return "Escribe el nombre de la persona.";
            if (n.Length > NombreMax) return $"El nombre no puede superar {NombreMax} caracteres.";
            return null;
        }

        // Quita caracteres de control y espacios sobrantes; recorta al largo máximo
        public static string LimpiarTexto(string texto, int max)
        {
            if (string.IsNullOrEmpty(texto)) return "";
            var sb = new StringBuilder(texto.Length);
            foreach (char c in texto)
                sb.Append(char.IsControl(c) ? ' ' : c);
            string r = sb.ToString().Trim();
            return r.Length > max ? r.Substring(0, max) : r;
        }

        // Código de recuperación: 16 caracteres sin ambiguos, mostrado en bloques de 4 (ABCD-EFGH-JKLM-NPQR)
        public static string GenerarCodigoRecuperacion()
        {
            const string alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var sb = new StringBuilder(19);
            for (int i = 0; i < 16; i++)
            {
                if (i > 0 && i % 4 == 0) sb.Append('-');
                sb.Append(alfabeto[bytes[i] % alfabeto.Length]);
            }
            return sb.ToString();
        }

        // Acepta el código con o sin guiones, espacios o minúsculas
        public static string NormalizarCodigo(string codigo)
        {
            var sb = new StringBuilder();
            foreach (char c in codigo ?? "") if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
            return sb.ToString();
        }

        // Contraseña temporal legible (sin 0/O, 1/l/I) con al menos una letra y un dígito
        public static string GenerarTemporal(int largo = 12)
        {
            const string letras = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";
            const string digitos = "23456789";
            const string todos = letras + digitos;
            if (largo < ContrasenaMin) largo = ContrasenaMin;

            var bytes = new byte[largo];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var sb = new StringBuilder(largo);
            sb.Append(letras[bytes[0] % letras.Length]);
            sb.Append(digitos[bytes[1] % digitos.Length]);
            for (int i = 2; i < largo; i++) sb.Append(todos[bytes[i] % todos.Length]);
            return sb.ToString();
        }
    }
}
