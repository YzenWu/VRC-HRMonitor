# HeartRateMonitor

Herramienta de frecuencia cardíaca BLE en tiempo real para VRChat: envía tus pulsaciones y la telemetría del hardware al ChatBox por OSC — con ventanas flotantes, un frontend web remoto, CLI/TUI y un kit de herramientas de VRChat, todo en una sola aplicación de Windows.

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [繁體中文（香港）](README.zh-HK.md) | [粵語（香港）](README.yue-HK.md) | [日本語](README.ja.md) | **Español** | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Français](README.fr.md)

<img src="images/hero.png" alt="Vista general de la aplicación">

## Características

### Dispositivos de frecuencia cardíaca BLE

- Lee cualquier dispositivo estándar de frecuencia cardíaca por Bluetooth Low Energy (servicio Heart Rate `0x180D`) — bandas de pecho, pulseras, relojes deportivos.
- **Compatibilidad multidispositivo**: conecta varios sensores a la vez; el límite solo lo imponen la pila Bluetooth y el hardware.
- Puntuación y ordenación inteligente de dispositivos, alias, reconexión automática, avisos de señal débil (RSSI) y una ventana flotante por dispositivo.
- Detección automática: conecta en lote los dispositivos candidatos por peso, omitiendo los de audio/hogar inteligente y los que no exponen la característica de frecuencia cardíaca.

<img src="images/hrcurve.png" alt="Curva de pulsaciones">

### Envío al ChatBox de VRChat por OSC con vista previa en vivo

- Envía «frecuencia cardíaca + CPU / GPU / RAM y más» al ChatBox de VRChat (`/chatbox/input`) por OSC/UDP mediante una plantilla libre con `{variables}`.
- Vista previa en vivo de la plantilla que se actualiza cada segundo mientras editas, con contador de caracteres y un aviso no bloqueante al acercarse al límite de 144 caracteres del ChatBox.
- Envío OSC personalizado (cualquier dirección/texto), push saliente por Webhook, receptor OSC (puerto 9001) para capturar el tráfico de parámetros del avatar de VRChat y la opción «empezar a enviar al arrancar».

<img src="images/pusher.png" alt="Vista previa de plantilla">

### Ventanas flotantes

- Widgets de escritorio siempre visibles que muestran las PPM actuales (o una imagen); una ventana principal más una por dispositivo.
- Bloqueables con click-through, redimensionado sensible a DPI y geometría independiente persistente por ventana.
- Fuente de datos (media o un dispositivo concreto) e intervalo de refresco configurables por ventana.

<img src="images/overlay.png" alt="Ventana flotante" width="500">

### Variables de telemetría del hardware

- Recopila información del equipo Windows vía registro, WMI, PowerShell y `systeminfo`; las métricas en tiempo real (carga de CPU/RAM/GPU/VRAM, temperaturas, disco, memoria confirmada) llegan por PDH, la misma fuente que el Administrador de tareas.
- Todo se convierte en variable de plantilla: `{CPU_USAGE}`, `{RAM_PERCENT}`, `{TIME_ISO}`, hora sincronizada por NTP… más variables personalizadas (aritmética, concatenación, regex, salida de comandos) y renombrado/sobrescritura/unidad por variable.
- Variables dinámicas de procesos como `CPU_USAGE_VRCHAT`, `MEM_USAGE_<nombre|PID>` y `USAGE_FILE_<ruta>`.

### Estado de salud

- Deduce un estado (Dormido / En reposo / Activo / Excitado) a partir de la calibración de la frecuencia en reposo y de factores de umbral, reaccionando también a los parámetros de pose OSC (AFK / Sentado / Velocidad).
- Se expone como la variable `{HEALTH_STATUS}` y puede usarse directamente en las plantillas de envío.

### Registro y exportación

- Registra frecuencia cardíaca, tráfico OSC, estado de salud e instantáneas de hardware en una base SQLite local, con backends opcionales JSONL/CSV diarios.
- Cinco categorías de registro (cambios de avatar, sesiones de VRChat, conexiones de dispositivos, detalles de pulsaciones, instantáneas de hardware) con retención independiente por categoría.
- Página de estadísticas (mín/media/mediana/máx/desviación típica, tendencia, histograma, por dispositivo, Top-N de direcciones OSC) con rangos seleccionables; exporta a TXT/JSON/YAML/CSV.

### Segundo frontend web remoto (niveles LAN/WAN + HTTPS)

- La misma interfaz servida en un único puerto (9460 por defecto) para móviles y tablets de tu red.
- Acceso por niveles de origen: las conexiones loopback son el administrador local (sin inicio de sesión); los orígenes LAN requieren el interruptor Remote y una cuenta local; los orígenes públicos/WAN requieren además el interruptor WAN — que exige una contraseña fuerte de administrador y un diálogo explícito de confirmación de riesgo.
- Roles (admin/usuario) con listas blancas por sección, contraseñas en PBKDF2, sesiones ligadas al UA con caducidad por inactividad, registro de auditoría y HTTPS opcional mediante huella de certificado.

### CLI / TUI

- `hrmcli.exe` (equivalente a `HeartRateMonitor.exe --cli`): comandos de un solo uso para scripts, REPL de texto plano (`--shell`) y, por defecto, un TUI de menús al estilo TestDisk.
- Unos 40 comandos que cubren dispositivos, OSC, plantillas de envío, variables de hardware, salud, registro/exportación, web/remoto, ajustes de interfaz, ventanas flotantes y registros — el mismo motor de comandos que la pestaña de consola de la aplicación.

### Kit de herramientas (Toolkit)

Un dock en la parte inferior izquierda de la barra lateral abre el kit de herramientas de VRChat:

- **Editor de config** — edición en tabla de los campos habituales del `config.json` de VRChat con validación estricta de tipos JSON.
- **Visor de registros** — listado, lectura y búsqueda de los registros de VRChat.
- **Limpiador de caché** — análisis del uso de caché y limpieza con vista previa dry-run y frase de confirmación.
- **Índice de fotos** — indexado en paralelo de la biblioteca de fotos y búsqueda por palabras clave (metadatos XMP de las capturas de VRChat).
- **Estadísticas de juego** — estadísticas agregadas de tiempo de juego / pulsaciones / hardware con gráficos.
- **Análisis de procesos** — instantáneas de CPU/memoria del proceso de VRChat.

<img src="images/toolkit.png" alt="Toolkit" width="400">

### Modo seguro

- `--safemode` (o la entrada en Ajustes / el gesto de triple R) pausa toda la automatización — autoconexión, autodetección, reconexión automática, envío OSC, recopilación de hardware — para facilitar el diagnóstico, con un banner persistente y reinicio normal en un clic.

### Interfaz: diez idiomas y temas

- Interfaz en diez idiomas: 繁體中文 / 简体中文 / 繁體中文（香港） / 粵語（香港） / English / 日本語 / Español / 한국어 / Deutsch / Français; el CLI y los registros siguen el mismo idioma.
- Modo claro/oscuro × paletas de color (predeterminada / bosque / atardecer / océano / violeta / **modo personalizado de color sólido** con tu propio color de acento, fondo y paneles), deslizadores de radio de esquinas y densidad, seguimiento del tema del sistema e interruptor global de animaciones.
- Las preferencias de diseño (orden de tarjetas, anchos de columna, ajustes de curvas…) se guardan localmente y se replican en el backend, sobreviviendo a las reinstalaciones.

## Requisitos del sistema

- Windows 10 u 11, 64 bits (x64).
- Microsoft Edge WebView2 Runtime (preinstalado en la mayoría de sistemas; en caso contrario instala el Evergreen Runtime de Microsoft).
- Un adaptador Bluetooth con soporte BLE (integrado o USB).
- Opcional: la compilación dependiente del framework necesita el runtime de .NET 10 — la compilación standalone es autocontenida.

## Uso desde un ZIP compilado

1. Descarga el último `HeartRateMonitor-*-x64.zip` desde [Releases](https://github.com/yzenwu/VRC-HRMonitor/releases) y extráelo donde quieras.
2. Ejecuta `HeartRateMonitor.exe` (o `hrm-webui.exe`): el motor se instala en la bandeja del sistema y la ventana WebView2 se abre con una pantalla de arranque.
3. En el primer arranque, la configuración se escribe en el directorio de datos `%AppData%\HeartRateMonitor` (los registros, exportaciones y la base de datos también viven ahí; la ubicación se puede redirigir con un archivo `data_location.txt` junto al ejecutable).
4. Inicia un escaneo, conecta tu sensor BLE, activa el envío OSC y entra en VRChat — el ChatBox empieza a actualizarse.
5. `hrmcli.exe` es la contrapartida de terminal; `hrmdump.exe` se ejecuta automáticamente como vigilante de fallos.
6. Acceso remoto desde el móvil: activa el interruptor Remote (pestaña Web), abre `http://<IP-DEL-PC>:9460/webui/` desde la misma red e inicia sesión con una cuenta local.

## Compilación desde el código fuente

> Este repositorio es una instantánea del código fuente del proyecto.

Requisitos previos:

- **gcc (MinGW-w64)** — compila el motor OSC en C (`Engine/`).
- **.NET 10 SDK** — publica los cuatro ejecutables C# (`HeartRateMonitor.exe`, `hrm-webui.exe`, `hrmcli.exe`, `hrmdump.exe`).
- **Node.js + npm** — compila el frontend Vue 3 (`WebUI/`).

Compila desde la raíz del repositorio (PowerShell):

```powershell
./build.ps1 --releases     # versión dependiente del framework + ZIP
./build.ps1 --debug        # compilación de depuración con consola y registros detallados
```

El script compila el motor en C, luego el frontend web (vite) y después los cuatro proyectos .NET, escribiendo todo en `Built/<rama>-<marca-de-tiempo>/`; las versiones release producen además un `HeartRateMonitor-*-x64.zip` con su sidecar SHA-256. `Release.json` es la única fuente de los metadatos de publicación (versiones, iconos, repositorio, fecha de compilación, licencia) y se incrusta en los ejecutables al compilar. No ejecutes dos compilaciones en paralelo (comparten los directorios intermedios `obj/`).

## Licencia

[MIT](LICENSE) — © Yzen Wu.

---

## AIGC Context
  
**La mayor parte del contenido del proyecto ha sido generado por ChatGPT y Claude Opus. Si tienes alguna pregunta o sugerencia, abre un issue o envía un PR en el repositorio.**
