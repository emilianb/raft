using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Raft.Components;
using Raft.Connections;
using Raft.StateMachine;

namespace Raft
{
    public class RaftNode
    {
        public RaftNode(uint nodeId, IRaftStateMachine stateMachine)
        {
            NodeId = nodeId;
            StateMachine = stateMachine;
            Log = new List<LogEntry>();

            electionTimer = new Timer(TriggerElection);
            heartbeatTimer = new Timer(SendHeartbeats);

            NextIndex = new Dictionary<uint, int>();
            MatchIndex = new Dictionary<uint, int>();
        }

        public uint NodeId { get; }

        public IRaftStateMachine StateMachine { get; private set; }

        public RaftCluster Cluster { get; private set; }

        public List<LogEntry> Log { get; private set; }

        public NodeState NodeState { get; private set; }
            = NodeState.Stopped;

        public uint? LeaderId { get; private set; }
            = null;

        public uint? VotedFor { get; private set; }
            = null;

        public int CommitIndex { get; private set; }
            = -1;

        public int LastApplied { get; private set; }
            = -1;

        public int ElectionTimeoutMs { get; private set; }

        private Timer electionTimer;

        private Timer heartbeatTimer;

        public Dictionary<uint, int> NextIndex { get; }

        public Dictionary<uint, int> MatchIndex { get; }

        private int currentTerm = 0;

        public int CurrentTerm
        {
            get => currentTerm;

            private set
            {
                if (value > currentTerm)
                {
                    currentTerm = value;

                    LeaderId = null;

                    VotedFor = null;

                    NodeState = NodeState.Follower;
                }
            }
        }

        public void Configure(RaftCluster cluster)
        {
            Cluster = cluster;

            ElectionTimeoutMs = Cluster.CalculateElectionTimeoutMs();

            NodeState = NodeState.Follower;
        }

        public void Run()
        {
            if (Cluster == null)
            {
                throw new InvalidOperationException("You must configure the cluster before you run it.");
            }

            switch (NodeState)
            {
                case NodeState.Candidate:
                    StopHeartbeatTimer();

                    ResetElectionTimer();

                    StartElection();

                    break;
                case NodeState.Leader:
                    StopElectionTimer();

                    ResetHeartbeatTimer();

                    ResetLeaderState();

                    break;
                case NodeState.Follower:
                    StopHeartbeatTimer();

                    ResetElectionTimer();

                    break;
                case NodeState.Stopped:
                    StopHeartbeatTimer();

                    StopElectionTimer();

                    break;
            }
        }

        public Result<bool> AppendEntries(uint leaderId, int term, int prevLogIndex, int prevLogTerm, List<LogEntry> entries, int leaderCommit)
        {
            if (NodeState == NodeState.Stopped)
                return new Result<bool>(false, CurrentTerm);

            if (term < CurrentTerm)
            {
                LogMessage("Received AppendEntries with outdated term. Declining.");

                return new Result<bool>(false, CurrentTerm);
            }

            if (entries != null && Log.Count > 0 && Log[prevLogIndex].Term != prevLogTerm)
                return new Result<bool>(false, CurrentTerm);

            StopHeartbeatTimer();

            ResetElectionTimer();

            CurrentTerm = term;

            NodeState = NodeState.Follower;

            LeaderId = leaderId;

            if (entries != null)
            {
                Log = Log.Take(entries[0].Index).ToList();

                Log.AddRange(entries);

                LogMessage($"Node {NodeId} appending new entry {entries[0].Command}");
            }
            else
            {
                LogMessage($"Node {NodeId} received heartbeat from {leaderId}");
            }

            if (leaderCommit > CommitIndex)
            {
                LogMessage($"Node {NodeId} applying entries");

                var toApply = Log.Skip(CommitIndex + 1).Take(leaderCommit - CommitIndex).ToList();

                if (toApply.Count == 0)
                {
                    LogMessage($"Node {NodeId} failed applying entries");

                    return new Result<bool>(false, CurrentTerm);
                }

                toApply.ForEach(x => StateMachine.Apply(x.Command));

                CommitIndex = Math.Min(leaderCommit, Log[Log.Count - 1].Index);

                LastApplied = CommitIndex;
            }

            return new Result<bool>(true, CurrentTerm);
        }

        public Result<bool> RequestVote(uint candidateId, int term, int lastLogIndex, int lastLogTerm)
        {
            if (NodeState == NodeState.Stopped)
                return new Result<bool>(false, CurrentTerm);

            LogMessage($"Node {candidateId} is requesting vote from node {NodeId}");

            var voteGranted = false;

            if (term < CurrentTerm)
                return new Result<bool>(voteGranted, CurrentTerm);

            StopHeartbeatTimer();

            ResetElectionTimer();

            CurrentTerm = term;

            if ((VotedFor == null || VotedFor == candidateId)
                && lastLogIndex >= Log.Count - 1
                && lastLogTerm >= GetLastLogTerm())
            {
                voteGranted = true;
            }

            if (voteGranted)
            {
                VotedFor = candidateId;
            }

            return new Result<bool>(voteGranted, CurrentTerm);
        }

        public void MakeRequest(string command)
        {
            if (NodeState == NodeState.Leader)
            {
                LogMessage("This node is the leader");

                var entry = new LogEntry(CurrentTerm, Log.Count, command);

                Log.Add(entry);
            }
            else if (NodeState == NodeState.Follower && LeaderId.HasValue)
            {
                LogMessage($"Redirecting to leader {LeaderId} by {NodeId}");

                Cluster.RedirectRequestToNode(command, LeaderId);
            }
            else
            {
                LogMessage("Couldn't find a leader. Dropping request.");
            }
        }

        public List<LogEntry> GetCommittedEntries()
            => Log.Take(CommitIndex + 1).ToList();

        private void StartElection()
        {
            CurrentTerm++;

            var voteCount = 1;

            VotedFor = NodeId;

            LogMessage($"A node has started an election: {NodeId} (term {CurrentTerm})");

            var nodes = Cluster.GetNodeIdsExcept(NodeId);

            var votes = 0;

            Parallel.ForEach(nodes, nodeId =>
            {
                var result = Cluster.RequestVoteFrom(nodeId, NodeId, CurrentTerm, Log.Count - 1, GetLastLogTerm());

                CurrentTerm = result.Term;

                if (result.Value) Interlocked.Increment(ref votes);
            });

            voteCount += votes;

            if (voteCount >= GetMajority())
            {
                LogMessage($"New leader!! : {NodeId} with {voteCount} votes");

                LeaderId = NodeId;

                NodeState = NodeState.Leader;

                Run();
            }
        }

        private void ResetLeaderState()
        {
            NextIndex.Clear();

            MatchIndex.Clear();

            Cluster.GetNodeIdsExcept(NodeId).ForEach(x => {
                NextIndex[x] = Log.Count;
                MatchIndex[x] = 0;
            });
        }

        public void Restart()
        {
            if (NodeState == NodeState.Stopped)
            {
                LogMessage($"Restarting node {NodeId}");

                NodeState = NodeState.Follower;

                Run();
            }
        }

        public void Stop()
        {
            if (NodeState != NodeState.Stopped)
            {
                LogMessage("Bringing node " + NodeId + " down");

                NodeState = NodeState.Stopped;

                Run();
            }
        }

        private int GetMajority()
        {
            double n = (Cluster.Size + 1) / 2;

            return (int)Math.Ceiling(n);
        }

        private void TriggerElection(object arg)
        {
            NodeState = NodeState.Candidate;
#if (SIM)
            Thread.Sleep(150);
#endif
            Run();
        }

        private void StopHeartbeatTimer()
            => heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);

        private void StopElectionTimer()
            =>  electionTimer.Change(Timeout.Infinite, Timeout.Infinite);

        private void ResetElectionTimer()
        {
            if (NodeState != NodeState.Leader)
                electionTimer.Change(ElectionTimeoutMs, ElectionTimeoutMs);
        }

        private void ResetHeartbeatTimer()
        {
            if (NodeState == NodeState.Leader)
            {
                heartbeatTimer.Change(0, ElectionTimeoutMs / 2);
            }
        }

        private int GetLastLogTerm()
            => (Log.Count > 0) ? Log[Log.Count - 1].Term : 0;

        private void SendHeartbeats(object arg)
        {
            var nodes = Cluster.GetNodeIdsExcept(NodeId);

            Parallel.ForEach(nodes, nodeId =>
            {
                if (!NextIndex.ContainsKey(nodeId)) return;

                var prevLogIndex = Math.Max(0, NextIndex[nodeId] - 1);

                int prevLogTerm = (Log.Count > 0) ? prevLogTerm = Log[prevLogIndex].Term : 0;

                List<LogEntry> entries;

                if (Log.Count > NextIndex[nodeId])
                {
                    LogMessage($"Log Count: {Log.Count} -- Target node[nextIndex]: {nodeId} [{NextIndex[nodeId]}]");

                    entries = Log.Skip(NextIndex[nodeId]).ToList();
                }
                else
                {
                    entries = null;
                }

                var res = Cluster.SendAppendEntriesTo(nodeId, NodeId, CurrentTerm, prevLogIndex, prevLogTerm, entries, CommitIndex);

                CurrentTerm = res.Term;

                if (res.Value)
                {
                    if (entries != null)
                    {
                        LogMessage($"Successful AE to {nodeId}. Setting nextIndex to {NextIndex[nodeId]}");

                        NextIndex[nodeId] = Log.Count;
                        MatchIndex[nodeId] = Log.Count - 1;
                    }
                }
                else
                {
                    LogMessage($"Failed AE to {nodeId}. Setting nextIndex to {NextIndex[nodeId]}");

                    if (NextIndex[nodeId] > 0)
                    {
                        NextIndex[nodeId]--;
                    }
                }
            });

            for (var i = CommitIndex + 1; i < Log.Count; i++)
            {
                var replicatedIn = MatchIndex.Values.Count(x => x >= i) + 1;

                if (Log[i].Term == CurrentTerm && replicatedIn >= GetMajority())
                {
                    CommitIndex = i;

                    StateMachine.Apply(Log[i].Command);

                    LastApplied = i;
                }
            }
        }

        internal void TestConnection()
            => StateMachine.TestConnection();

        public override string ToString()
        {
            string state;

            if (NodeState == NodeState.Follower)
                state = $"Follower (of {LeaderId})";
            else
                state = NodeState.ToString();

            return $"Node ({NodeId}) -- {state}";
        }

        private void LogMessage(string msg)
        {
#if (DEBUG)
            Console.WriteLine(msg);
#endif
        }
    }
}
