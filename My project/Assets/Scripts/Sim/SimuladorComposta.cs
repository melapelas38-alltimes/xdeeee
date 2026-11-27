using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using Composta.App;
using Composta.Core;
using Composta.Core.Lotes;
using Composta.Core.Seguridad;

public class SimuladorComposta : MonoBehaviour
{
    [Header("Tiempo")]
    [Min(0.1f)] public float horasPorSegundo = 6f;
    public int[] velocidades = { 1, 2, 5 };
    public bool pausado;
    public int semilla = 1234;
    public bool logPorDia = true;

    [Header("Perfil de composta (build)")]
    public TipoComposta tipo = TipoComposta.PilaAbierta;
    public float pesoKg = 500f;
    public float relacionCN = 28f;              // ideal ~25-30
    public bool permitirPesoExagerado = true;     // true = peso libre; fuera del rango marca PESO EXAGERADO
    public float tempAmbiente = 22f;
    [Tooltip("-1 = usar el valor del perfil")] public float humedadInicial = -1f;
    [Tooltip("-1 = usar el valor del perfil")] public float aislamiento = -1f;


    [Header("Modo del lote")]
    [Tooltip("Simular = guiado con rangos recomendados; Manual = variables libres. Lo fija la ventana Nueva composta.")]
    public ModoLote modo = ModoLote.Simular;

    // El motor solo corre con un lote de SIMULACIÓN. Con un lote de Control Manual queda detenido:
    // no se generan horas, curvas ni valores sintéticos.
    public bool MotorActivo { get; private set; } = true;
    public void DetenerMotor() { MotorActivo = false; Ultimo = null; }

    [Header("Control y sensores")]
    public bool modoAutomatico = false;
    [Range(0f, 0.02f)] public float probFallaPorHora = 0f;

    public SistemaComposta Sistema { get; private set; }
    public ResultadoPaso Ultimo { get; private set; }
    public ModeloComposta Modelo => Sistema.Modelo;
    public Historial Historial => Sistema.Historial;
    public MonitorAlertas Monitor => Sistema.Monitor;
    public int VelocidadActual => velocidades != null && velocidades.Length > 0
        ? velocidades[Mathf.Clamp(idxVel, 0, velocidades.Length - 1)] : 1;

    public event Action<Lectura> AlTick;
    public event Action<EventoAlerta> AlAlerta;

    // ---- Integración con lotes, permisos y UI (los conecta ControlAcceso) ----
    public event Action<TipoComando, float> AlComando;   // acción hecha por una persona -> bitácora del lote
    public event Action<SistemaComposta> AlReiniciar;     // empezó una composta nueva
    public event Action<SistemaComposta> AlCargar;        // se reanudó un lote guardado
    public event Action<string, TipoMensaje> AlMensaje;   // aviso visible para quien usa la app
    public event Action<string, string> AlReporte;        // título, cuerpo (comparativas)

    // null = sin control de acceso (comportamiento original)
    public Func<Permiso, bool> Autorizador;

    int idxVel;
    float acumulado;
    bool autoAplicado;
    float probAplicada;
    const int MaxTicksPorFrame = 50;

    PerfilComposta ConstruirPerfil()
    {
        var p = PerfilComposta.Crear(tipo, pesoKg, permitirPesoExagerado);
        p.RelacionCN = relacionCN;
        p.TempAmbiente = tempAmbiente;
        if (humedadInicial >= 0f) p.HumedadInicial = humedadInicial;
        if (aislamiento >= 0f) p.Aislamiento = aislamiento;
        return p;
    }

    // En el Inspector el peso se limita al rango del perfil (salvo permitirPesoExagerado)
    void OnValidate()
    {
        pesoKg = PerfilComposta.Crear(tipo, pesoKg, permitirPesoExagerado).PesoKg;
        if (horasPorSegundo < 0.1f) horasPorSegundo = 0.1f;
        if (velocidades == null || velocidades.Length == 0) velocidades = new[] { 1 };
        for (int i = 0; i < velocidades.Length; i++) velocidades[i] = Mathf.Max(1, velocidades[i]);
    }

    void Awake()
    {
        Sistema = new SistemaComposta(ConstruirPerfil(), semilla);
        AplicarAjustes();
    }

    // Empieza una composta nueva con los valores actuales del Inspector
    [ContextMenu("Reiniciar simulación")]
    public void Reiniciar()
    {
        if (!Autorizado(Permiso.CrearLote, "crear una composta nueva")) return;
        modo = ModoLote.Simular;                  // el simulador solo crea lotes de simulación
        MotorActivo = true;
        Sistema = new SistemaComposta(ConstruirPerfil(), semilla);
        Ultimo = null;
        acumulado = 0f;
        AplicarAjustes();
        var p = Sistema.Perfil;
        Debug.Log($"Nueva composta: {p.Nombre}, {p.PesoKg:F0} kg (rango {p.PesoMin:F0}-{p.PesoMax:F0}), " +
                  $"fase caliente ~{Modelo.TempPlateau:F0}°C");
        AlReiniciar?.Invoke(Sistema);
    }

    // Reemplaza el sistema por uno reconstruido desde un lote guardado (ver ReproductorLote)
    public void CargarSistema(SistemaComposta sistema, ResultadoPaso ultimo, int semillaLote)
    {
        Sistema = sistema ?? throw new ArgumentNullException(nameof(sistema));
        MotorActivo = true;
        Ultimo = ultimo;
        acumulado = 0f;
        semilla = semillaLote;

        var p = sistema.Perfil;
        tipo = p.Tipo;
        pesoKg = p.PesoKg;
        relacionCN = p.RelacionCN;
        tempAmbiente = p.TempAmbiente;
        humedadInicial = p.HumedadInicial;
        aislamiento = p.Aislamiento;
        permitirPesoExagerado = true;
        modoAutomatico = autoAplicado = sistema.Controlador.Activo;
        probFallaPorHora = probAplicada = sistema.Sensores.ProbFalla;
        AlCargar?.Invoke(Sistema);
    }

    public void SiguientePerfil()
    {
        if (!Autorizado(Permiso.CrearLote, "cambiar de perfil")) return;
        int n = Enum.GetValues(typeof(TipoComposta)).Length;
        tipo = (TipoComposta)(((int)tipo + 1) % n);
        pesoKg = PerfilComposta.Crear(tipo).PesoDefecto;
        humedadInicial = -1f;
        aislamiento = -1f;
        Reiniciar();
    }

    // Candado de sesión: sin una sesión válida no avanza ni una hora simulada, aunque algo quite la pausa
    public bool SesionPermiteSimular => Autorizador == null || Autorizador(Permiso.VerPanel);

    void Update()
    {
        if (Sistema == null || !MotorActivo) return;
        if (!SesionPermiteSimular) return;
        if (GuardiaEntrada.AtajosPermitidos) LeerTeclas();
        SincronizarAjustes();
        if (pausado) return;


        if (horasPorSegundo <= 0f) return;      // configuración inválida: evita división por cero

        float segPorTick = 1f / (horasPorSegundo * VelocidadActual);
        acumulado += Time.deltaTime;

        int n = 0;
        while (acumulado >= segPorTick && n < MaxTicksPorFrame)
        {
            acumulado -= segPorTick;
            n++;
            Paso();
        }
        if (n == MaxTicksPorFrame) acumulado = 0f;
    }

    void AplicarAjustes()
    {
        Sistema.Controlador.Activo = autoAplicado = modoAutomatico;
        Sistema.Sensores.ProbFalla = probAplicada = Mathf.Clamp(probFallaPorHora, 0f, 0.02f);
    }

    // Detecta cambios (botón, tecla o Inspector) y los registra en la bitácora para poder reproducirlos
    void SincronizarAjustes()
    {
        if (modoAutomatico != autoAplicado)
        {
            Sistema.Controlador.Activo = autoAplicado = modoAutomatico;
            AlComando?.Invoke(TipoComando.Automatico, modoAutomatico ? 1f : 0f);
        }
        float prob = Mathf.Clamp(probFallaPorHora, 0f, 0.02f);
        if (prob != probAplicada)
        {
            Sistema.Sensores.ProbFalla = probAplicada = prob;
            AlComando?.Invoke(TipoComando.ProbFalla, prob);
        }
    }

    void Paso()
    {
        var r = Sistema.Paso();
        Ultimo = r;

        foreach (var o in r.Ordenes)
            Debug.Log($"[AUTO] {o.Accion} ({o.Motivo})");

        foreach (var e in r.Eventos)
        {
            LogEvento(e);
            AlAlerta?.Invoke(e);
        }
        AlTick?.Invoke(r.Medida);

        if (logPorDia && Modelo.Ticks % 24 == 0)
        {
            var m = r.Medida; var re = r.Real; var p = Sistema.Perfil;
            Debug.Log($"[{p.Nombre} {p.PesoKg:F0} kg] Día {m.Dias:F0} [{m.Etapa}] medido T:{m.Temperatura:F1}°C pH:{m.PH:F2} H:{m.Humedad:F0}% O2:{m.Oxigeno:F0}%" +
                      $"  |  real T:{re.Temperatura:F1}°C pH:{re.PH:F2} H:{re.Humedad:F0}% O2:{re.Oxigeno:F0}%");
        }
    }

    void LogEvento(EventoAlerta e)
    {
        var a = e.Alerta;
        string tipoAlerta = a.Variable.StartsWith("Sensor", StringComparison.Ordinal) ? "SENSOR"
                          : a.Variable.StartsWith("Predicción", StringComparison.Ordinal) ? "PREDICCIÓN"
                          : a.Variable == "Perfil" ? "PERFIL" : "ALERTA";
        switch (e.Tipo)
        {
            case TipoEvento.Nueva:
                Debug.LogWarning($"[{tipoAlerta}/{a.Nivel}] {a.Mensaje} → {a.Accion}"); break;
            case TipoEvento.CambioNivel:
                Debug.LogWarning($"[{tipoAlerta}/{e.Anterior}→{a.Nivel}] {a.Mensaje} → {a.Accion}"); break;
            case TipoEvento.Resuelta:
                Debug.Log($"✔ {a.Variable} volvió a la normalidad"); break;
        }
    }

    // ---- Permisos y mensajes ----
    public bool PuedeHacer(Permiso p) => Autorizador == null || Autorizador(p);

    bool Autorizado(Permiso p, string accion)
    {
        if (PuedeHacer(p)) return true;
        Informar($"Tu rol no permite {accion}.", TipoMensaje.Aviso);
        return false;
    }

    void Informar(string texto, TipoMensaje tipo)
    {
        if (tipo == TipoMensaje.Error) Debug.LogError(texto);
        else if (tipo == TipoMensaje.Aviso) Debug.LogWarning(texto);
        else Debug.Log(texto);
        AlMensaje?.Invoke(texto, tipo);
    }

    // ---- Métodos públicos: los botones de la UI los llaman ----
    public void Voltear()
    {
        if (!Autorizado(Permiso.OperarComposta, "voltear la composta")) return;
        Sistema.Voltear();
        AlComando?.Invoke(TipoComando.Voltear, 0f);
    }

    public void Regar()
    {
        if (!Autorizado(Permiso.OperarComposta, "regar la composta")) return;
        Sistema.Regar();
        AlComando?.Invoke(TipoComando.Regar, -1f);
    }

    // ---- Ajustes manuales durante el monitoreo (los usa PanelAjustesManuales) ----
    public const float LitrosMin = 0.1f, LitrosMax = 100000f;

    public bool RegarLitros(float litros)
    {
        if (!Autorizado(Permiso.OperarComposta, "regar la composta")) return false;
        if (float.IsNaN(litros) || litros < LitrosMin || litros > LitrosMax)
        {
            Informar($"Los litros deben estar entre {LitrosMin} y {LitrosMax:F0}.", TipoMensaje.Aviso);
            return false;
        }
        Sistema.Regar(litros);
        AlComando?.Invoke(TipoComando.Regar, litros);
        return true;
    }

    public bool CambiarTempAmbiente(float grados)
    {
        if (!Autorizado(Permiso.OperarComposta, "cambiar la temperatura ambiente")) return false;
        var rango = RangosParametros.De(ModoLote.Manual, Parametro.TempAmbiente);
        if (!rango.Contiene(grados))
        {
            Informar($"La temperatura ambiente debe estar entre {rango.Min:F0} y {rango.Max:F0} °C.", TipoMensaje.Aviso);
            return false;
        }
        Modelo.EstablecerTempAmbiente(grados);
        AlComando?.Invoke(TipoComando.TempAmbiente, grados);
        return true;
    }

    public void AlternarPausa() => pausado = !pausado;

    public void CambiarVelocidad()
    {
        if (velocidades == null || velocidades.Length == 0) return;
        idxVel = (idxVel + 1) % velocidades.Length;
    }

    public void AlternarAutomatico()
    {
        if (!Autorizado(Permiso.OperarComposta, "cambiar el modo de control")) return;
        modoAutomatico = !modoAutomatico;
        SincronizarAjustes();
    }

    public void InyectarFalla()
    {
        if (!Autorizado(Permiso.InyectarFallas, "inyectar fallas de prueba")) return;
        Informar(Sistema.Sensores.InyectarAleatoria(), TipoMensaje.Aviso);
        AlComando?.Invoke(TipoComando.InyectarFalla, 0f);
    }

    public void RepararSensores()
    {
        if (!Autorizado(Permiso.RepararSensores, "recalibrar sensores")) return;
        Sistema.Sensores.RepararTodo();
        Informar("Sensores recalibrados", TipoMensaje.Exito);
        AlComando?.Invoke(TipoComando.RepararSensores, 0f);
    }

    public void GuardarCsv()
    {
        if (!Autorizado(Permiso.Exportar, "exportar datos")) return;
        try
        {
            string sello = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string ruta = System.IO.Path.Combine(Application.persistentDataPath, $"historial_composta_{sello}.csv");
            System.IO.File.WriteAllText(ruta, Historial.ToCsv());
            Informar($"CSV guardado en: {ruta}", TipoMensaje.Exito);
        }
        catch (Exception e)   // archivo abierto en Excel, disco lleno, sin permisos...
        {
            Informar("No se pudo guardar el CSV: " + e.Message, TipoMensaje.Error);
        }
    }

    // 3 escenarios con el perfil actual
    public void CorrerComparativa(int dias = 45)
    {
        var p = Sistema.Perfil;
        var nombres = new[] { "Sin intervención", "Automático", "Automático + fallas (2%/h)" };
        var datos = new[]
        {
            SistemaComposta.Experimento(p, dias, false, semilla, 0f),
            SistemaComposta.Experimento(p, dias, true,  semilla, 0f),
            SistemaComposta.Experimento(p, dias, true,  semilla, 0.02f)
        };
        string titulo = $"{p.Nombre} ({p.PesoKg:F0} kg), {dias} días";
        string tabla = Metricas.Tabla(nombres, datos);
        Debug.Log($"=== {titulo} ===\n" + tabla);
        AlReporte?.Invoke("Comparativa · " + titulo, tabla);
    }

    // Los 5 perfiles (peso por defecto) en modo automático
    public void CompararPerfiles(int dias = 45)
    {
        var tipos = (TipoComposta[])Enum.GetValues(typeof(TipoComposta));
        var nombres = new string[tipos.Length];
        var pesos = new float[tipos.Length];
        var datos = new Metricas[tipos.Length];
        for (int i = 0; i < tipos.Length; i++)
        {
            var p = PerfilComposta.Crear(tipos[i]);
            nombres[i] = p.Nombre; pesos[i] = p.PesoKg;
            datos[i] = SistemaComposta.Experimento(p, dias, true, semilla, 0f);
        }
        string tabla = Metricas.TablaPerfiles(nombres, pesos, datos);
        Debug.Log($"=== Comparativa de perfiles a {dias} días (automático) ===\n" + tabla);
        AlReporte?.Invoke($"Comparativa de perfiles · {dias} días (automático)", tabla);
    }

    void LeerTeclas()
    {
        var k = Keyboard.current;
        if (k == null) return;
        if (k.vKey.wasPressedThisFrame) Voltear();
        if (k.rKey.wasPressedThisFrame) Regar();
        if (k.spaceKey.wasPressedThisFrame) AlternarPausa();
        if (k.fKey.wasPressedThisFrame) { CambiarVelocidad(); Informar($"Velocidad x{VelocidadActual}", TipoMensaje.Info); }
        if (k.cKey.wasPressedThisFrame) GuardarCsv();
        if (k.mKey.wasPressedThisFrame && PuedeHacer(Permiso.OperarComposta))
        {
            AlternarAutomatico();
            Informar(modoAutomatico ? "Modo AUTOMÁTICO" : "Modo MANUAL", TipoMensaje.Info);
        }
        if (k.kKey.wasPressedThisFrame) InyectarFalla();
        if (k.lKey.wasPressedThisFrame) RepararSensores();
        if (k.bKey.wasPressedThisFrame) CorrerComparativa();
        if (k.nKey.wasPressedThisFrame) SiguientePerfil();
        if (k.pKey.wasPressedThisFrame) CompararPerfiles();
    }
}
