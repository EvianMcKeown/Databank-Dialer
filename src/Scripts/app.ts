declare var signalR: any;

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/audioHub")
    .configureLogging(signalR.LogLevel.Information)
    .build();

connection.on("ReceiveStatus", (message: string) => { console.log("Server responded: " + message); });

const startBtn = document.getElementById("startBtn")!;
const callContainer = document.getElementById("callContainer")!;
const dialLink = document.getElementById("dialLink") as HTMLAnchorElement;

const LCD_MAX = 14;

let fullNumber = "";
let audioContext: AudioContext | null = null;
let workletNode: AudioWorkletNode | null = null;
let mediaStream: MediaStream | null = null;
let isListening = false;

// Tear down the audio pipeline without touching the SignalR connection 
function stopAudio(): void {
    try { workletNode?.disconnect(); } catch { }
    try { mediaStream?.getTracks().forEach(t => t.stop()); } catch { }
    try { audioContext?.close(); } catch { }
    workletNode = null;
    mediaStream = null;
    audioContext = null;
}

// Reset display and local state for a fresh attempt
function resetSession(): void {
    fullNumber = "";
    callContainer.style.display = "none";
    dialLink.href = "#";
    document.dispatchEvent(new CustomEvent("dtmf:reset"));
}

function setButtonState(state: "idle" | "listening" | "error"): void {
    const labels = { idle: "START LISTENING", listening: "STOP LISTENING", error: "RETRY" };
    startBtn.textContent = labels[state];
}

connection.on("DetectedDigit", (digit: string) => {
    if (fullNumber.length >= LCD_MAX) return;
    fullNumber += digit;
    callContainer.style.display = "block";
    dialLink.href = `tel:${fullNumber}`;
    document.dispatchEvent(new CustomEvent("dtmf:fullNumber", { detail: fullNumber }));
});

connection.on("DebugLog", (msg: string) => console.log("SERVER DEBUG:", msg));

// Re-arm the button if SignalR drops unexpectedly mid-session.
connection.onclose(() => {
    if (isListening) {
        stopAudio();
        isListening = false;
        setButtonState("error");
    }
});

connection.onreconnecting(() => {
    stopAudio();
    setButtonState("error");
});

async function startAudio(): Promise<void> {
    if (isListening) {
        stopAudio();
        isListening = false;
        setButtonState("idle");
        return;
    }

    // Clean up any previous attempt before starting
    stopAudio();
    resetSession();
    setButtonState("listening");

    try {
        // Reuse an existing connection; start it only when necessary
        if (connection.state === signalR.HubConnectionState.Disconnected) {
            await connection.start();
            console.log("SignalR connected.");
        }

        isListening = true;
        mediaStream = await navigator.mediaDevices.getUserMedia({ audio: true });
        audioContext = new AudioContext({ sampleRate: 8000 });
        const source = audioContext.createMediaStreamSource(mediaStream);

        await audioContext.audioWorklet.addModule("js/audio-processor.js");
        workletNode = new AudioWorkletNode(audioContext, "casio-audio-processor");

        workletNode.port.onmessage = (event: MessageEvent) => {
            const audioChunk: Float32Array = event.data;
            if (connection.state !== signalR.HubConnectionState.Connected) return;
            connection
                .invoke("UploadAudioChunk", Array.from(audioChunk))
                .catch((err: unknown) => console.error("Upload failed:", err));
        };

        source.connect(workletNode);
        console.log("Audio pipeline active.");

    } catch (error) {
        console.error("Failed to start:", error);
        stopAudio();
        isListening = false;
        setButtonState("error");
    }
}

startBtn.addEventListener("click", startAudio);