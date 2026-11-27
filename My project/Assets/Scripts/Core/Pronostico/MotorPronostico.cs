using System;
using System.Collections.Generic;
using System.Globalization;
using Composta.Core.Lotes;

namespace Composta.Core.Pronostico
{
    public enum TipoHito { InicioFaseCaliente, PicoTemperatura, Volteo, RevisionSemanal, FinFaseCaliente, InicioMaduracion, Cosecha }

    public sealed class Hito
    {
        public TipoHito Tipo;
        public float DiasDesdeAhora;          // 0 = momento del pronóstico
        public string Titulo;
        public string Descripcion;
    }

    public sealed class ResultadoPronostico
    {
        public PerfilComposta Perfil;
        public float DiaSimuladoInicial;      // >0 si se proyecta desde un lote en curso
        public float DiasProyectados;
        public bool AlcanzaFaseCaliente;
        public float TempMaxima;
        public readonly List<Lectura> Lecturas = new List<Lectura>();   // valores proyectados hora a hora (eje X en días desde ahora)
        public readonly List<Hito> Hitos = new List<Hito>();
        public string Supuestos =
            "Supone manejo ideal (control automático de volteo y riego), sin fallas de sensores y clima constante.";
    }

    // Pronóstico aislado: SIEMPRE trabaja sobre una instancia propia de SistemaComposta.
    // Nunca recibe ni modifica el simulador en vivo, ni un lote de Control Manual (los lee y los copia).
    public static class MotorPronostico
    {
        public const int DiasMaximos = 120;
        public const int DiasCosechaTrasMaduracion = 21;
        public const float SeparacionMinimaVolteos = 3f;
        public const int VolteosMaximos = 10;

        // Desde parámetros (ventana "Nueva composta")
        public static ResultadoPronostico DesdePerfil(PerfilComposta perfil, int semilla = 2024)
        {
            if (perfil == null) throw new ArgumentNullException(nameof(perfil));
            return Proyectar(new SistemaComposta(Copiar(perfil), semilla), perfil);
        }

        // Desde un lote de simulación en curso: se reconstruye una COPIA y se proyecta hacia adelante
        public static ResultadoPronostico DesdeLoteSimulado(Lote lote)
        {
            if (lote == null) throw new ArgumentNullException(nameof(lote));
            if (lote.Modo == ModoLote.Manual) return DesdeMediciones(lote);
            var copia = CopiaDeLote(lote);
            ReproductorLote.Sanear(copia);
            var sistema = ReproductorLote.Reconstruir(copia, out _);
            return Proyectar(sistema, sistema.Perfil);
        }

        // Desde un lote de Control Manual: usa la última medición real como punto de partida (solo lectura)
        public static ResultadoPronostico DesdeMediciones(Lote lote, int semilla = 2024)
        {
            if (lote == null) throw new ArgumentNullException(nameof(lote));
            var regs = Campo.AnalisisCampo.Ordenados(lote);
            var ultimo = regs.Count > 0 ? regs[regs.Count - 1] : null;
            var p = PerfilComposta.Crear(lote.Tipo, ultimo != null ? ultimo.PesoKg : lote.PesoKg, true);
            p.RelacionCN = lote.RelacionCN;
            p.Aislamiento = lote.Aislamiento;
            p.TempAmbiente = lote.TempAmbiente;
            p.HumedadInicial = ultimo != null ? ultimo.Humedad : lote.HumedadInicial;
            var r = Proyectar(new SistemaComposta(p, semilla), p);
            r.Supuestos = (ultimo != null
                ? string.Format(CultureInfo.InvariantCulture, "Parte de tu última medición ({0:0.#} kg, {1:F0} % de humedad). ", ultimo.PesoKg, ultimo.Humedad)
                : "Aún no hay mediciones: parte de los valores típicos del tipo de compostera. ") + r.Supuestos;
            return r;
        }

        static ResultadoPronostico Proyectar(SistemaComposta s, PerfilComposta perfil)
        {
            var r = new ResultadoPronostico { Perfil = perfil, DiaSimuladoInicial = s.Modelo.Dias };
            s.Controlador.Activo = true;          // pronóstico con manejo ideal
            s.Sensores.ProbFalla = 0f;
            s.Sensores.RepararTodo();

            int tick0 = s.Modelo.Ticks;
            var etapaPrevia = s.Modelo.Etapa;
            float? inicioCaliente = etapaPrevia == Etapa.Termofilica ? 0f : (float?)null;
            float? finCaliente = null, inicioMaduracion = etapaPrevia == Etapa.Maduracion ? 0f : (float?)null;
            float diaPico = 0f, ultimoVolteo = -999f;
            int volteos = 0;
            r.TempMaxima = s.Modelo.Temperatura;

            for (int h = 1; h <= DiasMaximos * 24; h++)
            {
                var paso = s.Paso();
                float dia = (s.Modelo.Ticks - tick0) / 24f;
                var real = paso.Real;
                r.Lecturas.Add(new Lectura
                {
                    Tick = h, Dias = dia, Temperatura = real.Temperatura, Humedad = real.Humedad,
                    Oxigeno = real.Oxigeno, PH = real.PH, MasaKg = real.MasaKg, Etapa = real.Etapa
                });

                if (real.Temperatura > r.TempMaxima) { r.TempMaxima = real.Temperatura; diaPico = dia; }

                if (real.Etapa != etapaPrevia)
                {
                    if (real.Etapa == Etapa.Termofilica && inicioCaliente == null) inicioCaliente = dia;
                    if (etapaPrevia == Etapa.Termofilica && finCaliente == null) finCaliente = dia;
                    if (real.Etapa == Etapa.Maduracion && inicioMaduracion == null) inicioMaduracion = dia;
                    etapaPrevia = real.Etapa;
                }

                // Volteos que decidiría un manejo ideal (agrupados para no llenar el calendario)
                foreach (var o in paso.Ordenes)
                    if (o.Accion == Accion.Voltear && dia - ultimoVolteo >= SeparacionMinimaVolteos && volteos < VolteosMaximos)
                    {
                        ultimoVolteo = dia; volteos++;
                        Agregar(r, TipoHito.Volteo, dia, "Voltear la composta", $"Airear la pila ({o.Motivo}). Mejora el oxígeno y reparte el calor.");
                    }

                if (inicioMaduracion.HasValue && dia >= inicioMaduracion.Value + DiasCosechaTrasMaduracion) break;
            }

            r.DiasProyectados = r.Lecturas.Count / 24f;
            r.AlcanzaFaseCaliente = inicioCaliente.HasValue;
            var c = CultureInfo.InvariantCulture;

            if (inicioCaliente.HasValue && inicioCaliente.Value > 0f)
                Agregar(r, TipoHito.InicioFaseCaliente, inicioCaliente.Value, "Inicio de la fase caliente",
                        "La pila debería superar los 45 °C. Comprueba la temperatura en el centro.");
            if (r.AlcanzaFaseCaliente)
                Agregar(r, TipoHito.PicoTemperatura, diaPico, "Pico de temperatura estimado",
                        string.Format(c, "Máximo previsto ~{0:F0} °C. Si supera 70 °C, voltea para evitar que se esterilice.", r.TempMaxima));
            if (finCaliente.HasValue)
                Agregar(r, TipoHito.FinFaseCaliente, finCaliente.Value, "Fin de la fase caliente",
                        "La temperatura empieza a bajar: comienza el enfriamiento.");
            if (inicioMaduracion.HasValue)
            {
                Agregar(r, TipoHito.InicioMaduracion, inicioMaduracion.Value, "Inicio de la maduración",
                        "Deja de voltear con frecuencia; mantén la humedad.");
                float cosecha = inicioMaduracion.Value + DiasCosechaTrasMaduracion;
                if (cosecha <= r.DiasProyectados + 0.01f)
                    Agregar(r, TipoHito.Cosecha, cosecha, "Cosecha estimada del compost",
                            "Debería oler a tierra húmeda y no distinguirse los restos. Tamízalo antes de usar.");
            }

            // Revisión semanal hasta el final del horizonte
            for (float d = 7f; d <= r.DiasProyectados + 0.01f; d += 7f)
                Agregar(r, TipoHito.RevisionSemanal, d, "Revisión semanal de la composta",
                        "Mide temperatura, humedad y peso, y regístralos en la app.");

            r.Hitos.Sort((a, b) => a.DiasDesdeAhora != b.DiasDesdeAhora ? a.DiasDesdeAhora.CompareTo(b.DiasDesdeAhora) : a.Tipo.CompareTo(b.Tipo));
            return r;
        }

        static void Agregar(ResultadoPronostico r, TipoHito t, float dia, string titulo, string detalle)
            => r.Hitos.Add(new Hito { Tipo = t, DiasDesdeAhora = dia, Titulo = titulo, Descripcion = detalle });

        static PerfilComposta Copiar(PerfilComposta p)
        {
            var c = PerfilComposta.Crear(p.Tipo, p.PesoKg, true);
            c.RelacionCN = p.RelacionCN; c.TempAmbiente = p.TempAmbiente; c.HumedadInicial = p.HumedadInicial;
            c.Aislamiento = p.Aislamiento; c.CalorMax = p.CalorMax; c.AmplitudPH = p.AmplitudPH;
            return c;
        }

        // Copia con listas nuevas: Sanear ordena/filtra listas y no debe tocar el lote original
        static Lote CopiaDeLote(Lote l) => new Lote
        {
            Version = l.Version, Id = l.Id, Nombre = l.Nombre, Estado = l.Estado, Modo = l.Modo,
            Tipo = l.Tipo, PesoKg = l.PesoKg, RelacionCN = l.RelacionCN, HumedadInicial = l.HumedadInicial,
            Aislamiento = l.Aislamiento, TempAmbiente = l.TempAmbiente, Semilla = l.Semilla,
            CreadoPor = l.CreadoPor, CreadoUtc = l.CreadoUtc, ActualizadoUtc = l.ActualizadoUtc, TicksSimulados = l.TicksSimulados,
            Comandos = new List<Comando>(l.Comandos ?? new List<Comando>()),
            Aportes = new List<Aporte>(l.Aportes ?? new List<Aporte>()),
            Informes = new List<Campo.InformeCampo>(l.Informes ?? new List<Campo.InformeCampo>())
        };
    }
}
