using System;

namespace Raft.StateMachine
{
    public class NumeralStateMachine
        : IRaftStateMachine
    {
        protected int State { get; set; }
            = 0;

        public void Apply(string command)
        {
            try
            {
                var delta = int.Parse(command);

                State += delta;
            }
            catch (FormatException)
            {
                ; // Don't apply bad requests
            }
            catch (OverflowException)
            {
                ; // Don't apply bad requests
            }
        }

        public string RequestStatus(string param)
            => State.ToString();

        public void TestConnection()
        {
            var testState = 0;

            testState += int.Parse("-1");
        }
    }
}
