using System;
using brainflow;

namespace SimpleBCI.API
{
    internal class EEGFeatureExtractor
    {
        public const int ChannelCount = 4;
        public const int SamplingRate = 256;
        public const int WindowSize = 512;
        public const int FeatureCount = 16;

        public double[] ExtractFeatures(double[,] eegWindow)
        {
            if (eegWindow.GetLength(0) != ChannelCount ||
                eegWindow.GetLength(1) != WindowSize)
            {
                throw new ArgumentException(
                    "Expected 4 EEG channels with 512 samples each.");
            }

            // TODO:
            // 1. Jeden Kanal verarbeiten.
            // 2. Welch-PSD wie im Python-Script berechnen.
            // 3. Theta-, Alpha-, Beta- und Gamma-Bandpower bestimmen.
            // 4. Pro Band log10(max(power, 1e-12)) berechnen.
            // 5. Die 16 Features in derselben Reihenfolge
            //    wie beim Python-Training zurückgeben.

            throw new NotImplementedException();
        }
    }
}
