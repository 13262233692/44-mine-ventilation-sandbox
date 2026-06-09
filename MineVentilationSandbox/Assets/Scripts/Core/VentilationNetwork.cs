using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MineVentilation.Core
{
    [Serializable]
    public class JunctionNode
    {
        public int Id;
        public string Name;
        public Vector3 Position;

        public JunctionNode(int id, string name, Vector3 position)
        {
            Id = id;
            Name = name;
            Position = position;
        }
    }

    [Serializable]
    public class FanData
    {
        public int Id;
        public string Name;
        public float A0;
        public float A1;
        public float A2;

        public FanData(int id, string name, float a0, float a1, float a2)
        {
            Id = id;
            Name = name;
            A0 = a0;
            A1 = a1;
            A2 = a2;
        }

        public float GetPressure(float flow)
        {
            return A0 + A1 * flow + A2 * flow * flow;
        }

        public float GetPressureDerivative(float flow)
        {
            return A1 + 2f * A2 * flow;
        }
    }

    [Serializable]
    public class AirwayEdge
    {
        public int Id;
        public string Name;
        public int FromNodeId;
        public int ToNodeId;
        public float Resistance;
        public float Length;
        public float CrossSectionArea;
        public float Perimeter;
        public float InitialFlowGuess;
        public float SolvedFlow;
        public int FanId;
        public float FixedPressure;

        private float _currentFlow;

        public float CurrentFlow
        {
            get => _currentFlow;
            set => _currentFlow = value;
        }

        public AirwayEdge(int id, string name, int fromNodeId, int toNodeId,
            float resistance, float length = 0f, float crossSectionArea = 0f,
            float perimeter = 0f, float initialFlowGuess = 0f, int fanId = -1,
            float fixedPressure = 0f)
        {
            Id = id;
            Name = name;
            FromNodeId = fromNodeId;
            ToNodeId = toNodeId;
            Resistance = resistance;
            Length = length;
            CrossSectionArea = crossSectionArea;
            Perimeter = perimeter;
            InitialFlowGuess = initialFlowGuess;
            FanId = fanId;
            FixedPressure = fixedPressure;
            _currentFlow = initialFlowGuess;
            SolvedFlow = 0f;
        }

        public void ResetFlow()
        {
            _currentFlow = InitialFlowGuess;
            SolvedFlow = 0f;
        }

        public float GetFrictionPressureDrop()
        {
            return Resistance * _currentFlow * Math.Abs(_currentFlow);
        }
    }

    [Serializable]
    public class VentilationLoop
    {
        public int Id;
        public List<int> EdgeIds = new List<int>();
        public List<int> Signs = new List<int>();

        public VentilationLoop(int id)
        {
            Id = id;
        }

        public void AddEdge(int edgeId, int sign)
        {
            EdgeIds.Add(edgeId);
            Signs.Add(sign);
        }
    }

    [Serializable]
    public class SolverResult
    {
        public bool Converged;
        public int Iterations;
        public float MaxResidual;
        public List<float> BranchFlows = new List<float>();
        public List<float> BranchPressures = new List<float>();
        public string Diagnostics;
    }

    public class VentilationNetwork
    {
        public List<JunctionNode> Nodes = new List<JunctionNode>();
        public List<AirwayEdge> Edges = new List<AirwayEdge>();
        public List<FanData> Fans = new List<FanData>();
        public List<VentilationLoop> Loops = new List<VentilationLoop>();

        private Dictionary<int, JunctionNode> _nodeMap = new Dictionary<int, JunctionNode>();
        private Dictionary<int, AirwayEdge> _edgeMap = new Dictionary<int, AirwayEdge>();
        private Dictionary<int, FanData> _fanMap = new Dictionary<int, FanData>();
        private Dictionary<int, List<int>> _adjacency = new Dictionary<int, List<int>>();

        public JunctionNode AddNode(int id, string name, Vector3 position)
        {
            var node = new JunctionNode(id, name, position);
            Nodes.Add(node);
            _nodeMap[id] = node;
            _adjacency[id] = new List<int>();
            return node;
        }

        public AirwayEdge AddEdge(int id, string name, int fromNodeId, int toNodeId,
            float resistance, float length = 0f, float crossSectionArea = 0f,
            float perimeter = 0f, float initialFlowGuess = 0f, int fanId = -1,
            float fixedPressure = 0f)
        {
            var edge = new AirwayEdge(id, name, fromNodeId, toNodeId, resistance,
                length, crossSectionArea, perimeter, initialFlowGuess, fanId, fixedPressure);
            Edges.Add(edge);
            _edgeMap[id] = edge;
            _adjacency[fromNodeId].Add(id);
            _adjacency[toNodeId].Add(id);
            return edge;
        }

        public FanData AddFan(int id, string name, float a0, float a1, float a2)
        {
            var fan = new FanData(id, name, a0, a1, a2);
            Fans.Add(fan);
            _fanMap[id] = fan;
            return fan;
        }

        public JunctionNode GetNode(int id) => _nodeMap.TryGetValue(id, out var n) ? n : null;
        public AirwayEdge GetEdge(int id) => _edgeMap.TryGetValue(id, out var e) ? e : null;
        public FanData GetFan(int id) => _fanMap.TryGetValue(id, out var f) ? f : null;

        public int GetEdgeSignInDirection(int edgeId, int fromNodeId)
        {
            var edge = GetEdge(edgeId);
            if (edge == null) return 0;
            return edge.FromNodeId == fromNodeId ? 1 : -1;
        }

        public void FindIndependentLoops()
        {
            Loops.Clear();

            if (Nodes.Count == 0 || Edges.Count == 0) return;

            var inTree = new HashSet<int>();
            var treeEdges = new HashSet<int>();
            var nodeIds = Nodes.Select(n => n.Id).ToList();
            var rootNode = nodeIds[0];

            inTree.Add(rootNode);
            var queue = new Queue<int>();
            queue.Enqueue(rootNode);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!_adjacency.ContainsKey(current)) continue;

                foreach (var edgeId in _adjacency[current])
                {
                    var edge = GetEdge(edgeId);
                    if (edge == null || treeEdges.Contains(edgeId)) continue;

                    var neighbor = edge.FromNodeId == current ? edge.ToNodeId : edge.FromNodeId;
                    if (inTree.Contains(neighbor)) continue;

                    inTree.Add(neighbor);
                    treeEdges.Add(edgeId);
                    queue.Enqueue(neighbor);
                }
            }

            foreach (var nodeId in nodeIds)
            {
                if (!inTree.Contains(nodeId))
                {
                    inTree.Add(nodeId);
                    treeEdges.Add(-1);
                }
            }

            var chordEdges = Edges.Where(e => !treeEdges.Contains(e.Id)).ToList();
            var parent = new Dictionary<int, int>();
            var parentEdge = new Dictionary<int, int>();

            foreach (var nid in nodeIds)
            {
                parent[nid] = -1;
                parentEdge[nid] = -1;
            }

            var visited = new HashSet<int> { rootNode };
            var bfsQueue = new Queue<int>();
            bfsQueue.Enqueue(rootNode);

            while (bfsQueue.Count > 0)
            {
                var current = bfsQueue.Dequeue();
                if (!_adjacency.ContainsKey(current)) continue;

                foreach (var edgeId in _adjacency[current])
                {
                    if (!treeEdges.Contains(edgeId)) continue;
                    var edge = GetEdge(edgeId);
                    if (edge == null) continue;

                    var neighbor = edge.FromNodeId == current ? edge.ToNodeId : edge.FromNodeId;
                    if (visited.Contains(neighbor)) continue;

                    visited.Add(neighbor);
                    parent[neighbor] = current;
                    parentEdge[neighbor] = edgeId;
                    bfsQueue.Enqueue(neighbor);
                }
            }

            foreach (var unvisited in nodeIds.Where(n => !visited.Contains(n)))
            {
                visited.Add(unvisited);
            }

            for (int i = 0; i < chordEdges.Count; i++)
            {
                var chord = chordEdges[i];
                var loop = new VentilationLoop(i);
                var pathFromA = new List<int>();
                var pathEdgesFromA = new List<int>();
                var pathFromB = new List<int>();
                var pathEdgesFromB = new List<int>();

                int nodeA = chord.FromNodeId;
                int nodeB = chord.ToNodeId;

                var ancestorsA = new HashSet<int>();
                int temp = nodeA;
                while (temp != -1)
                {
                    ancestorsA.Add(temp);
                    temp = parent[temp];
                }

                int lca = nodeB;
                while (lca != -1 && !ancestorsA.Contains(lca))
                {
                    pathFromB.Add(lca);
                    pathEdgesFromB.Add(parentEdge[lca]);
                    lca = parent[lca];
                }

                temp = nodeA;
                while (temp != lca)
                {
                    pathFromA.Add(temp);
                    pathEdgesFromA.Add(parentEdge[temp]);
                    temp = parent[temp];
                }

                loop.AddEdge(chord.Id, 1);

                int prevNode = chord.ToNodeId;
                foreach (var eId in pathEdgesFromB)
                {
                    var e = GetEdge(eId);
                    int sign = e.FromNodeId == prevNode ? 1 : -1;
                    loop.AddEdge(eId, sign);
                    prevNode = sign == 1 ? e.ToNodeId : e.FromNodeId;
                }

                pathEdgesFromA.Reverse();
                var reversedPathA = pathFromA.ToList();
                reversedPathA.Reverse();
                reversedPathA.Add(lca);

                for (int j = 0; j < pathEdgesFromA.Count; j++)
                {
                    var e = GetEdge(pathEdgesFromA[j]);
                    int nextNode = reversedPathA[j + 1];
                    int sign = e.FromNodeId == nextNode ? -1 : 1;
                    loop.AddEdge(pathEdgesFromA[j], sign);
                }

                Loops.Add(loop);
            }
        }

        public void ResetAllFlows()
        {
            foreach (var edge in Edges)
            {
                edge.ResetFlow();
            }
        }

        public void EnforceKCL()
        {
            if (Nodes.Count < 2 || Edges.Count == 0) return;

            var outgoingEdges = new Dictionary<int, List<int>>();
            var incomingEdges = new Dictionary<int, List<int>>();

            foreach (var node in Nodes)
            {
                outgoingEdges[node.Id] = new List<int>();
                incomingEdges[node.Id] = new List<int>();
            }

            foreach (var edge in Edges)
            {
                outgoingEdges[edge.FromNodeId].Add(edge.Id);
                incomingEdges[edge.ToNodeId].Add(edge.Id);
            }

            var sortedNodes = new List<int>();
            var inDegree = new Dictionary<int, int>();

            foreach (var node in Nodes)
            {
                inDegree[node.Id] = incomingEdges[node.Id].Count;
            }

            var queue = new Queue<int>();
            foreach (var node in Nodes)
            {
                if (inDegree[node.Id] == 0)
                {
                    queue.Enqueue(node.Id);
                }
            }

            while (queue.Count > 0)
            {
                var nodeId = queue.Dequeue();
                sortedNodes.Add(nodeId);

                foreach (var edgeId in outgoingEdges[nodeId])
                {
                    var edge = GetEdge(edgeId);
                    if (edge == null) continue;
                    inDegree[edge.ToNodeId]--;
                    if (inDegree[edge.ToNodeId] == 0)
                    {
                        queue.Enqueue(edge.ToNodeId);
                    }
                }
            }

            foreach (var nodeId in sortedNodes)
            {
                float outflow = 0f;
                bool hasOutflow = false;

                foreach (var edgeId in outgoingEdges[nodeId])
                {
                    var edge = GetEdge(edgeId);
                    if (edge == null) continue;
                    if (edge.CurrentFlow > 0)
                    {
                        outflow += edge.CurrentFlow;
                        hasOutflow = true;
                    }
                }

                float inflow = 0f;
                bool hasInflow = false;

                foreach (var edgeId in incomingEdges[nodeId])
                {
                    var edge = GetEdge(edgeId);
                    if (edge == null) continue;
                    if (edge.CurrentFlow > 0)
                    {
                        inflow += edge.CurrentFlow;
                        hasInflow = true;
                    }
                }

                if (!hasInflow && hasOutflow) continue;
                if (!hasOutflow) continue;

                if (Math.Abs(inflow - outflow) > 0.001f && inflow > 0.001f)
                {
                    float scale = inflow / outflow;
                    foreach (var edgeId in outgoingEdges[nodeId])
                    {
                        var edge = GetEdge(edgeId);
                        if (edge == null || edge.CurrentFlow <= 0) continue;
                        edge.CurrentFlow *= scale;
                    }
                }
            }
        }

        public bool ValidateNetwork(out string message)
        {
            message = "";

            if (Nodes.Count < 2)
            {
                message = "网络至少需要2个节点";
                return false;
            }

            if (Edges.Count < Nodes.Count - 1)
            {
                message = "边数不足以形成连通图";
                return false;
            }

            foreach (var edge in Edges)
            {
                if (GetNode(edge.FromNodeId) == null || GetNode(edge.ToNodeId) == null)
                {
                    message = $"巷道 {edge.Name} 引用了不存在的节点";
                    return false;
                }
                if (edge.Resistance < 0)
                {
                    message = $"巷道 {edge.Name} 风阻不能为负";
                    return false;
                }
                if (edge.FanId >= 0 && GetFan(edge.FanId) == null)
                {
                    message = $"巷道 {edge.Name} 引用了不存在的风机";
                    return false;
                }
            }

            return true;
        }
    }
}
