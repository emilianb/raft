using System.Collections.Generic;

using Raft.Components;

namespace Raft.Connections
{
    public class ObjectRaftConnector
        : IRaftConnector
    {
        public ObjectRaftConnector(uint nodeId, RaftNode node)
        {
            NodeId = nodeId;

            Node = node;
        }

        public uint NodeId { get; private set; }

        private RaftNode Node { get; set; }

        public void MakeRequest(string command)
            => Node.MakeRequest(command);

        public Result<bool> RequestVote(uint candidateId, int term, int lastLogIndex, int lastLogTerm)
            => Node.RequestVote(candidateId, term, lastLogIndex, lastLogTerm);

        public Result<bool> AppendEntries(uint leaderId, int term, int prevLogIndex, int prevLogTerm, List<LogEntry> entries, int leaderCommit)
            => Node.AppendEntries(leaderId, term, prevLogIndex, prevLogTerm, entries, leaderCommit);

        public void TestConnection()
            => Node.TestConnection();
    }
}
