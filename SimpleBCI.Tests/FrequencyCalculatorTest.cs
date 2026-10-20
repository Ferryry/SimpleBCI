using SimpleBCI.API.Processing.Helper;

namespace SimpleBCI.Tests;

[TestClass]
public sealed class FrequencyCalculatorTest
{
    [TestMethod]
    public void FocusIndexReturnsBetaDividedByAlpha()
    {
        
    }

    [TestMethod]
    public void RelaxationIndexReturnsAlphaDividedByBeta()
    {
        double result = FrequencyCalculator.RelaxationIndex(alpha: 6, beta: 2);

        Assert.AreEqual(3.0, result);
    }

    [TestMethod]
    public void ArousalIndexReturnsBetaPlusGammaDividedByAlphaPlusTheta()
    {
        double result = FrequencyCalculator.ArousalIndex(
            alpha: 2,
            beta: 6,
            gamma: 4,
            theta: 2);

        Assert.AreEqual(2.5, result);
    }
}