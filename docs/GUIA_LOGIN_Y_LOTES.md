# Login, lotes persistentes y auditoría: guía técnica

## Arquitectura por capas

```
Assets/Scripts/
├── Core/                  (asmdef Composta.Core · C# puro, sin UnityEngine, con tests)
│   ├── (modelo, sensores, reglas, predicción, control: ya existían)
│   ├── Seguridad/         Roles + PoliticaAcceso, Usuario, HasherPbkdf2, ValidadorCredenciales,
│   │                      ServicioAutenticacion, Sesion
│   ├── Persistencia/      IAlmacenDatos/ISerializador/IReloj, AlmacenArchivos (escritura atómica),
│   │                      RepositorioUsuariosJson, RepositorioLotesJson
│   └── Lotes/             Lote/Comando/Aporte, ValidadorAporte, ReproductorLote, ExportadorCsv
├── App/                   (Unity: composición, flujo y vistas)
│   ├── ServiciosApp       raíz de composición (único "new" de implementaciones concretas)
│   ├── ControlAcceso      flujo login → lote → uso → cierre / inactividad (se crea solo)
│   ├── GestorLotes        bitácora de acciones, autoguardado, reanudar, aportes, exportar
│   ├── PanelLogin         vista del login (3 modos + error fatal)
│   ├── PanelSesion        franja superior: persona conectada, Cuenta, Salir
│   ├── PanelAportes       registro de residuos pesados
│   ├── PanelUsuarios      administración de cuentas + cambio de contraseña propia
│   ├── Notificador        toasts y diálogos visibles en la build (antes todo iba a Debug.Log)
│   ├── GuardiaEntrada     bloquea atajos de teclado con modales o campos de texto enfocados
│   └── UIFabrica          helpers de UI por código (mismo estilo que PanelUI/PanelBuild)
├── Sim/                   (existente, con integraciones mínimas)
└── Tests/EditMode         61 tests NUnit (Window > General > Test Runner)
```

## Estructura de la UI (orden de dibujo, `Canvas.sortingOrder`)

| Orden | Canvas | Contenido |
|---|---|---|
| 0 | Canvas UI (PanelUI) | tarjetas, gráficas, alertas, 10 botones inferiores |
| 10 | Canvas Build (PanelBuild) | botón «Nueva composta» y ventana de builds |
| 20 | Canvas Sesion | `Nombre · Rol` + «Usuarios/Mi cuenta» + «Salir» (x 0.405–0.655 de la barra superior) |
| 40 | Canvas Aportes / Usuarios | modales |
| 60 | Canvas Dialogo | confirmaciones y reportes |
| 100 | Canvas Login | fondo opaco + tarjeta centrada (x 0.34–0.66, y 0.12–0.88) |
| 300 | Canvas Toasts | mensajes efímeros (no bloquean clics) |

Tarjeta de login (de arriba a abajo): título · subtítulo según el modo · filas dinámicas
(etiqueta + `InputField`) · «Mostrar contraseña» · línea de error · botón principal · pie.

| Modo | Campos | Botón |
|---|---|---|
| `ConfiguracionInicial` (primer uso) | Nombre, Usuario, Contraseña, Confirmar | Crear cuenta de administración |
| `Login` | Usuario (recordado), Contraseña | Ingresar |
| `CambioObligatorio` (contraseña temporal) | Nueva, Confirmar | Guardar contraseña · Cancelar |
| `ErrorFatal` (archivo de cuentas dañado) | — | Reintentar |

Teclado: `Tab`/`Shift+Tab` cambian de campo, `Enter` envía y `Esc` cierra los modales.

## Roles

| Permiso | Consulta | Operación | Administración |
|---|:-:|:-:|:-:|
| Ver panel, exportar | ✔ | ✔ | ✔ |
| Voltear, regar, modo automático, crear lote, aportes, reparar sensores | | ✔ | ✔ |
| Inyectar fallas de prueba, gestionar cuentas | | | ✔ |

Los permisos se comprueban dos veces: en la UI (botones deshabilitados) y en `SimuladorComposta`
(métodos públicos y atajos), así una tecla o una llamada desde otro script no los salta.

## Datos en disco (`Application.persistentDataPath/datos`)

```
usuarios.json(.bak)                 cuentas: hash PBKDF2-SHA256 (100k iteraciones) + sal; nunca la contraseña
lotes/{id}.json(.bak)               un archivo por lote: parámetros, semilla, bitácora y aportes
exportaciones/{id}_{fecha}/*.csv    resumen, bitácora, aportes, historial
```

**Cómo se reanuda un lote:** el modelo es determinista (semilla + mismas acciones a la misma hora
simulada), así que se guarda la *bitácora de acciones humanas*, no el estado interno.
`ReproductorLote` reconstruye el sistema hora a hora. Las acciones del control automático no se
guardan: se vuelven a producir solas. Requisito: no cambiar `ModeloComposta`/`PanelSensores` de forma
que alteren los resultados, o los lotes viejos se reproducirán distinto. Si eso ocurre, sube
`Lote.VersionActual` y migra.

## Puesta en marcha

1. Abre el proyecto en Unity 6000.6. Se importan los scripts nuevos y los dos `.asmdef`.
2. **No hay que tocar la escena**: `ControlAcceso` se crea solo al arrancar (`RuntimeInitializeOnLoadMethod`).
   Para desactivar el login, define `COMPOSTA_SIN_LOGIN` (Project Settings › Player › Scripting Define Symbols).
3. Pulsa Play y crea la cuenta de administración. Desde «Usuarios» crea cuentas de Operación/Consulta
   (cada persona cambia su contraseña temporal en el primer ingreso).
4. Tests: Window › General › Test Runner › EditMode › Run All. Si no aparece, instala
   `com.unity.test-framework` desde el Package Manager.
5. Sube a git los `.meta` que Unity genere para las carpetas y scripts nuevos.

## Recomendaciones pendientes (no aplicadas)

- Dejar de versionar carpetas generadas (ver comandos en la respuesta / `.gitignore`).
- Borrar `Assets/Scripts/Core/NewMonoBehaviourScript.cs` (plantilla vacía) y `Assets/_Recovery/0.unity`
  (escena de recuperación de un crash). Después puedes poner `"noEngineReferences": true` en
  `Composta.Core.asmdef` para impedir que Core dependa de Unity.
- Player Settings: `companyName`/`productName` siguen en «DefaultCompany/My project» (definen la carpeta de
  datos; cámbialos **antes** de distribuir), `runInBackground: 0` detiene la simulación al perder el foco
  y `resizableWindow: 0` impide redimensionar la ventana.

## Simulación y Control Manual: separación estricta

| | Simulación (azul) | Control Manual (ámbar) |
|---|---|---|
| Motor | `SimuladorComposta` avanza hora a hora | **detenido** (`DetenerMotor`): cero horas sintéticas |
| Datos | modelo + sensores simulados, bitácora de comandos reproducible | solo mediciones reales (`Lote.Informes`) |
| Vista | `PanelUI` | `TableroManual` (estado vacío "Esperando primer registro" → KPIs, gráficas de puntos, recomendaciones, historial) |
| Interpretación | el propio modelo | `AnalisisCampo`: fase por reglas (≥45 °C termofílica; tras fase caliente → enfriamiento; ≥21 días, <35 °C y pérdida de peso <1,5 %/semana → maduración), tendencias y recomendaciones |
| Calendario | tiempo simulado | `PlanInformes`: una medición cada 7 días reales (sin registros / al día / pendiente ≤24 h / atrasado) |

`GestorLotes.CrearLoteManual` crea el lote sin pasar por el simulador. Las vistas se eligen en `ControlAcceso.ActualizarVista`;
nunca se muestran ambas. El motor de calibración (`CalibracionCampo`, modelo de campo) sigue en Core con sus tests, pero la
app ya no lo usa en Control Manual.

## Pesos: recomendado vs. exagerado

`PerfilComposta.EvaluarPeso` devuelve `Optimo` (barra verde), `FueraDeOptimo` (consejo, no bloquea) o `Exagerado` (fuera de los
límites físicos del recipiente; bloquea el inicio de una simulación y marca la medición como crítica).

| Tipo | Recomendado | Límites físicos (fuera = PESO EXAGERADO) |
|---|---|---|
| Compostera doméstica | 15–60 kg | 3–120 kg |
| Compostera cerrada | 50–200 kg | 10–400 kg |
| Pila abierta | 200–1000 kg | 50–3000 kg |
| Hilera industrial | 2000–20000 kg | 500–100000 kg |
| Vermicomposta | 5–50 kg | 1–150 kg |

Los pesos por defecto no cambiaron, así que la simulación por defecto da los mismos resultados (test dorado).

## Sistema de diseño

`App/Tema.cs` concentra los tokens: 3 niveles de superficie, texto primario/secundario/tenue, acento azul (Simulación) y
ámbar (Control Manual), estados verde/ámbar/rojo (siempre con texto, no solo color), radios y tamaños de letra.
`UIFabrica` genera en memoria sprites redondeados 9-slice y círculos (uGUI no los trae en una build) y ofrece
`Tarjeta` (sombra suave), `Pildora` (badges) y estilos de botón con hover/presionado. Toda la UI hecha con `UIFabrica`
hereda el estilo; cambiar la identidad visual es editar `Tema.cs`.
