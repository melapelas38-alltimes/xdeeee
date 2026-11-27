using System.Collections.Generic;
using Composta.Core.Campo;

namespace Composta.Core
{
    public class ResultadoPaso
    {
        public Lectura Real;
        public Lectura Medida;
        public Medicion[] Mediciones;
        public List<Orden> Ordenes = new List<Orden>();
        public List<EventoAlerta> Eventos;
    }

    public class SistemaComposta
    {
        public readonly PerfilComposta Perfil;
        public readonly ModeloComposta Modelo;
        public readonly PanelSensores Sensores;
        public readonly Predictor Predictor;
        public readonly Controlador Controlador;
        public readonly MonitorAlertas Monitor;
        public readonly Historial Historial;
        public readonly Metricas Metricas;

        // Solo en lotes Manual: no hay sensores físicos; los valores son estimaciones calibradas con informes
        public readonly CalibracionCampo Calibracion;
        public bool SinSensores => Calibracion != null;

        public SistemaComposta(PerfilComposta perfil, int semilla = 1234, bool modoCampo = false)
        {
            Perfil = perfil;
            Modelo = new ModeloComposta(perfil, semilla, modoCampo);
            Sensores = new PanelSensores(semilla + 1);
            Predictor = new Predictor(perfil);
            Controlador = new Controlador(perfil);
            Monitor = new MonitorAlertas();
            Historial = new Historial();
            Metricas = new Metricas(perfil);
            if (modoCampo) Calibracion = new CalibracionCampo(Modelo, Historial);
        }

        public SistemaComposta(int semilla = 1234)
            : this(PerfilComposta.Crear(TipoComposta.PilaAbierta), semilla) { }

        public ResultadoPaso Paso()
        {
            Modelo.AvanzarHora();
            var real = Modelo.Capturar();
            var med = SinSensores ? Estimar(real) : Sensores.Medir(real);
            var medida = Armar(real, med);
            Predictor.Observar(med);

            var res = new ResultadoPaso { Real = real, Medida = medida, Mediciones = med };

            foreach (var o in Controlador.Decidir(medida, med, real.Tick))
            {
                if (o.Accion == Accion.Voltear) Voltear(); else Regar();
                res.Ordenes.Add(o);
            }

            var conf = new bool[4];
            for (int i = 0; i < 4; i++) conf[i] = med[i].Confiable;

            var alertas = Reglas.Evaluar(medida, Perfil, conf);
            if (!SinSensores) alertas.AddRange(AlertasSensores(med));
            alertas.AddRange(Predictor.Evaluar());
            if (SinSensores)
                foreach (var a in alertas) a.Mensaje += " (estimado)";
            if (Perfil.PesoExagerado)
                alertas.Add(new Alerta
                {
                    Variable = "Perfil",
                    Nivel = Nivel.Aviso,
                    Mensaje = $"Peso {Perfil.PesoKg:F0} kg absurdo para {Perfil.Nombre} (límites {Perfil.ExtremoMin:F0}-{Perfil.ExtremoMax:F0} kg)",
                    Accion = "Cambiar de perfil o ajustar el peso"
                });
            res.Eventos = Monitor.Actualizar(alertas);

            Historial.Agregar(medida, real);
            Metricas.Registrar(real);
            return res;
        }

        public void Voltear()
        {
            Modelo.Voltear();
            Metricas.Volteos++;
            Controlador.Registrar(Accion.Voltear, Modelo.Ticks);
            Predictor.Reiniciar();
        }

        // litros < 0 = riego estándar del perfil
        public void Regar(float litros = -1f)
        {
            Modelo.Regar(litros);
            Metricas.Riegos++;
            Controlador.Registrar(Accion.Regar, Modelo.Ticks);
            Predictor.Reiniciar();
        }

        // Sin sensores, "lo medido" es la estimación del modelo (calibrada con los informes semanales)
        static Medicion[] Estimar(Lectura real)
        {
            var med = new Medicion[4];
            for (int i = 0; i < 4; i++)
                med[i] = new Medicion
                {
                    Magnitud = (Magnitud)i,
                    Valor = Agregador.Valor(real, (Magnitud)i),
                    Confiable = true,
                    Nivel = Nivel.Ok,
                    Diagnostico = "Sin sensor · estimado por el modelo"
                };
            return med;
        }

        static Lectura Armar(Lectura real, Medicion[] med) => new Lectura
        {
            MasaKg = real.MasaKg,
            Tick = real.Tick,
            Dias = real.Dias,
            Etapa = real.Etapa,
            Temperatura = med[(int)Magnitud.Temperatura].Valor,
            PH = med[(int)Magnitud.PH].Valor,
            Humedad = med[(int)Magnitud.Humedad].Valor,
            Oxigeno = med[(int)Magnitud.Oxigeno].Valor
        };

        static IEnumerable<Alerta> AlertasSensores(Medicion[] med)
        {
            foreach (var m in med)
                if (m.Nivel != Nivel.Ok)
                    yield return new Alerta
                    {
                        Variable = "Sensor " + Reglas.Nombre(m.Magnitud),
                        Nivel = m.Nivel,
                        Mensaje = m.Diagnostico,
                        Accion = "Revisar o recalibrar el sensor"
                    };
        }

        public static Metricas Experimento(PerfilComposta perfil, int dias, bool automatico, int semilla, float probFalla)
        {
            var s = new SistemaComposta(perfil, semilla);
            s.Controlador.Activo = automatico;
            s.Sensores.ProbFalla = probFalla;
            for (int i = 0; i < dias * 24; i++) s.Paso();
            return s.Metricas;
        }
    }
}