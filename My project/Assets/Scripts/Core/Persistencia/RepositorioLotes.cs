using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Composta.Core.Lotes;

namespace Composta.Core.Persistencia
{
    public interface IRepositorioLotes
    {
        void Guardar(Lote lote);
        Lote Cargar(string id);
        IReadOnlyList<Lote> Listar();
        Lote UltimoActivo();
    }

    // Un archivo JSON por lote: lotes/{id}.json (con .bak automático del almacén)
    public sealed class RepositorioLotesJson : IRepositorioLotes
    {
        const string Carpeta = "lotes";
        static readonly Regex IdValido = new Regex("^[a-f0-9]{32}$", RegexOptions.CultureInvariant);

        readonly IAlmacenDatos almacen;
        readonly ISerializador serializador;

        public List<string> ErroresDeLectura { get; } = new List<string>();

        public RepositorioLotesJson(IAlmacenDatos almacen, ISerializador serializador)
        {
            this.almacen = almacen ?? throw new ArgumentNullException(nameof(almacen));
            this.serializador = serializador ?? throw new ArgumentNullException(nameof(serializador));
        }

        public void Guardar(Lote lote)
        {
            if (lote == null || lote.Id == null || !IdValido.IsMatch(lote.Id)) throw new ArgumentException("Lote con Id inválido");
            almacen.Escribir(Clave(lote.Id), serializador.Serializar(lote));
        }

        public Lote Cargar(string id)
        {
            if (id == null || !IdValido.IsMatch(id)) return null;
            string clave = Clave(id);
            if (!almacen.Existe(clave)) return null;
            return Leer(clave);
        }

        public IReadOnlyList<Lote> Listar()
        {
            ErroresDeLectura.Clear();
            var lista = new List<Lote>();
            foreach (var clave in almacen.Listar(Carpeta, ".json"))
            {
                var l = Leer(clave);
                if (l != null) lista.Add(l);
            }
            lista.Sort((a, b) => b.ActualizadoUtc.CompareTo(a.ActualizadoUtc));   // más reciente primero
            return lista;
        }

        public Lote UltimoActivo()
        {
            foreach (var l in Listar()) if (l.Estado == EstadoLote.Activo) return l;
            return null;
        }

        // Un lote dañado no debe impedir abrir los demás: se intenta el .bak y si no, se omite
        Lote Leer(string clave)
        {
            if (Intentar(almacen.Leer(clave), out var l)) return l;
            if (Intentar(almacen.LeerRespaldo(clave), out l))
            {
                ErroresDeLectura.Add($"{clave}: dañado, se usó el respaldo");
                return l;
            }
            ErroresDeLectura.Add($"{clave}: dañado y sin respaldo válido");
            return null;
        }

        bool Intentar(string texto, out Lote l)
        {
            l = null;
            if (string.IsNullOrWhiteSpace(texto)) return false;
            try { l = serializador.Deserializar<Lote>(texto); }
            catch (Exception) { return false; }
            return l != null && l.Id != null && IdValido.IsMatch(l.Id) && l.Version <= Lote.VersionActual;
        }

        static string Clave(string id) => $"{Carpeta}/{id}.json";
    }
}
