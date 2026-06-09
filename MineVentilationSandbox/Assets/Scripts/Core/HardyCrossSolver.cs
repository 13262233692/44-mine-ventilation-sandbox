using System;
using System.Collections.Generic;
using UnityEngine;

namespace MineVentilation.Core
{
    public class HardyCrossSolver
    {
        public float ConvergenceTolerance = 0.001f;
        public int MaxIterations = 500;
        public float RelaxationFactor = 1.0f;
        public bool EnableDamping = true;
        public float DampingThreshold = 50f;

        private VentilationNetwork _network;

        public HardyCrossSolver(VentilationNetwork network)
        {
            _network = network;
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

            float maxCorrection = float.MaxValue;
            int iteration = 0;

            while (iteration < MaxIterations)
            {
                maxCorrection = 0f;

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

                        float effectiveFlow = sign * edge.CurrentFlow;
                        float frictionTerm = edge.Resistance * effectiveFlow * Math.Abs(effectiveFlow);
                        numerator += frictionTerm;

                        float absFlow = Math.Abs(edge.CurrentFlow);
                        if (absFlow < 0.001f) absFlow = 0.001f;
                        denominator += 2f * edge.Resistance * absFlow;

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

                    deltaQ *= RelaxationFactor;

                    for (int i = 0; i < loop.EdgeIds.Count; i++)
                    {
                        int edgeId = loop.EdgeIds[i];
                        int sign = loop.Signs[i];
                        var edge = _network.GetEdge(edgeId);
                        if (edge == null) continue;
                        edge.CurrentFlow += sign * deltaQ;
                    }

                    maxCorrection = Math.Max(maxCorrection, Math.Abs(deltaQ));
                }

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
            result.Diagnostics = result.Converged
                ? $"收敛完成,迭代{iteration}次,最大残差{maxCorrection:F6}"
                : $"未收敛,迭代{iteration}次,最大残差{maxCorrection:F6}";

            FillResult(result);
            return result;
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
                            float approxFlow = Mathf.Sqrt(fan.A0 / edge.Resistance);
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

            var edge = _network.Edges[0];
            float totalResistance = 0f;
            float totalFanPressure = 0f;

            foreach (var e in _network.Edges)
            {
                totalResistance += e.Resistance;
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

        public void UpdateNetworkResistance(int edgeId, float newResistance)
        {
            var edge = _network.GetEdge(edgeId);
            if (edge != null)
            {
                edge.Resistance = newResistance;
            }
        }

        public void UpdateFanCharacteristic(int fanId, float a0, float a1, float a2)
        {
            var fan = _network.GetFan(fanId);
            if (fan != null)
            {
                fan.A0 = a0;
                fan.A1 = a1;
                fan.A2 = a2;
            }
        }
    }
}
