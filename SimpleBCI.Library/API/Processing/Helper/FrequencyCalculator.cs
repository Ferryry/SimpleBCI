namespace SimpleBCI.API.Processing.Helper
{
    internal static class FrequencyCalculator
    {
        public static double FocusIndex(double alpha, double beta)
        {
            ValidatePower(alpha, nameof(alpha));
            ValidatePower(beta, nameof(beta));

            if (alpha == 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(alpha),
                    "Alpha power must be greater than zero to calculate the focus index.");
            }

            return beta / alpha;
        }

        public static double RelaxationIndex(double alpha, double beta)
        {
            ValidatePower(alpha, nameof(alpha));
            ValidatePower(beta, nameof(beta));

            if (beta == 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(beta),
                    "Beta power must be greater than zero to calculate the relaxation index.");
            }

            return alpha / beta;
        }

        public static double ArousalIndex(double alpha, double beta, double gamma, double theta)
        {
            ValidatePower(alpha, nameof(alpha));
            ValidatePower(beta, nameof(beta));
            ValidatePower(gamma, nameof(gamma));
            ValidatePower(theta, nameof(theta));

            if (alpha + theta == 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(alpha),
                    "Alpha plus theta power must be greater than zero to calculate the arousal index.");
            }

            return (beta + gamma) / (alpha + theta);
        }

        private static void ValidatePower(double power, string parameterName)
        {
            if (!double.IsFinite(power) || power < 0)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Band power must be finite and non-negative."
                );
            }
        }
    }
}
