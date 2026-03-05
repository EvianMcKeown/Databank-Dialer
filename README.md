# Casio-Databank-Dialer

A high-performance, lightweight web application designed to listen to the acoustic DTMF "dialer" tones from a *Casio Databank DBA-800*/*DBA-80* watch. It decodes in real-time using a C# backend and provides a "click-to-dial" interface for modern smartphones.

## Architecture
This project uses a low-latency audio pipeline to ensure hardware dial tones are captured and processed accurately:
* **Frontend**: TypeScript with *AudioWorklet* for thread-isolated sampling.
* **Transport**: *SignalR* streaming 16-bit PCM data from the browser to the server.
* **Backend**: *ASP.NET Core* (C#) implementing the *Goertzel Algorithm* for DTMF frequency detection.



## Technology Used
* **Server**: .NET 10 (C#)
* **Client**: TypeScript (ESNext)
* **Signal Processing**: NAudio + Goertzel Implementation
* **Communication**: ASP.NET Core SignalR

---

## Project Structure
```text
CasioDialer/
├── Scripts/                # TypeScript Source
│   ├── app.ts              # UI & SignalR Client logic
│   └── audio-processor.ts  # AudioWorklet (DSP Thread)
├── wwwroot/
│   ├── js/                 # Compiled JavaScript (Git Ignored)
│   └── index.html          # Main Application UI
├── AudioHub.cs             # C# SignalR Hub
├── Program.cs              # ASP.NET Core Entry Point
└── src.csproj              # .NET Project Configuration