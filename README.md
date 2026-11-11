# Personal Finances

Aplicación de escritorio Windows x64 para consumos de tarjetas: C# / .NET 10, WPF con MVVM, MySQL, MySqlConnector y Dapper. La aplicación publicada incluye su runtime y abre sin consola.

## Instalación en una nueva computadora

Si ya tenés Git, ejecutá:

```powershell
git clone https://github.com/feblesariel/personal-finances.git
cd personal-finances
.\instalar.bat
```

También podés descargar el repositorio como ZIP, extraerlo y hacer doble clic en **instalar.bat**. No ejecutes el BAT dentro del ZIP.

El instalador encuentra el proyecto aunque se ejecute desde otra carpeta. Verifica Windows x64, busca el SDK .NET 10, restaura NuGet, compila Release y publica un ejecutable Windows x64 self-contained. Si falta el SDK, ofrece descargarlo e instalarlo para el usuario actual mediante el script oficial de Microsoft, sin cambiar permanentemente la política de PowerShell ni pedir administrador. Si la descarga falla, muestra el enlace oficial. Necesita Internet para descargar el SDK y los paquetes NuGet.

La aplicación se instala en `%LOCALAPPDATA%\Programs\PersonalFinances`. Se crea **un único acceso directo Personal Finances** en el escritorio real del usuario, con el ícono incluido. No se necesita Visual Studio, Node.js ni un runtime .NET adicional para ejecutarla. Después de instalar podés mover o eliminar la carpeta del repositorio.

Recomendado: **Windows 11 x64 con soporte vigente**. Se verifica un mínimo técnico de Windows 10 1809 sobre Intel/AMD x64; la compatibilidad y el soporte dependen de la edición y ciclo de vida de Windows. Consultá la [matriz oficial de .NET 10](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md). El instalador no instala el servidor MySQL: debe existir un MySQL 8.0 o posterior accesible desde el equipo. Descargar MySQL puede requerir permisos de administrador según el método elegido.

Referencias: [SDK .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0), [instalación oficial de .NET en Windows](https://learn.microsoft.com/dotnet/core/install/windows), [MySQL Community Server](https://dev.mysql.com/downloads/mysql/).

## Primer inicio: MySQL y resumen

1. Abrí **Personal Finances** desde el escritorio. Si no hay conexión guardada, se abre **Conexión a base de datos**.
2. Ingresá host, puerto, usuario, contraseña y nombre de base. Se sugieren `localhost`, `3306` y `personal_finances`; usuario y contraseña no tienen valores predefinidos.
3. Presioná **Probar conexión**. Si la base no existe, podés marcar **Crear la base si no existe**. Si tu usuario no tiene permiso CREATE, pedí al administrador una base vacía o conectá una base existente de Personal Finances.
4. Presioná **Guardar**. Se ejecutan migraciones versionadas e idempotentes; una base nueva recibe VISA, MASTERCARD y las categorías iniciales, sin consumos ficticios. Una base existente conserva sus datos. Se rechaza una base con tablas ajenas a la aplicación.
5. En **Configuración → Exportación de resumen → Configurar carpeta**, elegí una carpeta existente, probá escritura y guardá. Puede ser cualquier carpeta local o sincronizada con Google Drive; no se presupone una unidad ni una letra específica.
6. Configurá las fechas reales del primer período de cada tarjeta en **Tarjetas y períodos**.

Usá un usuario MySQL propio, con permisos solo sobre la base de Personal Finances: SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX y REFERENCES para tablas y migraciones. Crear una base nueva requiere además permiso de creación sobre esa base. La aplicación no necesita permisos globales de administrador ni permisos para borrar bases. No distribuye credenciales administrativas. Un administrador de MySQL puede preparar estos permisos mediante sus herramientas habituales.

En **Configuración** podés probar o cambiar la conexión, o modificar el destino del resumen. Guardar una conexión distinta no borra ni sobrescribe la base anterior. Si MySQL está detenido, la aplicación muestra un mensaje y permite reintentar o editar la conexión. Si se cancela el primer inicio, permanece abierta y se puede configurar después.

**Cada instalación conectada a un MySQL local diferente tiene sus propios datos. Instalar en otra PC no sincroniza los consumos.** Compartir el TXT mediante Drive es una exportación unidireccional de consulta; editar el TXT no modifica MySQL.

## Actualizar o reinstalar

```powershell
git pull
.\instalar.bat
```

Si la aplicación está abierta, el instalador pide cerrarla. Primero compila y publica en un área temporal; si esa etapa falla, conserva la instalación anterior. El reemplazo se verifica mediante SHA-256 y se restaura la instalación previa si falla el intercambio o el acceso directo. No toca MySQL ni la configuración del usuario. Se conserva la misma instalación y el mismo acceso directo.

La compilación Release también queda en `artifacts\Release`. Para automatización se puede ejecutar `instalar.bat -Unattended -NoLaunch`; si falta el SDK, esa modalidad informa el requisito y sale con error. `-InstallSdk` permite autorizar explícitamente su instalación automática. En uso normal, el BAT mantiene visible el resultado antes de cerrar.

## Configuración local y seguridad

Todo lo específico del equipo está en `%LOCALAPPDATA%\PersonalFinances`, fuera del código fuente:

- `connection.dpapi`: host, usuario, contraseña y base cifrados con Windows DPAPI para el usuario actual. No puede trasladarse como credencial a otro usuario/equipo.
- `preferences.json`: carpeta de exportación y revisión exportada, sin contraseñas.
- `Tables`: preferencias independientes de las tablas.
- `errors.log`: tipos/códigos de error; no registra contraseñas ni parámetros SQL.

La actualización conserva las credenciales existentes y adopta una sola vez la carpeta que versiones anteriores guardaban en MySQL. Después, el destino es local por instalación. Nunca hay una contraseña en el repositorio ni dentro del ejecutable.

## Uso diario

**Ctrl + N** abre la carga rápida con foco en el importe. Acepta `10.699,00` o `10699,00`; elegí tarjeta y categoría y guardá con **Enter**. Podés seguir cargando sin cerrar el formulario. **Tab** recorre los campos y **Escape** cierra. ARS y USD tienen saldos independientes.

Los períodos incluyen inicio y cierre; no pueden superponerse para una tarjeta. El siguiente período propone fechas para confirmar. Al cambiarlas, se verifican y recalculan las asignaciones automáticas; los consumos fuera de fecha permanecen visibles como pendientes. Las imputaciones manuales se conservan con advertencia si quedan fuera de fecha. El dashboard muestra el período que contiene la fecha actual o el último/próximo configurado cuando corresponde.

Los reintegros se cargan en **Detalles adicionales**, descuentan del neto y pueden vincularse con un consumo de igual tarjeta y moneda. Historial permite filtrar, buscar, editar y eliminar con confirmación; las eliminaciones son lógicas y las ediciones tienen auditoría. Desactivar tarjetas o categorías conserva el historial.

En **Consumos**, **Restablecer tabla** recupera columnas y clasificación originales y elimina solo sus preferencias visuales. Es la única tabla personalizable. Las tablas sencillas de consulta conservan un diseño fijo. **Limpiar filtros** conserva la disposición de Consumos; sus preferencias persisten entre aperturas. Estadísticas usa filtros independientes de tarjeta, período y moneda (ARS/USD), sin búsqueda ni categoría.

Las utilidades de importación TXT se conservan internamente para mantenimiento y pruebas, sin una sección de importación en la interfaz. Los movimientos importados anteriormente permanecen intactos.

## Exportación del resumen

`RESUMEN_TARJETAS.txt` contiene fecha de actualización, tarjeta, período, fechas, saldos netos ARS/USD, cantidad de movimientos y total general. No incluye detalles, categorías ni historial. Los movimientos sin período siguen visibles en la aplicación y no se incluyen en un saldo de período.

Se actualiza al cambiar datos y se puede usar **Exportar ahora**. Usa UTF-8, importes argentinos y un temporal con reemplazo seguro. Si Drive o la carpeta no están disponibles, el consumo queda guardado en MySQL; la exportación se reintenta cada 30 segundos y al siguiente inicio. La recepción en otros dispositivos depende del cliente de sincronización de Drive.

## Desinstalación

Cerrá la aplicación, eliminá el acceso directo **Personal Finances** y la carpeta `%LOCALAPPDATA%\Programs\PersonalFinances`. Esto no borra MySQL ni tus preferencias. Si también querés quitar la configuración de este usuario, eliminá `%LOCALAPPDATA%\PersonalFinances` después de respaldar lo necesario. No borres la base MySQL salvo que quieras perder sus datos deliberadamente.

## Respaldo y transferencia de datos

El TXT es de consulta. Para guardar o trasladar toda la contabilidad, usá **Configuración → Respaldo y transferencia de datos**:

1. En el equipo de origen, elegí **Exportar todos los datos**, seleccioná dónde guardar el `.pfinance` y escribí dos veces una contraseña de al menos 12 caracteres. La aplicación confirma la ubicación después de cifrar y verificar el archivo.
2. Copiá ese único archivo por Drive, pendrive u otro medio. Conservá la contraseña por separado: no se guarda en el respaldo ni se puede recuperar si la olvidás.
3. En el otro equipo, instalá el programa, configurá su MySQL y su carpeta de resumen. Elegí **Importar respaldo**, seleccioná el archivo y escribí la contraseña.
4. Presioná **Validar respaldo**. Revisá fecha, versión, cantidades, rango de fechas y datos existentes en el destino. Se crea y verifica una copia previa antes de permitir el reemplazo.
5. Marcá la confirmación y presioná **Restaurar datos**. Se reemplaza toda la contabilidad del destino; no se mezclan ni duplican registros. El dashboard, historial y estadísticas se actualizan y se regenera el TXT en la carpeta de ese equipo. Si esa carpeta está desconectada, los datos restaurados quedan guardados y el resumen se reintenta normalmente.

El archivo incluye tarjetas y categorías activas e inactivas, todos los períodos, consumos ARS/USD, reintegros y vínculos, comercios, observaciones, tokens de importación, auditoría y movimientos eliminados lógicamente. Conserva identificadores, fechas y centavos exactos. Excluye credenciales, connection strings, rutas del equipo, preferencias y logs técnicos. Cada equipo mantiene su propia configuración; una copia es una fotografía de la contabilidad, no sincronización entre computadoras.

**Recuperación:** las copias automáticas quedan en `%LOCALAPPDATA%\PersonalFinances\Backups`, con nombre `Antes-de-restaurar-….pfinance`. Usan **la misma contraseña del archivo que estabas importando**, y se conserva su ruta en la vista previa y en el resultado. Para volver al estado anterior, importá esa copia desde la misma sección. También podés abrir la carpeta local desde Configuración. Las copias no se eliminan automáticamente; conservá las que necesites en un lugar seguro. Si cancelás la vista previa, no se reemplaza ningún dato y la copia previa igualmente permanece disponible.

La restauración usa una única transacción InnoDB con claves foráneas activas y verifica todos los registros antes de confirmar. Si cambia el destino después de la vista previa, exige volver a validar; si falla la inserción o la verificación, MySQL revierte todos los cambios. No se ejecutan reinicializaciones ni cambios destructivos de esquema.

### Formato y compatibilidad

Formato de respaldo **v5** (contenido; contenedor cifrado v1 compatible), independiente de la versión del programa: JSON comprimido con Brotli y cifrado autenticado **AES-256-GCM** (nonce de 12 bytes, tag de 16 bytes), clave derivada con **PBKDF2-HMAC-SHA256, 600.000 iteraciones y salt aleatorio de 32 bytes**. La cabecera también se autentica. Se usan las implementaciones criptográficas de .NET, sin algoritmos propios. Cada archivo se escribe mediante temporal cifrado, se descifra para verificarlo y se reemplaza de forma segura. Límite de esta versión: **64 MB cifrados / 128 MB descomprimidos**; archivos dañados o versiones incompatibles se rechazan antes de modificar la base.

La migración 3 agrega solamente un identificador de contabilidad (`ledger_id`). Ese identificador junto con los IDs originales y los tokens de movimientos se conserva al transferir, para permitir una futura fusión con resolución de conflictos. Esta versión implementa únicamente restauración completa. La compatibilidad futura se comprobará antes de restaurar; no se interpreta un formato desconocido por aproximación.

## Desarrollo y pruebas

Solución `PersonalFinances.slnx`: `src/PersonalFinances.Core` contiene validación, migraciones, repositorio, configuración y exportación; `src/PersonalFinances.App` contiene WPF/MVVM. Los importes usan DECIMAL(18,2) y las consultas están parametrizadas. Se mantiene un diseño local, sin servidor web.

```powershell
dotnet run --project tests/PersonalFinances.Tests -c Release
dotnet run --project tests/PersonalFinances.UiTests -c Release
```

La suite incluye una transferencia entre dos bases independientes, comparación exacta de entidades y totales, contraseñas incorrectas, corrupción, versiones incompatibles, fallo SQL con rollback, copia de recuperación, concurrencia y exportación sin Drive. Las pruebas WPF ejecutan los diálogos reales, validación, vista previa, confirmación y actualización de todas las pantallas.

Las pruebas de MySQL reciben credenciales de prueba por las variables de proceso `PF_ADMIN_USER` y `PF_ADMIN_PASSWORD`, usan bases temporales `personal_finances_tests_*` y las eliminan al terminar. Aíslan configuración y carpetas de exportación y no insertan datos en producción. Los resultados se guardan en `artifacts`, excluida de Git. Las pruebas del instalador usan destinos temporales propios. No versionar datos reales, archivos `.pfinance`, respaldos SQL, ejecutables, credenciales ni configuración local. Esta distribución compilada desde fuente no tiene firma Authenticode.

## Servicios: planilla y modal (1.7)

La planilla conserva su diseño compacto, pero es **sólo lectura**. Un clic selecciona la fila; **doble clic o Enter** abre el servicio del mes seleccionado. Escape cierra el modal. Las columnas siguen admitiendo cambios de ancho/orden, clasificación y Restablecer tabla, sin modificar datos.

  El modal muestra importe del período, **nuevo** monto abonado, fecha del pago y vencimiento opcional. El nuevo abono siempre empieza vacío. Si ya hay pagos, muestra la fecha del último pago registrado: cambiarla y guardar **sin un nuevo monto** corrige ese mismo pago, conservando su importe, notas e historial de auditoría. Los demás pagos se corrigen desde Historial. Al ingresar un nuevo monto se propone hoy, conservando una fecha elegida expresamente; al borrar ese monto se recupera la fecha del pago registrado. Guardar sin cambiar la fecha ni ingresar un abono sólo modifica importe/vencimiento; guardar con abono positivo agrega un registro independiente. No supera el saldo ni duplica por doble clic/Enter o reintento de una respuesta perdida. Las correcciones de fecha actualizan tabla, vigencia anual y TXT; los respaldos conservan la fecha corregida. Los errores mantienen el modal abierto. Corrección disponible desde **1.7.1**.

- **SIN CARGAR:** importe no informado, almacenado como NULL en MySQL.
- **SIN CARGO:** importe confirmado igual a cero.
- **PENDIENTE / PARCIAL / PAGADO:** calculados con los pagos reales.
- Vencido y mínimo cubierto son indicadores discretos; cubrir el mínimo no cancela el saldo.

**Nuevo período** conserva los servicios activos y comienza sin importes confirmados ni pagos copiados. Los meses anteriores y los servicios inactivos conservan su historial. Cada tarjeta ocupa una fila ARS con el importe final ingresado manualmente; los antiguos USD siguen archivados, sin conversiones ni mezcla con consumos.

### Servicios anuales

Una obligación anual completamente pagada cubre desde su fecha real de pago completo hasta antes del aniversario siguiente (`AddYears(1)`, incluyendo el ajuste de 29/02). Durante la vigencia se muestra PAGADO con la fecha original en los meses cubiertos, sin crear doce pagos ni repetir obligaciones. El mes actual usa la fecha de hoy; los demás meses se consultan a su cierre. Historial conserva todas las renovaciones. Registrar renovación permite cargar otro pago anual sin borrar el anterior; antes de pagar nunca se muestra una vigencia ficticia. Al vencer, la renovación queda Sin cargar o muestra la obligación pendiente que corresponda.

### Historial y eliminación

Historial permite consultar, corregir y anular pagos individuales. Datos del servicio permite cambiar su configuración. **Eliminar servicio** es definitivo y transaccional: borra definición, obligaciones, pagos y auditoría propia, sin tocar otros servicios ni consumos. Exige un respaldo completo cifrado generado con el sistema existente, verificado y todavía disponible, y confirmación explícita del nombre/historial. Si los datos o el archivo cambian después de la copia, se exige generar otra. Conservá la contraseña del respaldo para recuperar los datos mediante la importación completa habitual.

### Corrección del bloqueo y diagnóstico

La versión anterior guardaba durante `RowEditEnding`, cancelaba y volvía a confirmar la edición y refrescaba la CollectionView en ese ciclo. Refrescar una vista con edición abierta produce `InvalidOperationException`; se agregó una prueba que reproduce ese mecanismo. Los registros anteriores incluían esa excepción sin pila, por lo que no permiten atribuir retrospectivamente cada bloqueo a una única causa.

Se eliminó toda edición de celdas y sus eventos reentrantes. El guardado del modal usa comandos que rechazan ejecuciones repetidas, transacciones y tokens de pago únicos. La exportación del TXT, incluido el acceso síncrono al sistema de archivos/Drive, corre en un worker separado; no se espera para cerrar un pago guardado. Fallar Drive no revierte el pago. Se mantiene el reemplazo seguro y el reintento automático cada 30 segundos. Los logs locales incluyen operación, duración, hilo y métodos de origen de errores, sin importes, nombres de servicios, SQL parametrizado ni credenciales.

La suite WPF registra 60 pagos consecutivos en seis servicios, intenta doble Guardar/reaperturas y comprueba actividad del dispatcher con una escritura del TXT deliberadamente bloqueada. También prueba la tabla en lectura, el modal, las fechas, los estados, la alineación y los respaldos. Las bases `personal_finances_tests_*` usan un servidor independiente configurable con `PF_ADMIN_PORT`.

### Migración y respaldos

La migración no destructiva **7** permite `amount=NULL` en obligaciones: únicamente los importes ya marcados sin confirmar pasan de su antiguo cero interno a NULL. Se conservan IDs, importes confirmados, pagos, notas y relaciones. La cobertura anual se deriva de pagos existentes; no requiere duplicarlos.

El respaldo **v5/esquema 7** conserva NULL/cero, todos los meses, pagos, historial y datos para recalcular vigencias. Acepta v1–v4. La restauración conserva cifrado, copia previa, validación e integridad transaccional. No reemplaza credenciales MySQL ni ruta local de Drive. El bloque Servicios del TXT refleja los nuevos estados, fechas y vigencias; el bloque de consumos de tarjetas conserva sus cálculos.
