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
        //_logger.LogInformation("--- AudioHub Initialised ---");
    }
    private static List<float> _accumulationBuffer = new List<float>();

    private (List<GoertzelResult>, List<GoertzelResult>) SeperateRowsCols(List<GoertzelResult> results)
    {
        var lowTones = results.Where(r => rows.Contains(r.Frequency)).OrderByDescending(r => r.Power).ToList();
        var highTones = results.Where(r => cols.Contains(r.Frequency)).OrderByDescending(r => r.Power).ToList();
        return (lowTones, highTones);
    }

    private bool StandardTwist(List<GoertzelResult> results)
    {
        /***
        Calculate standard (low tone: high tone) twist ratio & set threshold to 4dB acceptable standard twist
        ***/
        var (lowTones, highTones) = SeperateRowsCols(results);
        // DEBUG
        _logger.LogInformation("Row: {RP} @ {RF} ——— Col: {CP} @ {CF} ——— ReverseTwist: {Ratio} ——— StandardTwist: {SRatio}", Math.Round(lowTones[0].Power, 2), Math.Round(lowTones[0].Frequency, 2), Math.Round(highTones[0].Power, 2), Math.Round(highTones[0].Frequency, 2), Math.Round(lowTones[0].Power / highTones[0].Power, 2), Math.Round(highTones[0].Power / lowTones[0].Power, 2));
        if ((highTones[0].Power / lowTones[0].Power) < 5) return true;
        // else
        return false;
    }

    private bool ReverseTwist(List<GoertzelResult> results)
    {
        /***
        Calculate reverse (high tone : low tone) twist ratio & set threshold to acceptable reverse twist
        ***/

        // (ratio = 10) to account for microphone frequency response oddities   ~10dB
        var (lowTones, highTones) = SeperateRowsCols(results);
        if ((lowTones[0].Power / highTones[0].Power) < 10) return true;
        // else
        return false;
    }

    private bool SecondOrderHarmonic(List<GoertzelResult> results, float[] samples)
    {
        /***
        Check that second order harmonic is at least 10dB quieter than fundamental for winning row and column respectively
        ***/

        var (lowTones, highTones) = SeperateRowsCols(results);
        float row2nd = lowTones[0].Frequency * 2; 
        float col2nd = highTones[0].Frequency * 2;
        float[] winner2ndOrder = {row2nd, col2nd};

        // rerun Goertzel with harmonic freqs of winners
        List<GoertzelResult> harmonics = Goertzel.Compute(samples, winner2ndOrder, 8000);

        if ((lowTones[0].Power - harmonics[0].Power) > 10 && (highTones[0].Power - harmonics[1].Power) > 10) return true;
        return false;
    }

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

        //await Clients.Caller.SendAsync("DebugLog", "C# received the audio!");

        //_logger.LogInformation("Received a chunk of {Count} samples", chunk.Length);

        const int N = 256; // target window size @ 8kHz

        // Sliding window with 50% overlap
        if (_accumulationBuffer.Count >= N)
        {
            float[] toProcess = _accumulationBuffer.ToArray();
            _accumulationBuffer.RemoveRange(0, N / 2);

            char? digit = DetectCasioDigit(toProcess);

            // TODO: PAUSE detection algorithm
            //  + minimum/maximum digit duration checks

            if (digit.HasValue)
            {
                await Clients.Caller.SendAsync("DetectedDigit", digit.Value.ToString());
            }
            else
            {
                //_logger.LogInformation("No digit detected!!!");
            }
        }
    }

    internal char? DetectCasioDigit(float[] samples)
    {
        List<GoertzelResult> results = Goertzel.Compute(samples, freqs, 8000);
        _logger.LogInformation("Results: " + results.Count);

        var lowGroup = results.Where(r => rows.Contains(r.Frequency)).OrderByDescending(r => r.Power).First();
        var highGroup = results.Where(r => cols.Contains(r.Frequency)).OrderByDescending(r => r.Power).First();

        double threshold = 2 * Math.Pow((double)samples.Length / 512, 2);

        // both Row + Col need to be above threshold
        if (lowGroup.Power > threshold && highGroup.Power > threshold)
        {
            // Check relative peaks power
            if (!SignalToNoise(results, rows) || !SignalToNoise(results, cols))
            {
                return null;
            }

            // Check Twist
            if (!StandardTwist(results) || !ReverseTwist(results)) return null;

            // Check 2nd order harmonic
            if (!SecondOrderHarmonic(results, samples)) return null;

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