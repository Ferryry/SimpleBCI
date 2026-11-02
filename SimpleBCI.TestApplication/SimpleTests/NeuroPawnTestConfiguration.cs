using SimpleBCI.API;

namespace SimpleBCI.TestApplication.SimpleTests
{
    /// <summary>
    /// Shared AF7/AF8 setup for NeuroPawn feature tests.
    /// </summary>
    internal static class NeuroPawnTestConfiguration
    {
        private const int Gain = 4;

        public static BciClient CreateFrontalClient()
        {
            ChannelInput[] layout =
            {
                new(0, ElectrodePosition.Unspecific),
                new(1, ElectrodePosition.Unspecific),
                new(2, ElectrodePosition.Unspecific),
                new(3, ElectrodePosition.Unspecific),
                new(4, ElectrodePosition.Unspecific),
                new(5, ElectrodePosition.Unspecific),
                new(6, ElectrodePosition.Af7),
                new(7, ElectrodePosition.Af8),
            };

            return new BciClient(
                new SimpleBCIBoardSelection(BoardType.NeuropawnKnightBoard),
                new SimpleBCIParameters(
                    "COM4",
                    null,
                    "{\"gain\": 4}",
                    layout,
                    new NeuroPawnKnightConfiguration(
                    [
                        new(7, Gain),
                        new(8, Gain),
                    ])));
        }
    }
}
