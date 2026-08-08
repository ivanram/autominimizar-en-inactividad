<p align="center">
  <img src="docs/icon-active.png" width="96" height="96" alt="Autominimizar en inactividad" />
</p>

<h1 align="center">Autominimizar en inactividad</h1>

<p align="center">
  Minimiza WhatsApp Desktop (u otras apps) automáticamente cuando llevas<br />
  un rato sin tocar el ratón o el teclado.
</p>

### Qué hace

Vive en la bandeja del sistema. Tras cierto tiempo de inactividad, minimiza WhatsApp (o cualquier otra app que elijas) sin que tengas que hacer nada. En cuanto vuelves a moverte, se rearma solo para la próxima vez.

- **Icono activo**: WhatsApp con gafas de sol.
- **Icono pausado**: el mismo icono, con un badge de pausa — la inactividad deja de minimizar nada.
- **Un clic** en el icono de la bandeja alterna entre Activo y Pausado.
- **Doble clic** abre los ajustes.

<p align="center">
  <img src="docs/icon-active.png" width="64" height="64" alt="Estado activo" />
  &nbsp;&nbsp;&nbsp;
  <img src="docs/icon-paused.png" width="64" height="64" alt="Estado pausado" />
</p>

### Ajustes

Doble clic en el icono de la bandeja (o clic derecho → Ajustes):

- **Tiempo de inactividad**: en segundos, desde 5 en adelante (el slider llega a 300 por comodidad, pero puedes escribir el número que quieras).
- **Aplicaciones a minimizar**: lista de apps abiertas, con botón para actualizarla. Marca las que quieras auto-minimizar — WhatsApp viene marcada por defecto.
- **Minimizar todo**: opción especial dentro de la misma lista — en vez de apps concretas, muestra el escritorio (como el rincón de "Mostrar escritorio" de la barra de tareas). Al marcarla, el resto de la lista se desactiva visualmente pero no pierde tu selección: si la desmarcas, tus apps siguen elegidas.
- **Ajustes adicionales**: iniciar automáticamente con Windows (activado por defecto), parar también la música/vídeo que esté sonando, y abrir también el salvapantallas de Windows tras minimizar.

### Instalación

1. Ve a la sección [Releases](../../releases) y descarga el `.exe` de la última versión.
2. Muévelo a una carpeta permanente antes de ejecutarlo por primera vez (no lo dejes en Descargas). El arranque automático con Windows registra la ruta desde la que lo ejecutas — si luego borras o mueves ese archivo, dejará de arrancar solo.
3. Ejecútalo — no hace falta instalar nada más. Hay dos variantes: una autocontenida (no necesita .NET instalado) y otra más ligera que requiere el .NET Desktop Runtime.

### Notas técnicas

- El icono de WhatsApp de la lista de aplicaciones se resuelve en caliente desde la propia instalación de WhatsApp del usuario (vía la API de iconos de apps empaquetadas de Windows), no se descarga de ningún sitio.
- La detección de inactividad usa el reloj de última entrada de Windows (`GetLastInputInfo`), sin hooks de teclado/ratón.
- El arranque con Windows usa la clave del registro `HKCU\...\CurrentVersion\Run` (no una Tarea Programada: crear tareas mediante `schtasks` requiere permisos que un equipo gestionado por una empresa puede denegar a usuarios no administradores).

### Si "Iniciar con Windows" no arranca la app

Si la casilla está activada pero la app no aparece al iniciar sesión:

1. Comprueba que sigue apareciendo en Administrador de tareas → pestaña Inicio, como Habilitada.
2. Si el equipo está gestionado por una empresa (verás cosas como Cloudflare WARP, Microsoft Defender for Endpoint u otros agentes corporativos instalados), es posible que una política central esté filtrando qué puede autoarrancar, incluso con la entrada del registro correctamente creada. En ese caso no es algo que la app pueda controlar — habría que consultarlo con el departamento de TI.
