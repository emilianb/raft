using System;
using System.Collections.Generic;

namespace Raft.StateMachine
{
    public class DictionaryStateMachine
        : IRaftStateMachine
    {
        protected Dictionary<string, int> State { get; set; }
            = new Dictionary<string, int>();

        public void Apply(string command)
        {
            var commands = command.Split(" ");

            try
            {
                switch (commands[0].ToUpper())
                {
                    case "SET":
                        State[commands[1]] = int.Parse(commands[2]);

                        break;
                    case "CLEAR":
                        if (State.ContainsKey(commands[1]))
                            State.Remove(commands[1]);

                        break;
                    default:
                        break;
                }
            }
            catch (FormatException)
            {
                ; // Don't apply bad requests
            }
        }

        public string RequestStatus(string param)
        {
            if (State.ContainsKey(param))
            {
                return State[param].ToString();
            }
            else
            {
                return string.Empty;
            }
        }

        public void TestConnection()
        {
            var testState = new Dictionary<string, int>();

            testState["X"] = int.Parse("0");

            testState.Clear();
        }
    }
}
