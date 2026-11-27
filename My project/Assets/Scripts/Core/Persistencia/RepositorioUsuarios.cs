using System;
using System.Collections.Generic;
using Composta.Core.Seguridad;

namespace Composta.Core.Persistencia
{
    public interface IRepositorioUsuarios
    {
        IReadOnlyList<Usuario> Todos();
        Usuario BuscarPorNombre(string nombreNormalizado);
        Usuario BuscarPorId(string id);
        void Guardar(Usuario usuario);                  // inserta o actualiza por Id
    }

    [Serializable]
    public class ArchivoUsuarios
    {
        public int Version = RepositorioUsuariosJson.VersionActual;
        public List<Usuario> Usuarios = new List<Usuario>();
    }

    public sealed class RepositorioUsuariosJson : IRepositorioUsuarios
    {
        public const int VersionActual = 1;
        public const string Clave = "usuarios.json";

        readonly IAlmacenDatos almacen;
        readonly ISerializador serializador;
        ArchivoUsuarios cache;

        public RepositorioUsuariosJson(IAlmacenDatos almacen, ISerializador serializador)
        {
            this.almacen = almacen ?? throw new ArgumentNullException(nameof(almacen));
            this.serializador = serializador ?? throw new ArgumentNullException(nameof(serializador));
        }

        public IReadOnlyList<Usuario> Todos() => new List<Usuario>(Cargar().Usuarios);

        public Usuario BuscarPorNombre(string nombreNormalizado)
        {
            foreach (var u in Cargar().Usuarios)
                if (string.Equals(u.NombreUsuario, nombreNormalizado, StringComparison.Ordinal)) return u;
            return null;
        }

        public Usuario BuscarPorId(string id)
        {
            foreach (var u in Cargar().Usuarios)
                if (string.Equals(u.Id, id, StringComparison.Ordinal)) return u;
            return null;
        }

        public void Guardar(Usuario usuario)
        {
            if (usuario == null || string.IsNullOrEmpty(usuario.Id)) throw new ArgumentException("Usuario sin Id");
            var lista = Cargar().Usuarios;
            int i = lista.FindIndex(u => u.Id == usuario.Id);
            if (i >= 0) lista[i] = usuario; else lista.Add(usuario);
            almacen.Escribir(Clave, serializador.Serializar(cache));
        }

        ArchivoUsuarios Cargar()
        {
            if (cache != null) return cache;
            if (!almacen.Existe(Clave)) return cache = new ArchivoUsuarios();

            if (IntentarLeer(almacen.Leer(Clave), out var archivo, out var error))
                return cache = archivo;

            // El principal está dañado: probar con el respaldo y, si sirve, restaurarlo
            if (IntentarLeer(almacen.LeerRespaldo(Clave), out archivo, out _))
            {
                cache = archivo;
                almacen.Escribir(Clave, serializador.Serializar(cache));
                return cache;
            }
            throw new DatosCorruptosException($"El archivo de cuentas está dañado y no hay respaldo válido ({error}).");
        }

        bool IntentarLeer(string texto, out ArchivoUsuarios archivo, out string error)
        {
            archivo = null; error = null;
            if (string.IsNullOrWhiteSpace(texto)) { error = "vacío"; return false; }
            try { archivo = serializador.Deserializar<ArchivoUsuarios>(texto); }
            catch (Exception e) { error = e.Message; return false; }

            if (archivo == null) { error = "sin contenido"; return false; }
            if (archivo.Version > VersionActual) { error = $"versión {archivo.Version} más nueva que esta app"; return false; }
            if (archivo.Usuarios == null) archivo.Usuarios = new List<Usuario>();
            archivo.Usuarios.RemoveAll(u => u == null || string.IsNullOrEmpty(u.Id) || string.IsNullOrEmpty(u.NombreUsuario));
            return true;
        }
    }
}
