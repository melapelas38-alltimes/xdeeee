using System;
using System.Collections.Generic;

namespace Composta.Core.Persistencia
{
    // Almacén clave -> texto. Las claves son rutas relativas tipo "lotes/abc.json".
    // Abstraerlo permite probar repositorios en memoria y cambiar a SQLite/servidor sin tocar la lógica (DIP).
    public interface IAlmacenDatos
    {
        bool Existe(string clave);
        string Leer(string clave);
        string LeerRespaldo(string clave);              // null si no hay respaldo
        void Escribir(string clave, string contenido);  // debe ser atómico
        IReadOnlyList<string> Listar(string carpeta, string extension);
        string RutaAbsoluta(string clave);
    }

    public interface ISerializador
    {
        string Serializar(object obj);
        T Deserializar<T>(string texto);
    }

    public interface IReloj
    {
        DateTime UtcAhora { get; }
    }

    public sealed class RelojSistema : IReloj
    {
        public DateTime UtcAhora => DateTime.UtcNow;
    }

    // El archivo existe pero ni él ni su respaldo se pueden leer.
    // Importante: NO se debe tratar como "no hay datos" (dejaría crear un administrador nuevo).
    public sealed class DatosCorruptosException : Exception
    {
        public DatosCorruptosException(string mensaje, Exception interna = null) : base(mensaje, interna) { }
    }
}
