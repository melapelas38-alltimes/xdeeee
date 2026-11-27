using System;
using System.Collections.Generic;
using Composta.Core.Persistencia;
using UnityEngine;

namespace Composta.Tests
{
    // Dobles de prueba: almacén en memoria, reloj controlable y serializador JsonUtility
    // (el mismo que usa la app, así los tests cubren también la compatibilidad con JsonUtility)

    public sealed class AlmacenMemoria : IAlmacenDatos
    {
        public readonly Dictionary<string, string> Archivos = new Dictionary<string, string>();

        public bool Existe(string clave) => Archivos.ContainsKey(clave);
        public string Leer(string clave) => Archivos.TryGetValue(clave, out var v) ? v : null;
        public string LeerRespaldo(string clave) => Archivos.TryGetValue(clave + ".bak", out var v) ? v : null;

        public void Escribir(string clave, string contenido)
        {
            if (Archivos.TryGetValue(clave, out var previo)) Archivos[clave + ".bak"] = previo;
            Archivos[clave] = contenido;
        }

        public IReadOnlyList<string> Listar(string carpeta, string extension)
        {
            var l = new List<string>();
            foreach (var k in Archivos.Keys)
                if (k.StartsWith(carpeta + "/", StringComparison.Ordinal) && k.EndsWith(extension, StringComparison.Ordinal)) l.Add(k);
            l.Sort(StringComparer.Ordinal);
            return l;
        }

        public string RutaAbsoluta(string clave) => "/memoria/" + clave;
    }

    public sealed class RelojFalso : IReloj
    {
        public DateTime UtcAhora { get; set; } = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        public void Avanzar(TimeSpan t) => UtcAhora += t;
    }

    public sealed class SerializadorPruebas : ISerializador
    {
        public string Serializar(object obj) => JsonUtility.ToJson(obj);
        public T Deserializar<T>(string texto) => JsonUtility.FromJson<T>(texto);
    }
}
