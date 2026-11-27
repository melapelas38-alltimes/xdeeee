using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Composta.App
{
    // Decide si los atajos de teclado (V, R, Espacio...) pueden ejecutarse.
    // Sin esto, escribir "v" en el campo de usuario volteaba la composta detrás del login.
    public static class GuardiaEntrada
    {
        static readonly HashSet<object> bloqueos = new HashSet<object>();

        // Por defecto no hay login (la app funciona igual que antes si se desactiva ControlAcceso)
        public static Func<bool> SesionActiva = () => true;

        public static void Bloquear(object dueno) { if (dueno != null) bloqueos.Add(dueno); }
        public static void Liberar(object dueno) { if (dueno != null) bloqueos.Remove(dueno); }

        public static bool HayModalAbierto => bloqueos.Count > 0;

        public static bool AtajosPermitidos
        {
            get
            {
                if (bloqueos.Count > 0) return false;
                if (SesionActiva != null && !SesionActiva()) return false;
                return !CampoDeTextoEnfocado();
            }
        }

        public static bool CampoDeTextoEnfocado()
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var go = es.currentSelectedGameObject;
            if (go == null) return false;
            var campo = go.GetComponent<InputField>();
            return campo != null && campo.isFocused;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reiniciar()
        {
            bloqueos.Clear();
            SesionActiva = () => true;
        }
    }
}
