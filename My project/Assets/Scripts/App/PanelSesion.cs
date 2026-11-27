using System;
using Composta.Core.Seguridad;
using UnityEngine;
using UnityEngine.UI;

namespace Composta.App
{
    // Franja en la barra superior: quién está conectado + "Cuenta" + "Cerrar sesión"
    public sealed class PanelSesion : MonoBehaviour
    {
        public event Action PedirCuenta;
        public event Action PedirSalir;

        RectTransform raiz;
        Text txtUsuario;
        Button btnCuenta;

        public void Mostrar(Usuario u)
        {
            Asegurar();
            txtUsuario.text = $"{UIFabrica.SinRich(u.NombreVisible)}  <color=#9aa3b5>· {PoliticaAcceso.Nombre(u.Rol)}</color>";
            UIFabrica.Etiqueta(btnCuenta).text = u.EsAdministrador ? "Usuarios" : "Mi cuenta";
            raiz.gameObject.SetActive(true);
        }

        public void Ocultar()
        {
            if (raiz != null) raiz.gameObject.SetActive(false);
        }

        void Asegurar()
        {
            if (raiz != null) return;
            raiz = UIFabrica.Canvas("Canvas Sesion", transform, 20);
            // Ocupa el hueco entre la cabecera de PanelUI (izquierda) y el HUD de PanelBuild (derecha)
            var franja = UIFabrica.Panel("Sesion", raiz, new Vector2(0.405f, 0.943f), new Vector2(0.655f, 0.982f), new Color(0, 0, 0, 0));
            // Móvil: segunda fila bajo la barra superior (izquierda), con "Nueva composta" a la derecha
            Responsivo.En(franja, new Vector2(0f, 1f), new Vector2(0.62f, 1f), new Vector2(12, -128), new Vector2(-4, -72));
            txtUsuario = UIFabrica.Texto(franja, "Usuario", new Vector2(0f, 0f), new Vector2(0.52f, 1f), 18, TextAnchor.MiddleRight, Color.white);
            txtUsuario.resizeTextForBestFit = true; txtUsuario.resizeTextMinSize = 11; txtUsuario.resizeTextMaxSize = 18;
            btnCuenta = UIFabrica.Boton(franja, "Cuenta", new Vector2(0.53f, 0f), new Vector2(0.75f, 1f), 17, UIFabrica.ColBoton, () => PedirCuenta?.Invoke());
            var salir = UIFabrica.Boton(franja, "Salir", new Vector2(0.77f, 0f), new Vector2(1f, 1f), 17, UIFabrica.ColPeligro, () => PedirSalir?.Invoke());
            foreach (var b in new[] { btnCuenta, salir })
            {
                var et = UIFabrica.Etiqueta(b);
                et.resizeTextForBestFit = true; et.resizeTextMinSize = 10; et.resizeTextMaxSize = 17;
                et.horizontalOverflow = HorizontalWrapMode.Overflow;      // "Usuarios" nunca se parte en dos líneas
            }
            // Móvil: más espacio para los botones
            Responsivo.En(txtUsuario, new Vector2(0f, 0f), new Vector2(0.44f, 1f));
            Responsivo.En(btnCuenta, new Vector2(0.46f, 0f), new Vector2(0.72f, 1f));
            Responsivo.En(salir, new Vector2(0.74f, 0f), new Vector2(1f, 1f));
        }
    }
}
