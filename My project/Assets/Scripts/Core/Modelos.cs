namespace Composta.Core
{
    public enum Etapa { Mesofilica, Termofilica, Enfriamiento, Maduracion }
    public enum Nivel { Ok, Aviso, Critico }
    public enum TipoEvento { Nueva, CambioNivel, Resuelta }

    public class Lectura
    {
        public int Tick;          // hora simulada (eje X de las gráficas)
        public float Dias;
        public float Temperatura;
        public float PH;
        public float Humedad;
        public float Oxigeno;
        public float MasaKg;      // peso estimado (en el modelo de campo baja al descomponerse)
        public Etapa Etapa;
    }

    public class Alerta
    {
        public string Variable;
        public Nivel Nivel;
        public string Mensaje;
        public string Accion;     // qué hacer para corregirla
    }

    public class EventoAlerta
    {
        public TipoEvento Tipo;
        public Alerta Alerta;
        public Nivel Anterior;
    }
}