# Scanner/Scale OPOS Wrapper

This wrapper publishes scanner and scale activity to a Windows named pipe so another process can consume barcode scans and live weight values.

## Workflow

The wrapper has two runtime modes controlled by `Settings.ini`:

- `MODE=OPOS` connects to the configured OPOS scanner and optional OPOS scale, waits for real device events, and forwards them to the named pipe.
- `MODE=EMULATOR` skips OPOS initialization, starts the same named pipe server, and lets you emit test scan and weight events from the console.

The named pipe name comes from `PIPE_NAME`. The default pipe is `\\\\.\\pipe\\ScannerScaleOPOSPipe`.

## Switching Modes

Edit `Settings.ini` and change:

```ini
[GENERAL]
MODE=OPOS
```

To test without hardware, change it to:

```ini
[GENERAL]
MODE=EMULATOR
```

Mode-specific behavior:

- `OPOS` uses `SCANNER_NAME`, `SCALE_NAME`, and `SCALE_ENABLED`.
- `EMULATOR` ignores OPOS device setup and always shows the console prompt.
- `DEBUG=1` keeps the console visible in `OPOS`; in `EMULATOR` the console is always shown.

## Messages Emitted

The wrapper writes line-delimited UTF-8 messages to the named pipe.

Scanner messages:

```text
SCAN:<barcode>
```

Examples:

```text
SCAN:012345678905
SCAN:ABC123
```

Weight messages:

```text
WEIGHT:<formatted weight>
```

Examples:

```text
WEIGHT:1.250 lb.
WEIGHT:0.000 lb.
WEIGHT:0.500 kg.
```

Notes:

- In `EMULATOR`, `weight <value>` always emits pounds formatted as `0.000 lb.`.
- In `OPOS`, weight units come from the scale and may be `gr.`, `kg.`, `oz.`, or `lb.`.
- Unstable, overweight, not-ready, and other scale errors are logged to the console/log file, but they are not sent through the named pipe.
- If no client is connected, the wrapper logs `Pipe is not connected. Message not sent.` and drops the event.

## Manual Console Test

Use this when you want to validate the pipe contract without real hardware.

1. Set `MODE=EMULATOR` in `Settings.ini`.
2. Start the wrapper from a console in the wrapper folder:

```powershell
.\Scanner_Scale_OPOS_Wrapper.exe
```

3. In a second PowerShell window, connect to the named pipe and print every line:

```powershell
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'ScannerScaleOPOSPipe', [System.IO.Pipes.PipeDirection]::In)
$pipe.Connect()
$reader = New-Object System.IO.StreamReader($pipe)
while (($line = $reader.ReadLine()) -ne $null) { $line }
```

4. Back in the wrapper console, emit test events:

```text
scan 123456789
weight 1.250
status
help
exit
```

Expected results:

- `scan 123456789` emits `SCAN:123456789`.
- `weight 1.250` emits `WEIGHT:1.250 lb.`.
- `status` reports whether a pipe client is currently connected.
- `exit` or `Ctrl+C` stops the wrapper cleanly.

## Emulator Console Commands

```text
scan <barcode>
weight <lb>
status
clear
help
exit
```

## Example Client

Python example:

```python
pipe_name = r'\\.\pipe\ScannerScaleOPOSPipe'

with open(pipe_name, 'r', encoding='utf-8') as pipe:
    while True:
        line = pipe.readline().replace("\ufeff", "").strip()
        if line:
            print(line)
```

## Settings

`Settings.ini` is typically deployed with the executable. Key fields:

```ini
[GENERAL]
DEBUG=1
MODE=OPOS
PIPE_NAME=ScannerScaleOPOSPipe
SCANNER_NAME=ZEBRA_SCANNER
SCALE_NAME=ZEBRA_SCALE
SCALE_ENABLED=1
```

- `DEBUG=1` shows console output in `OPOS`.
- `MODE` selects real hardware or emulator mode.
- `PIPE_NAME` must match whatever the client opens.
- `SCANNER_NAME` and `SCALE_NAME` are OPOS logical names.
- `SCALE_ENABLED=0` disables scale initialization in `OPOS`.

## Build Requirements

- Windows
- .NET Framework 4.7.2+
- OPOS Common Control Objects (CCO) v1.14

## Notes

- The wrapper creates daily log files under `.\Logs`.
- Running from an elevated console is recommended when OPOS device access requires it.
