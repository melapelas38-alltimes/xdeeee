using System;

namespace Composta.Core.Seguridad
{
    // Cuenta persistida. Solo campos públicos para que JsonUtility la serialice.
    // Las fechas se guardan como Ticks UTC (JsonUtility no soporta DateTime).
    [Serializable]
    public class Usuario
    {
        public string Id;
        public string NombreUsuario;          // normalizado (minúsculas), único
        public string NombreVisible;
        public Rol Rol;

        // Nunca se guarda la contraseña: solo el hash PBKDF2, su sal y las iteraciones usadas
        public string Hash;
        public string Sal;
        public int Iteraciones;

        public bool Activo = true;
        public bool DebeCambiarContrasena;    // contraseña temporal asignada por administración

        // Código de recuperación de un solo uso (se guarda solo su hash, igual que la contraseña)
        public string HashRecuperacion;
        public string SalRecuperacion;
        public int IteracionesRecuperacion;
        public bool TieneCodigoRecuperacion => !string.IsNullOrEmpty(HashRecuperacion);
        public int IntentosFallidos;
        public long BloqueadoHastaUtc;
        public long CreadoUtc;
        public long UltimoAccesoUtc;

        public bool EstaBloqueado(DateTime ahoraUtc) => BloqueadoHastaUtc > ahoraUtc.Ticks;
        public bool EsAdministrador => Rol == Rol.Administrador;
    }
}
