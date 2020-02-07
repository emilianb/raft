namespace Raft.Components
{
    public class LogEntry
    {
        public LogEntry(int index, int term, string command)
        {
            Index = index;
            Term = term;
            Command = command;
        }

        public int Index { get; }

        public int Term { get; }

        public string Command { get; }
    }
}
