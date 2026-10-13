namespace SimpleBCI.Library.API.Processing.Helper
{
    internal static class FrequencyCalculator
    {
        public static double FocusIndex(double alpha, double beta)
        {
            ValidatePower(alpha, nameof(alpha));
            ValidatePower(beta, nameof(beta));

            return beta / alpha;
        }

        public static double RelaxationIndex(double alpha, double beta)
        {
            ValidatePower(alpha, nameof(alpha));
            ValidatePower(beta, nameof(beta));

            return alpha / beta;
        }

        public static double ArousalIndex(double alpha, double beta, double gamma, double theta)
        {
            ValidatePower(alpha, nameof(alpha));
            ValidatePower(beta, nameof(beta));
            ValidatePower(gamma, nameof(gamma));
            ValidatePower(theta, nameof(theta));

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