using MineVentilation.Core;
using MineVentilation.Visualization;
using UnityEngine;

namespace MineVentilation
{
    public class VentilationSimulator : MonoBehaviour
    {
        [Header("Solver Settings")]
        public float ConvergenceTolerance = 0.001f;
        public int MaxIterations = 500;
        public float RelaxationFactor = 1.0f;
        public bool EnableDamping = true;
        public float DampingThreshold = 50f;

        [Header("Visualization Settings")]
        public float WorldScale = 1f;
        public float DefaultTunnelRadius = 2.5f;

        [Header("Real-time Update")]
        public bool AutoResolveOnChange = true;
        public float ResolveInterval = 0.5f;

        [Header("UI References")]
        public UnityEngine.UI.Text StatusText;
        public UnityEngine.UI.Text IterationText;
        public UnityEngine.UI.Text ConvergenceText;

        [Header("Camera Control")]
        public Transform CameraTarget;
        public float CameraOrbitSpeed = 20f;
        public float CameraZoomSpeed = 5f;
        public float CameraMinDistance = 10f;
        public float CameraMaxDistance = 200f;

        private VentilationNetwork _network;
        private HardyCrossSolver _solver;
        private VentilationNetworkMapper _mapper;
        private SolverResult _lastResult;
        private float _resolveTimer;
        private bool _networkDirty;

        public VentilationNetwork Network => _network;
        public HardyCrossSolver Solver => _solver;
        public SolverResult LastResult => _lastResult;
        public VentilationNetworkMapper Mapper => _mapper;

        void Awake()
        {
            _mapper = gameObject.AddComponent<VentilationNetworkMapper>();
        }

        void Start()
        {
            BuildDemoNetwork();
            RunSolver();
            BuildVisualization();
        }

        void Update()
        {
            HandleCameraControl();
            HandleKeyboardInput();

            if (AutoResolveOnChange && _networkDirty)
            {
                _resolveTimer += Time.deltaTime;
                if (_resolveTimer >= ResolveInterval)
                {
                    _resolveTimer = 0f;
                    _networkDirty = false;
                    RunSolver();
                    _mapper.ApplySolverResults(_lastResult);
                    UpdateUI();
                }
            }
        }

        public void BuildDemoNetwork()
        {
            _network = new VentilationNetwork();

            _network.AddNode(0, "进风井口", new Vector3(0f, 0f, 0f));
            _network.AddNode(1, "井底车场", new Vector3(0f, -50f, 0f));
            _network.AddNode(2, "运输大巷东", new Vector3(60f, -50f, 0f));
            _network.AddNode(3, "运输大巷西", new Vector3(-60f, -50f, 0f));
            _network.AddNode(4, "采区A上口", new Vector3(60f, -50f, 50f));
            _network.AddNode(5, "采区B上口", new Vector3(-60f, -50f, 50f));
            _network.AddNode(6, "采区A工作面", new Vector3(60f, -80f, 50f));
            _network.AddNode(7, "采区B工作面", new Vector3(-60f, -80f, 50f));
            _network.AddNode(8, "回风巷东", new Vector3(60f, -80f, -30f));
            _network.AddNode(9, "回风巷西", new Vector3(-60f, -80f, -30f));
            _network.AddNode(10, "回风井底", new Vector3(0f, -80f, -30f));
            _network.AddNode(11, "回风井口", new Vector3(0f, 0f, -30f));

            _network.AddFan(0, "主通风机", 3000f, -30f, -0.5f);
            _network.AddFan(1, "局部通风机A", 800f, -15f, -0.3f);
            _network.AddFan(2, "局部通风机B", 600f, -12f, -0.2f);

            _network.AddEdge(0, "进风井", 0, 1, 0.05f, 50f, 12f, 14f, 17.5f, fanId: -1);
            _network.AddEdge(1, "井底车场东", 1, 2, 0.02f, 60f, 10f, 13f, 10f);
            _network.AddEdge(2, "井底车场西", 1, 3, 0.025f, 60f, 10f, 13f, 7.5f);
            _network.AddEdge(3, "采区A上山", 2, 4, 0.08f, 50f, 6f, 9f, 8f);
            _network.AddEdge(4, "采区B上山", 3, 5, 0.1f, 50f, 6f, 9f, 6f);
            _network.AddEdge(5, "采区A进风", 4, 6, 0.15f, 30f, 5f, 8f, 8f, fanId: 1);
            _network.AddEdge(6, "采区B进风", 5, 7, 0.18f, 30f, 5f, 8f, 6f, fanId: 2);
            _network.AddEdge(7, "采区A回风", 6, 8, 0.12f, 80f, 6f, 9f, 8f);
            _network.AddEdge(8, "采区B回风", 7, 9, 0.14f, 80f, 6f, 9f, 6f);
            _network.AddEdge(9, "东翼回风巷", 8, 10, 0.04f, 60f, 8f, 11f, 10f);
            _network.AddEdge(10, "西翼回风巷", 9, 10, 0.05f, 60f, 8f, 11f, 7.5f);
            _network.AddEdge(11, "回风井", 10, 11, 0.06f, 50f, 10f, 12f, 17.5f, fanId: 0);

            _network.AddEdge(12, "东翼联络巷", 2, 8, 0.5f, 30f, 4f, 7f, 2f);
            _network.AddEdge(13, "西翼联络巷", 3, 9, 0.6f, 30f, 4f, 7f, 1.5f);

            _network.FindIndependentLoops();

            Debug.Log($"[通风模拟] 网络构建完成: {_network.Nodes.Count}节点, {_network.Edges.Count}巷道, {_network.Loops.Count}独立回路, {_network.Fans.Count}风机");
        }

        public void RunSolver()
        {
            if (_network == null) return;

            if (_solver == null)
            {
                _solver = new HardyCrossSolver(_network);
            }

            _solver.ConvergenceTolerance = ConvergenceTolerance;
            _solver.MaxIterations = MaxIterations;
            _solver.RelaxationFactor = RelaxationFactor;
            _solver.EnableDamping = EnableDamping;
            _solver.DampingThreshold = DampingThreshold;

            _lastResult = _solver.Solve();

            Debug.Log($"[通风模拟] {_lastResult.Diagnostics}");
        }

        public void BuildVisualization()
        {
            if (_network == null || _mapper == null) return;
            _mapper.WorldScale = WorldScale;
            _mapper.DefaultTunnelRadius = DefaultTunnelRadius;
            _mapper.BuildVisualization(_network, _lastResult);
        }

        public void MarkDirty()
        {
            _networkDirty = true;
            _resolveTimer = 0f;
        }

        public void UpdateEdgeResistance(int edgeId, float newResistance)
        {
            if (_network == null) return;
            var edge = _network.GetEdge(edgeId);
            if (edge != null)
            {
                edge.Resistance = newResistance;
                MarkDirty();
            }
        }

        public void UpdateFanPressure(int fanId, float a0, float a1, float a2)
        {
            if (_network == null) return;
            var fan = _network.GetFan(fanId);
            if (fan != null)
            {
                fan.A0 = a0;
                fan.A1 = a1;
                fan.A2 = a2;
                MarkDirty();
            }
        }

        public void ToggleFan(int fanId, bool enabled)
        {
            if (_network == null) return;
            var fan = _network.GetFan(fanId);
            if (fan != null && !enabled)
            {
                fan.A0 = 0f;
                fan.A1 = 0f;
                fan.A2 = 0f;
                MarkDirty();
            }
        }

        void HandleCameraControl()
        {
            if (CameraTarget == null)
            {
                CameraTarget = transform;
            }

            var cam = Camera.main;
            if (cam == null) return;

            if (Input.GetMouseButton(1))
            {
                float h = Input.GetAxis("Mouse X") * CameraOrbitSpeed * Time.deltaTime;
                float v = Input.GetAxis("Mouse Y") * CameraOrbitSpeed * Time.deltaTime;

                cam.transform.RotateAround(CameraTarget.position, Vector3.up, h);
                cam.transform.RotateAround(CameraTarget.position, cam.transform.right, -v);
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.01f)
            {
                Vector3 dir = (cam.transform.position - CameraTarget.position).normalized;
                float dist = Vector3.Distance(cam.transform.position, CameraTarget.position);
                dist -= scroll * CameraZoomSpeed * dist * 0.1f;
                dist = Mathf.Clamp(dist, CameraMinDistance, CameraMaxDistance);
                cam.transform.position = CameraTarget.position + dir * dist;
            }
        }

        void HandleKeyboardInput()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                RunSolver();
                _mapper.ApplySolverResults(_lastResult);
                UpdateUI();
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                _network.ResetAllFlows();
                RunSolver();
                BuildVisualization();
            }

            if (Input.GetKeyDown(KeyCode.D))
            {
                BuildDemoNetwork();
                RunSolver();
                BuildVisualization();
            }
        }

        void UpdateUI()
        {
            if (_lastResult == null) return;

            if (StatusText != null)
            {
                StatusText.text = _lastResult.Converged ? "✓ 收敛" : "✗ 未收敛";
                StatusText.color = _lastResult.Converged ? Color.green : Color.red;
            }

            if (IterationText != null)
            {
                IterationText.text = $"迭代次数: {_lastResult.Iterations}";
            }

            if (ConvergenceText != null)
            {
                ConvergenceText.text = $"最大残差: {_lastResult.MaxResidual:F6}";
            }
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 380, 600));
            GUILayout.Label("<b>深部矿井通风模拟沙盘</b>", new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold });
            GUILayout.Space(8);

            if (_network != null)
            {
                GUILayout.Label($"网络: {_network.Nodes.Count}节点 | {_network.Edges.Count}巷道 | {_network.Loops.Count}回路");

                if (_lastResult != null)
                {
                    GUILayout.Label(_lastResult.Diagnostics);
                }

                GUILayout.Space(5);
                GUILayout.Label("─── 各巷道风量 ───", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });

                for (int i = 0; i < _network.Edges.Count; i++)
                {
                    var edge = _network.Edges[i];
                    float flow = _lastResult != null && i < _lastResult.BranchFlows.Count
                        ? _lastResult.BranchFlows[i]
                        : 0f;

                    string direction = flow >= 0 ? "→" : "←";
                    string fanLabel = edge.FanId >= 0 ? $" [风机{edge.FanId}]" : "";

                    Color prevColor = GUI.color;
                    if (Mathf.Abs(flow) < 4f) GUI.color = Color.red;
                    else if (Mathf.Abs(flow) > 50f) GUI.color = Color.cyan;
                    else GUI.color = Color.white;

                    GUILayout.Label($"{edge.Name}{fanLabel}: {direction} {Mathf.Abs(flow):F2} m³/s");
                    GUI.color = prevColor;
                }

                GUILayout.Space(5);
                GUILayout.Label("─── 操作 ───", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
                GUILayout.Label("[Space] 重新解算  [R] 重置风量  [D] 加载演示");
                GUILayout.Label("[右键拖拽] 旋转  [滚轮] 缩放");
            }

            GUILayout.EndArea();
        }
    }
}
