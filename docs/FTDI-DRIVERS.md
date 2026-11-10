# FTDI Driver & Library Approach

## Investigation summary

The following options for accessing FTDI devices from C#/.NET were evaluated
before implementation:

| Option | Type | Status | Decision |
|---|---|---|---|
| **FTDI.FTD2XX_NET** (official FTDI NuGet, v1.3.4) | Managed wrapper over `FTD2XX.DLL` (D2XX) | Active (updated Jan 2026); targets .NET 8, .NET Standard 2.0, .NET Framework 4.8 | **✅ CHOSEN** |
| FtdiSharp (swharden) | Managed wrapper + high-level SPI/I2C/GPIO | Active; MIT | ❌ High-level abstractions hide MPSSE command details we need to control and validate; README itself warns its model suits "updates a few times per second" |
| FTD2XX_DotNetWrapper (wwwMADwww) | P/Invoke wrapper | Niche, small project | ❌ Too small to depend on |
| MPSSELight (zhelnio) | MPSSE library over FTD2XX_NET | Last significant work ~2016 | ❌ .NET Framework-era, unmaintained (used only as an opcode cross-reference, MIT) |
| FTDI libMPSSE + wrappers | Native C DLL + thin wrappers | Official native lib, unofficial .NET wrappers | ❌ Extra native deployment, limited control |
| Raw P/Invoke over `FTD2XX.DLL` | Hand-written interop | — | ❌ Unnecessary risk; the official wrapper is maintained and covers the full API |

## Chosen approach

**`FTDI.FTD2XX_NET` 1.3.4 (official FTDI wrapper) for D2XX device management +
our own MPSSE command engine** (`FT232H.Hardware.Mpsse.MpsseCommandBuilder`),
built strictly against FTDI application note **AN_108**
("Command Processor for MPSSE and MCU Host Bus Emulation Modes", FT_000109 v1.5).

Rationale:

1. The official wrapper is maintained by FTDI and targets .NET 8.
2. The wrapper covers D2XX only (enumeration, open/close, bit mode, latency,
   timeouts, read/write). The MPSSE *command language* is ours to build, which
   gives complete control over:
   - SPI clock-edge semantics per mode (opcodes 0x31 / 0x34 and the write/read
     variants),
   - bit order (MSB/LSB first opcodes),
   - chip-select on any free ADBUS/ACBUS line with manual or automatic control,
   - staged write-then-read transfers with CS held continuously,
   - accurate reporting of the actual configured clock frequency.
3. Everything sits behind `FT232H.Core` interfaces; the wrapper is replaceable.

The MPSSE opcode table used is cross-validated between two independent,
documented implementations (ftdi-mpsse for Rust; MPSSELight for C#/.NET) and
AN_108's own revision history. Golden-vector unit tests lock the encoding down
(`FT232H.Tests/MpsseCommandBuilderTests.cs`).

## Dependencies

- `FTDI.FTD2XX_NET` 1.3.4 — NuGet (Hardware project only)
- `FTD2XX.DLL` — FTDI D2XX driver runtime (installed on the host; NOT redistributable
  with the application)

## Driver installation

1. Download the D2XX driver ("FTDI D2XX Direct Drivers") from
   <https://ftdichip.com/drivers/d2xx-drivers/> for your Windows version.
2. Run the installer (or extract `FTD2XX.DLL` and use the CDM installer).
3. Plug in the Adafruit FT232H breakout (VID 0x0403, PID 0x6014 for FT232H).
4. Windows usually auto-installs the VCP driver; D2XX works alongside it — the
   application opens the device via D2XX, not via a COM port.
5. The app's Simulation tab shows a **driver diagnostics** panel (driver version,
   wrapper version, device count). A red `FTD2XX.DLL NOT FOUND` line means the
   driver is missing.

## Limitations & notes

- **Windows x64** is the initial target. The wrapper also supports Linux via
  libftd2xx, but that is untested here.
- The wrapper API is synchronous; `FT232H.Hardware` wraps calls in `Task.Run`
  with `CancellationToken` support so the UI never blocks. This is *host-side*
  async only — the MPSSE engine itself generates the SPI/GPIO timing on-chip.
- One MPSSE channel per FT232H (ADBUS0–7 + ACBUS0–7).
- Duplicate serial numbers are real (cheap clones and factory defaults): the
  manager flags them and `DeviceSelector` supports disambiguation by USB
  location (`LocId`).
- A single `DeviceArbiter` (semaphore lease) serializes all access per physical
  device so SPI, GPIO and test automation cannot issue interleaved MPSSE
  command streams.
- The MPSSE bad-command echo (`0xFA 0xAA`) is used at connect time to verify the
  command processor is synchronized before any real traffic.
