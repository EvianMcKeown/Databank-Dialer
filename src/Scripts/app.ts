declare var signalR: any;

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/audioHub")
    .configureLogging(signalR.LogLevel.Information)
    .build();

connection.on("ReceiveStatus", (message: string) => { console.log("Server responded: " + message); });

const startBtn      = document.getElementById("startBtn")!;
const callContainer = document.getElementById("callContainer")!;
const dialLink      = document.getElementById("dialLink") as HTMLAnchorElement;

// Total character cap: 8 chars on the top row + 8 on the bottom row.
const LCD_MAX = 16;

let fullNumber = "";

connection.on("DetectedDigit", (digit: string) => {
    // Silently drop digits once the display is full.
    if (fullNumber.length >= LCD_MAX) return;

    fullNumber += digit;

    // ── Keep callContainer in sync ───────────────────────────────────
    callContainer.style.display = "block";
    dialLink.href               = `tel:${fullNumber}`;

    // ── Delegate all display rendering to the LCD overlay script ────
    document.dispatchEvent(new CustomEvent("dtmf:fullNumber", { detail: fullNumber }));
});

connection.on("DebugLog", (msg: string) => console.log("SERVER DEBUG:", msg));

async function startAudio(): Promise<void> {
    try {
        await connection.start();
        console.log("SignalR Connected!");

        startBtn.textContent = "LISTENING...";
        startBtn.setAttribute("disabled", "true");

        // ── Reset display state for a fresh session ──────────────────
        fullNumber = "";
        document.dispatchEvent(new CustomEvent("dtmf:reset"));

        const stream       = await navigator.mediaDevices.getUserMedia({ audio: true });
        const audioContext = new AudioContext({ sampleRate: 8000 });
        const source       = audioContext.createMediaStreamSource(stream);

        await audioContext.audioWorklet.addModule("js/audio-processor.js");
        const workletNode = new AudioWorkletNode(audioContext, "casio-audio-processor");

        workletNode.port.onmessage = (event: MessageEvent) => {
            const audioChunk: Float32Array = event.data;
            connection
                .invoke("UploadAudioChunk", Array.from(audioChunk))
                .catch((err: unknown) => console.error("Upload failed:", err));
        };

        source.connect(workletNode);
        console.log("Audio pipeline active.");

    } catch (error) {
        console.error("Failed to start audio/connection:", error);
        startBtn.textContent = "START LISTENING";
        startBtn.removeAttribute("disabled");
    }
}

startBtn.addEventListener("click", startAudio);