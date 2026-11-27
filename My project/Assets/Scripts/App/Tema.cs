using Composta.Core;
using Composta.Core.Campo;
using UnityEngine;

namespace Composta.App
{
    // Sistema de diseño (tokens). Un solo lugar para colores, radios y tamaños de texto:
    // cambiar la identidad visual de la app es editar este archivo.
    //
    // Criterios:
    //  · Modo oscuro con 3 niveles de superficie (fondo < superficie < elevada) para dar profundidad sin bordes duros.
    //  · Un acento por modo: AZUL = Simulación (algo calculado), ÁMBAR = Control Manual (algo medido a mano).
    //  · Colores de estado reservados (verde/ámbar/rojo) y siempre acompañados de texto, nunca solo color.
    //  · Contraste de texto ≥ 4.5:1 sobre las superficies (WCAG AA).
    public static class Tema
    {
        // ---- Superficies ----
        public static readonly Color Fondo = Hex(0x0E1116);
        public static readonly Color Superficie = Hex(0x161A21);
        public static readonly Color SuperficieAlta = Hex(0x1E232C);
        public static readonly Color SuperficieCampo = Hex(0x0B0E12);
        public static readonly Color Borde = Hex(0x2A313C);
        public static readonly Color Velo = new Color(0.02f, 0.03f, 0.04f, 0.82f);    // detrás de modales

        // ---- Texto ----
        public static readonly Color TextoPrimario = Hex(0xE8ECF2);
        public static readonly Color TextoSecundario = Hex(0xA3ADBB);
        public static readonly Color TextoTenue = Hex(0x6E7987);

        // ---- Acentos por modo ----
        public static readonly Color AcentoSimulacion = Hex(0x4C8DFF);
        public static readonly Color AcentoManual = Hex(0xF0A24A);
        public static readonly Color AcentoPrediccion = Hex(0xA78BFA);      // VIOLETA = proyección (ni simulado en vivo ni medido)

        // ---- Estados ----
        public static readonly Color Exito = Hex(0x3FB37F);
        public static readonly Color Aviso = Hex(0xE8B13A);
        public static readonly Color Critico = Hex(0xE5534B);
        public static readonly Color ExitoSuave = Hex(0x15291F);
        public static readonly Color AvisoSuave = Hex(0x2B2413);
        public static readonly Color CriticoSuave = Hex(0x301817);

        // ---- Botones ----
        public static readonly Color BotonNeutro = Hex(0x2A3240);
        public static readonly Color BotonPrimario = Hex(0x2E9A68);
        public static readonly Color BotonPeligro = Hex(0x8E3A35);

        // ---- Geometría (unidades de referencia 1920x1080) ----
        public const int RadioTarjeta = 14;
        public const int RadioBoton = 10;
        public const int RadioCampo = 8;
        public const int RadioPildora = 16;

        // ---- Tipografía (tamaños) ----
        public const int TxtTitulo = 32, TxtSubtitulo = 22, TxtCuerpo = 19, TxtPequeno = 15, TxtKpi = 44;

        public static Color Acento(ModoLote m) => m == ModoLote.Manual ? AcentoManual : AcentoSimulacion;

        public static Color Estado(NivelEstado n)
            => n == NivelEstado.Critico ? Critico : n == NivelEstado.Aviso ? Aviso : n == NivelEstado.Ok ? Exito : TextoTenue;

        public static Color EstadoSuave(NivelEstado n)
            => n == NivelEstado.Critico ? CriticoSuave : n == NivelEstado.Aviso ? AvisoSuave : n == NivelEstado.Ok ? ExitoSuave : SuperficieAlta;

        public static string HexDe(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        public static Color ConAlfa(Color c, float a) { c.a = a; return c; }

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
