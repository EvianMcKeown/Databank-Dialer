using Microsoft.AspNetCore.SignalR;

public class AudioHub : Hub
{
    private static readonly float[] rows = [697f, 770f, 852f, 941f];
    private static readonly float[] cols = [1209f, 1336f, 1477f];
    private static readonly float[] freqs = { 697f, 770f, 852f, 941f, 1209f, 1336f, 1477f };

    private readonly ILogger<AudioHub> _logger;

    // Minimum consecutive passing frames before a digit is confirmed.
    // At 136-sample windows with 120-sample slide @ 8kHz → ~15ms per advance.
    // 3 frames ≈ 45ms minimum tone duration. ITU-T Q.24 requires 40ms minimum.
    private const int DIGIT_COUNT_THRESH = 3;

    // Frames of silence required to declare inter-digit gap and re-arm.
    // 2 frames ≈ 30ms.
    private const int PAUSE_COUNT_THRESH = 2;

    // --- Per-connection state ---
    // Keyed by SignalR connection ID so concurrent clients don't corrupt each other.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ConnectionState> _connectionStates = new();

    public enum DetectionState
    {
        Idle,
        Candidate,
        Confirmed,  // digit detected more than THRESH
        Holding,    // digit sustained
        PauseCandidate  // digit dropped, waiting to confirm
    }

    private class ConnectionState
    {
        public List<float> AccumulationBuffer { get; } = new();
        public DetectionState State { get; set; } = DetectionState.Idle;
        public char? LastFrameDigit { get; set; } = null;
        public char? CandidateDigit { get; set; } = null;
        public int ConsecutiveDetections { get; set; } = 0;
        public int ConsecutivePause { get; set; } = 0;
    }

    public AudioHub(ILogger<AudioHub> logger)
    {
        _logger = logger;
    }

    // Allocate state when a client connects.
    public override Task OnConnectedAsync()
    {
        _connectionStates[Context.ConnectionId] = new ConnectionState();
        _logger.LogInformation("Client connected: {Id}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    // Free state when a client disconnects to avoid memory leaks.
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _connectionStates.TryRemove(Context.ConnectionId, out _);
        _logger.LogInformation("Client disconnected: {Id}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    private (List<GoertzelResult>, List<GoertzelResult>) SeperateRowsCols(List<GoertzelResult> results)
    {
        var lowTones = results.Where(r => rows.Contains(r.Frequency)).OrderByDescending(r => r.Power).ToList();
        var highTones = results.Where(r => cols.Contains(r.Frequency)).OrderByDescending(r => r.Power).ToList();
        return (lowTones, highTones);
    }

    private bool StandardTwist(List<GoertzelResult> results)
    {
        /***
        Calculate standard (low tone: high tone) twist ratio & set threshold to acceptable standard twist
        ***/
        var (lowTones, highTones) = SeperateRowsCols(results);
        _logger.LogInformation("Row: {RP} @ {RF} ——— Col: {CP} @ {CF} ——— ReverseTwist: {Ratio} ——— StandardTwist: {SRatio}", Math.Round(lowTones[0].Power, 2), Math.Round(lowTones[0].Frequency, 2), Math.Round(highTones[0].Power, 2), Math.Round(highTones[0].Frequency, 2), Math.Round(lowTones[0].Power / highTones[0].Power, 2), Math.Round(highTones[0].Power / lowTones[0].Power, 2));
        if ((highTones[0].Power / lowTones[0].Power) < 4) return true;
        return false;
    }

    private bool ReverseTwist(List<GoertzelResult> results)
    {
        /***
        Calculate reverse (high tone : low tone) twist ratio & set threshold to acceptable reverse twist
        ***/
        var (lowTones, highTones) = SeperateRowsCols(results);
        if ((lowTones[0].Power / highTones[0].Power) < 8) return true;
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
        float[] winner2ndOrder = { row2nd, col2nd };

        List<GoertzelResult> harmonics = Goertzel.Compute(samples, winner2ndOrder, 8000);

        bool rowCondition = harmonics[0].Power < (lowTones[0].Power * 0.1f);
        bool colCondition = harmonics[1].Power < (highTones[0].Power * 0.1f);

        return rowCondition && colCondition;
    }

    private bool SignalToNoise(List<GoertzelResult> results, float[] groupFreqs)
    {
        var group = results.Where(r => groupFreqs.Contains(r.Frequency)).OrderByDescending(r => r.Power).ToList();

        if (group.Count < 2) return true;

        return group[0].Power > (group[1].Power * 4);
    }

    public async Task UploadAudioChunk(float[] chunk)
    {
        if (!_connectionStates.TryGetValue(Context.ConnectionId, out var state))
        {
            _logger.LogWarning("Received chunk from unknown connection: {Id}", Context.ConnectionId);
            return;
        }

        state.AccumulationBuffer.AddRange(chunk);

        const int N = 136;
        const int SLIDE = N - 16; // 120 - advance by SLIDE to overlap by 16

        // Drain ALL available windows per chunk
        while (state.AccumulationBuffer.Count >= N)
        {
            float[] toProcess = state.AccumulationBuffer.Take(N).ToArray();
            state.AccumulationBuffer.RemoveRange(0, SLIDE);

            char? digit = DetectCasioDigit(toProcess);
            await ProcessDetectionState(digit, state);
        }
    }

    private async Task ProcessDetectionState(char? currentFrameDigit, ConnectionState state)
    {

        switch (state.State)
        {
            case DetectionState.Idle:
            case DetectionState.Candidate:
                await HandleCandidate(currentFrameDigit, state);
                break;

            case DetectionState.Confirmed:
            case DetectionState.Holding:
                await HandleHolding(currentFrameDigit, state);
                break;

            case DetectionState.PauseCandidate:
                await HandlePauseCandidate(currentFrameDigit, state);
                break;
        }
    }

    private Task HandlePauseCandidate(char? currentFrameDigit, ConnectionState state)
    {
        if (currentFrameDigit == state.LastFrameDigit)
        {
            // tone resumed; return to holding
            state.ConsecutivePause = 0;
            state.State = DetectionState.Holding;
            return Task.CompletedTask;
        }

        if (currentFrameDigit != null)
        {
            state.CandidateDigit = currentFrameDigit;
            state.ConsecutiveDetections = 1;
            state.ConsecutivePause = 0;
            state.State = DetectionState.Candidate;
            return Task.CompletedTask;
        }

        // implied 'if (currentFrameDigit == null)'
        state.ConsecutivePause++;

        if (state.ConsecutivePause >= PAUSE_COUNT_THRESH)
        {
            state.State = DetectionState.Idle;
            state.LastFrameDigit = null;
            state.CandidateDigit = null;
            state.ConsecutiveDetections = 0;
            state.ConsecutivePause = 0;
        }

        return Task.CompletedTask;
    }

    private async Task HandleHolding(char? currentFrameDigit, ConnectionState state)
    {
        if (currentFrameDigit == state.LastFrameDigit)
        {
            state.ConsecutivePause = 0;
            state.State = DetectionState.Holding;
            return;
        }

        if (currentFrameDigit != null)
        {
            // _logger.LogInformation("While Holding, digit changed without any Pause, this is likely noise.");
            state.ConsecutivePause++;
            state.State = DetectionState.PauseCandidate;
            return;
        }

        // implied 'if (currentFrameDigit == null)'
        state.ConsecutivePause++;
        state.State = DetectionState.PauseCandidate;
    }

    private async Task HandleCandidate(char? currentFrameDigit, ConnectionState state)
    {
        if (currentFrameDigit == null)
        {
            // only reset if candidate changes
            return;
        }

        if (currentFrameDigit != state.CandidateDigit)
        {
            state.CandidateDigit = currentFrameDigit;
            state.ConsecutiveDetections = 1;
            state.State = DetectionState.Candidate;
        }

        state.ConsecutiveDetections++;

        if (state.ConsecutiveDetections >= DIGIT_COUNT_THRESH)
        {
            state.LastFrameDigit = currentFrameDigit;
            state.ConsecutivePause = 0;
            state.State = DetectionState.Confirmed;
            await Clients.Caller.SendAsync("DetectedDigit", currentFrameDigit.Value.ToString());
            _logger.LogInformation("Confirmed: {D}", currentFrameDigit.Value);
        }
    }

    internal char? DetectCasioDigit(float[] samples)
    {
        List<GoertzelResult> results = Goertzel.Compute(samples, freqs, 8000);
        _logger.LogInformation("Results: " + results.Count);

        var lowGroup = results.Where(r => rows.Contains(r.Frequency)).OrderByDescending(r => r.Power).First();
        var highGroup = results.Where(r => cols.Contains(r.Frequency)).OrderByDescending(r => r.Power).First();

        double threshold = 2 * Math.Pow((double)samples.Length / 512, 2);

        if (lowGroup.Power > threshold && highGroup.Power > threshold)
        {
            if (!SignalToNoise(results, rows) || !SignalToNoise(results, cols))
            {
                return null;
            }

            if (!StandardTwist(results) || !ReverseTwist(results)) return null;

            if (!SecondOrderHarmonic(results, samples)) return null;

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