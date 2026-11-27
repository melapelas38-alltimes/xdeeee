using UnityEngine;
using Composta.Core;

public static class DibujanteGrafica
{
    // reutilizar: textura previa del mismo tamaño para evitar crear/destruir una por refresco
    // marcas: puntos medidos en campo (x en días), se dibujan como círculos blancos
    public static Texture2D Dibujar(Serie s, int w, int h, (float min, float max)? zona = null, Texture2D reutilizar = null,
                                    System.Collections.Generic.IList<(float x, float v)> marcas = null)
    {
        w = Mathf.Max(w, 32); h = Mathf.Max(h, 32);       // con tamaños menores los márgenes quedan invertidos
        var buf = new Color32[w * h];
        Rellenar(buf, w, h, 0, 0, w - 1, h - 1, ColFondo);

        const int m = 12;
        int x0 = m, x1 = w - m - 1, y0 = m, y1 = h - m - 1;
        var (rMin, rMax) = Agregador.Rango(s.Magnitud);
        var z = zona ?? Agregador.ZonaIdeal(s.Magnitud);
        float zMin = z.Item1, zMax = z.Item2;

        float xMin = 0f, xMax = 1f;
        if (s.Puntos.Count > 0)
        {
            xMin = s.Puntos[0].X;
            xMax = s.Puntos[s.Puntos.Count - 1].X;
            if (xMax - xMin < 0.01f) xMax = xMin + 1f;
        }

        int Y(float v) => y0 + Mathf.RoundToInt((Mathf.Clamp(v, rMin, rMax) - rMin) / (rMax - rMin) * (y1 - y0));
        int X(float x) => x0 + Mathf.RoundToInt((x - xMin) / (xMax - xMin) * (x1 - x0));

        // Zona ideal
        Rellenar(buf, w, h, x0, Y(zMin), x1, Y(zMax), ColZona);

        // Cuadrícula (4 divisiones) y ejes
        var rejilla = ColRejilla;
        for (int i = 1; i < 4; i++)
        {
            int y = y0 + (y1 - y0) * i / 4;
            Rellenar(buf, w, h, x0, y, x1, y, rejilla);
        }
        var eje = ColEje;
        Rellenar(buf, w, h, x0, y0, x1, y0, eje);
        Rellenar(buf, w, h, x0, y0, x0, y1, eje);

        Color32 c = ColorDe(s.Magnitud);
        Color32 tenue = new Color32((byte)(c.r * 0.5f), (byte)(c.g * 0.5f), (byte)(c.b * 0.5f), 255);
        var pts = s.Puntos;

        // Barras min-max de cada bloque
        for (int i = 0; i < pts.Count; i++)
        {
            int cx = X(pts[i].X);
            Linea(buf, w, h, cx, Y(pts[i].Min), cx, Y(pts[i].Max), tenue, 1);
        }

        // Línea del promedio
        for (int i = 0; i < pts.Count; i++)
        {
            int cx = X(pts[i].X), cy = Y(pts[i].Prom);
            if (i > 0) Linea(buf, w, h, X(pts[i - 1].X), Y(pts[i - 1].Prom), cx, cy, c, 2);
            else Rellenar(buf, w, h, cx - 2, cy - 2, cx + 2, cy + 2, c);
        }

        if (marcas != null)
            foreach (var (mx, mv) in marcas)
            {
                // Con agrupación diaria/semanal el último punto es el inicio del bloque: se ajusta al borde
                int cx = X(Mathf.Clamp(mx, xMin, xMax)), cy = Y(mv);
                Circulo(buf, w, h, cx, cy, 5, new Color32(20, 20, 24, 255));
                Circulo(buf, w, h, cx, cy, 3, new Color32(255, 255, 255, 255));
            }

        var tex = reutilizar;
        if (tex == null || tex.width != w || tex.height != h)
        {
            if (tex != null) Object.Destroy(tex);
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
        }
        tex.SetPixels32(buf);
        tex.Apply();
        return tex;
    }

    // Colores del tema (misma superficie que las tarjetas, rejilla discreta, zona ideal verde translúcida)
    static readonly Color32 ColFondo = new Color32(22, 26, 33, 255);
    static readonly Color32 ColRejilla = new Color32(38, 44, 54, 255);
    static readonly Color32 ColEje = new Color32(70, 80, 94, 255);
    static readonly Color32 ColZona = new Color32(63, 179, 127, 46);

    // Gráfica SOLO con puntos medidos (Control Manual): sin curvas inventadas entre mediciones.
    // Los puntos se unen con una línea fina punteada para leer la tendencia; cada medición es un círculo.
    public static Texture2D DibujarPuntos(System.Collections.Generic.IList<(float x, float v)> puntos, float yMin, float yMax,
                                          (float min, float max)? zona, Color32 color, int w, int h, Texture2D reutilizar = null)
    {
        w = Mathf.Max(w, 32); h = Mathf.Max(h, 32);
        var buf = new Color32[w * h];
        Rellenar(buf, w, h, 0, 0, w - 1, h - 1, ColFondo);
        const int m = 14;
        int x0 = m, x1 = w - m - 1, y0 = m, y1 = h - m - 1;

        float xMin = 0f, xMax = 7f;
        if (puntos != null && puntos.Count > 0)
        {
            xMin = puntos[0].x; xMax = puntos[puntos.Count - 1].x;
            if (xMax - xMin < 7f) xMax = xMin + 7f;            // al menos una semana de ancho
        }
        int Y(float v) => y0 + Mathf.RoundToInt((Mathf.Clamp(v, yMin, yMax) - yMin) / (yMax - yMin) * (y1 - y0));
        int X(float x) => x0 + Mathf.RoundToInt((x - xMin) / (xMax - xMin) * (x1 - x0));

        if (zona.HasValue) Rellenar(buf, w, h, x0, Y(zona.Value.min), x1, Y(zona.Value.max), ColZona);
        for (int i = 1; i < 4; i++) { int y = y0 + (y1 - y0) * i / 4; Rellenar(buf, w, h, x0, y, x1, y, ColRejilla); }
        Rellenar(buf, w, h, x0, y0, x1, y0, ColEje);

        if (puntos != null)
        {
            var tenue = new Color32((byte)(color.r * 0.6f), (byte)(color.g * 0.6f), (byte)(color.b * 0.6f), 255);
            for (int i = 1; i < puntos.Count; i++)
                LineaPunteada(buf, w, h, X(puntos[i - 1].x), Y(puntos[i - 1].v), X(puntos[i].x), Y(puntos[i].v), tenue);
            for (int i = 0; i < puntos.Count; i++)
            {
                int cx = X(puntos[i].x), cy = Y(puntos[i].v);
                Circulo(buf, w, h, cx, cy, 7, ColFondo);
                Circulo(buf, w, h, cx, cy, 5, color);
            }
        }

        var tex = reutilizar;
        if (tex == null || tex.width != w || tex.height != h)
        {
            if (tex != null) Object.Destroy(tex);
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        }
        tex.SetPixels32(buf);
        tex.Apply();
        return tex;
    }

    static void LineaPunteada(Color32[] buf, int w, int h, int xa, int ya, int xb, int yb, Color32 c)
    {
        int pasos = Mathf.Max(Mathf.Abs(xb - xa), Mathf.Abs(yb - ya));
        for (int i = 0; i <= pasos; i++)
        {
            if ((i / 6) % 2 == 1) continue;                     // 6 px trazo, 6 px hueco
            float t = pasos == 0 ? 0f : (float)i / pasos;
            Pixel(buf, w, h, Mathf.RoundToInt(Mathf.Lerp(xa, xb, t)), Mathf.RoundToInt(Mathf.Lerp(ya, yb, t)), c, 2);
        }
    }

    public static Color32 ColorDe(Magnitud m)
    {
        switch (m)
        {
            case Magnitud.Temperatura: return new Color32(255, 110, 70, 255);
            case Magnitud.PH: return new Color32(190, 120, 255, 255);
            case Magnitud.Humedad: return new Color32(80, 160, 255, 255);
            default: return new Color32(90, 230, 230, 255);
        }
    }

    static void Rellenar(Color32[] buf, int w, int h, int xa, int ya, int xb, int yb, Color32 c)
    {
        if (ya > yb) { int t = ya; ya = yb; yb = t; }
        xa = Mathf.Max(xa, 0); xb = Mathf.Min(xb, w - 1);
        ya = Mathf.Max(ya, 0); yb = Mathf.Min(yb, h - 1);
        float a = c.a / 255f;
        for (int y = ya; y <= yb; y++)
            for (int x = xa; x <= xb; x++)
            {
                var o = buf[y * w + x];
                buf[y * w + x] = new Color32(
                    (byte)(o.r + (c.r - o.r) * a),
                    (byte)(o.g + (c.g - o.g) * a),
                    (byte)(o.b + (c.b - o.b) * a), 255);
            }
    }

    static void Circulo(Color32[] buf, int w, int h, int cx, int cy, int r, Color32 c)
    {
        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
                if (x * x + y * y <= r * r)
                {
                    int px = cx + x, py = cy + y;
                    if (px >= 0 && px < w && py >= 0 && py < h) buf[py * w + px] = c;
                }
    }

    static void Pixel(Color32[] buf, int w, int h, int x, int y, Color32 c, int grosor)
    {
        for (int i = 0; i < grosor; i++)
            for (int j = 0; j < grosor; j++)
            {
                int px = x + i, py = y + j;
                if (px >= 0 && px < w && py >= 0 && py < h) buf[py * w + px] = c;
            }
    }

    // Línea de Bresenham
    static void Linea(Color32[] buf, int w, int h, int x0, int y0, int x1, int y1, Color32 c, int grosor)
    {
        int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            Pixel(buf, w, h, x0, y0, c, grosor);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }
}