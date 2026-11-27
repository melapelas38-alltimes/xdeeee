using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Composta.Core.Persistencia
{
    // Guarda cada clave como archivo bajo una carpeta raíz (en Unity: persistentDataPath/datos).
    // Escritura atómica: se escribe un .tmp y luego se reemplaza el original dejando un .bak,
    // así un corte de luz a mitad de escritura nunca deja el archivo a medias.
    public sealed class AlmacenArchivos : IAlmacenDatos
    {
        static readonly Regex ClaveValida =
            new Regex(@"^[a-z0-9_\-]+(/[a-z0-9_\-]+)*\.(json|csv)$", RegexOptions.CultureInvariant);
        static readonly Encoding Utf8SinBom = new UTF8Encoding(false);

        readonly string raiz;
        readonly object candado = new object();

        public string Raiz => raiz;

        public AlmacenArchivos(string carpetaRaiz)
        {
            if (string.IsNullOrWhiteSpace(carpetaRaiz)) throw new ArgumentException("Carpeta raíz vacía", nameof(carpetaRaiz));
            raiz = Path.GetFullPath(carpetaRaiz);
            Directory.CreateDirectory(raiz);
        }

        public bool Existe(string clave) => File.Exists(Ruta(clave));

        public string Leer(string clave)
        {
            string ruta = Ruta(clave);
            lock (candado) return File.Exists(ruta) ? File.ReadAllText(ruta, Encoding.UTF8) : null;
        }

        public string LeerRespaldo(string clave)
        {
            string ruta = Ruta(clave) + ".bak";
            lock (candado) return File.Exists(ruta) ? File.ReadAllText(ruta, Encoding.UTF8) : null;
        }

        public void Escribir(string clave, string contenido)
        {
            string ruta = Ruta(clave);
            string tmp = ruta + ".tmp", bak = ruta + ".bak";
            lock (candado)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ruta));
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var w = new StreamWriter(fs, Utf8SinBom))
                {
                    w.Write(contenido ?? "");
                    w.Flush();
                    fs.Flush(true);                      // forzar a disco antes de reemplazar
                }

                if (!File.Exists(ruta)) { File.Move(tmp, ruta); return; }
                try
                {
                    File.Replace(tmp, ruta, bak);
                }
                catch (Exception e) when (e is PlatformNotSupportedException || e is IOException)
                {
                    // Algunos sistemas de archivos (unidades de red, FAT) no soportan Replace
                    File.Copy(ruta, bak, true);
                    File.Delete(ruta);
                    File.Move(tmp, ruta);
                }
            }
        }

        public IReadOnlyList<string> Listar(string carpeta, string extension)
        {
            var lista = new List<string>();
            string dir = Path.Combine(raiz, carpeta);
            if (!Directory.Exists(dir)) return lista;
            foreach (var f in Directory.GetFiles(dir, "*" + extension))
            {
                string clave = carpeta + "/" + Path.GetFileName(f);
                if (ClaveValida.IsMatch(clave)) lista.Add(clave);
            }
            lista.Sort(StringComparer.Ordinal);
            return lista;
        }

        public string RutaAbsoluta(string clave) => Ruta(clave);

        // Bloquea rutas fuera de la raíz ("../", rutas absolutas, mayúsculas raras, etc.)
        string Ruta(string clave)
        {
            if (clave == null || !ClaveValida.IsMatch(clave))
                throw new ArgumentException($"Clave de almacenamiento no válida: '{clave}'", nameof(clave));
            string ruta = Path.GetFullPath(Path.Combine(raiz, clave.Replace('/', Path.DirectorySeparatorChar)));
            if (!ruta.StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException("La clave sale de la carpeta de datos", nameof(clave));
            return ruta;
        }
    }
}
