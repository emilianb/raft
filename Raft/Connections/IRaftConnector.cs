using System.Collections.Generic;

using Raft.Components;

namespace Raft.Connections
{
    public interface IRaftConnector
    {
        uint NodeId { get; }

        void MakeRequest(string command);

        Result<bool> RequestVote(uint candidateId, int term, int lastLogIndex, int lastLogTerm);

        Result<bool> AppendEntries(uint leaderId, int term, int prevLogIndex, int prevLogTerm, List<LogEntry> entries, int leaderCommit);

        void TestConnection();
    }
}
