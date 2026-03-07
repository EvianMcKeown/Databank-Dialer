using Microsoft.Extensions.Logging.Abstractions;

public class DetectionTests
{
    private const int SAMPLE_RATE = 8000;
    private const int N = 205; // Window Size

    private float[] GenerateDTMFTone(double freqRow, double freqCol, int sampleRate, int durationMs)
    {
        int sampleCount = (sampleRate * durationMs) / 1000;
        float[] buffer = new float[sampleCount];

        for (int n = 0; n < sampleCount; n++)
        {
            double rowSignal = Math.Sin(2 * Math.PI * freqRow * n / sampleRate);
            double colSignal = Math.Sin(2 * Math.PI * freqCol * n / sampleRate);

            // Normalise level
            buffer[n] = (float)((rowSignal + colSignal) / 2.0);
        }
        return buffer;
    }

    [Theory]
    [InlineData(697, 1209, '1')]
    [InlineData(941, 1336, '0')]
    [InlineData(852, 1477, '9')]
    public void DetectCasioDigit_ShouldIdentifyCorrectDigits(float rowF, float colF, char expected)
    {
        // Arrange
        var hub = new AudioHub(NullLogger<AudioHub>.Instance); // Use NullLogger for tests
        float[] signal = GenerateDTMFTone(rowF, colF, SAMPLE_RATE, N);

        // Act
        char? result = hub.DetectCasioDigit(signal);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DetectCasioDigit_ShouldRejectSingleTones_ViaTwistAndSNR()
    {
        // Arrange: Generate ONLY 941Hz (Row 3), no Column frequency
        var hub = new AudioHub(NullLogger<AudioHub>.Instance);
        float[] signal = new float[N];
        for (int i = 0; i < N; i++)
            signal[i] = MathF.Sin(2 * MathF.PI * 941 * i / SAMPLE_RATE);

        // Act
        char? result = hub.DetectCasioDigit(signal);

        // Assert: Should be null because highGroup.Power will fail threshold/twist
        Assert.Null(result);
    }

    [Fact]
    public void DetectCasioDigit_ShouldHandleNoise_WithDynamicThreshold()
    {
        // Arrange: Digit '5' (770, 1336)
        var hub = new AudioHub(NullLogger<AudioHub>.Instance);
        float[] signal = GenerateDTMFTone(770, 1336, SAMPLE_RATE, N);

        // Add White Noise (approx 10% amplitude)
        Random rand = new Random(42);
        for (int i = 0; i < signal.Length; i++)
            signal[i] += (float)(rand.NextDouble() * 2 - 1) * 0.1f;

        // Act
        char? result = hub.DetectCasioDigit(signal);

        // Assert
        Assert.Equal('5', result);
    }

    [Fact]
    public void Goertzel_Compute_ShouldCalculateCorrectRelativePower()
    {
        // Arrange
        float[] freqs = { 697f, 770f, 852f, 941f };
        float[] signal = new float[N];
        // Pure 770Hz tone
        for (int i = 0; i < N; i++)
            signal[i] = MathF.Sin(2 * MathF.PI * 770 * i / SAMPLE_RATE);

        // Act
        var results = Goertzel.Compute(signal, freqs, SAMPLE_RATE);

        var winner = results.OrderByDescending(r => r.Power).First();
        var runnerUp = results.Where(r => r.Frequency != 770f).OrderByDescending(r => r.Power).First();

        // Assert
        Assert.Equal(770f, winner.Frequency);
        // SNR Check: Winner should be significantly stronger than neighbors at N=205
        Assert.True(winner.Power > runnerUp.Power * 10);
    }
}