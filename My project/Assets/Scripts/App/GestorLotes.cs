using System;
using System.Globalization;
using Composta.Core;
using Composta.Core.Campo;
using Composta.Core.Lotes;
using Composta.Core.Seguridad;
using UnityEngine;

namespace Composta.App
{
    // Persistencia y ciclo de vida de los lotes. Dos flujos separados:
    //  · SIMULACIÓN: el lote nace del simulador (AlReiniciar), anota cada acción humana en la bitácora,
    //    se autoguarda y se reanuda reproduciendo la bitácora.
    //  · CONTROL MANUAL: el lote NO usa el simulador. Solo guarda mediciones reales (registros de campo);
    //    el motor queda detenido mientras este lote está abierto.
    public sealed class GestorLotes : IDisposable
    {
        const int AutoguardadoCadaTicks = 24;      // una vez por día simulado
        const float SegundosEntreErrores = 30f;

        readonly SimuladorComposta sim;
        readonly ServiciosApp svc;
        SistemaComposta sistemaDelLote;            // solo en lotes de Simulación
        float ultimoError = -999f;

        public Lote LoteActual { get; private set; }
        public event Action<Lote> AlCambiarLote;
        public event Action<Lote> AlCambiarAportes;
        public event Action<Lote> AlCambiarRegistros;

        public bool EsLoteManual => LoteActual != null && LoteActual.Modo == ModoLote.Manual;
        public SituacionInforme Situacion() => PlanInformes.Evaluar(LoteActual, svc.Reloj.UtcAhora);
        public EstadoCampo Analisis() => AnalisisCampo.Analizar(LoteActual);

        public GestorLotes(SimuladorComposta simulador, ServiciosApp servicios)
        {
            sim = simulador ? simulador : throw new ArgumentNullException(nameof(simulador));
            svc = servicios ?? throw new ArgumentNullException(nameof(servicios));
            sim.AlReiniciar += CrearLoteSimulacion;
            sim.AlComando += RegistrarComando;
            sim.AlTick += AlTick;
        }

        public void Dispose()
        {
            if (sim == null) return;
            sim.AlReiniciar -= CrearLoteSimulacion;
            sim.AlComando -= RegistrarComando;
            sim.AlTick -= AlTick;
        }

        string Usuario => svc.Sesion.NombreActual;
        int TickActual => sistemaDelLote?.Modelo.Ticks ?? 0;

        // ---------------- Ciclo de vida ----------------
        void CerrarLoteActual()
        {
            if (LoteActual == null) return;
            LoteActual.Estado = EstadoLote.Cerrado;
            Guardar();
        }

        void CrearLoteSimulacion(SistemaComposta nuevo)
        {
            CerrarLoteActual();
            sistemaDelLote = nuevo;
            LoteActual = Lote.Crear(nuevo.Perfil, sim.semilla, Usuario, svc.Reloj.UtcAhora, ModoLote.Simular);
            // Estado inicial de los ajustes que afectan la simulación (necesario para reproducirla)
            Anotar(TipoComando.Automatico, sim.modoAutomatico ? 1f : 0f);
            Anotar(TipoComando.ProbFalla, sim.probFallaPorHora);
            Guardar();
            AlCambiarLote?.Invoke(LoteActual);
            Notificador.Mostrar($"Simulación iniciada: {LoteActual.Nombre}", TipoMensaje.Exito);
        }

        // Control Manual: sin simulador, sin datos sintéticos. El tablero queda "Esperando primer registro".
        public bool CrearLoteManual(PerfilComposta perfil, out string error)
        {
            error = null;
            if (!svc.Sesion.Puede(Permiso.CrearLote)) { error = "Tu rol no permite crear lotes."; return false; }
            if (perfil == null) { error = "Falta el tipo de compostera."; return false; }
            CerrarLoteActual();
            sim.DetenerMotor();
            sim.modo = ModoLote.Manual;
            sistemaDelLote = null;
            LoteActual = Lote.Crear(perfil, 0, Usuario, svc.Reloj.UtcAhora, ModoLote.Manual);
            LoteActual.Nombre = $"{perfil.Nombre} · control manual · {svc.Reloj.UtcAhora.ToLocalTime():yyyy-MM-dd HH:mm}";
            if (!Guardar()) { error = "No se pudo guardar el lote en disco."; return false; }
            AlCambiarLote?.Invoke(LoteActual);
            Notificador.Mostrar("Lote de control manual creado. Registra la primera medición.", TipoMensaje.Exito);
            return true;
        }

        public bool Reanudar(Lote lote, out string error)
        {
            error = null;
            if (!svc.Sesion.Activa) { error = "Inicia sesión para abrir un lote."; return false; }
            try
            {
                var avisos = ReproductorLote.Sanear(lote);
                if (lote.Modo == ModoLote.Manual)
                {
                    // Nada que reproducir: los datos son las mediciones guardadas
                    sim.DetenerMotor();
                    sim.modo = ModoLote.Manual;
                    sistemaDelLote = null;
                }
                else
                {
                    var s = ReproductorLote.Reconstruir(lote, out var ultimo);
                    sim.modo = ModoLote.Simular;          // antes de CargarSistema: los oyentes de AlCargar lo leen
                    sim.CargarSistema(s, ultimo, lote.Semilla);
                    sistemaDelLote = s;
                }
                LoteActual = lote;
                if (avisos.Count > 0)
                {
                    Debug.LogWarning($"Lote {lote.Id} saneado al cargar: {string.Join("; ", avisos)}");
                    Notificador.Mostrar("El lote tenía datos inconsistentes y se corrigieron al cargarlo.", TipoMensaje.Aviso);
                }
                AlCambiarLote?.Invoke(LoteActual);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                error = e.Message;
                return false;
            }
        }

        // ---------------- Bitácora de la simulación ----------------
        void RegistrarComando(TipoComando tipo, float valor)
        {
            if (LoteActual == null || sistemaDelLote == null || sim.Sistema != sistemaDelLote) return;
            Anotar(tipo, valor);
            if (tipo != TipoComando.ProbFalla) Guardar();   // ProbFalla puede cambiar muchas veces desde el Inspector
        }

        void Anotar(TipoComando tipo, float valor)
        {
            LoteActual.Comandos.Add(new Comando
            {
                Tick = sistemaDelLote.Modelo.Ticks,
                Tipo = tipo,
                Valor = valor,
                Usuario = Usuario,
                FechaUtc = svc.Reloj.UtcAhora.Ticks
            });
        }

        void AlTick(Lectura l)
        {
            if (LoteActual != null && sistemaDelLote != null && l.Tick % AutoguardadoCadaTicks == 0) Guardar();
        }

        // ---------------- Control Manual: mediciones reales ----------------
        public bool RegistrarMedicion(float temperatura, float humedad, float pesoKg, string nota, out string error)
        {
            error = null;
            if (!svc.Sesion.Puede(Permiso.RegistrarInformes)) { error = "Tu rol no permite registrar mediciones."; return false; }
            if (!EsLoteManual) { error = "Las mediciones de campo son para lotes de Control Manual."; return false; }
            if (LoteActual.Estado != EstadoLote.Activo) { error = "El lote está cerrado."; return false; }

            var registro = new InformeCampo
            {
                Id = Guid.NewGuid().ToString("N"),
                FechaUtc = svc.Reloj.UtcAhora.Ticks,
                Temperatura = temperatura,
                Humedad = humedad,
                PesoKg = pesoKg,
                Nota = ValidadorAporte.LimpiarNota(nota),
                Usuario = Usuario
            };
            LoteActual.Informes.Add(registro);
            if (!Guardar())
            {
                LoteActual.Informes.Remove(registro);   // no mostrar como guardado algo que no llegó a disco
                error = "No se pudo guardar la medición en disco. Revisa el espacio o los permisos.";
                return false;
            }
            AlCambiarRegistros?.Invoke(LoteActual);
            return true;
        }

        // ---------------- Aportes de residuos (ambos modos) ----------------
        public bool RegistrarAporte(string kgTexto, TipoMaterial material, string nota, out string mensaje)
        {
            if (!svc.Sesion.Puede(Permiso.RegistrarAportes)) { mensaje = "Tu rol no permite registrar aportes."; return false; }
            if (LoteActual == null) { mensaje = "No hay un lote activo. Crea una composta primero."; return false; }
            if (LoteActual.Estado != EstadoLote.Activo) { mensaje = "El lote está cerrado."; return false; }
            if (!Enum.IsDefined(typeof(TipoMaterial), material)) { mensaje = "Tipo de material no válido."; return false; }
            if (!ValidadorAporte.IntentarLeerKg(kgTexto, out float kg, out mensaje)) return false;

            var aporte = new Aporte
            {
                Id = Guid.NewGuid().ToString("N"),
                FechaUtc = svc.Reloj.UtcAhora.Ticks,
                Tick = TickActual,
                Kg = kg,
                Material = material,
                Nota = ValidadorAporte.LimpiarNota(nota),
                Usuario = Usuario
            };
            LoteActual.Aportes.Add(aporte);
            if (!Guardar())
            {
                LoteActual.Aportes.Remove(aporte);
                mensaje = "No se pudo guardar el aporte en disco. Revisa el espacio o los permisos.";
                return false;
            }
            mensaje = string.Format(CultureInfo.InvariantCulture, "Aporte registrado: {0:0.##} kg ({1}).", kg, ValidadorAporte.Nombre(material));
            AlCambiarAportes?.Invoke(LoteActual);
            return true;
        }

        // ---------------- Persistencia ----------------
        public bool Guardar()
        {
            if (LoteActual == null) return true;
            try
            {
                // Simulación: siempre la hora actual, así ninguna acción queda "en el futuro" respecto a lo guardado
                if (sistemaDelLote != null) LoteActual.TicksSimulados = sistemaDelLote.Modelo.Ticks;
                LoteActual.ActualizadoUtc = svc.Reloj.UtcAhora.Ticks;
                svc.Lotes.Guardar(LoteActual);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Time.unscaledTime - ultimoError > SegundosEntreErrores)
                {
                    ultimoError = Time.unscaledTime;
                    Notificador.Mostrar("No se pudo guardar el lote: " + e.Message, TipoMensaje.Error);
                }
                return false;
            }
        }

        public bool Exportar(out string carpeta, out string error)
        {
            carpeta = null; error = null;
            if (!svc.Sesion.Puede(Permiso.Exportar)) { error = "Tu rol no permite exportar."; return false; }
            if (LoteActual == null) { error = "No hay un lote para exportar."; return false; }
            try
            {
                Guardar();
                string sello = svc.Reloj.UtcAhora.ToLocalTime().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                string baseClave = $"exportaciones/{LoteActual.Id}_{sello}/";
                var archivos = new System.Collections.Generic.List<(string nombre, string contenido)>
                {
                    ("resumen.csv", ExportadorCsv.Resumen(LoteActual)),
                    ("aportes.csv", ExportadorCsv.Aportes(LoteActual))
                };
                if (EsLoteManual) archivos.Add(("mediciones.csv", ExportadorCsv.Mediciones(LoteActual)));
                else
                {
                    archivos.Add(("bitacora.csv", ExportadorCsv.Bitacora(LoteActual)));
                    archivos.Add(("historial.csv", sistemaDelLote.Historial.ToCsv()));
                }

                // Copia interna (respaldo de la app)
                foreach (var (nombre, contenido) in archivos) svc.Almacen.Escribir(baseClave + nombre, contenido);
                carpeta = System.IO.Path.GetDirectoryName(svc.Almacen.RutaAbsoluta(baseClave + "resumen.csv"));

                // Android: copia en Descargas/Composta/..., visible en la app Archivos y fácil de compartir
                if (DescargasAndroid.Disponible)
                {
                    string sub = $"Composta/{sello}_{(EsLoteManual ? "manual" : "simulacion")}";
                    var bom = new System.Text.UTF8Encoding(true);       // con BOM: Excel muestra bien los acentos
                    foreach (var (nombre, contenido) in archivos)
                    {
                        var bytes = bom.GetPreamble();
                        var cuerpo = System.Text.Encoding.UTF8.GetBytes(contenido);
                        var datos = new byte[bytes.Length + cuerpo.Length];
                        Buffer.BlockCopy(bytes, 0, datos, 0, bytes.Length);
                        Buffer.BlockCopy(cuerpo, 0, datos, bytes.Length, cuerpo.Length);
                        if (!DescargasAndroid.Guardar(sub, nombre, "text/csv", datos, out string errDesc))
                        {
                            error = "Se guardaron en la carpeta interna de la app, pero no en Descargas: " + errDesc;
                            return true;
                        }
                    }
                    carpeta = "Descargas › " + sub.Replace("/", " › ");
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                error = e.Message;
                return false;
            }
        }
    }
}
