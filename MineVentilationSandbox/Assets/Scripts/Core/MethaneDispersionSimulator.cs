using System;
using System.Collections.Generic;
using UnityEngine;

namespace MineVentilation.Core
{
    public class MethaneDispersionSimulator
    {
        public float DiffusionCoefficient = 0.22f;
        public float ExplosionLimit = 0.05f;
        public float WarningLimit = 0.01f;
        public float CellSize = 5f;
        public int MaxCellsPerEdge = 60;
        public float SimulationSpeed = 1.0f;
        public int SubSteps = 3;
        public float MaxStableDT = 0.5f;
        public float OutburstEmissionRate = 0.15f;
        public float OutburstDuration = 30f;
        public float BackgroundDecay = 0.0001f;

        private VentilationNetwork _network;
        private MethaneSimulationState _state;
        private Dictionary<int, List<int>> _nodeOutgoingEdges;
        private Dictionary<int, List<int>> _nodeIncomingEdges;

        public MethaneSimulationState State => _state;
        public bool IsSimulating => _state != null && _state.ActiveSources.Count > 0;
        public float MaxConcentration => _state?.MaxGlobalConcentration ?? 0f;

        public MethaneDispersionSimulator(VentilationNetwork network)
        {
            _network = network;
            _state = new MethaneSimulationState();
            BuildNodeEdgeMapping();
        }

        void BuildNodeEdgeMapping()
        {
            _nodeOutgoingEdges = new Dictionary<int, List<int>>();
            _nodeIncomingEdges = new Dictionary<int, List<int>>();

            foreach (var node in _network.Nodes)
            {
                _nodeOutgoingEdges[node.Id] = new List<int>();
                _nodeIncomingEdges[node.Id] = new List<int>();
            }

            foreach (var edge in _network.Edges)
            {
                _nodeOutgoingEdges[edge.FromNodeId].Add(edge.Id);
                _nodeIncomingEdges[edge.ToNodeId].Add(edge.Id);
            }
        }

        public void InitializeDiscretization()
        {
            _state.EdgeCells.Clear();

            foreach (var edge in _network.Edges)
            {
                int cellCount = Mathf.CeilToInt(edge.Length / CellSize);
                cellCount = Mathf.Clamp(cellCount, 4, MaxCellsPerEdge);

                float cellLength = edge.Length / cellCount;
                float flow = edge.SolvedFlow;
                float velocity = edge.CrossSectionArea > 0.001f ? flow / edge.CrossSectionArea : 0f;

                var disc = new EdgeDiscretization(
                    edge.Id, cellCount, cellLength,
                    velocity, edge.CrossSectionArea, edge.Length,
                    edge.FromNodeId, edge.ToNodeId
                );

                _state.EdgeCells[edge.Id] = disc;
            }

            foreach (var node in _network.Nodes)
            {
                _state.JunctionConcentrations[node.Id] = 0f;
            }
        }

        public void UpdateWindVelocities()
        {
            foreach (var edge in _network.Edges)
            {
                if (!_state.EdgeCells.TryGetValue(edge.Id, out var disc)) continue;

                float flow = edge.SolvedFlow;
                float area = Mathf.Max(edge.CrossSectionArea, 0.1f);
                float velocity = flow / area;
                disc.WindVelocity = velocity;
            }
        }

        public void TriggerOutburst(int nodeId, float emissionRate = -1f, float duration = -1f)
        {
            if (emissionRate < 0) emissionRate = OutburstEmissionRate;
            if (duration < 0) duration = OutburstDuration;

            var outEdges = _nodeOutgoingEdges != null && _nodeOutgoingEdges.ContainsKey(nodeId)
                ? _nodeOutgoingEdges[nodeId] : null;
            var inEdges = _nodeIncomingEdges != null && _nodeIncomingEdges.ContainsKey(nodeId)
                ? _nodeIncomingEdges[nodeId] : null;

            bool sourceAdded = false;

            if (outEdges != null)
            {
                foreach (var edgeId in outEdges)
                {
                    var edge = _network.GetEdge(edgeId);
                    if (edge == null) continue;

                    float flow = edge.SolvedFlow;
                    if (flow > 0.1f)
                    {
                        var source = new MethaneSource(nodeId, edgeId, true, emissionRate, duration);
                        _state.ActiveSources.Add(source);
                        sourceAdded = true;
                        Debug.Log($"[瓦斯弥散] 突出源: 节点{nodeId}→巷道'{edge.Name}'(正向), 排放{emissionRate}/s, 持续{duration}s");
                    }
                }
            }

            if (inEdges != null)
            {
                foreach (var edgeId in inEdges)
                {
                    var edge = _network.GetEdge(edgeId);
                    if (edge == null) continue;

                    float flow = edge.SolvedFlow;
                    if (flow < -0.1f)
                    {
                        var source = new MethaneSource(nodeId, edgeId, false, emissionRate, duration);
                        _state.ActiveSources.Add(source);
                        sourceAdded = true;
                        Debug.Log($"[瓦斯弥散] 突出源: 节点{nodeId}→巷道'{edge.Name}'(逆向), 排放{emissionRate}/s, 持续{duration}s");
                    }
                }
            }

            if (!sourceAdded)
            {
                if (outEdges != null && outEdges.Count > 0)
                {
                    var edgeId = outEdges[0];
                    var source = new MethaneSource(nodeId, edgeId, true, emissionRate * 0.5f, duration);
                    _state.ActiveSources.Add(source);
                    Debug.Log($"[瓦斯弥散] 突出源(低风): 节点{nodeId}→巷道{edgeId}");
                }
                else if (inEdges != null && inEdges.Count > 0)
                {
                    var edgeId = inEdges[0];
                    var source = new MethaneSource(nodeId, edgeId, false, emissionRate * 0.5f, duration);
                    _state.ActiveSources.Add(source);
                    Debug.Log($"[瓦斯弥散] 突出源(低风): 节点{nodeId}←巷道{edgeId}");
                }
            }

            _state.JunctionConcentrations[nodeId] = Mathf.Clamp01(
                _state.JunctionConcentrations.GetValueOrDefault(nodeId, 0f) + emissionRate * 2f);
        }

        public void StepSimulation(float deltaTime)
        {
            float scaledDT = deltaTime * SimulationSpeed;
            float subDT = scaledDT / SubSteps;

            for (int sub = 0; sub < SubSteps; sub++)
            {
                float dt = Mathf.Min(subDT, MaxStableDT);
                ApplySources(dt);
                SolveConvectionDiffusion(dt);
                ApplyJunctionMixing();
                ApplyBackgroundDecay(dt);
                DetectAlarmEdges();
            }

            UpdateExpiredSources(deltaTime);
            _state.SimulationTime += scaledDT;
            UpdateMaxGlobalConcentration();
        }

        void ApplySources(float dt)
        {
            foreach (var source in _state.ActiveSources)
            {
                if (!source.Active) continue;

                if (!_state.EdgeCells.TryGetValue(source.EdgeId, out var disc)) continue;

                float amount = source.EmissionRate * dt;
                if (source.AtEdgeStart)
                {
                    disc.InjectSourceAtStart(amount);
                }
                else
                {
                    disc.InjectSourceAtEnd(amount);
                }
                source.TotalMassReleased += amount;
            }
        }

        void SolveConvectionDiffusion(float dt)
        {
            foreach (var kvp in _state.EdgeCells)
            {
                var disc = kvp.Value;
                disc.SwapBuffers();

                float v = disc.WindVelocity;
                float D = DiffusionCoefficient;
                float dx = disc.CellLength;

                float courant = Mathf.Abs(v) * dt / dx;
                float diffNum = D * dt / (dx * dx);

                if (diffNum > 0.5f)
                {
                    dt = 0.45f * dx * dx / D;
                    diffNum = D * dt / (dx * dx);
                }

                float cUpJunction = _state.JunctionConcentrations.ContainsKey(disc.FromNodeId)
                    ? _state.JunctionConcentrations[disc.FromNodeId] : 0f;
                float cDownJunction = _state.JunctionConcentrations.ContainsKey(disc.ToNodeId)
                    ? _state.JunctionConcentrations[disc.ToNodeId] : 0f;

                for (int i = 0; i < disc.CellCount; i++)
                {
                    float cI = disc.ConcentrationPrev[i];

                    float cLeft;
                    if (i == 0)
                    {
                        cLeft = cUpJunction;
                    }
                    else
                    {
                        cLeft = disc.ConcentrationPrev[i - 1];
                    }

                    float cRight;
                    if (i == disc.CellCount - 1)
                    {
                        cRight = cDownJunction;
                    }
                    else
                    {
                        cRight = disc.ConcentrationPrev[i + 1];
                    }

                    float convection;
                    if (v >= 0)
                    {
                        convection = -v * (cI - cLeft) / dx;
                    }
                    else
                    {
                        convection = -v * (cRight - cI) / dx;
                    }

                    float diffusion = D * (cRight - 2f * cI + cLeft) / (dx * dx);

                    float dCdt = convection + diffusion;

                    disc.Concentration[i] = cI + dt * dCdt;
                    disc.Concentration[i] = Mathf.Clamp(disc.Concentration[i], 0f, 1f);
                }
            }
        }

        void ApplyJunctionMixing()
        {
            foreach (var node in _network.Nodes)
            {
                int nodeId = node.Id;
                float totalIncomingMass = 0f;
                float totalIncomingFlow = 0f;

                var inEdges = _nodeIncomingEdges.ContainsKey(nodeId) ? _nodeIncomingEdges[nodeId] : null;
                if (inEdges != null)
                {
                    foreach (var edgeId in inEdges)
                    {
                        var edge = _network.GetEdge(edgeId);
                        if (edge == null) continue;
                        float flow = edge.SolvedFlow;
                        if (flow > 0.001f)
                        {
                            if (_state.EdgeCells.TryGetValue(edgeId, out var disc))
                            {
                                float c = disc.Concentration[disc.CellCount - 1];
                                totalIncomingMass += c * flow;
                                totalIncomingFlow += flow;
                            }
                        }
                    }
                }

                var outEdges = _nodeOutgoingEdges.ContainsKey(nodeId) ? _nodeOutgoingEdges[nodeId] : null;
                if (outEdges != null)
                {
                    foreach (var edgeId in outEdges)
                    {
                        var edge = _network.GetEdge(edgeId);
                        if (edge == null) continue;
                        float flow = edge.SolvedFlow;
                        if (flow < -0.001f)
                        {
                            if (_state.EdgeCells.TryGetValue(edgeId, out var disc))
                            {
                                float c = disc.Concentration[0];
                                totalIncomingMass += c * Mathf.Abs(flow);
                                totalIncomingFlow += Mathf.Abs(flow);
                            }
                        }
                    }
                }

                if (totalIncomingFlow > 0.001f)
                {
                    float junctionC = totalIncomingMass / totalIncomingFlow;
                    junctionC = Mathf.Clamp01(junctionC);
                    _state.JunctionConcentrations[nodeId] = junctionC;

                    if (outEdges != null)
                    {
                        foreach (var edgeId in outEdges)
                        {
                            var edge = _network.GetEdge(edgeId);
                            if (edge == null) continue;
                            if (edge.SolvedFlow > 0.001f)
                            {
                                if (_state.EdgeCells.TryGetValue(edgeId, out var disc))
                                {
                                    float boundaryC = (disc.Concentration[0] + junctionC) * 0.5f;
                                    disc.Concentration[0] = Mathf.Lerp(disc.Concentration[0], boundaryC, 0.3f);
                                }
                            }
                        }
                    }

                    if (inEdges != null)
                    {
                        foreach (var edgeId in inEdges)
                        {
                            var edge = _network.GetEdge(edgeId);
                            if (edge == null) continue;
                            if (edge.SolvedFlow < -0.001f)
                            {
                                if (_state.EdgeCells.TryGetValue(edgeId, out var disc))
                                {
                                    float boundaryC = (disc.Concentration[disc.CellCount - 1] + junctionC) * 0.5f;
                                    disc.Concentration[disc.CellCount - 1] = Mathf.Lerp(
                                        disc.Concentration[disc.CellCount - 1], boundaryC, 0.3f);
                                }
                            }
                        }
                    }
                }
                else
                {
                    float currentC = _state.JunctionConcentrations.GetValueOrDefault(nodeId, 0f);
                    _state.JunctionConcentrations[nodeId] = currentC * 0.98f;
                }
            }
        }

        void ApplyBackgroundDecay(float dt)
        {
            foreach (var kvp in _state.EdgeCells)
            {
                var disc = kvp.Value;
                for (int i = 0; i < disc.CellCount; i++)
                {
                    disc.Concentration[i] *= (1f - BackgroundDecay * dt);
                }
            }

            foreach (var node in _network.Nodes)
            {
                float c = _state.JunctionConcentrations.GetValueOrDefault(node.Id, 0f);
                _state.JunctionConcentrations[node.Id] = c * (1f - BackgroundDecay * dt * 2f);
            }
        }

        void DetectAlarmEdges()
        {
            _state.AlarmEdges.Clear();
            _state.MaxGlobalConcentration = 0f;

            foreach (var kvp in _state.EdgeCells)
            {
                float maxC = kvp.Value.GetMaxConcentration();
                if (maxC > _state.MaxGlobalConcentration)
                {
                    _state.MaxGlobalConcentration = maxC;
                }

                if (maxC >= ExplosionLimit)
                {
                    _state.AlarmEdges.Add(kvp.Key);
                }
            }
        }

        void UpdateExpiredSources(float dt)
        {
            for (int i = _state.ActiveSources.Count - 1; i >= 0; i--)
            {
                var source = _state.ActiveSources[i];
                source.Elapsed += dt;

                if (source.Elapsed >= source.Duration)
                {
                    source.Active = false;
                    _state.ActiveSources.RemoveAt(i);
                    Debug.Log($"[瓦斯弥散] 突出源耗尽: 节点{source.NodeId}, 总释放{source.TotalMassReleased:F2}");
                }
            }
        }

        void UpdateMaxGlobalConcentration()
        {
            float maxC = 0f;
            foreach (var kvp in _state.EdgeCells)
            {
                float edgeMax = kvp.Value.GetMaxConcentration();
                if (edgeMax > maxC) maxC = edgeMax;
            }
            foreach (var kvp in _state.JunctionConcentrations)
            {
                if (kvp.Value > maxC) maxC = kvp.Value;
            }
            _state.MaxGlobalConcentration = maxC;
        }

        public float GetEdgeConcentration(int edgeId)
        {
            if (_state.EdgeCells.TryGetValue(edgeId, out var disc))
            {
                return disc.GetMaxConcentration();
            }
            return 0f;
        }

        public float GetEdgeAverageConcentration(int edgeId)
        {
            if (_state.EdgeCells.TryGetValue(edgeId, out var disc))
            {
                return disc.GetAverageConcentration();
            }
            return 0f;
        }

        public bool IsEdgeInAlarm(int edgeId)
        {
            return _state.AlarmEdges.Contains(edgeId);
        }

        public bool IsEdgeInWarning(int edgeId)
        {
            if (_state.AlarmEdges.Contains(edgeId)) return false;
            float c = GetEdgeConcentration(edgeId);
            return c >= WarningLimit;
        }

        public float GetJunctionConcentration(int nodeId)
        {
            return _state.JunctionConcentrations.GetValueOrDefault(nodeId, 0f);
        }

        public void ResetSimulation()
        {
            _state.Reset();
        }
    }
}
