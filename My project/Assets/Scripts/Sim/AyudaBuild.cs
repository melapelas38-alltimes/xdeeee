using Composta.Core;

// Textos de ayuda de la ventana "Nueva composta". Centralizados para editarlos sin tocar la UI.
// Los rangos de peso salen del perfil real: si cambias PerfilComposta, la ayuda se actualiza sola.
public static class AyudaBuild
{
    public static string Tipo(PerfilComposta perfil)
        => $"{Descripcion(perfil.Tipo)} Rango recomendado: {perfil.PesoMin:0.#}-{perfil.PesoMax:0.#} kg. " +
           $"Fuera de {perfil.ExtremoMin:0.#}-{perfil.ExtremoMax:0.#} kg el peso se considera exagerado para este recipiente.";

    static string Descripcion(TipoComposta t)
    {
        switch (t)
        {
            case TipoComposta.Domestica: return "Un bote en casa (80-300 L).";
            case TipoComposta.Cerrada: return "Recipiente con tapa o rotativo, retiene más calor.";
            case TipoComposta.PilaAbierta: return "Montón al aire libre (~1 m³ ≈ 500 kg).";
            case TipoComposta.Industrial: return "Hilera de planta de compostaje.";
            default: return "Con lombrices. Se mantiene fría (máx. ~28 °C) y más húmeda.";
        }
    }

    public static string Monograma(TipoComposta t)
    {
        switch (t)
        {
            case TipoComposta.Domestica: return "CD";
            case TipoComposta.Cerrada: return "CC";
            case TipoComposta.PilaAbierta: return "PA";
            case TipoComposta.Industrial: return "HI";
            default: return "VC";
        }
    }

    public const string Modo =
        "Simulación: el motor calcula hora a hora temperatura, humedad, oxígeno, pH y fases a partir de los parámetros. " +
        "Rangos guiados; un peso exagerado bloquea el inicio.\n\n" +
        "Control manual: SIN simulación. El tablero empieza vacío (\"Esperando primer registro\") y muestra solo lo que " +
        "midas en la pila: temperatura, humedad y peso, una vez por semana. Fase y recomendaciones se deducen de tus datos.";

    public const string Resumen =
        "Indica qué pasará con la configuración actual. Si dice 'no alcanza fase caliente', con ese peso la pila no llegará " +
        "a ~45 °C, la temperatura necesaria para eliminar patógenos y semillas.";

    public const string PesoExagerado =
        "Aparece solo cuando el peso es físicamente absurdo para el tipo de recipiente (por ejemplo 300 kg en una compostera " +
        "doméstica). Si el peso está algo fuera del rango ideal verás un consejo, pero no un bloqueo.";

    // Mismo orden que los parámetros: Peso, Relación C/N, Humedad inicial, Aislamiento, Temp. ambiente
    public static readonly string[] Parametros =
    {
        "Kilos de residuos en el lote. Verde = rango recomendado; ámbar = funciona pero no es ideal; gris = exagerado para " +
        "este recipiente. Más masa = se calienta más y se seca menos; una pila chica pierde calor.",

        "Carbono (hojas secas, cartón) vs. nitrógeno (restos de comida, pasto). Ideal: 25-30:1 (barra verde). " +
        "Con poco carbono es más activa y huele a amoníaco; con mucho carbono es lenta.",

        "Qué tan mojado empieza el lote (barra verde = rango óptimo). " +
        "Muy seca: los microbios no trabajan; muy mojada: falta oxígeno y huele mal.",

        "Qué tanto retiene el calor el recipiente. x1.00 = normal. Más de 1 = guarda más calor y evapora menos.",

        "Temperatura del lugar. La composta parte de esta temperatura y nunca baja de ella."
    };

    // ---- Control manual: preguntas fáciles en lugar de parámetros difíciles de medir ----
    public static (string texto, float valor)[] OpcionesMaterial()
        => new[] { ("Restos de cocina", 18f), ("Mezcla", 28f), ("Hojas secas / cartón", 40f) };

    public static (string texto, float valor)[] OpcionesTapada()
        => new[] { ("Sí, tapada o cubierta", 1.3f), ("No, al aire libre", 1f) };

    public const string AyudaMaterial =
        "Elige lo que más abunda. Se usa para interpretar tus mediciones (por ejemplo, si la pila no se calienta, " +
        "te sugerirá agregar material verde). No hace falta conocer la relación C/N.";

    public const string AyudaTapada =
        "Una pila tapada o cubierta retiene calor y humedad. Se usa para interpretar tus mediciones.";

    public const string Prediccion =
        "Abre una ventana de pronóstico con estos datos: curvas proyectadas, fechas previstas (fase caliente, volteos, " +
        "maduración y cosecha) y la opción de enviarlas a tu Google Calendar. No crea ni modifica ningún lote.";

    public const string Cancelar =
        "Cierra esta ventana sin cambiar nada. Solo aparece si ya tienes un lote en curso.";

    public const string IniciarSimulacion =
        "Crea un lote de simulación que queda guardado y se reanuda al volver a entrar. Si había otro lote activo, se archiva.";

    public const string IniciarManual =
        "Crea un lote de control manual vacío (sin simulación). Después registra la primera medición desde el tablero. " +
        "Si había otro lote activo, se archiva.";
}
