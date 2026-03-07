// Implementation of Goertzel Algorithm

public struct GoertzelResult
{
    public float Frequency { get; set; }
    public double Power { get; set; }
}

public class Goertzel
{
    public const int SAMPLE_RATE = 8000;
    public static List<GoertzelResult> Compute(float[] samples, float[] targetFreqs, int sampleRate)
    {
        int N = samples.Length;
        var results = new List<GoertzelResult>(targetFreqs.Length);

        // loop over target frequencies
        foreach (float targetFreq in targetFreqs)
        {
            // Parameterisation
            // k is the real-valued frequency bin
            double k = (double)N * targetFreq / sampleRate;
            double omega = (2.0 * Math.PI * k) / N;
            double cosine = Math.Cos(omega);
            double coeff = 2.0 * cosine;

            // Recursive phase (IIR Filter)
            // DE: s[n] = x[n] + coeff * s[n-1] - s[n-2]
            double s1 = 0; // s[n-1]
            double s2 = 0; // s[n-2]

            for (int i = 0; i < N; i++)
            {
                double s0 = samples[i] + (coeff * s1) - s2;
                s2 = s1;
                s1 = s0;
            }

            // Feed-forward phase
            // |X(k)|^2 = s1^2 + s2^2 - (coeff * s1 * s2)
            double power = (s1 * s1) + (s2 * s2) - (coeff * s1 * s2);

            results.Add(new GoertzelResult{
                Frequency = targetFreq,
                Power = power
            });
        }

        return results;
    }
}