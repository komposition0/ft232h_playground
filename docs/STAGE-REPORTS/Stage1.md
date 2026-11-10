# Stage 1 — Foundation: Implementation Report

Date: 2026-11-10 · Branch: `stage/1-foundation`

## What was implemented

### Solution & projects
- `FT232H.ControlStudio.slnx` (SDK 10 solution format; opens in VS 2022 17.13+)
- 6 projects: `FT232H.Core`, `FT232H.Hardware`, `FT232H.Simulation`,
  `FT232H.Automation`, `FT232H.ControlStudio` (WinForms), `FT232H.Tests` (xUnit)
- All hardware communication is UI-independent.

### FT232H.Core (no hardware/GUI dependencies)
- Models: `SpiConfiguration` (modes 0–3, MSB/LSB, CS pin + mode), `SpiClock`
  (TCK = 60 MHz/(1+divisor), clamped 915.5 Hz–30 MHz), `SpiTransferRequest/Result`,
  `FtdiDeviceInfo` (with duplicate-serial flag), `DeviceSelector`
  (index / serial / USB location), GPIO models (ADBUS vs ACBUS), transaction
  records, `CommunicationCounters`, ring buffer, hex/ASCII/binary codecs.
- Interfaces: `IFtdiDeviceManager`, `IFtdiDevice`, `ISpiController`,
  `IGpioController`, `ITransactionLogger`, `ITestSequenceRunner`, `DeviceArbiter`.
- Validation: SPI config, GPIO-vs-SPI pin conflicts, pin-map conflicts.
- Test sequence models (12 action kinds, JSON-friendly).

### FT232H.Hardware (real FTDI)
- `MpsseCommandBuilder` — pure AN_108 command encoder (every opcode golden-tested):
  set/get data bits (0x80–0x83), clock divisor (0x86), loopback (0x84/0x85),
  send-immediate (0x87), clock divide-by-5 (0x8A/0x8B), shift commands with
  correct per-mode edge opcodes (0x10/0x11/0x20/0x24/0x31/0x34 + LSB variants),
  64 KiB payload chunking with CS continuity.
- `FtdiDeviceManager` — D2XX enumeration, duplicate-serial detection, open by
  index/serial/location, driver diagnostics.
- `FtdiDevice` — MPSSE bring-up (reset → purge → MPSSE bit mode → bad-command
  sync 0xFA 0xAA), raw read/write with timeouts, safe shutdown.
- `MpsseSpiController` / `MpsseGpioController` — SPI modes 0–3, auto/manual CS,
  staged write-then-read, GPIO direction/value with SPI pin protection.
- `DeviceArbiter` — single-owner lease per physical device.

### FT232H.Simulation
- `SimulatedFtdiDeviceManager` — virtual devices incl. a duplicate-serial pair.
- Simulated SPI (loopback / fill byte), simulated GPIO with identical pin
  reservation rules, configurable latency. Same contracts as hardware.

### FT232H.Automation
- `TestSequenceRunner` — all 12 action kinds, nested repeat/conditional,
  cancellation, progress reporting, per-step pass/fail with details.
- `TestSequenceSerializer` — System.Text.Json save/load.

### FT232H.ControlStudio (WinForms, dark theme)
- Device toolbar (backend switch, enumeration, connect/disconnect), status strip
  (connection, SPI summary, actual clock, live TX/RX/op/error counters).
- Tabs: SPI Playground (config, hex TX, hex/ASCII/binary RX, 4 transfer kinds,
  repeat, manual CS, history + CSV/JSON export), GPIO Playground (all 16 pins
  with function/direction/value, polling, pulse with software-timing warning,
  event log), Signal Monitor (ScottPlot 5, ring buffers, host-observed values
  clearly labeled), Automation (visual sequence editor, run/cancel, JSON),
  Simulation (options, device list, diagnostics).
- Live log viewer (bounded, non-blocking), config save/load (JSON).
- DI via Microsoft.Extensions.Hosting; async I/O with cancellation everywhere.

### Docs
- `README.md`, `LICENSE` (MIT), `docs/FTDI-DRIVERS.md`,
  `docs/ELECTRICAL-LIMITATIONS.md`, this report.

## What was tested (automated)

`dotnet test` — **71/71 passing**:
- MPSSE golden vectors (every opcode + length encoding + chunking + SPI preambles)
- SPI clock divisor math (clamping, formula)
- Configuration + GPIO conflict validation
- Hex codec round-trips and error reporting
- Ring buffer overflow semantics
- Simulated manager: duplicate-serial flagging, ambiguous-serial rejection,
  location disambiguation
- Simulated SPI: loopback, read fill, manual CS enforcement, config rejection
- Simulated GPIO: write/read, SPI pin protection (all 4 reserved pins), pulse
- Test runner: pass/fail paths, compare-bytes, repeat, stop, cancellation
- Serializer round-trips

Also verified: full solution builds with 0 errors; the WinForms GUI launches and
runs (smoke test).

## What needs hardware verification (NOT verified yet)

- Real D2XX enumeration/open/close on an actual FT232H
- MPSSE bring-up + bad-command sync on silicon
- SPI waveforms for modes 0–3 (logic analyzer; especially modes 1/2 CPHA edge
  semantics), actual SCK frequency vs configured divisor
- CS assertion timing (auto and manual modes)
- GPIO direction/value handling on ADBUS and ACBUS
- Electrical limits on the Adafruit board (3.3 V, current)

Manual test procedures will accompany Stage 2 (connectivity) and Stage 3 (SPI).

## Known limitations / deferred

- `ITestSequenceRunner` interface defined in Core; the GUI Automation tab uses
  the Stage-1 `TestSequenceRunner` (all 12 action kinds) but the *visual* editor
  is basic (single-line actions; nested Repeat/Conditional created via JSON or
  extended in Stage 6).
- Pin Mapping Designer, benchmarks, stress tester, register explorer, packet
  inspector, fault injection, profiles, snippet generator, session recorder,
  doc generator: scheduled for Stages 5–8 per the roadmap.
- RF node protocol layer: Stage 7 (no packet format invented yet, by design).
- NU1701 warnings from ScottPlot's optional OpenTK/SkiaSharp transitive
  packages (unused rendering backends).

## Committed & pushed

- Branch `stage/1-foundation`, see commit hashes in the PR.
- Remote: `https://github.com/komposition0/ft232h_playground.git`
- No credentials or machine-specific data committed.

## Next (Stage 2 — FT232H Connectivity)

Real-hardware bring-up: device discovery validation, connection management,
reconnect, resource cleanup hardening, driver diagnostics in the GUI, and a
manual hardware test procedure.
