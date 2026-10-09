using brainflow;
using Microsoft.VisualStudio.TestTools.UnitTesting.Logging;
using SimpleBCI.API;
using System.Reflection;

namespace SimpleBCI.Tests
{
    [TestClass]
    public sealed class SimpleTests
    {
        SimpleBCI simpleBCI;
        MotorImageryClassifier classifier;
        BoardShim board;
        

        public SimpleTests()
        {
            simpleBCI = new SimpleBCI();

            FieldInfo? field = typeof(SimpleBCI).GetField(
                "board",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

            board = (BoardShim)field!.GetValue(simpleBCI)!;

            classifier = new MotorImageryClassifier(board);

            simpleBCI.StartAPI();

            Task.Delay(TimeSpan.FromSeconds(5));
        }

        [TestMethod]
        public void ReceiveDataFromBoardShinTest()
        {
            Assert.IsNotNull(board);

            if(!board.is_prepared())
            {
                Logger.LogMessage("{0} not connected!", board.ToString());
            }

            Assert.IsGreaterThan<double>(0, board.get_board_data_count());
            Assert.IsGreaterThan<double>(0, board.get_board_data().Length);
        }

        [TestMethod]
        public void MLModelGetModelTest()
        {
            MLModel model = ModelLoader.GetModel();

            Assert.IsNotNull(model);
        }
    }
}
