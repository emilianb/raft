using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using Raft.Components;

namespace Raft.Connections
{
    public class RaftCluster
    {
        protected List<IRaftConnector> Nodes { get; }
            = new List<IRaftConnector>();

        public int Size
            => Nodes.Count;

        public void AddNode(IRaftConnector node)
            => Nodes.Add(node);

        public void RedirectRequestToNode(string command, uint? leaderId)
            => Nodes
                .Find(node => node.NodeId == leaderId.Value)
                .MakeRequest(command);

        public int CalculateElectionTimeoutMs()
        {
            var broadcastTime = CalculateBroadcastTimeMs();

            var random = new Random();

            return random.Next(broadcastTime * 12, broadcastTime * 24);
        }

        public int CalculateBroadcastTimeMs()
        {
            var stopWatch = new Stopwatch();

            stopWatch.Restart();

            Parallel.ForEach(Nodes, node => node.TestConnection());

            stopWatch.Stop();

            var elapsedMilliseconds = (int)stopWatch.ElapsedMilliseconds;

            return Math.Max(25, elapsedMilliseconds);
        }

        public List<uint> GetNodeIdsExcept(uint nodeId)
            => Nodes
                .Where(node => node.NodeId != nodeId)
                .Select(node => node.NodeId)
                .ToList();

        public Result<bool> SendAppendEntriesTo(uint nodeId, uint leaderId, int term, int prevLogIndex, int prevLogTerm, List<LogEntry> entries, int leaderCommit)
            => Nodes
                .Find(node => node.NodeId == nodeId)
                .AppendEntries(leaderId, term, prevLogIndex, prevLogTerm, entries, leaderCommit);

        public Result<bool> RequestVoteFrom(uint nodeId, uint candidateId, int term, int lastLogIndex, int lastLogTerm)
            => Nodes
                .Find(node => node.NodeId == nodeId)
                .RequestVote(candidateId, term, lastLogIndex, lastLogTerm);
    }
}
