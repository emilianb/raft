namespace Raft.StateMachine
{
    public interface IRaftStateMachine
    {
        void Apply(string command);

        string RequestStatus(string param);

        void TestConnection();
    }
}
