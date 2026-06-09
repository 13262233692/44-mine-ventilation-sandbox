using System;
using System.Collections.Generic;
using UnityEngine;

namespace MineVentilation.Core
{
    public class NewtonRaphsonSolver
    {
        public float ConvergenceTolerance = 0.0005f;
        public int MaxIterations = 100;
        public float MinRelaxation = 0.05f;
        public float MaxStepNorm = 100f;
        public float RegularizationLambda = 1e-6f;
        public float MinResistanceClamp = 1e-4f;
        public float MinFlowClamp = 1e-3f;

        private VentilationNetwork _network;
        private int _numBranches;
        private int _numKCLEquations;
        private int _numKVLEquations;
        private List<int> _kclNodeIds;
        private Dictionary<int, int> _kclNodeIndex;
        private Dictionary<int, int> _branchIndex;
        private double[] _flows;
        private int _totalEquations;

        public NewtonRaphsonSolver(VentilationNetwork network)
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

            PrepareEquationStructure();

            if (_numBranches == 0)
            {
                result.Converged = true;
                result.Diagnostics = "空网络";
                return result;
            }

            InitializeFlows();
            _network.EnforceKCL();

            int iteration = 0;
            float maxResidual = float.MaxValue;
            float prevResidual = float.MaxValue;
            float relaxation = 1.0f;
            int oscillationCount = 0;
            int stagnationCount = 0;

            while (iteration < MaxIterations)
            {
                double[] F = BuildResidualVector();
                maxResidual = ComputeMaxResidual(F);

                if (maxResidual < ConvergenceTolerance)
                {
                    break;
                }

                if (maxResidual > prevResidual * 1.5)
                {
                    oscillationCount++;
                    relaxation = Mathf.Max(relaxation * 0.5f, MinRelaxation);
                }
                else if (maxResidual > prevResidual * 0.95 && maxResidual < prevResidual * 1.05)
                {
                    stagnationCount++;
                    if (stagnationCount > 5)
                    {
                        relaxation = Mathf.Max(relaxation * 0.7f, MinRelaxation);
                    }
                }
                else
                {
                    oscillationCount = Math.Max(0, oscillationCount - 1);
                    stagnationCount = Math.Max(0, stagnationCount - 1);
                    if (relaxation < 1.0f && oscillationCount == 0 && stagnationCount == 0)
                    {
                        relaxation = Mathf.Min(relaxation * 1.2f, 1.0f);
                    }
                }

                SparseMatrix J = BuildJacobian();

                double[] deltaQ;
                try
                {
                    deltaQ = LinearSolver.SolveWithRegularization(J, NegateVector(F), RegularizationLambda);
                }
                catch (Exception)
                {
                    result.Converged = false;
                    result.Diagnostics = $"Newton-Raphson线性系统求解失败(迭代{iteration}),残差{maxResidual:F6}";
                    FillResultFromFlows(result);
                    return result;
                }

                float stepNorm = ComputeVectorNorm(deltaQ);
                if (stepNorm > MaxStepNorm)
                {
                    float scale = MaxStepNorm / stepNorm;
                    for (int i = 0; i < deltaQ.Length; i++)
                    {
                        deltaQ[i] *= scale;
                    }
                }

                ApplyCorrection(deltaQ, relaxation);

                prevResidual = maxResidual;
                iteration++;
            }

            SyncFlowsToNetwork();

            result.Converged = maxResidual < ConvergenceTolerance;
            result.Iterations = iteration;
            result.MaxResidual = maxResidual;

            string method = oscillationCount > 3 ? " (含阻尼修正)" : "";
            result.Diagnostics = result.Converged
                ? $"Newton-Raphson收敛{method},迭代{iteration}次,残差{maxResidual:F6}"
                : $"Newton-Raphson未收敛{method},迭代{iteration}次,残差{maxResidual:F6}";

            FillResultFromFlows(result);
            return result;
        }

        void PrepareEquationStructure()
        {
            _numBranches = _network.Edges.Count;

            if (_network.Loops.Count == 0)
            {
                _network.FindIndependentLoops();
            }

            _kclNodeIds = new List<int>();
            _kclNodeIndex = new Dictionary<int, int>();
            _branchIndex = new Dictionary<int, int>();

            for (int i = 0; i < _network.Edges.Count; i++)
            {
                _branchIndex[_network.Edges[i].Id] = i;
            }

            for (int i = 0; i < _network.Nodes.Count - 1; i++)
            {
                int nodeId = _network.Nodes[i].Id;
                _kclNodeIds.Add(nodeId);
                _kclNodeIndex[nodeId] = i;
            }

            _numKCLEquations = _kclNodeIds.Count;
            _numKVLEquations = _network.Loops.Count;
            _totalEquations = _numKCLEquations + _numKVLEquations;
        }

        void InitializeFlows()
        {
            _flows = new double[_numBranches];

            for (int i = 0; i < _network.Edges.Count; i++)
            {
                var edge = _network.Edges[i];
                int idx = _branchIndex[edge.Id];
                _flows[idx] = edge.CurrentFlow;

                if (Math.Abs(_flows[idx]) < MinFlowClamp)
                {
                    if (edge.FanId >= 0)
                    {
                        var fan = _network.GetFan(edge.FanId);
                        if (fan != null && fan.A0 > 0)
                        {
                            float r = Mathf.Max(edge.Resistance, MinResistanceClamp);
                            _flows[idx] = Mathf.Sqrt(fan.A0 / r) * 0.3;
                        }
                        else
                        {
                            _flows[idx] = 3.0;
                        }
                    }
                    else
                    {
                        _flows[idx] = 2.0;
                    }
                }
            }
        }

        double[] BuildResidualVector()
        {
            var F = new double[_totalEquations];

            for (int i = 0; i < _numKCLEquations; i++)
            {
                int nodeId = _kclNodeIds[i];
                double kclSum = 0.0;

                foreach (var edge in _network.Edges)
                {
                    int idx = _branchIndex[edge.Id];
                    if (edge.FromNodeId == nodeId)
                    {
                        kclSum += _flows[idx];
                    }
                    else if (edge.ToNodeId == nodeId)
                    {
                        kclSum -= _flows[idx];
                    }
                }

                F[i] = kclSum;
            }

            for (int k = 0; k < _numKVLEquations; k++)
            {
                var loop = _network.Loops[k];
                int eqIdx = _numKCLEquations + k;
                double kvlSum = 0.0;

                for (int i = 0; i < loop.EdgeIds.Count; i++)
                {
                    int edgeId = loop.EdgeIds[i];
                    int sign = loop.Signs[i];
                    int idx = _branchIndex[edgeId];
                    var edge = _network.GetEdge(edgeId);
                    if (edge == null) continue;

                    float r = Mathf.Max(edge.Resistance, MinResistanceClamp);
                    double effectiveFlow = sign * _flows[idx];
                    kvlSum += r * effectiveFlow * Math.Abs(effectiveFlow);

                    if (edge.FanId >= 0)
                    {
                        var fan = _network.GetFan(edge.FanId);
                        if (fan != null)
                        {
                            kvlSum -= sign * fan.GetPressure((float)_flows[idx]);
                        }
                    }

                    if (Math.Abs(edge.FixedPressure) > 0.0001f)
                    {
                        kvlSum -= sign * edge.FixedPressure;
                    }
                }

                F[eqIdx] = kvlSum;
            }

            return F;
        }

        SparseMatrix BuildJacobian()
        {
            var J = new SparseMatrix(_totalEquations, _numBranches);

            for (int i = 0; i < _numKCLEquations; i++)
            {
                int nodeId = _kclNodeIds[i];

                foreach (var edge in _network.Edges)
                {
                    int idx = _branchIndex[edge.Id];
                    if (edge.FromNodeId == nodeId)
                    {
                        J.Add(i, idx, 1.0);
                    }
                    else if (edge.ToNodeId == nodeId)
                    {
                        J.Add(i, idx, -1.0);
                    }
                }
            }

            for (int k = 0; k < _numKVLEquations; k++)
            {
                var loop = _network.Loops[k];
                int eqIdx = _numKCLEquations + k;

                for (int i = 0; i < loop.EdgeIds.Count; i++)
                {
                    int edgeId = loop.EdgeIds[i];
                    int sign = loop.Signs[i];
                    int idx = _branchIndex[edgeId];
                    var edge = _network.GetEdge(edgeId);
                    if (edge == null) continue;

                    float r = Mathf.Max(edge.Resistance, MinResistanceClamp);
                    double absQ = Math.Max(Math.Abs(_flows[idx]), MinFlowClamp);
                    double derivative = sign * 2.0 * r * absQ;

                    J.Add(eqIdx, idx, derivative);

                    if (edge.FanId >= 0)
                    {
                        var fan = _network.GetFan(edge.FanId);
                        if (fan != null)
                        {
                            J.Add(eqIdx, idx, -fan.GetPressureDerivative((float)_flows[idx]));
                        }
                    }
                }
            }

            return J;
        }

        void ApplyCorrection(double[] deltaQ, float relaxation)
        {
            for (int i = 0; i < _numBranches && i < deltaQ.Length; i++)
            {
                _flows[i] += deltaQ[i] * relaxation;
            }
        }

        void SyncFlowsToNetwork()
        {
            for (int i = 0; i < _network.Edges.Count; i++)
            {
                int idx = _branchIndex[_network.Edges[i].Id];
                _network.Edges[i].CurrentFlow = (float)_flows[idx];
                _network.Edges[i].SolvedFlow = (float)_flows[idx];
            }
        }

        float ComputeMaxResidual(double[] F)
        {
            double maxR = 0.0;
            for (int i = 0; i < _numKCLEquations; i++)
            {
                maxR = Math.Max(maxR, Math.Abs(F[i]));
            }
            for (int i = _numKCLEquations; i < _totalEquations; i++)
            {
                maxR = Math.Max(maxR, Math.Abs(F[i]) * 0.001);
            }
            return (float)maxR;
        }

        float ComputeVectorNorm(double[] v)
        {
            double sum = 0.0;
            for (int i = 0; i < v.Length; i++)
            {
                sum += v[i] * v[i];
            }
            return (float)Math.Sqrt(sum);
        }

        double[] NegateVector(double[] v)
        {
            var result = new double[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                result[i] = -v[i];
            }
            return result;
        }

        void FillResultFromFlows(SolverResult result)
        {
            for (int i = 0; i < _network.Edges.Count; i++)
            {
                int idx = _branchIndex[_network.Edges[i].Id];
                float flow = (float)_flows[idx];
                result.BranchFlows.Add(flow);
                float r = Mathf.Max(_network.Edges[i].Resistance, MinResistanceClamp);
                result.BranchPressures.Add(r * flow * Math.Abs(flow));
            }
        }
    }
}
