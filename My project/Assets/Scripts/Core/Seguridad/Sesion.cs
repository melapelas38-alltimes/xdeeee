using System;

namespace Composta.Core.Seguridad
{
    // Sesión de la persona que está usando la app. Se usa solo desde el hilo principal.
    public sealed class Sesion
    {
        public Usuario Actual { get; private set; }
        public DateTime InicioUtc { get; private set; }
        public bool Activa => Actual != null;
        public string NombreActual => Actual?.NombreUsuario ?? "sistema";

        public event Action<Usuario> AlCambiar;   // null = sesión cerrada

        public void Iniciar(Usuario usuario, DateTime ahoraUtc)
        {
            Actual = usuario ?? throw new ArgumentNullException(nameof(usuario));
            InicioUtc = ahoraUtc;
            AlCambiar?.Invoke(Actual);
        }

        public void Cerrar()
        {
            if (Actual == null) return;
            Actual = null;
            AlCambiar?.Invoke(null);
        }

        public bool Puede(Permiso permiso)
            => Actual != null && Actual.Activo && PoliticaAcceso.Tiene(Actual.Rol, permiso);
    }
}
