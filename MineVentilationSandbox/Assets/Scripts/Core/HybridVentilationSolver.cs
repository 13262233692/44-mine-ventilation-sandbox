using System.Collections;
using UnityEngine;

namespace MineVentilation.Core
{
    public enum SolverMethod
    {
        Auto,
        HardyCrossOnly,
        NewtonRaphsonOnly,
        HybridSequential
    }

    public class HybridVentilationSolver
    {
        public SolverMethod Method = SolverMethod.HybridSequential;
        public float ConvergenceTolerance = 0.001f;
        public int MaxIterationsHardyCross = 200;
        public int MaxIterationsNewtonRaphson = 80;
        public float HardyCrossFallbackResidual = 1.0f;

        private VentilationNetwork _network;
        private HardyCrossSolver _hcSolver;
        private NewtonRaphsonSolver _nrSolver;

        public event System.Action<string> OnSolverProgress;
        public event System.Action<SolverResult> OnSolverComplete;

        public HybridVentilationSolver(VentilationNetwork network)
        {
            _network = network;
        }

        public SolverResult Solve()
        {
            switch (Method)
            {
                case SolverMethod.HardyCrossOnly:
                    return SolveHardyCross();
                case SolverMethod.NewtonRaphsonOnly:
                    return SolveNewtonRaphson();
                case SolverMethod.Auto:
                case SolverMethod.HybridSequential:
                default:
                    return SolveHybrid();
            }
        }

        public IEnumerator SolveAsync(MonoBehaviour runner, System.Action<SolverResult> onComplete)
        {
            SolverResult result = null;
            bool done = false;

            runner.StartCoroutine(SolveAsyncInternal(r =>
            {
                result = r;
                done = true;
            }));

            while (!done)
            {
                yield return null;
            }

            onComplete?.Invoke(result);
        }

        IEnumerator SolveAsyncInternal(System.Action<SolverResult> onComplete)
        {
            SolverResult result;

            switch (Method)
            {
                case SolverMethod.HardyCrossOnly:
                    result = SolveHardyCross();
                    break;
                case SolverMethod.NewtonRaphsonOnly:
                    result = SolveNewtonRaphson();
                    break;
                default:
                    result = SolveHybrid();
                    break;
            }

            onComplete?.Invoke(result);
            yield break;
        }

        SolverResult SolveHybrid()
        {
            OnSolverProgress?.Invoke("阶段1: Hardy-Cross迭代解算...");

            var hcResult = SolveHardyCross();

            if (hcResult.Converged)
            {
                hcResult.Diagnostics = $"[混合] Hardy-Cross成功收敛 | {hcResult.Diagnostics}";
                OnSolverComplete?.Invoke(hcResult);
                return hcResult;
            }

            bool hcPartialProgress = hcResult.MaxResidual < HardyCrossFallbackResidual * 10f;
            bool hcOscillation = _hcSolver != null && _hcSolver.WasOscillationDetected;

            if (hcPartialProgress || hcOscillation)
            {
                OnSolverProgress?.Invoke($"Hardy-Cross残差{hcResult.MaxResidual:F4},切换Newton-Raphson...");

                var nrResult = SolveNewtonRaphson();

                if (nrResult.Converged)
                {
                    string reason = hcOscillation ? "振荡切换" : "残差切换";
                    nrResult.Diagnostics = $"[混合] {reason}: HC残差{hcResult.MaxResidual:F4}→NR | {nrResult.Diagnostics}";
                    OnSolverComplete?.Invoke(nrResult);
                    return nrResult;
                }

                SolverResult best = hcResult.MaxResidual <= nrResult.MaxResidual ? hcResult : nrResult;
                best.Diagnostics = $"[混合] 两种方法均未完全收敛,取较优解(HC:{hcResult.MaxResidual:F4}, NR:{nrResult.MaxResidual:F4}) | {best.Diagnostics}";
                OnSolverComplete?.Invoke(best);
                return best;
            }

            OnSolverProgress?.Invoke("Hardy-Cross无进展,直接Newton-Raphson...");

            var nrResult2 = SolveNewtonRaphson();
            nrResult2.Diagnostics = $"[混合] HC无进展→NR | {nrResult2.Diagnostics}";
            OnSolverComplete?.Invoke(nrResult2);
            return nrResult2;
        }

        SolverResult SolveHardyCross()
        {
            if (_hcSolver == null)
            {
                _hcSolver = new HardyCrossSolver(_network);
            }

            _hcSolver.ConvergenceTolerance = ConvergenceTolerance;
            _hcSolver.MaxIterations = MaxIterationsHardyCross;

            return _hcSolver.Solve();
        }

        SolverResult SolveNewtonRaphson()
        {
            if (_nrSolver == null)
            {
                _nrSolver = new NewtonRaphsonSolver(_network);
            }

            _nrSolver.ConvergenceTolerance = ConvergenceTolerance * 0.5f;
            _nrSolver.MaxIterations = MaxIterationsNewtonRaphson;

            return _nrSolver.Solve();
        }

        public void UpdateEdgeResistance(int edgeId, float newResistance)
        {
            var edge = _network.GetEdge(edgeId);
            if (edge != null) edge.Resistance = newResistance;
        }

        public void UpdateFanCharacteristic(int fanId, float a0, float a1, float a2)
        {
            var fan = _network.GetFan(fanId);
            if (fan != null) { fan.A0 = a0; fan.A1 = a1; fan.A2 = a2; }
        }
    }
}
