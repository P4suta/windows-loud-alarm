# Alarm

Windows alarm clock with volume override and a countdown.

## Installation

```sh
# Requires Windows x64 and Windows App Runtime 2.x.
powershell.exe -NoProfile -Command "Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/P4suta/windows-loud-alarm/releases/download/v0.1.0/Alarm-v0.1.0-win-x64.zip' -OutFile 'Alarm.zip' -ErrorAction Stop"
powershell.exe -NoProfile -Command "Expand-Archive -LiteralPath 'Alarm.zip' -DestinationPath 'Alarm' -ErrorAction Stop"
```

## Usage

```powershell
.\Alarm\Alarm.exe
```

## Documentation

[Read the documentation](docs/README.md).
