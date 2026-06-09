using System;
using System.Collections.Generic;
using UnityEngine;

namespace MineVentilation.Core
{
    public class EdgeDiscretization
    {
        public int EdgeId;
        public int CellCount;
        public float CellLength;
        public float[] Concentration;
        public float[] ConcentrationPrev;
        public float WindVelocity;
        public float CrossSectionArea;
        public float EdgeLength;
        public int FromNodeId;
        public int ToNodeId;

        public EdgeDiscretization(int edgeId, int cellCount, float cellLength,
            float windVelocity, float crossSectionArea, float edgeLength,
            int fromNodeId, int toNodeId)
        {
            EdgeId = edgeId;
            CellCount = cellCount;
            CellLength = cellLength;
            WindVelocity = windVelocity;
            CrossSectionArea = crossSectionArea;
            EdgeLength = edgeLength;
            FromNodeId = fromNodeId;
            ToNodeId = toNodeId;

            Concentration = new float[cellCount];
            ConcentrationPrev = new float[cellCount];
        }

        public float GetMaxConcentration()
        {
            float maxC = 0f;
            for (int i = 0; i < CellCount; i++)
            {
                if (Concentration[i] > maxC) maxC = Concentration[i];
            }
            return maxC;
        }

        public float GetAverageConcentration()
        {
            float sum = 0f;
            for (int i = 0; i < CellCount; i++)
            {
                sum += Concentration[i];
            }
            return CellCount > 0 ? sum / CellCount : 0f;
        }

        public void Reset()
        {
            Array.Clear(Concentration, 0, CellCount);
            Array.Clear(ConcentrationPrev, 0, CellCount);
        }

        public void SwapBuffers()
        {
            var temp = ConcentrationPrev;
            ConcentrationPrev = Concentration;
            Concentration = temp;
        }

        public void InjectSource(int cellIndex, float amount)
        {
            if (cellIndex >= 0 && cellIndex < CellCount)
            {
                Concentration[cellIndex] = Mathf.Clamp01(Concentration[cellIndex] + amount);
            }
        }

        public void InjectSourceAtStart(float amount)
        {
            InjectSource(0, amount);
        }

        public void InjectSourceAtEnd(float amount)
        {
            InjectSource(CellCount - 1, amount);
        }
    }

    public class MethaneSource
    {
        public int NodeId;
        public int EdgeId;
        public bool AtEdgeStart;
        public float EmissionRate;
        public float Duration;
        public float Elapsed;
        public float TotalMassReleased;
        public bool Active;

        public MethaneSource(int nodeId, int edgeId, bool atEdgeStart,
            float emissionRate, float duration)
        {
            NodeId = nodeId;
            EdgeId = edgeId;
            AtEdgeStart = atEdgeStart;
            EmissionRate = emissionRate;
            Duration = duration;
            Elapsed = 0f;
            TotalMassReleased = 0f;
            Active = true;
        }
    }

    public class MethaneSimulationState
    {
        public Dictionary<int, EdgeDiscretization> EdgeCells = new Dictionary<int, EdgeDiscretization>();
        public List<MethaneSource> ActiveSources = new List<MethaneSource>();
        public Dictionary<int, float> JunctionConcentrations = new Dictionary<int, float>();
        public float SimulationTime;
        public HashSet<int> AlarmEdges = new HashSet<int>();
        public float MaxGlobalConcentration;

        public void Reset()
        {
            foreach (var cell in EdgeCells.Values)
            {
                cell.Reset();
            }
            ActiveSources.Clear();
            JunctionConcentrations.Clear();
            AlarmEdges.Clear();
            SimulationTime = 0f;
            MaxGlobalConcentration = 0f;
        }
    }
}
