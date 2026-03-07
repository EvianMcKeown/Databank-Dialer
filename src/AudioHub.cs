using Microsoft.AspNetCore.SignalR;

public class AudioHub : Hub
{
    private static readonly float[] rows = [697f, 770f, 852f, 941f];
    private static readonly float[] cols = [1209f, 1336f, 1477f];
    private static readonly float[] freqs = { 697f, 770f, 852f, 941f, 1209f, 1336f, 1477f };

    private readonly ILogger<AudioHub> _logger;
    public AudioHub(ILogger<AudioHub> logger)
    {
        _logger = logger;
        _logger.LogInformation("--- AudioHub Initialised ---");
    }
    private static List<float> _accumulationBuffer = new List<float>();


    private bool SignalToNoise(List<GoertzelResult> results, float[] groupFreqs)
    {
        var group = results.Where(r => groupFreqs.Contains(r.Frequency)).OrderByDescending(r => r.Power).ToList();

        if (group.Count < 2) return true;

        // signal at least 6db louder than second best
        return group[0].Power > (group[1].Power * 4);
    }

    public async Task UploadAudioChunk(float[] chunk)
    {
        _accumulationBuffer.AddRange(chunk);

        await Clients.Caller.SendAsync("DebugLog", "C# received the audio!");

        _logger.LogInformation("Received a chunk of {Count} samples", chunk.Length);

        const int N = 256; // target window size @ 8kHz

        // Sliding window with 50% overlap
        if (_accumulationBuffer.Count >= N)
        {
            float[] toProcess = _accumulationBuffer.ToArray();
            _accumulationBuffer.RemoveRange(0, N / 2);

            char? digit = DetectCasioDigit(toProcess);

            if (digit.HasValue)
            {
                await Clients.Caller.SendAsync("DetectedDigit", digit.Value.ToString());
            }
            else
            {
                _logger.LogInformation("No digit detected!!!");
            }
        }
    }

    internal char? DetectCasioDigit(float[] samples)
    {
        List<GoertzelResult> results = Goertzel.Compute(samples, freqs, 8000);
        _logger.LogInformation("Results: " + results.Count);

        var lowGroup = results.Where(r => rows.Contains(r.Frequency)).OrderByDescending(r => r.Power).First();
        var highGroup = results.Where(r => cols.Contains(r.Frequency)).OrderByDescending(r => r.Power).First();

        double threshold = 0.05 * Math.Pow((double)samples.Length / 512, 2);

        // both Row + Col need to be above threshold
        if (lowGroup.Power > threshold && highGroup.Power > threshold)
        {
            // Check signal to noise ratio
            if (!SignalToNoise(results, rows) || !SignalToNoise(results, cols))
            {
                return null;
            }

            // Check Twist
            double twist = highGroup.Power / lowGroup.Power;
            if (twist < 0.1 || twist > 10.0) return null;

            // freq -> key indices
            int rowIdx = Array.IndexOf(rows, lowGroup.Frequency);
            int colIdx = Array.IndexOf(cols, highGroup.Frequency);

            char[,] keypad = { { '1', '2', '3' }, { '4', '5', '6' }, { '7', '8', '9' }, { '*', '0', '#' } };
            char detected = keypad[rowIdx, colIdx];
            _logger.LogInformation("Match: {Num} (RowIdx:{R}, ColIdx:{C})", detected, rowIdx, colIdx);

            return detected;
        }

        return null;
    }
}