using SimpleBCI.API.Processing.Helper;

namespace SimpleBCI.Tests;

[TestClass]
public sealed class InstantiationTest
{
    [TestMethod]
    public void FocusIndex_ReturnsBetaDividedByAlpha()
    {
        double result = FrequencyCalculator.FocusIndex(alpha: 2, beta: 6);

        Assert.AreEqual(3.0, result);
    }

    [TestMethod]
    public void RelaxationIndex_ReturnsAlphaDividedByBeta()
    {
        double result = FrequencyCalculator.RelaxationIndex(alpha: 6, beta: 2);

        Assert.AreEqual(3.0, result);
    }

    [TestMethod]
    public void ArousalIndex_ReturnsBetaPlusGammaDividedByAlphaPlusTheta()
    {
        double result = FrequencyCalculator.ArousalIndex(
            alpha: 2,
            beta: 6,
            gamma: 4,
            theta: 2);

        Assert.AreEqual(2.5, result);
    }
}