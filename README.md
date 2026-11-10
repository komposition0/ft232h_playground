# FT232H Control Studio

A professional, modular Windows desktop application for experimenting with the
**Adafruit FT232H breakout board** over FTDI MPSSE: SPI and GPIO playgrounds,
communication monitoring, repeatable test automation, and a simulated backend
that runs without any hardware attached.

This is an engineering tool, not a demo: layered architecture, interface-based
design, dependency injection, async I/O, unit tests, and a simulated hardware
backend so every workflow can be exercised before touching real hardware.

## Solution structure

| Project | Purpose |
|---|---|
| `FT232H.ControlStudio` | WinForms GUI (dark theme, tabs, log viewer) |
| `FT232H.Core` | Interfaces, models, validation, protocol definitions (no hardware/GUI deps) |
| `FT232H.Hardware` | Real FTDI D2XX/MPSSE implementation (official `FTDI.FTD2XX_NET` wrapper) |
| `FT232H.Simulation` | Simulated devices, SPI loopback, virtual GPIO, duplicate-serial scenarios |
| `FT232H.Automation` | Test sequence runner + JSON serialization |
| `FT232H.Tests` | xUnit test suite (71 tests) |

All hardware communication is independent of WinForms and can be reused as a
library (e.g. for the 16-node RF power measurement system).

## Key interfaces

- `IFtdiDeviceManager` — enumeration, connection by index/serial/USB location, diagnostics
- `IFtdiDevice` — single owner/arbiter of one physical device (SPI + GPIO serialize through it)
- `ISpiController` — SPI transactions (modes 0–3, MSB/LSB, auto/manual chip select)
- `IGpioController` — ADBUS0–7 / ACBUS0–7 control with SPI pin protection
- `ITransactionLogger` — transaction + diagnostic event logging
- `ITestSequenceRunner` — JSON test sequences with assertions and cancellation

## Requirements

- Windows 10/11 x64
- .NET 8 SDK (build) / .NET 8 Desktop Runtime (run)
- For real hardware: FTDI D2XX driver (`FTD2XX.DLL`) — see [docs/FTDI-DRIVERS.md](docs/FTDI-DRIVERS.md)

## Getting started

```powershell
dotnet build FT232H.ControlStudio.slnx
dotnet test FT232H.ControlStudio.slnx
dotnet run --project src/FT232H.ControlStudio
```

The app starts in **Simulation** backend by default — connect a virtual device and
use every tab without hardware. Switch the backend to **Hardware (D2XX)** in the
toolbar when a real FT232H is attached.

## Documentation

- [docs/FTDI-DRIVERS.md](docs/FTDI-DRIVERS.md) — chosen FTDI driver/library approach, dependencies, installation, limitations
- [docs/ELECTRICAL-LIMITATIONS.md](docs/ELECTRICAL-LIMITATIONS.md) — 3.3 V logic, current limits, chip-select behavior, software vs hardware timing
- [docs/STAGE-REPORTS/](docs/STAGE-REPORTS/) — per-stage implementation reports

## Honesty rules

- No hardware feature is marked **verified** unless tested on a real FT232H.
- All timestamps/durations shown in the app are **host-observed** (PC-side); they
  are not oscilloscope- or logic-analyzer-grade signal timing.
- GPIO "polling" is software polling over USB — not hardware interrupts.
- Software-generated pulses are not microsecond-accurate.

## License

MIT — see [LICENSE](LICENSE).