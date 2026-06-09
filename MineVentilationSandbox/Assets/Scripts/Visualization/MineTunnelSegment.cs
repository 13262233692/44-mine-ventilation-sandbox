using System.Collections.Generic;
using UnityEngine;

namespace MineVentilation.Visualization
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class MineTunnelSegment : MonoBehaviour
    {
        [Header("Tunnel Geometry")]
        public int RadialSegments = 12;
        public int LengthSegments = 8;
        public float Radius = 2.5f;
        public float WallThickness = 0.3f;

        [Header("Visual Settings")]
        public Color BaseColor = new Color(0.35f, 0.3f, 0.25f);
        public Color HighFlowColor = new Color(0.2f, 0.8f, 1.0f);
        public Color LowFlowColor = new Color(0.8f, 0.3f, 0.1f);
        public Color ReverseFlowColor = new Color(1.0f, 0.1f, 0.1f);

        [Header("Runtime Data")]
        public int EdgeId = -1;
        public Vector3 StartPosition;
        public Vector3 EndPosition;
        public float CurrentFlowRate;
        public float FlowSpeed;
        public bool FlowReversed;

        [Header("Methane Hazard State")]
        public float MethaneConcentration;
        public bool IsAlarm;
        public bool IsWarning;
        public float AlarmPulse;

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Material _tunnelMaterial;
        private float _displayFlow;
        private bool _meshBuilt;

        void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
        }

        public void Initialize(Vector3 start, Vector3 end, int edgeId, float radius = 2.5f)
        {
            StartPosition = start;
            EndPosition = end;
            EdgeId = edgeId;
            Radius = radius;
            BuildTunnelMesh();
            UpdateMaterial();
            _meshBuilt = true;
        }

        public void UpdateFlowVisualization(float flowRate)
        {
            CurrentFlowRate = flowRate;
            FlowReversed = flowRate < 0;
            _displayFlow = Mathf.Abs(flowRate);

            float maxFlow = 80f;
            FlowSpeed = Mathf.Clamp01(_displayFlow / maxFlow);

            if (_meshBuilt)
            {
                UpdateMaterial();
            }
        }

        private void BuildTunnelMesh()
        {
            Vector3 direction = EndPosition - StartPosition;
            float length = direction.magnitude;
            if (length < 0.01f) return;

            direction.Normalize();

            Vector3 up = Mathf.Abs(direction.y) < 0.9f ? Vector3.up : Vector3.right;
            Vector3 right = Vector3.Cross(direction, up).normalized;
            up = Vector3.Cross(right, direction).normalized;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            for (int l = 0; l <= LengthSegments; l++)
            {
                float t = (float)l / LengthSegments;
                float v = t;
                Vector3 center = StartPosition + direction * length * t;

                for (int r = 0; r <= RadialSegments; r++)
                {
                    float angle = ((float)r / RadialSegments) * Mathf.PI * 2f;
                    float u = (float)r / RadialSegments;

                    Vector3 offset = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * Radius;
                    Vector3 vertex = center + offset;
                    Vector3 normal = offset.normalized;

                    vertices.Add(vertex - transform.position);
                    normals.Add(normal);
                    uvs.Add(new Vector2(u, v));
                }
            }

            for (int l = 0; l < LengthSegments; l++)
            {
                for (int r = 0; r < RadialSegments; r++)
                {
                    int a = l * (RadialSegments + 1) + r;
                    int b = a + 1;
                    int c = a + RadialSegments + 1;
                    int d = c + 1;

                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(d);
                }
            }

            var mesh = new Mesh
            {
                name = $"Tunnel_E{EdgeId}"
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            _meshFilter.mesh = mesh;
        }

        private void UpdateMaterial()
        {
            if (_tunnelMaterial == null)
            {
                _tunnelMaterial = new Material(Shader.Find("Standard"));
                _tunnelMaterial.SetFloat("_Glossiness", 0.1f);
                _tunnelMaterial.SetFloat("_Metallic", 0.0f);
                _meshRenderer.material = _tunnelMaterial;
            }

            if (IsAlarm)
            {
                Color alarmBase = new Color(1.0f, 0.15f, 0.0f);
                Color alarmPulse = Color.Lerp(alarmBase, new Color(1.0f, 0.5f, 0.0f), AlarmPulse);
                _tunnelMaterial.SetColor("_Color", alarmPulse);
                _tunnelMaterial.SetColor("_EmissionColor", alarmPulse * (1.5f + AlarmPulse * 1.5f));
                return;
            }

            if (IsWarning)
            {
                Color warnColor = Color.Lerp(
                    new Color(0.6f, 0.6f, 0.2f),
                    new Color(1.0f, 0.7f, 0.0f),
                    Mathf.Clamp01(MethaneConcentration / 0.05f));
                Color displayColor = Color.Lerp(BaseColor, warnColor, 0.6f);
                _tunnelMaterial.SetColor("_Color", displayColor);
                _tunnelMaterial.SetColor("_EmissionColor", warnColor * 0.5f);
                return;
            }

            Color normalColor;
            if (FlowReversed)
            {
                normalColor = ReverseFlowColor;
            }
            else
            {
                normalColor = Color.Lerp(LowFlowColor, HighFlowColor, FlowSpeed);
            }

            _tunnelMaterial.SetColor("_Color", Color.Lerp(BaseColor, normalColor, 0.5f));
            _tunnelMaterial.SetColor("_EmissionColor", normalColor * FlowSpeed * 0.3f);
        }

        public void SetMethaneConcentration(float concentration, bool isAlarm, bool isWarning, float pulse)
        {
            MethaneConcentration = concentration;
            IsAlarm = isAlarm;
            IsWarning = isWarning;
            AlarmPulse = pulse;

            if (_meshBuilt)
            {
                UpdateMaterial();
            }
        }

        public Vector3 GetTunnelDirection()
        {
            Vector3 dir = EndPosition - StartPosition;
            if (FlowReversed) dir = -dir;
            return dir.normalized;
        }

        public float GetTunnelLength()
        {
            return Vector3.Distance(StartPosition, EndPosition);
        }

        public Vector3 GetRandomPointInTunnel()
        {
            float t = UnityEngine.Random.Range(0.1f, 0.9f);
            Vector3 center = Vector3.Lerp(StartPosition, EndPosition, t);

            Vector3 direction = EndPosition - StartPosition;
            direction.Normalize();
            Vector3 up = Mathf.Abs(direction.y) < 0.9f ? Vector3.up : Vector3.right;
            Vector3 right = Vector3.Cross(direction, up).normalized;
            up = Vector3.Cross(right, direction).normalized;

            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float r = UnityEngine.Random.Range(0f, Radius * 0.7f);

            return center + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * r;
        }

        public Vector3 GetWorldPosition()
        {
            return transform.position;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(StartPosition, EndPosition);
            Gizmos.DrawWireSphere(StartPosition, 0.5f);
            Gizmos.DrawWireSphere(EndPosition, 0.5f);
        }
    }
}
