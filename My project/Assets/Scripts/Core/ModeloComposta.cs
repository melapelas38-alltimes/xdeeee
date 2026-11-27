using System;

namespace Composta.Core
{
    public class ModeloComposta
    {
        public readonly PerfilComposta Perfil;

        // ---- Estado ----
        public float Temperatura = 22f;
        public float PH = 6.5f;
        public float Humedad;
        public float Oxigeno = 20f;
        public int Ticks = 0;                 // 1 tick = 1 hora simulada
        public float Dias = 0f;
        public Etapa Etapa = Etapa.Mesofilica;
        public float TempAmbiente = 22f;

        // ---- Perillas (se derivan del perfil: peso, aislamiento y C/N) ----
        public float KTemp, KPH = 0.03f, Evaporacion, ConsumoO2;
        public float MaxCambioTemp = 3f;
        public float TempMax = 75f;

        float calor, cubica;
        readonly System.Random rnd;

        // ---- Modelo de campo (lotes en modo Manual con informes semanales) ----
        // En el modelo clásico (Simular) nada de esto altera la evolución: los resultados son idénticos a antes.
        public readonly bool ModeloCampo;
        public const float FraccionDegradable = 0.5f;     // ~la mitad de la masa inicial puede descomponerse
        const float TasaDescomposicion = 0.0012f;          // por hora, en condiciones óptimas
        public float MasaKg;                               // peso actual estimado
        public float MasaDegradable;                       // lo que aún puede descomponerse
        public float MasaDegradableInicial { get; private set; }
        public float UltimaPerdidaHora { get; private set; }   // kg de material degradable perdidos en la última hora

        // Perillas ocultas que la calibración ajusta con cada informe (1 = valor deducido del perfil)
        public float FactorActividad = 1f;        // equivale a la relación C/N
        public float FactorEvaporacion = 1f;      // equivale al aislamiento / exposición al sol y viento
        public float FactorDescomposicion = 1f;   // velocidad con que se deshace el material

        // Promedio del freno por falta de oxígeno desde el último informe (1 = sin freno).
        // La calibración lo usa para saber si "más actividad" calienta o enfría la pila.
        double sumaFactorO2; int horasFactorO2;
        public float FactorO2Promedio => horasFactorO2 == 0 ? 1f : (float)(sumaFactorO2 / horasFactorO2);
        public void ReiniciarPromedioO2() { sumaFactorO2 = 0; horasFactorO2 = 0; }

        public float FraccionRestante => MasaDegradableInicial <= 0f ? 0f : MasaDegradable / MasaDegradableInicial;

        // Temperatura a la que tiende la fase caliente con este peso y este aislamiento
        public float TempPlateau => TempAmbiente + Math.Max(0f, Perfil.CalorMax - TempAmbiente) * calor;
        public bool FaseCalientePosible => TempPlateau >= 45f;

        public ModeloComposta(PerfilComposta perfil, int semilla = 1234, bool modeloCampo = false)
        {
            Perfil = perfil;
            ModeloCampo = modeloCampo;
            rnd = new System.Random(semilla);
            Humedad = perfil.HumedadInicial;
            TempAmbiente = perfil.TempAmbiente;
            Temperatura = perfil.TempAmbiente;

            MasaKg = perfil.PesoKg;
            MasaDegradable = MasaDegradableInicial = perfil.PesoKg * FraccionDegradable;
            RecalcularParametros();
        }

        // Deriva las constantes térmicas. En el modelo clásico se llama una sola vez (como antes);
        // en el de campo, cada hora: la masa baja y las perillas cambian con la calibración.
        void RecalcularParametros()
        {
            float masa = ModeloCampo ? Math.Max(MasaKg, RangosParametros.PesoAbsMin) : Perfil.PesoKg;
            // Relación superficie/volumen ~ 1 / (peso^(1/3)); 500 kg es la pila de referencia
            cubica = (float)Math.Pow(masa / 500f, 1.0 / 3.0);
            float act = Perfil.Actividad();
            if (ModeloCampo) act *= FactorActividad;

            calor = Clamp(cubica * Perfil.Aislamiento * act, 0.2f, 1.1f);
            KTemp = Clamp(0.06f / cubica, 0.03f, 0.15f);                                  // más masa = más lenta
            Evaporacion = Clamp(0.03f / (cubica * Perfil.Aislamiento), 0.01f, 0.12f);    // más chica = se seca más
            if (ModeloCampo) Evaporacion = Clamp(Evaporacion * FactorEvaporacion, 0.003f, 0.36f);
            ConsumoO2 = 0.08f * Clamp(act, 0.4f, 1.4f);
        }

        public ModeloComposta(int semilla = 1234)
            : this(PerfilComposta.Crear(TipoComposta.PilaAbierta), semilla, false) { }

        // Avanza EXACTAMENTE 1 hora simulada (no depende de los FPS)
        public void AvanzarHora()
        {
            Ticks++;
            Dias = Ticks / 24f;
            if (ModeloCampo) { RecalcularParametros(); ActualizarEtapaCampo(); }
            else ActualizarEtapa();

            // Temperatura: persigue un objetivo; con poco O2 el calor se frena
            float factorO2 = Clamp(Oxigeno / 12f, 0.2f, 1f);
            sumaFactorO2 += factorO2; horasFactorO2++;
            float objetivo = TempAmbiente + (TempObjetivo() - TempAmbiente) * factorO2;
            float dT = (objetivo - Temperatura) * KTemp + Ruido(0.15f);
            Temperatura += Clamp(dT, -MaxCambioTemp, MaxCambioTemp);

            // pH
            PH += (PHObjetivo() - PH) * KPH + Ruido(0.01f);

            // Humedad: se evapora más con calor
            Humedad -= Evaporacion * (Temperatura / 60f);

            // Oxígeno: lo consumen los microbios; muy húmedo = más compacto
            float consumo = ConsumoO2 * (Temperatura > 30f ? 1f : 0.5f);
            if (Humedad > Perfil.LimitesDe(Magnitud.Humedad).AvisoMax + 5f) consumo *= 1.5f;
            Oxigeno -= consumo;

            Descomponer();     // no usa números aleatorios: no altera la secuencia del modelo clásico
            Limitar();
        }

        // Pérdida de masa: máxima a 50-60 °C con humedad de 40-65 %; se agota a medida que se descompone
        void Descomponer()
        {
            float fT = Math.Max(0.1f, (float)Math.Exp(-Math.Pow((Temperatura - 55f) / 18f, 2)));
            float fH = Humedad < 40f ? Clamp((Humedad - 15f) / 25f, 0f, 1f)
                     : Humedad > 65f ? Clamp(1f - (Humedad - 65f) / 25f * 0.7f, 0.3f, 1f) : 1f;
            float k = TasaDescomposicion * (ModeloCampo ? FactorDescomposicion : 1f);
            float perdida = Math.Min(MasaDegradable, k * fT * fH * MasaDegradable);
            MasaDegradable -= perdida;
            MasaKg -= perdida;
            UltimaPerdidaHora = perdida;
        }

        // Corrección por un informe de campo (la calcula CalibracionCampo)
        public void AplicarCorreccion(float dTemp, float dHumedad, float dMasa)
        {
            Temperatura += dTemp;
            Humedad += dHumedad;
            MasaKg = Math.Max(RangosParametros.PesoAbsMin, MasaKg + dMasa);
            MasaDegradable = Clamp(MasaDegradable + dMasa, 0f, MasaKg);
            RecalcularParametros();
            Limitar();
            if (ModeloCampo) ActualizarEtapaCampo();
        }

        // ---- Acciones del usuario ----
        public void Voltear()
        {
            Oxigeno = 20f;
            Temperatura -= Clamp(5f / cubica, 2.5f, 7.5f);   // una pila grande se enfría menos
            Humedad -= 2f;
            Limitar();
        }

        // Ajuste manual durante el monitoreo (cambio de clima, traslado de la pila...)
        public void EstablecerTempAmbiente(float grados)
        {
            if (float.IsNaN(grados) || float.IsInfinity(grados)) return;
            TempAmbiente = grados;
            Limitar();
        }

        // litros < 0 = riego estándar del perfil (8% del peso). El efecto depende del peso.
        public void Regar(float litros = -1f)
        {
            if (float.IsNaN(litros) || float.IsInfinity(litros) || litros < 0f) litros = Perfil.LitrosPorRiego;
            Humedad += litros / (ModeloCampo ? Math.Max(MasaKg, 1f) : Perfil.PesoKg) * 100f;
            Temperatura -= 1f;
            Limitar();
        }

        // ---- Parámetros por etapa (relativos a la fase caliente del perfil) ----
        float TempObjetivo()
        {
            float span = TempPlateau - TempAmbiente;
            if (ModeloCampo)
            {
                // El calor lo genera el material que queda: meseta mientras abunda, baja cuando se agota
                float g = Math.Min(1f, FraccionRestante / 0.6f);
                return TempAmbiente + span * g;
            }
            switch (Etapa)
            {
                case Etapa.Mesofilica: return TempAmbiente + 0.70f * span;
                case Etapa.Termofilica: return TempPlateau;
                case Etapa.Enfriamiento: return TempAmbiente + 0.325f * span;
                default: return TempAmbiente + 3f;
            }
        }

        float PHObjetivo()
        {
            float baseEtapa;
            switch (Etapa)
            {
                case Etapa.Mesofilica: baseEtapa = 6.0f; break;
                case Etapa.Termofilica: baseEtapa = 7.8f; break;
                case Etapa.Enfriamiento: baseEtapa = 7.4f; break;
                default: baseEtapa = 7.0f; break;
            }
            float v = 7f + (baseEtapa - 7f) * Perfil.AmplitudPH;
            if (Etapa == Etapa.Termofilica)
                v += Clamp((28f - Perfil.RelacionCN) * 0.02f, -0.4f, 0.4f);   // poco carbono = más amoníaco
            return v;
        }

        // Fases deducidas de la temperatura y del material restante (no de los días), para poder
        // corregirlas con mediciones reales
        void ActualizarEtapaCampo()
        {
            float fr = FraccionRestante;
            switch (Etapa)
            {
                case Etapa.Mesofilica:
                    if (Temperatura >= 45f) Etapa = Etapa.Termofilica;
                    else if (fr < 0.6f) Etapa = Etapa.Enfriamiento;          // pilas frías: sin fase caliente
                    break;
                case Etapa.Termofilica:
                    if (Temperatura < 40f) Etapa = Etapa.Enfriamiento;
                    break;
                case Etapa.Enfriamiento:
                    if (Temperatura >= 45f) Etapa = Etapa.Termofilica;       // se reactivó (p. ej. tras voltear)
                    else if (fr < 0.35f && Temperatura < TempAmbiente + 8f) Etapa = Etapa.Maduracion;
                    break;
                case Etapa.Maduracion:
                    if (Temperatura >= 45f) Etapa = Etapa.Termofilica;
                    break;
            }
        }

        void ActualizarEtapa()
        {
            float span = TempPlateau - TempAmbiente;
            switch (Etapa)
            {
                case Etapa.Mesofilica:
                    if (FaseCalientePosible && Temperatura >= TempAmbiente + 0.575f * span) Etapa = Etapa.Termofilica;
                    else if (Dias >= 15f) Etapa = Etapa.Enfriamiento;   // pilas frías saltan la fase caliente
                    break;
                case Etapa.Termofilica:
                    if (Dias >= 15f) Etapa = Etapa.Enfriamiento;
                    break;
                case Etapa.Enfriamiento:
                    if ((Dias >= 30f && Temperatura < TempAmbiente + 0.4f * span) || Dias >= 45f)
                        Etapa = Etapa.Maduracion;
                    break;
            }
        }

        public Lectura Capturar() => new Lectura
        {
            Tick = Ticks,
            Dias = Dias,
            Temperatura = Temperatura,
            PH = PH,
            Humedad = Humedad,
            Oxigeno = Oxigeno,
            MasaKg = MasaKg,
            Etapa = Etapa
        };

        // ---- Utilidades ----
        void Limitar()
        {
            Temperatura = Clamp(Temperatura, TempAmbiente, TempMax);
            Humedad = Clamp(Humedad, 0f, 100f);
            Oxigeno = Clamp(Oxigeno, 2f, 21f);
            PH = Clamp(PH, 3f, 10f);
        }

        float Ruido(float amp) => (float)(rnd.NextDouble() * 2 - 1) * amp;
        static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}