declare var signalR: any;

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/audioHub?clientApp=Databank-Frontend")
    .configureLogging(signalR.LogLevel.Warning)
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
let sessionOver = false;
let endReason: string | null = null;

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

function setButtonState(state: "idle" | "listening" | "error" | "ended" | "busy"): void {
    const labels = {
        idle: "START LISTENING",
        listening: "STOP LISTENING",
        error: "RETRY",
        ended: "SESSION ENDED. RETRY",
        busy: "SERVER BUSY. RETRY",
    };
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

// The server announces why it is about to close the stream (time limit, rate limit or capacity).
connection.on("SessionEnded", (reason: string) => {
    sessionOver = true;
    endReason = reason;
});

// Re-arm the button if SignalR drops mid-session, saying why when the server told us.
connection.onclose(() => {
    sessionOver = true;
    if (isListening) {
        stopAudio();
        isListening = false;
        setButtonState(endReason === "busy" ? "busy" : endReason ? "ended" : "error");
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
        await connection.stop().catch(() => { });
        return;
    }

    // Clean up any previous attempt before starting
    stopAudio();
    resetSession();
    setButtonState("listening");

    try {
        // Every listening session gets its own connection, so the server's session limit starts fresh
        if (connection.state !== signalR.HubConnectionState.Disconnected) {
            await connection.stop().catch(() => { });
        }
        sessionOver = false;
        endReason = null;
        await connection.start();

        isListening = true;
        mediaStream = await navigator.mediaDevices.getUserMedia({ audio: true });
        audioContext = new AudioContext({ sampleRate: 8000 });
        const source = audioContext.createMediaStreamSource(mediaStream);

        await audioContext.audioWorklet.addModule("js/audio-processor.js");
        workletNode = new AudioWorkletNode(audioContext, "audio-processor");

        workletNode.port.onmessage = (event: MessageEvent) => {
            const audioChunk: Float32Array = event.data;
            if (sessionOver || connection.state !== signalR.HubConnectionState.Connected) return;
            connection
                .invoke("UploadAudioChunk", Array.from(audioChunk))
                .catch((err: unknown) => {
                    if (!sessionOver) console.error("Upload failed:", err);
                });
        };

        source.connect(workletNode);

    } catch (error) {
        console.error("Failed to start:", error);
        stopAudio();
        isListening = false;
        setButtonState("error");
    }
}

startBtn.addEventListener("click", startAudio);