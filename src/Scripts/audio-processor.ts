// Define interface for TypeScript
interface AudioWorkletProcessor {
    readonly port: MessagePort;
    process(inputs: Float32Array[][], outputs: Float32Array[][], parameters: Record<string, Float32Array>): boolean;
}

declare var AudioWorkletProcessor: {
    prototype: AudioWorkletProcessor;
    new(): AudioWorkletProcessor;
}

declare function registerProcessor(name: string, processorCtor: any): void;

class CasioAudioProcessor extends AudioWorkletProcessor {
    process(inputs: Float32Array[][]): boolean {
        const input = inputs[0];
        if (input && input.length > 0) {
            // Mono
            const samples = input[0];
            // send to main thread
            this.port.postMessage(samples);
        }
        return true;
    }
}

registerProcessor("casio-audio-processor", CasioAudioProcessor);