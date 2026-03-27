# Casio-Databank-Dialer
A high-performance, lightweight web application designed to listen to the acoustic DTMF "dialer" tones from a *Casio Databank DBA-800*/*DBA-80* watch. It decodes in real-time using a C# backend and provides a "click-to-dial" interface for modern smartphones.

## Status
[![Web App](https://github.com/EvianMcKeown/Databank-Dialer/actions/workflows/deploy.yml/badge.svg)](https://github.com/EvianMcKeown/Databank-Dialer/actions/workflows/deploy.yml)

## Architecture
This project uses a low-latency audio pipeline to ensure hardware dial tones are captured and processed accurately:
* **Frontend**: TypeScript with *AudioWorklet* for thread-isolated sampling.
* **Transport**: *SignalR* streaming 16-bit PCM data from the browser to the server.
* **Backend**: *ASP.NET Core* (C#) implementing the [*Goertzel Algorithm*](http://en.wikipedia.org/wiki/Goertzel_algorithm) for DTMF frequency detection.

![DBA-800-preview](https://github.com/user-attachments/assets/aab3ab68-803d-45e5-89e6-3a7af018c68b)


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
```

## Setup & Development
### Prerequisites
* [*.NET SDK*](http://dotnet.microsoft.com/download)
* [*Node.js & npm*](https://nodejs.org/en)

1. ### Install Dependencies
```
# Install .NET packages
dotnet restore

# Install TypeScript & SignalR
npm install
```

2. ### Development Workflow
Run in two separate terminal windows to handle the build process:
#### C# Server
```
dotnet watch
```
#### TypeScript Compiler
```
# Watch and compile TS to wwwroot/js/
npx tsc -w
```

## License
[GNU General Public License 3.0](https://github.com/EvianMcKeown/Casio-Databank-Dialer/blob/dev/LICENSE)
