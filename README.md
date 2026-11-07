# I · El Mago — escribe en el PC desde el celular

<p align="center"><img src="docs/carta.svg" alt="I · El Mago" width="180"></p>

Una burbuja ⌨ flotante en el celular Android. Al tocarla se abre una ventanita con un campo de texto:
lo que escribes ahí (con tu teclado de siempre, dictado por voz incluido) aparece en la ventana
que esté al frente en el PC con Windows, por el Wi-Fi de la casa.

```
Celular (Android)                         PC (Windows)
 burbuja ⌨ ─► teclado ─► Wi-Fi ─► El Mago (ícono junto al reloj) ─► SendInput ─► ventana activa
              ◄── "Conectado · Escribiendo en: Bloc de notas" ──┘
```

## Partes

| Carpeta | Qué es | Cómo se usa |
|---|---|---|
| `windows/` | Programa de bandeja en C# 5 (WinForms). Se compila solo con el `csc.exe` que trae Windows: no hay que instalar nada. | `Instalar.bat` (arranque con Windows + firewall) o `Probar.bat` |
| `android/` | App en Kotlin, sin librerías. AGP 9.4.0, Gradle 9.6.0, compileSdk 37, minSdk 26. | Abrir la carpeta en Android Studio y ejecutar en el Motorola |

## Primer uso

1. **PC:** doble clic en `windows\Instalar.bat`. Windows pide permiso de administrador para el firewall:
   pulsa **Sí**. Si tu Wi-Fi está como red **pública**, te pregunta si la marcas como privada (en casa: S).
2. Se abre la ventana de El Mago con el **PIN** de 6 cifras y la IP del PC.
3. **Celular:** abre El Mago → **Buscar PC** → escribe el PIN. (Si no lo encuentra: **Escribir IP**.)
4. Activa **Burbuja flotante** (pide «Mostrar sobre otras apps» y notificaciones).
5. Toca la burbuja, escribe, y mira el PC.

## Cómo funciona

- **Descubrir:** el celular pregunta `MAGO?v1` por difusión UDP (puerto 47801); el PC responde con su nombre.
- **Conexión:** TCP 47800, una línea JSON por mensaje. El PC manda un reto; el celular responde
  HMAC-SHA256(llave, reto). Al emparejar se usa el PIN y el PC entrega una llave de 256 bits.
  - PC: llaves cifradas con DPAPI en `%APPDATA%\ElMago\config.json`. 5 PIN fallidos = 1 minuto bloqueado.
  - Celular: llave cifrada con el Android Keystore.
- **Mensajes:** `texto`, `tecla` (enter, backspace, tab, esc, flechas, inicio, fin, supr, f1–f12),
  `combo` (ctrl/alt/shift/win + tecla), `edicion` y `ping`/`pong` (el PC contesta con la ventana activa).
- **En vivo:** cada cambio del campo se convierte en lo mínimo para el PC. Si el autocorrector cambia una
  palabra de atrás, el PC hace ← n, ⌫ m, escribe y → n (función `Protocolo.cambios`). Cuenta emojis como una tecla.
- **Escribir:** `SendInput` con `KEYEVENTF_UNICODE` (tildes, ñ y emojis sin depender del idioma del teclado).
- **Wi-Fi:** el celular usa siempre la red Wi-Fi (aunque tenga datos móviles) y avisa si no hay Wi-Fi.
  Con la pantalla apagada y el teclado cerrado no mantiene la conexión (ahorra batería).

## Límites conocidos

- Windows no deja escribir en ventanas abiertas **como administrador** (si El Mago no lo es) ni en la pantalla de bloqueo.
- El texto viaja **sin cifrar** por el Wi-Fi (sí autenticado). Úsalo en redes de confianza; no escribas contraseñas en redes públicas.
- Si el router aísla los equipos (red de invitados), la búsqueda no encuentra el PC y tampoco conecta.
- En vivo supone que nadie mueve el cursor del PC mientras escribes. Si lo mueves, pulsa **Limpiar** (vacía el campo sin borrar nada en el PC).

## Pruebas

- Windows (mono): `cd windows/tests && mcs -langversion:5 -r:System.Web.Extensions.dll -r:System.Security.dll -out:p.exe ../src/Protocolo.cs ../src/Config.cs ../src/Servidor.cs ../src/Teclado.cs Pruebas.cs && mono p.exe` → 26 comprobaciones, con un servidor TCP real y teclado simulado.
- Android (JUnit): `ProtocoloTest` → 10 pruebas, incluidas 3000 ediciones al azar.
- Cruce: 2003 ediciones generadas por el código Kotlin y aplicadas por el traductor de C# dan el mismo texto.
