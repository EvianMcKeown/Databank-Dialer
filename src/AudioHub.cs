using Microsoft.AspNetCore.SignalR;

public class AudioHub : Hub
{
    private readonly ILogger<AudioHub> _logger;
    public AudioHub(ILogger<AudioHub> logger)
    {
        _logger = logger;
        _logger.LogInformation("--- AudioHub Initialised ---");
    }
    private static List<float> _accumulationBuffer = new List<float>();

    public async Task UploadAudioChunk(float[] chunk)
    {
        _accumulationBuffer.AddRange(chunk);

        await Clients.Caller.SendAsync("DebugLog", "C# received the audio!");

        _logger.LogInformation("Received a chunk of {Count} samples", chunk.Length);

        // wait until 512 samples @ 8 khz
        if (_accumulationBuffer.Count >= 512)
        {
            float[] toProcess = _accumulationBuffer.ToArray();
            _accumulationBuffer.Clear();

            char? digit = DetectCasioDigit(toProcess);

            if (digit.HasValue)
            {
                await Clients.Caller.SendAsync("DetectedDigit", digit.Value.ToString());
            }
        }
    }

    private char? DetectCasioDigit(float[] samples)
    {
        // TODO: NAudio Goertzel/FFT

        return null;
    }
}