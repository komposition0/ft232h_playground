# Electrical Limitations & Timing Caveats

**These constraints apply to the Adafruit FT232H breakout and the FT232H chip.
Read before connecting any hardware.**

## Logic levels

- **All FT232H MPSSE I/O (ADBUS0–7, ACBUS0–7) is 3.3 V logic.**
  - HIGH ≈ 3.3 V, LOW ≈ 0 V.
  - **Do not connect 5 V logic directly.** The pins are **not** 5 V tolerant in
    output mode and driving them from 5 V rails can damage the chip. Use level
    shifters for 5 V peripherals.
- The Adafruit breakout provides a **3.3 V regulator output (VREGOUT/3V3)** and
  a **5 V USB rail (VIN/5V)**. The 3V3 pin on the breakout can source limited
  current (see below) — it is meant for small loads, not powering large circuits.

## Current limitations

- FT232H I/O pins have **limited drive strength** (FTDI datasheet: 4 mA
  recommended per pin at 3.3 V; do not treat pins as power sources).
- **Total package current** and thermal limits apply — never drive LEDs or
  relays directly from GPIO without a transistor/MOSFET driver stage.
- The onboard 3.3 V regulator on the Adafruit board can supply roughly
  **500 mA total** (thermally limited, shared with the chip); budget carefully
  and prefer an external supply for anything beyond logic-level experiments.

## Pin function map (MPSSE SPI defaults used by this application)

| Pin | Default function |
|---|---|
| ADBUS0 | SCK (SPI clock) |
| ADBUS1 | MOSI / SDO (controller out) |
| ADBUS2 | MISO / SDI (controller in) |
| ADBUS3 | CS (chip select, GPIOL0) — configurable |
| ADBUS4–7 | GPIO |
| ACBUS0–7 | GPIO |

The application **refuses GPIO writes to pins reserved by the active SPI
configuration** (SCK/MOSI/MISO always; the configured CS line while SPI is
active). This is enforced in both hardware and simulation backends.

## Chip-select behavior

- CS is **active LOW** in this implementation (standard SPI convention).
- **Automatic mode:** the controller asserts CS before a transfer and releases
  it after. Multi-byte transfers keep CS asserted for the whole payload.
- **Manual mode:** the application asserts/releases CS explicitly — required for
  multi-stage transactions (e.g. write command, pause, read response) where CS
  must stay LOW continuously between stages.
- MPSSE changes CS in-band (SetDataBits commands) at USB transfer boundaries;
  CS edges are **not** timestamped by hardware counters. For sub-millisecond CS
  sequencing, use manual mode and verify on a logic analyzer.

## Software vs hardware timing — read carefully

| Aspect | What this application provides |
|---|---|
| SPI clock edges | **Hardware-timed** by the MPSSE engine at the configured clock (TCK = 60 MHz / (1 + divisor)). Accurate on the wire. |
| SPI data timing | **Hardware-timed** (shifted/sampled by MPSSE). |
| GPIO "polling" | **Software polling over USB.** Host reads pin state on a timer (configurable interval). This is **NOT a hardware interrupt**; latency and jitter are milliseconds-scale. |
| GPIO pulses / toggles | **Software-timed** (`Task.Delay` between writes). **NOT microsecond-accurate** — expect several ms of jitter from USB scheduling, driver buffering and Windows thread scheduling. The GUI warns when this is used. |
| Transaction durations shown in the GUI | **Host-observed** (Stopwatch around the USB round-trip). Includes USB latency, driver buffering and scheduling. **NOT oscilloscope-grade signal timing.** |
| Signal Monitor plots | Same host-observed timestamps. Do not interpret them as logic-analyzer captures. |

If you need microsecond-accurate pulse generation or precise trigger timing,
use an external microcontroller (e.g. the RP2350B nodes) or a dedicated pulse
generator — the FT232H MPSSE cannot provide host-driven µs-accurate GPIO timing.

## RF pulse trigger context (16-node RF system)

- Node measurement triggers are **TTL edge driven** (measure while HIGH) and are
  handled by the node firmware, not by this host application.
- SPI transfers of results over FT232H are host-observed for logging purposes;
  measurement timestamps come from the node itself.

## Verification status

- Electrical characteristics above are from the FT232H datasheet and Adafruit
  documentation. Pin-current behavior and CS timing on *this specific board*
  must be verified with real hardware (Stage 2+ manual test procedures).
