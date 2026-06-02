# Guía de Configuración e Inicio Rápido

Este documento detalla los parámetros de configuración de **RikiLoquitoContador** y cómo iniciar la aplicación en entornos locales.

## Configuración del Sistema (`appsettings.json`)

El archivo de configuración principal se encuentra centralizado en `Config/appsettings.json` y consta de las siguientes propiedades:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=facturas.db"
  },
  "SecuritySettings": {
    "PasswordHash": "$2a$11$9/X4yDqC3G3bYfCdfp/juef6u8bQ/bK1dM1oF3L0H5U1tT8rP/Cxe"
  },
  "ScanningSettings": {
    "WatchFolderPath": "C:\\FacturasContador",
    "ScanIntervalSeconds": 10
  }
}
```

### Detalle de Campos
1. **`ConnectionStrings:DefaultConnection`**: Ruta física de la base de datos local SQLite. Por defecto se creará un archivo `facturas.db` en el directorio de ejecución de la aplicación.
2. **`SecuritySettings:PasswordHash`**: Hash encriptado de la contraseña de acceso mediante el algoritmo BCrypt. El valor por defecto de fábrica corresponde a la contraseña: **`contador123`**.
3. **`ScanningSettings:WatchFolderPath`**: Directorio local que la aplicación escaneará y monitoreará de manera activa para detectar nuevos archivos de facturas.
4. **`ScanningSettings:ScanIntervalSeconds`**: Frecuencia del temporizador de refresco secundario para el monitoreo de archivos.

---

## Cambio Seguro de Contraseña

Dado que las contraseñas se almacenan mediante hashes de BCrypt y no en texto plano, para configurar una contraseña personalizada en el sistema:

1. Modifica la contraseña en la base de datos o ejecuta el generador de pruebas.
2. Si deseas generar un nuevo hash manualmente, puedes utilizar cualquier herramienta compatible con BCrypt (Bcrypt.Net) y guardar el hash resultante en el campo `PasswordHash` del archivo `appsettings.json`.

---

## Cómo Ejecutar la Aplicación

Asegúrate de tener instalado el SDK de .NET 10.

### 1. Iniciar en Modo Web
Para levantar el servidor web local y acceder mediante el navegador:

```bash
# Navegar a la carpeta del host Web
cd src/RikiLoquitoContador.Web

# Ejecutar el proyecto
dotnet run
```
La consola indicará la dirección local de escucha (ej. `https://localhost:7123`). Ábrela en tu navegador.

### 2. Iniciar en Modo Escritorio (Windows)
Para lanzar la aplicación de escritorio nativa usando MAUI:

```bash
# Navegar a la carpeta raíz de la solución
cd ../..

# Compilar y ejecutar la app de escritorio en Windows
dotnet build src/RikiLoquitoContador.Maui/RikiLoquitoContador.Maui.csproj -t:Run -f net10.0-windows10.0.19041.0
```

---

## Funcionamiento de la Sincronización a Excel

Al pulsar el botón **"Sincronizar Excel (.xlsx)"** en el panel de control:
1. El sistema buscará el archivo `FacturasSincronizadas.xlsx` dentro de la carpeta local configurada en `WatchFolderPath`.
2. Si el archivo no existe, lo creará con el formato de cabecera predeterminado.
3. Si el archivo ya existe, leerá la columna **ID** para filtrar y omitir los registros que ya fueron exportados anteriormente.
4. Escribirá de forma **incremental y al final del archivo** únicamente las nuevas facturas indexadas que no se encontraban en el documento de Excel.
