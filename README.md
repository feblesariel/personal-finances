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

Formato de respaldo **v4** (contenido; contenedor cifrado v1 compatible), independiente de la versión del programa: JSON comprimido con Brotli y cifrado autenticado **AES-256-GCM** (nonce de 12 bytes, tag de 16 bytes), clave derivada con **PBKDF2-HMAC-SHA256, 600.000 iteraciones y salt aleatorio de 32 bytes**. La cabecera también se autentica. Se usan las implementaciones criptográficas de .NET, sin algoritmos propios. Cada archivo se escribe mediante temporal cifrado, se descifra para verificarlo y se reemplaza de forma segura. Límite de esta versión: **64 MB cifrados / 128 MB descomprimidos**; archivos dañados o versiones incompatibles se rechazan antes de modificar la base.

La migración 3 agrega solamente un identificador de contabilidad (`ledger_id`). Ese identificador junto con los IDs originales y los tokens de movimientos se conserva al transferir, para permitir una futura fusión con resolución de conflictos. Esta versión implementa únicamente restauración completa. La compatibilidad futura se comprobará antes de restaurar; no se interpreta un formato desconocido por aproximación.

## Desarrollo y pruebas

Solución `PersonalFinances.slnx`: `src/PersonalFinances.Core` contiene validación, migraciones, repositorio, configuración y exportación; `src/PersonalFinances.App` contiene WPF/MVVM. Los importes usan DECIMAL(18,2) y las consultas están parametrizadas. Se mantiene un diseño local, sin servidor web.

```powershell
dotnet run --project tests/PersonalFinances.Tests -c Release
dotnet run --project tests/PersonalFinances.UiTests -c Release
```

La suite incluye una transferencia entre dos bases independientes, comparación exacta de entidades y totales, contraseñas incorrectas, corrupción, versiones incompatibles, fallo SQL con rollback, copia de recuperación, concurrencia y exportación sin Drive. Las pruebas WPF ejecutan los diálogos reales, validación, vista previa, confirmación y actualización de todas las pantallas.

Las pruebas de MySQL reciben credenciales de prueba por las variables de proceso `PF_ADMIN_USER` y `PF_ADMIN_PASSWORD`, usan bases temporales `personal_finances_tests_*` y las eliminan al terminar. Aíslan configuración y carpetas de exportación y no insertan datos en producción. Los resultados se guardan en `artifacts`, excluida de Git. Las pruebas del instalador usan destinos temporales propios. No versionar datos reales, archivos `.pfinance`, respaldos SQL, ejecutables, credenciales ni configuración local. Esta distribución compilada desde fuente no tiene firma Authenticode.

## Servicios: planilla mensual (1.6)

Servicios es independiente de los consumos. Cada tarjeta ocupa una sola fila en **ARS**, con el importe final que ingreses manualmente. No convierte USD ni descuenta pagos de los consumos del dashboard.

- Seleccioná el mes arriba de la tabla. **Nuevo período** propone el mes siguiente y crea toda la lista de servicios activos, sin copiar pagos, fechas ni importes confirmados. Los meses anteriores conservan su historial. No se permiten meses duplicados.
- **Doble clic** en una celda para editar importe, abonado acumulado, fecha de pago, vencimiento u observaciones. **Tab** pasa de campo, **Enter** confirma la fila y **Escape** cancela. Se guarda la fila en una transacción; los errores conservan la edición y se muestran al pie.
- Un aumento del abonado acumulado registra un nuevo pago por la diferencia. No borra los pagos previos. Se propone la fecha actual y podés reemplazarla por la fecha real. Si hay varios pagos, la fecha visible es la última; las correcciones individuales se hacen desde **⋯ → Pagos de esta fila**.
- Una celda de importe vacía es **SIN CARGAR**. Al cargar un importe positivo pasa a **PENDIENTE**, **PARCIAL** o **PAGADO** según el saldo. Escribir explícitamente **0** confirma **BONIFICADO**. Los importes habituales se ofrecen como referencia, sin confirmarlos automáticamente.
- El vencimiento es opcional. Si corresponde, se muestra un indicador discreto **Vencido**. El pago mínimo cubierto se informa sin confundirlo con el pago completo.
- Los servicios anuales se crean solamente en su mes de renovación. En los demás meses se muestran como **No corresponde** y no suman pendientes. Los servicios inactivos conservan sus meses anteriores.
- **⋯** o clic derecho permite consultar/corregir/anular pagos, agregar otro pago, editar los datos generales/desactivar el servicio y consultar mínimo o cuotas. El historial se muestra sólo a pedido.
- Podés cambiar anchos, orden de columnas y clasificación. **Restablecer tabla** recupera la presentación original y borra las preferencias locales de esa tabla; no modifica datos. Consumos y Servicios tienen preferencias independientes.

La tabla crece con la ventana y tiene un único desplazamiento de filas, con encabezados fijos. La pantalla muestra sólo el selector mensual, Nuevo período, Agregar servicio, un resumen compacto y la planilla.

### Migración y datos anteriores

La migración incremental **6** agrega `amount_confirmed` y la tabla `service_months`; conserva todas las entidades, IDs, importes, observaciones y pagos existentes. Los antiguos ceros que no distinguían entre falta de carga y bonificación quedan **Sin cargar**: ingresá 0 si efectivamente no corresponde pagar. Los importes positivos y pagos no se modifican.

Las obligaciones USD anteriores de tarjetas permanecen en la base y en los respaldos. Si tienen cargos, pagos, notas o mínimos, la fila ARS muestra **Revisar USD anteriores**; se consultan en Pagos de esta fila, sin convertir ni sumar monedas. Los registros USD vacíos no generan otra fila.

El TXT conserva intacto el bloque de tarjetas y agrega un bloque compacto de Servicios del mes vigente, distinguiendo **Exigible**, **Abonado** y saldo. Usa el mismo orden de clasificación de la planilla. No exporta el historial extenso de pagos. Mantiene reemplazo seguro y reintentos de Drive.

El respaldo de contenido **v4/esquema 6** conserva meses incluso vacíos, importes pendientes de confirmar, bonificaciones, todos los pagos, mínimos, cuotas, notas y archivos USD anteriores. Mantiene el cifrado y restauración transaccional existentes. Acepta v1, v2 y v3; los ceros antiguos sin confirmación se interpretan como Sin cargar. No transfiere credenciales ni rutas del equipo.

Las pruebas MySQL usan bases `personal_finances_tests_*` en un servidor independiente; `PF_ADMIN_PORT` permite seleccionar su puerto. Las pruebas WPF verifican edición real de celdas, fechas, pagos acumulados, cancelar filas, nuevos meses, columnas y crecimiento vertical.