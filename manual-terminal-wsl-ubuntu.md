# Manual práctico: Terminal Ubuntu en WSL (desde VS Code)

Esta guía usa como referencia tu propio proyecto (`~/repos/legal-case-management`) para que cada comando lo veas aplicado a algo real, no abstracto.

---

## 0. Entender dónde estás

Tu prompt dice algo así:
```
mm@MIO1:~/repos/legal-case-management$
```
- `mm` → tu usuario en Ubuntu
- `MIO1` → nombre del equipo (host)
- `~/repos/legal-case-management` → carpeta actual (`~` = tu home, `/home/mm`)
- `$` → estás como usuario normal (si vieras `#`, serías root)

Esto es exactamente igual que un Ubuntu "de verdad" con ventanas: la terminal es la misma, solo que no tienes escritorio gráfico (salvo que instales uno, que casi nunca hace falta).

---

## 1. Moverte por carpetas (navegación)

| Comando | Qué hace | Ejemplo con tu proyecto |
|---|---|---|
| `pwd` | Muestra dónde estás | `pwd` → `/home/mm/repos/legal-case-management` |
| `ls` | Lista archivos | `ls` |
| `ls -la` | Lista con detalles y ocultos (como `.gitignore`) | `ls -la` |
| `cd carpeta` | Entra a una carpeta | `cd src/Domain/Entities` |
| `cd ..` | Sube un nivel | `cd ..` |
| `cd ~` o `cd` | Vuelve a tu home | `cd` |
| `cd -` | Vuelve a la carpeta anterior | `cd -` |

**Tip clave de VS Code**: si tienes el explorador de archivos abierto a la izquierda (como en tu captura) y haces clic derecho sobre una carpeta → "Open in Integrated Terminal", la terminal se abre ya posicionada ahí. Te ahorras el `cd`.

---

## 2. Ver y crear archivos/carpetas

| Comando | Qué hace | Ejemplo |
|---|---|---|
| `mkdir nombre` | Crea carpeta | `mkdir Tests` |
| `mkdir -p a/b/c` | Crea carpetas anidadas de una vez | `mkdir -p src/Domain/ValueObjects` |
| `touch archivo` | Crea archivo vacío | `touch Domain/Entities/Cliente.cs` |
| `cat archivo` | Muestra contenido completo | `cat docker-compose.yml` |
| `less archivo` | Muestra contenido paginado (más cómodo en archivos largos) | `less README.md` (sales con `q`) |
| `head -n 20 archivo` | Primeras 20 líneas | `head -n 20 Expediente.cs` |
| `tail -n 20 archivo` | Últimas 20 líneas | `tail -n 20 docker-compose.yml` |
| `tail -f archivo` | Sigue el archivo en vivo (útil para logs) | `tail -f logs/app.log` |

---

## 3. Copiar, mover, borrar

⚠️ En Linux **no hay papelera** por defecto en terminal: `rm` borra de verdad.

| Comando | Qué hace | Ejemplo |
|---|---|---|
| `cp origen destino` | Copia archivo | `cp appsettings.json appsettings.Development.json` |
| `cp -r origen destino` | Copia carpeta completa | `cp -r src/Domain src/Domain.bak` |
| `mv origen destino` | Mueve o renombra | `mv Expediente.cs Expediente.old.cs` |
| `rm archivo` | Borra archivo | `rm temp.txt` |
| `rm -r carpeta` | Borra carpeta y contenido | `rm -r bin obj` |
| `rm -rf carpeta` | Borra sin preguntar, forzado (**úsalo con cuidado**) | `rm -rf node_modules` |

**Práctica típica en .NET**: limpiar carpetas de compilación:
```bash
find . -type d -name "bin" -exec rm -rf {} +
find . -type d -name "obj" -exec rm -rf {} +
```

---

## 4. Buscar cosas

| Comando | Qué hace | Ejemplo |
|---|---|---|
| `find . -name "*.cs"` | Busca archivos por nombre/patrón | `find . -name "*Entity.cs"` |
| `grep "texto" archivo` | Busca texto dentro de un archivo | `grep "EstadoExpediente" Expediente.cs` |
| `grep -r "texto" .` | Busca texto en todos los archivos de la carpeta actual (recursivo) | `grep -r "TenantId" src/` |
| `grep -rn "texto" .` | Igual, pero mostrando número de línea | `grep -rn "public class" src/Domain` |
| `grep -ri "texto" .` | Igual, ignorando mayúsculas/minúsculas | `grep -ri "cliente" src/` |

`grep -rn` es tu mejor amigo para encontrar "¿dónde está definida esta clase?" sin abrir VS Code Search.

---

## 5. Permisos (chmod / chown / sudo)

| Comando | Qué hace | Ejemplo |
|---|---|---|
| `sudo comando` | Ejecuta como administrador (te pide tu contraseña de Ubuntu, no la de Windows) | `sudo apt update` |
| `chmod +x archivo` | Da permiso de ejecución | `chmod +x deploy.sh` |
| `chmod 644 archivo` | Permisos típicos de archivo normal (rw- r-- r--) | `chmod 644 appsettings.json` |
| `chown usuario archivo` | Cambia el dueño del archivo | `sudo chown mm:mm archivo` |

`sudo` es equivalente a "Ejecutar como administrador" en Windows, pero por comando, no por ventana.

---

## 6. Gestión de paquetes (apt) — el "Programas y características" de Ubuntu

| Comando | Qué hace |
|---|---|
| `sudo apt update` | Actualiza la lista de paquetes disponibles (hazlo antes de instalar algo) |
| `sudo apt upgrade` | Actualiza los paquetes instalados |
| `sudo apt install nombre` | Instala un paquete, ej: `sudo apt install htop` |
| `sudo apt remove nombre` | Desinstala un paquete |
| `apt list --installed` | Lista lo instalado |
| `which comando` | Te dice dónde está instalado un comando, ej: `which dotnet` |

---

## 7. Procesos (equivalente al Administrador de Tareas)

| Comando | Qué hace |
|---|---|
| `ps aux` | Lista todos los procesos corriendo |
| `top` | Monitor en vivo de CPU/RAM (como el Administrador de Tareas) |
| `htop` | Versión mejorada y visual de `top` (instálalo: `sudo apt install htop`) |
| `kill PID` | Mata un proceso por su número (PID) |
| `kill -9 PID` | Lo mata a la fuerza |
| `killall nombre` | Mata todos los procesos con ese nombre, ej: `killall dotnet` |

Ejemplo real: si dejaste corriendo `dotnet run` en otra terminal y ya no la ves:
```bash
ps aux | grep dotnet
kill -9 <PID>
```

---

## 8. Comprimir / descomprimir

| Comando | Qué hace |
|---|---|
| `tar -czvf salida.tar.gz carpeta/` | Comprime una carpeta |
| `tar -xzvf archivo.tar.gz` | Descomprime |
| `zip -r salida.zip carpeta/` | Comprime en .zip |
| `unzip archivo.zip` | Descomprime .zip |

---

## 9. Red

| Comando | Qué hace |
|---|---|
| `curl https://url` | Hace una petición HTTP y muestra la respuesta (útil para probar tu API) |
| `curl -X POST http://localhost:5000/api/expedientes -H "Content-Type: application/json" -d '{"titulo":"Test"}'` | Prueba tu endpoint POST directo desde terminal |
| `wget url` | Descarga un archivo |
| `ss -tulpn` | Muestra qué puertos están escuchando (útil si te dice "puerto ya en uso") |

---

## 10. Git (lo usarás constantemente)

| Comando | Qué hace |
|---|---|
| `git status` | Qué archivos cambiaste |
| `git add .` | Agrega todos los cambios |
| `git add archivo` | Agrega solo uno |
| `git commit -m "mensaje"` | Confirma los cambios |
| `git push` | Sube al remoto |
| `git pull` | Trae cambios del remoto |
| `git log --oneline -10` | Últimos 10 commits, resumidos |
| `git diff` | Qué cambió exactamente, línea por línea |
| `git checkout -b nombre-rama` | Crea y cambia a una nueva rama |
| `git stash` | Guarda cambios sin commitear, para cambiar de rama rápido |

---

## 11. .NET (específico de tu proyecto)

| Comando | Qué hace |
|---|---|
| `dotnet build` | Compila |
| `dotnet run` | Corre el proyecto (desde la carpeta con el `.csproj`, ej: `cd src/Api && dotnet run`) |
| `dotnet watch run` | Corre y recompila automáticamente al guardar cambios |
| `dotnet restore` | Restaura paquetes NuGet |
| `dotnet ef migrations add NombreMigracion` | Crea una migración (si usas Entity Framework) |
| `dotnet ef database update` | Aplica migraciones a la base de datos |
| `dotnet test` | Corre tests |

Ejemplo con tu estructura actual:
```bash
cd ~/repos/legal-case-management/src/Api
dotnet watch run
```

---

## 12. Docker / docker-compose (veo que tienes `docker-compose.yml`)

| Comando | Qué hace |
|---|---|
| `docker-compose up` | Levanta los servicios definidos en el yml |
| `docker-compose up -d` | Igual, pero en segundo plano (detached) |
| `docker-compose down` | Apaga y elimina los contenedores |
| `docker-compose logs -f` | Muestra logs en vivo |
| `docker-compose ps` | Lista contenedores del proyecto y su estado |
| `docker ps` | Lista **todos** los contenedores corriendo en el sistema |
| `docker exec -it nombre_contenedor bash` | Entra a la terminal dentro del contenedor |

Ejemplo típico de flujo de trabajo diario:
```bash
docker-compose up -d        # levantas la base de datos, etc.
cd src/Api
dotnet watch run             # corres tu API en local
```

---

## 13. Atajos de teclado imprescindibles

| Atajo | Qué hace |
|---|---|
| `Ctrl + C` | Cancela el comando/proceso actual (ej: parar `dotnet run`) |
| `Ctrl + L` | Limpia la pantalla (igual que `clear`) |
| `Ctrl + R` | Búsqueda inversa en tu historial (escribe parte de un comando anterior y aparece) |
| `Tab` | Autocompleta nombres de archivos/carpetas/comandos |
| `↑` / `↓` | Navega por comandos anteriores |
| `Ctrl + A` / `Ctrl + E` | Ir al inicio / final de la línea que estás escribiendo |

---

## 14. Cosas específicas de WSL que no existen en un Ubuntu normal

| Comando / concepto | Qué hace |
|---|---|
| `explorer.exe .` | Abre el explorador de archivos de **Windows** en la carpeta actual de Linux |
| `code .` | Abre VS Code en la carpeta actual (ya lo estás usando) |
| `/mnt/c/Users/TuUsuario/...` | Así se accede a tu disco C: de Windows desde Ubuntu |
| `wslpath -w /home/mm/repos/legal-case-management` | Convierte una ruta de Linux a formato Windows |
| `clip.exe` | Copia al portapapeles de Windows. Ej: `cat archivo.txt \| clip.exe` |
| `wsl --shutdown` (esto se ejecuta en **PowerShell de Windows**, no en Ubuntu) | Reinicia toda la instancia de WSL si algo se cuelga |

**Regla de oro de rendimiento**: trabaja siempre dentro del sistema de archivos de Linux (`/home/mm/...`), no en `/mnt/c/...`. Si tu proyecto vive en `/mnt/c/Users/...`, todo (git, dotnet, npm) va mucho más lento. Por lo que veo en tu ruta (`~/repos/legal-case-management`), ya lo estás haciendo bien.

---

## 15. Personalizar tu terminal (opcional pero útil)

Puedes crear atajos propios (alias) editando tu archivo de configuración:
```bash
nano ~/.bashrc
```
Agrega al final, por ejemplo:
```bash
alias ll="ls -la"
alias gs="git status"
alias dcu="docker-compose up -d"
alias dr="dotnet run"
```
Guarda con `Ctrl+O`, Enter, `Ctrl+X` para salir. Luego aplica los cambios:
```bash
source ~/.bashrc
```
A partir de ahí, escribir `gs` equivale a `git status`.

---

## 16. Editor de texto rápido en terminal: nano

Si necesitas editar un archivo sin abrir VS Code:
```bash
nano archivo.txt
```
- Escribe normal
- `Ctrl + O` → guardar
- `Ctrl + X` → salir
- `Ctrl + K` → cortar línea

(No necesitas `vim` a menos que quieras aprenderlo aparte; es más potente pero con curva de aprendizaje mayor.)

---

## Resumen: flujo de trabajo diario típico en tu proyecto

```bash
cd ~/repos/legal-case-management   # ubicarte
git pull                            # traer cambios
docker-compose up -d                # levantar base de datos/servicios
cd src/Api
dotnet watch run                    # correr la API con recarga automática
```
Y en otra pestaña de terminal, para revisar cosas:
```bash
git status
grep -rn "TenantId" src/Domain
```
