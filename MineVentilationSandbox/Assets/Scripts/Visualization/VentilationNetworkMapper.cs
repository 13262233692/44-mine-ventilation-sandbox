using System.Collections.Generic;
using MineVentilation.Core;
using UnityEngine;

namespace MineVentilation.Visualization
{
    public class VentilationNetworkMapper : MonoBehaviour
    {
        [Header("Prefab References")]
        public GameObject TunnelSegmentPrefab;
        public GameObject JunctionMarkerPrefab;
        public GameObject FanMarkerPrefab;

        [Header("Visual Settings")]
        public float DefaultTunnelRadius = 2.5f;
        public float JunctionMarkerSize = 1f;
        public float WorldScale = 1f;

        [Header("Runtime References")]
        public Dictionary<int, MineTunnelSegment> TunnelSegments = new Dictionary<int, MineTunnelSegment>();
        public Dictionary<int, GameObject> JunctionMarkers = new Dictionary<int, GameObject>();
        public Dictionary<int, AirflowParticleSystem> AirflowSystems = new Dictionary<int, AirflowParticleSystem>();

        private Transform _tunnelContainer;
        private Transform _junctionContainer;
        private VentilationNetwork _network;

        public void BuildVisualization(VentilationNetwork network, SolverResult result)
        {
            _network = network;
            ClearVisualization();

            CreateContainers();
            CreateJunctionMarkers();
            CreateTunnelSegments();
            ApplySolverResults(result);
        }

        void CreateContainers()
        {
            var tunnelGO = new GameObject("Tunnels");
            tunnelGO.transform.SetParent(transform);
            _tunnelContainer = tunnelGO.transform;

            var junctionGO = new GameObject("Junctions");
            junctionGO.transform.SetParent(transform);
            _junctionContainer = junctionGO.transform;
        }

        void CreateJunctionMarkers()
        {
            foreach (var node in _network.Nodes)
            {
                Vector3 worldPos = node.Position * WorldScale;
                GameObject marker;

                if (JunctionMarkerPrefab != null)
                {
                    marker = Instantiate(JunctionMarkerPrefab, worldPos, Quaternion.identity, _junctionContainer);
                }
                else
                {
                    marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    marker.transform.SetParent(_junctionContainer);
                    marker.transform.position = worldPos;
                    marker.transform.localScale = Vector3.one * JunctionMarkerSize;
                    var renderer = marker.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        renderer.material = new Material(Shader.Find("Standard"));
                        renderer.material.color = Color.yellow;
                        renderer.material.SetFloat("_Glossiness", 0.3f);
                    }
                }

                marker.name = $"Junction_{node.Id}_{node.Name}";
                JunctionMarkers[node.Id] = marker;
            }
        }

        void CreateTunnelSegments()
        {
            foreach (var edge in _network.Edges)
            {
                var fromNode = _network.GetNode(edge.FromNodeId);
                var toNode = _network.GetNode(edge.ToNodeId);
                if (fromNode == null || toNode == null) continue;

                Vector3 start = fromNode.Position * WorldScale;
                Vector3 end = toNode.Position * WorldScale;

                GameObject tunnelGO;

                if (TunnelSegmentPrefab != null)
                {
                    tunnelGO = Instantiate(TunnelSegmentPrefab, Vector3.zero, Quaternion.identity, _tunnelContainer);
                }
                else
                {
                    tunnelGO = new GameObject($"Tunnel_{edge.Id}_{edge.Name}");
                    tunnelGO.transform.SetParent(_tunnelContainer);
                    tunnelGO.AddComponent<MeshFilter>();
                    tunnelGO.AddComponent<MeshRenderer>();
                }

                var segment = tunnelGO.GetComponent<MineTunnelSegment>();
                if (segment == null)
                {
                    segment = tunnelGO.AddComponent<MineTunnelSegment>();
                }

                float radius = edge.CrossSectionArea > 0
                    ? Mathf.Sqrt(edge.CrossSectionArea / Mathf.PI)
                    : DefaultTunnelRadius;

                segment.Initialize(start, end, edge.Id, radius);
                TunnelSegments[edge.Id] = segment;

                if (edge.FanId >= 0)
                {
                    CreateFanMarker(edge, start, end);
                }

                var particleGO = new GameObject($"Airflow_{edge.Id}");
                particleGO.transform.SetParent(tunnelGO.transform);
                var ps = particleGO.AddComponent<ParticleSystem>();
                var airflowPS = particleGO.AddComponent<AirflowParticleSystem>();
                airflowPS.Initialize(segment);
                AirflowSystems[edge.Id] = airflowPS;
            }
        }

        void CreateFanMarker(AirwayEdge edge, Vector3 start, Vector3 end)
        {
            Vector3 fanPos = Vector3.Lerp(start, end, 0.3f);
            GameObject fanMarker;

            if (FanMarkerPrefab != null)
            {
                fanMarker = Instantiate(FanMarkerPrefab, fanPos, Quaternion.identity, _tunnelContainer);
            }
            else
            {
                fanMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                fanMarker.transform.SetParent(_tunnelContainer);
                fanMarker.transform.position = fanPos;

                Vector3 dir = end - start;
                if (dir != Vector3.zero)
                {
                    fanMarker.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90f, 0f, 0f);
                }

                fanMarker.transform.localScale = new Vector3(
                    DefaultTunnelRadius * 1.5f,
                    0.2f,
                    DefaultTunnelRadius * 1.5f
                );

                var renderer = fanMarker.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.material = new Material(Shader.Find("Standard"));
                    renderer.material.color = Color.green;
                    renderer.material.SetFloat("_Glossiness", 0.5f);
                    renderer.material.SetFloat("_Metallic", 0.3f);
                }
            }

            fanMarker.name = $"Fan_{edge.FanId}";
        }

        public void ApplySolverResults(SolverResult result)
        {
            if (result == null || _network == null) return;

            for (int i = 0; i < _network.Edges.Count && i < result.BranchFlows.Count; i++)
            {
                var edge = _network.Edges[i];
                float flow = result.BranchFlows[i];

                if (TunnelSegments.TryGetValue(edge.Id, out var segment))
                {
                    segment.UpdateFlowVisualization(flow);
                }

                if (AirflowSystems.TryGetValue(edge.Id, out var airflow))
                {
                    airflow.UpdateFlowVisualization(flow);
                }
            }
        }

        public void UpdateSingleEdgeFlow(int edgeId, float flow)
        {
            if (TunnelSegments.TryGetValue(edgeId, out var segment))
            {
                segment.UpdateFlowVisualization(flow);
            }

            if (AirflowSystems.TryGetValue(edgeId, out var airflow))
            {
                airflow.UpdateFlowVisualization(flow);
            }
        }

        public void ClearVisualization()
        {
            foreach (var kv in TunnelSegments)
            {
                if (kv.Value != null) Destroy(kv.Value.gameObject);
            }
            foreach (var kv in JunctionMarkers)
            {
                if (kv.Value != null) Destroy(kv.Value);
            }

            TunnelSegments.Clear();
            JunctionMarkers.Clear();
            AirflowSystems.Clear();

            if (_tunnelContainer != null) Destroy(_tunnelContainer.gameObject);
            if (_junctionContainer != null) Destroy(_junctionContainer.gameObject);
        }

        public MineTunnelSegment GetTunnelSegment(int edgeId)
        {
            return TunnelSegments.TryGetValue(edgeId, out var seg) ? seg : null;
        }

        public AirflowParticleSystem GetAirflowSystem(int edgeId)
        {
            return AirflowSystems.TryGetValue(edgeId, out var ps) ? ps : null;
        }
    }
}
