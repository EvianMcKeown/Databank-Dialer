declare var signalR: any;

const connection = new signalR.HubConnectionBuilder().withUrl("/audioHub").configureLogging(signalR.LogLevel.Information).build();

connection.on("ReceiveStatus", (message: string) => { console.log("Server responded: " + message); });

//const numberDisplay = document.getElementById("numberDisplay")!;
//const startBtn = document.getElementById("startBtn")!;
//const callContainer = document.getElementById("callContainer")!;
//const dialLink = document.getElementById("dialLink") as HTMLAnchorElement;

let fullNumber = "";

//connection.on("DetectedDigit", (digit: string) => {
//    fullNumber += digit;
//    numberDisplay.innerText = fullNumber;
//    // Show dial button
//    callContainer.style.display = "block";
//    dialLink.href = `tel:${fullNumber}`;
//});

connection.on("DebugLog", (msg:string) => console.log("SERVER DEBUG:", msg));

async function startAudio() {
    try {
        await connection.start();
        console.log("SignalR Connected!");

        //startBtn.innerText = "LISTENING...";

        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        const audioContext = new AudioContext({ sampleRate: 8000 });
        const source = audioContext.createMediaStreamSource(stream);

        await audioContext.audioWorklet.addModule('js/audio-processor.js');
        const workletNode = new AudioWorkletNode(audioContext, 'casio-audio-processor');

        workletNode.port.onmessage = (event) => {
            const audioChunk: Float32Array = event.data;
            // send to C# SignalR Hub

            connection.invoke("UploadAudioChunk", Array.from(audioChunk)).catch((err: any) => console.error("Upload failed:", err));
        };

        source.connect(workletNode);
        console.log("Audio pipeline active.");

    } catch (error) {
        console.error("Failed to start audio/connection:", error);
    }
}

document.getElementById("startBtn")!.addEventListener("click", startAudio);