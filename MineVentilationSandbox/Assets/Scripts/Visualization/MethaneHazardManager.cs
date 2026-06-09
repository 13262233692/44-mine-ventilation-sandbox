using System.Collections.Generic;
using MineVentilation.Core;
using MineVentilation.Visualization;
using UnityEngine;

namespace MineVentilation.Visualization
{
    public class MethaneHazardManager : MonoBehaviour
    {
        [Header("Simulation Parameters")]
        public float ExplosionLimit = 0.05f;
        public float WarningLimit = 0.01f;
        public float SimulationSpeed = 1.0f;
        public float OutburstEmissionRate = 0.15f;
        public float OutburstDuration = 30f;
        public float CellSize = 5f;
        public float DiffusionCoefficient = 0.22f;

        [Header("Alarm Visuals")]
        public Color AlarmColor = new Color(1.0f, 0.25f, 0.0f);
        public Color WarningColor = new Color(1.0f, 0.7f, 0.0f);
        public float AlarmPulseFrequency = 3f;

        [Header("Runtime State")]
        public bool SimulationActive;
        public float GlobalMaxConcentration;
        public int AlarmEdgeCount;
        public int ActiveSourceCount;
        public float SimulationTime;

        private MethaneDispersionSimulator _simulator;
        private VentilationNetwork _network;
        private VentilationNetworkMapper _mapper;
        private Dictionary<int, float> _edgeConcentrations = new Dictionary<int, float>();
        private Dictionary<int, bool> _edgeAlarmState = new Dictionary<int, bool>();
        private Dictionary<int, bool> _edgeWarningState = new Dictionary<int, bool>();

        private List<GameObject> _sourceMarkers = new List<GameObject>();
        private float _pulsePhase;

        public MethaneDispersionSimulator Simulator => _simulator;

        public void Initialize(VentilationNetwork network, VentilationNetworkMapper mapper)
        {
            _network = network;
            _mapper = mapper;
            _simulator = new MethaneDispersionSimulator(network);
            _simulator.ExplosionLimit = ExplosionLimit;
            _simulator.WarningLimit = WarningLimit;
            _simulator.SimulationSpeed = SimulationSpeed;
            _simulator.OutburstEmissionRate = OutburstEmissionRate;
            _simulator.OutburstDuration = OutburstDuration;
            _simulator.CellSize = CellSize;
            _simulator.DiffusionCoefficient = DiffusionCoefficient;
            _simulator.InitializeDiscretization();
            SimulationActive = false;
        }

        public void TriggerOutburstAtNode(int nodeId, float emissionRate = -1f, float duration = -1f)
        {
            if (_simulator == null) return;

            if (emissionRate < 0) emissionRate = OutburstEmissionRate;
            if (duration < 0) duration = OutburstDuration;

            _simulator.TriggerOutburst(nodeId, emissionRate, duration);
            SimulationActive = true;

            CreateSourceMarker(nodeId);
        }

        public void TriggerOutburstAtEdge(int edgeId, bool atStart, float emissionRate = -1f, float duration = -1f)
        {
            if (_simulator == null) return;

            if (emissionRate < 0) emissionRate = OutburstEmissionRate;
            if (duration < 0) duration = OutburstDuration;

            var edge = _network.GetEdge(edgeId);
            if (edge == null) return;

            int nodeId = atStart ? edge.FromNodeId : edge.ToNodeId;
            var source = new MethaneSource(nodeId, edgeId, atStart, emissionRate, duration);
            _simulator.State.ActiveSources.Add(source);
            SimulationActive = true;

            CreateSourceMarker(nodeId);
        }

        public void UpdateSimulation(float deltaTime)
        {
            if (_simulator == null || !SimulationActive) return;

            _simulator.StepSimulation(deltaTime);

            UpdateRuntimeState();
            UpdateVisualization();
        }

        void UpdateRuntimeState()
        {
            var state = _simulator.State;
            GlobalMaxConcentration = state.MaxGlobalConcentration;
            AlarmEdgeCount = state.AlarmEdges.Count;
            ActiveSourceCount = state.ActiveSources.Count;
            SimulationTime = state.SimulationTime;

            if (ActiveSourceCount == 0 && GlobalMaxConcentration < 0.001f)
            {
                SimulationActive = false;
            }
        }

        void UpdateVisualization()
        {
            if (_mapper == null) return;

            _pulsePhase += Time.deltaTime * AlarmPulseFrequency * Mathf.PI * 2f;
            float pulse = (Mathf.Sin(_pulsePhase) + 1f) * 0.5f;

            foreach (var edge in _network.Edges)
            {
                float concentration = _simulator.GetEdgeConcentration(edge.Id);
                bool isAlarm = _simulator.IsEdgeInAlarm(edge.Id);
                bool isWarning = _simulator.IsEdgeInWarning(edge.Id);

                _edgeConcentrations[edge.Id] = concentration;

                bool wasAlarm;
                if (!_edgeAlarmState.TryGetValue(edge.Id, out wasAlarm))
                    wasAlarm = false;

                _edgeAlarmState[edge.Id] = isAlarm;
                _edgeWarningState[edge.Id] = isWarning;

                var tunnel = _mapper.GetTunnelSegment(edge.Id);
                if (tunnel != null)
                {
                    tunnel.SetMethaneConcentration(concentration, isAlarm, isWarning, pulse);
                }

                var airflow = _mapper.GetAirflowSystem(edge.Id);
                if (airflow != null)
                {
                    if (isAlarm)
                    {
                        float intensity = Mathf.Lerp(0.7f, 1.0f, pulse);
                        airflow.SetAlarmMode(true, intensity, concentration);
                    }
                    else if (isWarning)
                    {
                        airflow.SetWarningMode(true, concentration);
                    }
                    else
                    {
                        airflow.SetAlarmMode(false, 0f, 0f);
                        airflow.SetWarningMode(false, 0f);
                    }
                }
            }
        }

        void CreateSourceMarker(int nodeId)
        {
            var node = _network.GetNode(nodeId);
            if (node == null) return;

            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"CH4_Source_{node.Name}";
            marker.transform.position = node.Position;
            marker.transform.localScale = Vector3.one * 3f;

            var renderer = marker.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.material = new Material(Shader.Find("Standard"));
                renderer.material.color = Color.magenta;
                renderer.material.SetFloat("_Glossiness", 0.8f);
                renderer.material.SetColor("_EmissionColor", Color.magenta * 2f);
            }

            _sourceMarkers.Add(marker);

            var lightGO = new GameObject($"CH4_Light_{node.Name}");
            lightGO.transform.position = node.Position;
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Color.magenta;
            light.intensity = 5f;
            light.range = 30f;

            _sourceMarkers.Add(lightGO);
        }

        public int DetectClickedNode(Vector3 worldClickPoint, float maxDistance = 15f)
        {
            if (_network == null) return -1;

            int closestNode = -1;
            float closestDist = maxDistance;

            foreach (var node in _network.Nodes)
            {
                float dist = Vector3.Distance(worldClickPoint, node.Position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestNode = node.Id;
                }
            }

            return closestNode;
        }

        public void ResetSimulation()
        {
            if (_simulator != null)
            {
                _simulator.ResetSimulation();
                _simulator.InitializeDiscretization();
            }

            SimulationActive = false;
            GlobalMaxConcentration = 0f;
            AlarmEdgeCount = 0;
            ActiveSourceCount = 0;
            SimulationTime = 0f;

            _edgeConcentrations.Clear();
            _edgeAlarmState.Clear();
            _edgeWarningState.Clear();

            foreach (var marker in _sourceMarkers)
            {
                if (marker != null) Destroy(marker);
            }
            _sourceMarkers.Clear();

            if (_mapper != null)
            {
                foreach (var edge in _network.Edges)
                {
                    var tunnel = _mapper.GetTunnelSegment(edge.Id);
                    if (tunnel != null) tunnel.SetMethaneConcentration(0f, false, false, 0f);

                    var airflow = _mapper.GetAirflowSystem(edge.Id);
                    if (airflow != null)
                    {
                        airflow.SetAlarmMode(false, 0f, 0f);
                        airflow.SetWarningMode(false, 0f);
                    }
                }
            }
        }

        public float GetEdgeConcentration(int edgeId)
        {
            return _edgeConcentrations.GetValueOrDefault(edgeId, 0f);
        }

        public bool IsEdgeInAlarm(int edgeId)
        {
            return _edgeAlarmState.GetValueOrDefault(edgeId, false);
        }
    }
}
