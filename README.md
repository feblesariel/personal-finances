# Personal Finances

Aplicación de escritorio Windows x64 para consumos de tarjetas: C# / .NET 10, WPF con MVVM, MySQL, MySqlConnector y Dapper. La aplicación publicada incluye su runtime y abre sin consola.

## Instalación en una nueva computadora

Si ya tenés Git, copiá la URL real del repositorio en estos comandos:

```powershell
git clone URL_DEL_REPOSITORIO
cd personal-finances
.\instalar.bat
```

También podés descargar el repositorio como ZIP, extraerlo y hacer doble clic en **instalar.bat**. No ejecutes el BAT dentro del ZIP. Esta copia del proyecto no tiene un remoto Git configurado: `URL_DEL_REPOSITORIO` es un marcador que debés reemplazar, no una URL de publicación.

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

**Restablecer tabla** recupera columnas y clasificación originales y elimina solo sus preferencias visuales. **Limpiar filtros** conserva la disposición de la tabla. Las preferencias persisten entre aperturas.

La importación TXT ofrece una vista previa editable; duplicados y líneas ambiguas requieren revisión. Las categorías son sugerencias. `REFUND` en el importador genérico indica un reintegro acreditado: si solo es una devolución pendiente, revisá y corregí el tipo a consumo y conservá la anotación antes de importar.

## Exportación del resumen

`RESUMEN_TARJETAS.txt` contiene exclusivamente fecha de actualización, tarjeta, período, fechas, saldos netos ARS/USD, cantidad de movimientos y total general. No incluye detalles, categorías ni historial. Los movimientos sin período siguen visibles en la aplicación y no se incluyen en un saldo de período.

Se actualiza al cambiar datos y se puede usar **Exportar ahora**. Usa UTF-8, importes argentinos y un temporal con reemplazo seguro. Si Drive o la carpeta no están disponibles, el consumo queda guardado en MySQL; la exportación se reintenta cada 30 segundos y al siguiente inicio. La recepción en otros dispositivos depende del cliente de sincronización de Drive.

## Desinstalación

Cerrá la aplicación, eliminá el acceso directo **Personal Finances** y la carpeta `%LOCALAPPDATA%\Programs\PersonalFinances`. Esto no borra MySQL ni tus preferencias. Si también querés quitar la configuración de este usuario, eliminá `%LOCALAPPDATA%\PersonalFinances` después de respaldar lo necesario. No borres la base MySQL salvo que quieras perder sus datos deliberadamente.

## Respaldo

El TXT es de consulta, no un respaldo completo. Con MySQL Workbench, usá **Server → Data Export**, seleccioná solo tu base de Personal Finances y exportá estructura y datos a un SQL local o disco externo. Usá respaldo transaccional InnoDB. Hacé copias periódicas y antes de actualizar. Restaurá mediante **Data Import / Restore** y configurá la conexión en el programa. No copies las credenciales DPAPI a otro equipo: ingresalas de nuevo allí.

## Desarrollo y pruebas

Solución `PersonalFinances.slnx`: `src/PersonalFinances.Core` contiene validación, migraciones, repositorio, configuración y exportación; `src/PersonalFinances.App` contiene WPF/MVVM. Los importes usan DECIMAL(18,2) y las consultas están parametrizadas. Se mantiene un diseño local, sin servidor web.

```powershell
dotnet run --project tests/PersonalFinances.Tests -c Release
dotnet run --project tests/PersonalFinances.UiTests -c Release
```

Las pruebas de MySQL reciben credenciales de prueba por las variables de proceso `PF_ADMIN_USER` y `PF_ADMIN_PASSWORD`, usan bases temporales `personal_finances_tests_*` y las eliminan al terminar. Aíslan configuración y carpetas de exportación y no insertan datos en producción. Los resultados se guardan en `artifacts`, excluida de Git. Las pruebas del instalador usan destinos temporales propios. No versionar datos reales, respaldos SQL, ejecutables, credenciales ni configuración local. Esta distribución compilada desde fuente no tiene firma Authenticode.