using System;
using System.Collections.Generic;
using UnityEngine;

namespace MineVentilation.Core
{
    public class HardyCrossSolver
    {
        public float ConvergenceTolerance = 0.001f;
        public int MaxIterations = 300;
        public float InitialRelaxationFactor = 1.0f;
        public float MinRelaxationFactor = 0.01f;
        public bool EnableDamping = true;
        public float DampingThreshold = 30f;
        public float MinResistanceClamp = 1e-4f;
        public int OscillationWindow = 6;
        public float OscillationThreshold = 0.7f;
        public int StagnationLimit = 15;
        public float StagnationThreshold = 0.98f;

        private VentilationNetwork _network;
        private float _currentRelaxation;
        private List<float> _correctionHistory;
        private int _oscillationCount;
        private int _stagnationCount;
        private bool _oscillationDetected;

        public bool WasOscillationDetected => _oscillationDetected;

        public HardyCrossSolver(VentilationNetwork network)
        {
            _network = network;
            _correctionHistory = new List<float>();
        }

        public SolverResult Solve()
        {
            var result = new SolverResult();

            string validateMsg;
            if (!_network.ValidateNetwork(out validateMsg))
            {
                result.Converged = false;
                result.Diagnostics = $"网络验证失败: {validateMsg}";
                return result;
            }

            if (_network.Loops.Count == 0)
            {
                _network.FindIndependentLoops();
            }

            if (_network.Loops.Count == 0)
            {
                result.Converged = true;
                result.Diagnostics = "无回路网络,直接计算";
                ComputeSinglePathFlows();
                FillResult(result);
                return result;
            }

            InitializeFlows();
            _network.EnforceKCL();

            _currentRelaxation = InitialRelaxationFactor;
            _correctionHistory.Clear();
            _oscillationCount = 0;
            _stagnationCount = 0;
            _oscillationDetected = false;

            float maxCorrection = float.MaxValue;
            int iteration = 0;
            float prevMaxCorrection = float.MaxValue;
            float bestResidual = float.MaxValue;
            float[] bestFlows = null;

            while (iteration < MaxIterations)
            {
                maxCorrection = 0f;
                int signChangeCount = 0;
                int totalLoopCorrections = 0;

                foreach (var loop in _network.Loops)
                {
                    float numerator = 0f;
                    float denominator = 0f;

                    for (int i = 0; i < loop.EdgeIds.Count; i++)
                    {
                        int edgeId = loop.EdgeIds[i];
                        int sign = loop.Signs[i];
                        var edge = _network.GetEdge(edgeId);
                        if (edge == null) continue;

                        float r = Mathf.Max(edge.Resistance, MinResistanceClamp);
                        float effectiveFlow = sign * edge.CurrentFlow;
                        float frictionTerm = r * effectiveFlow * Math.Abs(effectiveFlow);
                        numerator += frictionTerm;

                        float absFlow = Math.Abs(edge.CurrentFlow);
                        if (absFlow < MinResistanceClamp) absFlow = MinResistanceClamp;
                        denominator += 2f * r * absFlow;

                        if (edge.FanId >= 0)
                        {
                            var fan = _network.GetFan(edge.FanId);
                            if (fan != null)
                            {
                                float fanPressure = fan.GetPressure(edge.CurrentFlow);
                                numerator -= sign * fanPressure;
                                float fanDerivative = fan.GetPressureDerivative(edge.CurrentFlow);
                                denominator -= fanDerivative;
                            }
                        }

                        if (Math.Abs(edge.FixedPressure) > 0.0001f)
                        {
                            numerator -= sign * edge.FixedPressure;
                        }
                    }

                    if (Math.Abs(denominator) < 1e-10f)
                    {
                        continue;
                    }

                    float deltaQ = -numerator / denominator;

                    if (EnableDamping && Math.Abs(deltaQ) > DampingThreshold)
                    {
                        deltaQ = Math.Sign(deltaQ) * DampingThreshold;
                    }

                    deltaQ *= _currentRelaxation;

                    if (float.IsNaN(deltaQ) || float.IsInfinity(deltaQ))
                    {
                        continue;
                    }

                    for (int i = 0; i < loop.EdgeIds.Count; i++)
                    {
                        int edgeId = loop.EdgeIds[i];
                        int sign = loop.Signs[i];
                        var edge = _network.GetEdge(edgeId);
                        if (edge == null) continue;

                        float oldFlow = edge.CurrentFlow;
                        edge.CurrentFlow += sign * deltaQ;
                        edge.CurrentFlow = Mathf.Clamp(edge.CurrentFlow, -500f, 500f);

                        if (Math.Sign(oldFlow) != Math.Sign(edge.CurrentFlow) && Math.Abs(oldFlow) > 0.1f)
                        {
                            signChangeCount++;
                        }
                    }

                    maxCorrection = Math.Max(maxCorrection, Math.Abs(deltaQ));
                    totalLoopCorrections++;
                }

                if (maxCorrection < bestResidual)
                {
                    bestResidual = maxCorrection;
                    bestFlows = new float[_network.Edges.Count];
                    for (int i = 0; i < _network.Edges.Count; i++)
                    {
                        bestFlows[i] = _network.Edges[i].CurrentFlow;
                    }
                }

                _correctionHistory.Add(maxCorrection);
                DetectOscillation(maxCorrection, prevMaxCorrection, signChangeCount, totalLoopCorrections);

                DetectStagnation();

                AdaptRelaxation(maxCorrection, prevMaxCorrection);

                if (_oscillationDetected && _currentRelaxation <= MinRelaxationFactor && iteration > 30)
                {
                    if (bestFlows != null)
                    {
                        for (int i = 0; i < _network.Edges.Count; i++)
                        {
                            _network.Edges[i].CurrentFlow = bestFlows[i];
                        }
                    }
                    break;
                }

                prevMaxCorrection = maxCorrection;
                iteration++;

                if (maxCorrection < ConvergenceTolerance)
                {
                    break;
                }
            }

            foreach (var edge in _network.Edges)
            {
                edge.SolvedFlow = edge.CurrentFlow;
            }

            result.Converged = maxCorrection < ConvergenceTolerance;
            result.Iterations = iteration;
            result.MaxResidual = maxCorrection;

            string oscWarning = _oscillationDetected ? " [检测到振荡,已阻尼收敛]" : "";
            string stagWarning = _stagnationCount > StagnationLimit ? " [检测到停滞,已降松弛]" : "";
            result.Diagnostics = result.Converged
                ? $"Hardy-Cross收敛,迭代{iteration}次,残差{maxCorrection:F6}{oscWarning}{stagWarning}"
                : $"Hardy-Cross未收敛,迭代{iteration}次,残差{maxCorrection:F6}{oscWarning}{stagWarning}";

            FillResult(result);
            return result;
        }

        void DetectOscillation(float currentCorrection, float prevCorrection, int signChanges, int totalCorrections)
        {
            if (_correctionHistory.Count < OscillationWindow * 2) return;

            float signChangeRatio = totalCorrections > 0 ? (float)signChanges / totalCorrections : 0f;

            int reversals = 0;
            int windowStart = _correctionHistory.Count - OscillationWindow;
            for (int i = windowStart + 1; i < _correctionHistory.Count; i++)
            {
                float diff = _correctionHistory[i] - _correctionHistory[i - 1];
                float prevDiff = _correctionHistory[i - 1] - _correctionHistory[i - 2];
                if (diff * prevDiff < 0)
                {
                    reversals++;
                }
            }

            bool directionOscillation = reversals >= OscillationWindow * OscillationThreshold;

            bool magnitudeOscillation = false;
            if (currentCorrection > prevCorrection * 0.9 && prevCorrection > currentCorrection * 0.9)
            {
                float recentAvg = 0f;
                for (int i = _correctionHistory.Count - OscillationWindow; i < _correctionHistory.Count; i++)
                {
                    recentAvg += _correctionHistory[i];
                }
                recentAvg /= OscillationWindow;

                float variance = 0f;
                for (int i = _correctionHistory.Count - OscillationWindow; i < _correctionHistory.Count; i++)
                {
                    float d = _correctionHistory[i] - recentAvg;
                    variance += d * d;
                }
                variance /= OscillationWindow;

                if (variance < recentAvg * recentAvg * 0.1f && recentAvg > ConvergenceTolerance * 10f)
                {
                    magnitudeOscillation = true;
                }
            }

            if (directionOscillation || magnitudeOscillation || signChangeRatio > 0.3f)
            {
                _oscillationCount++;
                if (_oscillationCount >= 3)
                {
                    _oscillationDetected = true;
                }
            }
            else
            {
                _oscillationCount = Math.Max(0, _oscillationCount - 1);
            }
        }

        void DetectStagnation()
        {
            if (_correctionHistory.Count < StagnationLimit) return;

            float recent = _correctionHistory[_correctionHistory.Count - 1];
            float older = _correctionHistory[_correctionHistory.Count - StagnationLimit];

            if (older > 0.001f && recent / older > StagnationThreshold && recent / older < 1.0f / StagnationThreshold)
            {
                _stagnationCount++;
            }
            else
            {
                _stagnationCount = Math.Max(0, _stagnationCount - 2);
            }
        }

        void AdaptRelaxation(float currentCorrection, float prevCorrection)
        {
            if (_oscillationDetected)
            {
                _currentRelaxation = Mathf.Max(_currentRelaxation * 0.6f, MinRelaxationFactor);
                return;
            }

            if (_stagnationCount > StagnationLimit)
            {
                _currentRelaxation = Mathf.Max(_currentRelaxation * 0.8f, MinRelaxationFactor);
                _stagnationCount = 0;
                return;
            }

            if (prevCorrection > 0.001f && currentCorrection < prevCorrection * 0.5f)
            {
                _currentRelaxation = Mathf.Min(_currentRelaxation * 1.1f, 1.0f);
            }
            else if (currentCorrection > prevCorrection * 1.1f)
            {
                _currentRelaxation = Mathf.Max(_currentRelaxation * 0.85f, MinRelaxationFactor);
            }
        }

        private void InitializeFlows()
        {
            foreach (var edge in _network.Edges)
            {
                if (Math.Abs(edge.CurrentFlow) < 0.001f && Math.Abs(edge.InitialFlowGuess) < 0.001f)
                {
                    if (edge.FanId >= 0)
                    {
                        var fan = _network.GetFan(edge.FanId);
                        if (fan != null && fan.A0 > 0 && edge.Resistance > 0)
                        {
                            float r = Mathf.Max(edge.Resistance, MinResistanceClamp);
                            float approxFlow = Mathf.Sqrt(fan.A0 / r);
                            edge.CurrentFlow = approxFlow * 0.5f;
                        }
                        else
                        {
                            edge.CurrentFlow = 5f;
                        }
                    }
                    else
                    {
                        edge.CurrentFlow = 2f;
                    }
                }
                else if (Math.Abs(edge.CurrentFlow) < 0.001f)
                {
                    edge.CurrentFlow = edge.InitialFlowGuess;
                }
            }
        }

        private void ComputeSinglePathFlows()
        {
            if (_network.Edges.Count == 0) return;

            float totalResistance = 0f;
            float totalFanPressure = 0f;

            foreach (var e in _network.Edges)
            {
                totalResistance += Mathf.Max(e.Resistance, MinResistanceClamp);
                if (e.FanId >= 0)
                {
                    var fan = _network.GetFan(e.FanId);
                    if (fan != null) totalFanPressure += fan.A0;
                }
            }

            if (totalResistance > 0)
            {
                float flow = Mathf.Sqrt(totalFanPressure / totalResistance);
                foreach (var e in _network.Edges)
                {
                    e.SolvedFlow = flow;
                    e.CurrentFlow = flow;
                }
            }
        }

        private void FillResult(SolverResult result)
        {
            foreach (var edge in _network.Edges)
            {
                result.BranchFlows.Add(edge.SolvedFlow);
                result.BranchPressures.Add(edge.GetFrictionPressureDrop());
            }
        }
    }
}
