namespace Composta.Core.Seguridad
{
    // Rol de cada cuenta. El orden importa: un rol "mayor" incluye los permisos de los menores.
    public enum Rol { Consulta = 0, Operador = 1, Administrador = 2 }

    // Acciones protegidas de la aplicación
    public enum Permiso
    {
        VerPanel,           // ver telemetría, gráficas y alertas
        Exportar,           // CSV / PNG
        OperarComposta,     // voltear, regar, modo automático
        CrearLote,          // nueva composta (build)
        RegistrarAportes,   // pesar residuos que entran al lote
        RepararSensores,
        InyectarFallas,     // herramienta de prueba: solo administración
        GestionarUsuarios,
        RegistrarInformes   // informe semanal de campo (lotes en modo Manual)
    }

    // Única fuente de verdad de "quién puede hacer qué" (principio abierto/cerrado:
    // agregar un permiso nuevo solo toca este archivo)
    public static class PoliticaAcceso
    {
        public static bool Tiene(Rol rol, Permiso permiso)
        {
            switch (permiso)
            {
                case Permiso.VerPanel:
                case Permiso.Exportar:
                    return true;

                case Permiso.OperarComposta:
                case Permiso.CrearLote:
                case Permiso.RegistrarAportes:
                case Permiso.RepararSensores:
                case Permiso.RegistrarInformes:
                    return rol >= Rol.Operador;

                default:
                    return rol == Rol.Administrador;
            }
        }

        public static string Nombre(Rol rol)
        {
            switch (rol)
            {
                case Rol.Administrador: return "Administración";
                case Rol.Operador: return "Operación";
                default: return "Consulta";
            }
        }
    }
}
