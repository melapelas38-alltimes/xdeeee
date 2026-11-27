using System.IO;
using Composta.Core.Persistencia;
using Composta.Core.Seguridad;
using UnityEngine;

namespace Composta.App
{
    // Raíz de composición: el único lugar donde se eligen implementaciones concretas.
    // Todo lo demás recibe interfaces (DIP), así se puede cambiar JSON por SQLite o un servidor.
    public sealed class ServiciosApp
    {
        public static ServiciosApp Instancia { get; private set; }

        public AlmacenArchivos Almacen { get; }
        public IReloj Reloj { get; }
        public ServicioAutenticacion Autenticacion { get; }
        public Sesion Sesion { get; }
        public IRepositorioLotes Lotes { get; }

        ServiciosApp(string carpetaDatos)
        {
            var serializador = new SerializadorJsonUnity();
            Almacen = new AlmacenArchivos(carpetaDatos);
            Reloj = new RelojSistema();
            Autenticacion = new ServicioAutenticacion(
                new RepositorioUsuariosJson(Almacen, serializador), new HasherPbkdf2(), Reloj);
            Sesion = new Sesion();
            Lotes = new RepositorioLotesJson(Almacen, serializador);
        }

        // Debe llamarse desde el hilo principal (Application.persistentDataPath no es thread-safe)
        public static ServiciosApp Obtener()
        {
            if (Instancia == null)
                Instancia = new ServiciosApp(Path.Combine(Application.persistentDataPath, "datos"));
            return Instancia;
        }

        // Con "Enter Play Mode Options" (sin recarga de dominio) los estáticos sobreviven entre sesiones de Play
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reiniciar() => Instancia = null;
    }

    public sealed class SerializadorJsonUnity : ISerializador
    {
        public string Serializar(object obj) => JsonUtility.ToJson(obj, true);
        public T Deserializar<T>(string texto) => JsonUtility.FromJson<T>(texto);
    }
}
