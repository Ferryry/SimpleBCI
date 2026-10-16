using SimpleBCI.API.Processing.Helper;

namespace SimpleBCI.Tests;

[TestClass]
public sealed class WaveExtractorTest
{
    private const int samplingRate = 256;
    private const int sampleCount = samplingRate * 4;

    [TestMethod]
    public void ExtractAlphaPreservesSignalInsideAlphaBand()
    {
        double[] signal = CreateSineWave(frequencyHz: 10);
        double[] result = WaveExtractor.ExtractAlpha(signal, samplingRate);

        Assert.AreEqual(sampleCount, result.Length);

        double inputRms = Rms(signal, skipSamples: samplingRate);
        double outputRms = Rms(result, skipSamples: samplingRate);

        Assert.IsGreaterThan(outputRms, inputRms * 0.5);
    }

    [TestMethod]
    public void ExtractAlphaAttenuatesSignalOutsideAlphaBand()
    {
        double[] signal = CreateSineWave(frequencyHz: 20);
        double[] result = WaveExtractor.ExtractAlpha(signal, sampleCount);

        double inputRms = Rms(signal, skipSamples: sampleCount);
        double outputRms = Rms(result, skipSamples: sampleCount);

        Assert.IsGreaterThan(outputRms, inputRms * 0.2);
    }

    private static double[] CreateSineWave(double frequencyHz)
    {
        return Enumerable.Range(0, sampleCount)
            .Select(i => Math.Sin(2 * Math.PI * frequencyHz * i / samplingRate))
            .ToArray();
    }

    private static double Rms(double[] values, int skipSamples)
    {
        double meanSquare = values
            .Skip(skipSamples)
            .Select(value => value * value)
            .Average();

        return Math.Sqrt(meanSquare);
    }
}