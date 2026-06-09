using System.Collections;
using MineVentilation.Core;
using MineVentilation.Visualization;
using UnityEngine;

namespace MineVentilation
{
    public class VentilationSimulator : MonoBehaviour
    {
        [Header("Solver Settings")]
        public SolverMethod SolverMode = SolverMethod.HybridSequential;
        public float ConvergenceTolerance = 0.001f;
        public int MaxIterationsHardyCross = 200;
        public int MaxIterationsNewtonRaphson = 80;
        public bool UseAsyncSolving = true;
        public float SolverTimeoutSeconds = 5f;

        [Header("Visualization Settings")]
        public float WorldScale = 1f;
        public float DefaultTunnelRadius = 2.5f;

        [Header("Real-time Update")]
        public bool AutoResolveOnChange = true;
        public float ResolveInterval = 0.5f;

        [Header("Methane Hazard")]
        public float MethaneExplosionLimit = 0.05f;
        public float MethaneWarningLimit = 0.01f;
        public float MethaneSimulationSpeed = 1.0f;
        public float MethaneOutburstRate = 0.15f;
        public float MethaneOutburstDuration = 30f;
        public float MethaneDiffusion = 0.22f;
        public float MethaneCellSize = 5f;
        public float ClickDetectRadius = 20f;

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
        private HybridVentilationSolver _solver;
        private VentilationNetworkMapper _mapper;
        private MethaneHazardManager _hazardManager;
        private SolverResult _lastResult;
        private float _resolveTimer;
        private bool _networkDirty;
        private bool _isSolving;
        private string _solverStatus = "";
        private float _solverStartTime;
        private int _selectedNodeId = -1;

        public VentilationNetwork Network => _network;
        public HybridVentilationSolver Solver => _solver;
        public SolverResult LastResult => _lastResult;
        public VentilationNetworkMapper Mapper => _mapper;
        public MethaneHazardManager HazardManager => _hazardManager;
        public bool IsSolving => _isSolving;

        void Awake()
        {
            _mapper = gameObject.AddComponent<VentilationNetworkMapper>();
            _hazardManager = gameObject.AddComponent<MethaneHazardManager>();
        }

        void Start()
        {
            BuildDemoNetwork();
            RunSolver();
            BuildVisualization();
            InitializeHazardManager();
        }

        void Update()
        {
            HandleCameraControl();
            HandleKeyboardInput();
            HandleMouseClick();

            if (_hazardManager != null && _hazardManager.SimulationActive)
            {
                _hazardManager.UpdateSimulation(Time.deltaTime);
            }

            if (_isSolving)
            {
                if (Time.time - _solverStartTime > SolverTimeoutSeconds)
                {
                    Debug.LogWarning("[通风模拟] 解算超时,强制终止");
                    _isSolving = false;
                    _solverStatus = "超时终止";
                }
            }

            if (AutoResolveOnChange && _networkDirty && !_isSolving)
            {
                _resolveTimer += Time.deltaTime;
                if (_resolveTimer >= ResolveInterval)
                {
                    _resolveTimer = 0f;
                    _networkDirty = false;
                    RunSolverAsync();
                }
            }
        }

        void InitializeHazardManager()
        {
            if (_hazardManager != null && _network != null)
            {
                _hazardManager.ExplosionLimit = MethaneExplosionLimit;
                _hazardManager.WarningLimit = MethaneWarningLimit;
                _hazardManager.SimulationSpeed = MethaneSimulationSpeed;
                _hazardManager.OutburstEmissionRate = MethaneOutburstRate;
                _hazardManager.OutburstDuration = MethaneOutburstDuration;
                _hazardManager.DiffusionCoefficient = MethaneDiffusion;
                _hazardManager.CellSize = MethaneCellSize;
                _hazardManager.Initialize(_network, _mapper);
            }
        }

        void HandleMouseClick()
        {
            if (Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1))
            {
                var cam = Camera.main;
                if (cam == null) return;

                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                RaycastHit hit;

                if (Physics.Raycast(ray, out hit, 500f))
                {
                    int nodeId = _hazardManager.DetectClickedNode(hit.point, ClickDetectRadius);
                    if (nodeId >= 0)
                    {
                        _selectedNodeId = nodeId;
                        var node = _network.GetNode(nodeId);
                        Debug.Log($"[通风模拟] 选中节点: {node.Name} (ID:{nodeId})");
                    }
                }
            }
        }

        void TriggerOutburstAtSelectedNode()
        {
            if (_selectedNodeId < 0 || _hazardManager == null) return;

            var node = _network.GetNode(_selectedNodeId);
            if (node == null) return;

            _hazardManager.TriggerOutburstAtNode(_selectedNodeId);
            Debug.Log($"[瓦斯突出] 在'{node.Name}'触发瓦斯突出! 排放速率{MethaneOutburstRate}/s");
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

        public void BuildStressTestNetwork()
        {
            _network = new VentilationNetwork();

            _network.AddNode(0, "进风井口", new Vector3(0f, 0f, 0f));
            _network.AddNode(1, "井底车场", new Vector3(0f, -80f, 0f));

            _network.AddFan(0, "主通风机", 5000f, -50f, -1.0f);

            int nodeId = 2;
            int edgeId = 0;

            _network.AddEdge(edgeId++, "主井", 0, 1, 0.03f, 80f, 16f, 17f, 30f);

            int numLevels = 4;
            int numPanelsPerLevel = 5;

            for (int level = 0; level < numLevels; level++)
            {
                float levelDepth = -80f - (level + 1) * 60f;
                float levelZ = (level + 1) * 40f;

                int intakeNodeId = nodeId++;
                _network.AddNode(intakeNodeId, $"水平{level + 1}进风", new Vector3(0f, levelDepth, levelZ));
                _network.AddEdge(edgeId++, $"进风石门{level + 1}", 1, intakeNodeId, 0.02f + level * 0.005f, 60f, 10f, 13f, 15f - level * 2f);

                int returnCollectNodeId = nodeId++;
                _network.AddNode(returnCollectNodeId, $"水平{level + 1}回风汇", new Vector3(0f, levelDepth - 30f, levelZ));

                for (int panel = 0; panel < numPanelsPerLevel; panel++)
                {
                    float panelX = (panel - numPanelsPerLevel / 2f) * 50f;
                    int panelIntakeId = nodeId++;
                    _network.AddNode(panelIntakeId, $"L{level + 1}P{panel + 1}进风", new Vector3(panelX, levelDepth, levelZ));

                    _network.AddEdge(edgeId++, $"L{level + 1}P{panel + 1}进风巷",
                        intakeNodeId, panelIntakeId, 0.03f + panel * 0.002f, 50f, 8f, 11f, 8f);

                    int faceId = nodeId++;
                    _network.AddNode(faceId, $"L{level + 1}P{panel + 1}工作面", new Vector3(panelX, levelDepth - 25f, levelZ));

                    float faceResistance = 0.1f + panel * 0.01f;
                    _network.AddEdge(edgeId++, $"L{level + 1}P{panel + 1}工作面",
                        panelIntakeId, faceId, faceResistance, 25f, 5f, 8f, 5f);

                    int panelReturnId = nodeId++;
                    _network.AddNode(panelReturnId, $"L{level + 1}P{panel + 1}回风", new Vector3(panelX, levelDepth - 30f, levelZ));

                    _network.AddEdge(edgeId++, $"L{level + 1}P{panel + 1}回风巷",
                        faceId, panelReturnId, 0.05f + panel * 0.003f, 30f, 7f, 10f, 5f);

                    _network.AddEdge(edgeId++, $"L{level + 1}P{panel + 1}回风汇",
                        panelReturnId, returnCollectNodeId, 0.02f, 40f, 8f, 11f, 6f);

                    if (panel > 0 && panel < numPanelsPerLevel)
                    {
                        float crossResistance = UnityEngine.Random.Range(0.001f, 0.005f);
                        int prevPanelIntakeId = panelIntakeId - 3;
                        if (_network.GetNode(prevPanelIntakeId) != null)
                        {
                            _network.AddEdge(edgeId++, $"L{level + 1}P{panel}-P{panel + 1}风门短路",
                                prevPanelIntakeId, panelIntakeId, crossResistance, 10f, 4f, 7f, 1f);
                        }
                    }
                }

                if (level > 0)
                {
                    int prevReturnCollectId = returnCollectNodeId - numPanelsPerLevel * 3 - 2;
                    if (_network.GetNode(prevReturnCollectId) != null)
                    {
                        _network.AddEdge(edgeId++, $"水平{level}-水平{level + 1}天井",
                            prevReturnCollectId, returnCollectNodeId, 0.15f, 60f, 5f, 8f, 3f);
                    }
                }

                int returnShaftBottomId = nodeId++;
                _network.AddNode(returnShaftBottomId, $"水平{level + 1}回风井底", new Vector3(0f, levelDepth - 30f, 0f));
                _network.AddEdge(edgeId++, $"水平{level + 1}总回风",
                    returnCollectNodeId, returnShaftBottomId, 0.025f, 40f, 10f, 13f, 10f);
            }

            int returnShaftTopId = nodeId;
            _network.AddNode(returnShaftTopId, "回风井口", new Vector3(0f, 0f, -40f));
            _network.AddEdge(edgeId++, "回风井", nodeId - 1, returnShaftTopId, 0.04f, 80f, 14f, 16f, 20f, fanId: 0);

            _network.FindIndependentLoops();

            Debug.Log($"[通风模拟] 压力测试网络: {_network.Nodes.Count}节点, {_network.Edges.Count}巷道, {_network.Loops.Count}独立回路");
        }

        public void RunSolver()
        {
            if (_network == null) return;

            EnsureSolver();

            _solverStartTime = Time.time;
            _isSolving = true;
            _solverStatus = "解算中...";

            _solver.OnSolverProgress += OnSolverProgress;
            _lastResult = _solver.Solve();
            _solver.OnSolverProgress -= OnSolverProgress;

            _isSolving = false;
            _solverStatus = _lastResult.Converged ? "已收敛" : "未收敛";

            Debug.Log($"[通风模拟] {_lastResult.Diagnostics}");
        }

        public void RunSolverAsync()
        {
            if (_network == null || _isSolving) return;

            EnsureSolver();

            if (UseAsyncSolving)
            {
                _solverStartTime = Time.time;
                _isSolving = true;
                _solverStatus = "异步解算中...";
                StartCoroutine(SolveCoroutine());
            }
            else
            {
                RunSolver();
                _mapper.ApplySolverResults(_lastResult);
                UpdateUI();
            }
        }

        IEnumerator SolveCoroutine()
        {
            SolverResult result = null;

            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    _solver.OnSolverProgress += OnSolverProgress;
                    result = _solver.Solve();
                    _solver.OnSolverProgress -= OnSolverProgress;
                }
                catch (System.Exception e)
                {
                    result = new SolverResult
                    {
                        Converged = false,
                        Diagnostics = $"解算异常: {e.Message}"
                    };
                }
            });

            thread.Start();

            while (thread.IsAlive)
            {
                if (Time.time - _solverStartTime > SolverTimeoutSeconds)
                {
                    Debug.LogWarning("[通风模拟] 解算超时,强制终止线程");
                    try { thread.Abort(); } catch { }
                    _isSolving = false;
                    _solverStatus = "超时终止";
                    yield break;
                }
                yield return null;
            }

            _lastResult = result;
            _isSolving = false;
            _solverStatus = _lastResult != null && _lastResult.Converged ? "已收敛" : "未收敛";

            if (_lastResult != null)
            {
                Debug.Log($"[通风模拟] {_lastResult.Diagnostics}");
                _mapper.ApplySolverResults(_lastResult);
                UpdateUI();
            }
        }

        void EnsureSolver()
        {
            if (_solver == null)
            {
                _solver = new HybridVentilationSolver(_network);
            }

            _solver.Method = SolverMode;
            _solver.ConvergenceTolerance = ConvergenceTolerance;
            _solver.MaxIterationsHardyCross = MaxIterationsHardyCross;
            _solver.MaxIterationsNewtonRaphson = MaxIterationsNewtonRaphson;
        }

        void OnSolverProgress(string message)
        {
            _solverStatus = message;
            Debug.Log($"[通风解算] {message}");
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
                InitializeHazardManager();
            }

            if (Input.GetKeyDown(KeyCode.D))
            {
                BuildDemoNetwork();
                RunSolver();
                BuildVisualization();
                InitializeHazardManager();
            }

            if (Input.GetKeyDown(KeyCode.T))
            {
                BuildStressTestNetwork();
                RunSolverAsync();
            }

            if (Input.GetKeyDown(KeyCode.M))
            {
                TriggerOutburstAtSelectedNode();
            }

            if (Input.GetKeyDown(KeyCode.C))
            {
                if (_hazardManager != null)
                {
                    _hazardManager.ResetSimulation();
                    Debug.Log("[瓦斯弥散] 已重置瓦斯模拟");
                }
            }

            if (Input.GetKeyDown(KeyCode.Alpha6))
            {
                _selectedNodeId = 6;
                TriggerOutburstAtSelectedNode();
            }

            if (Input.GetKeyDown(KeyCode.Alpha7))
            {
                _selectedNodeId = 7;
                TriggerOutburstAtSelectedNode();
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
            GUILayout.BeginArea(new Rect(10, 10, 440, 800));
            GUILayout.Label("<b>深部矿井通风模拟沙盘</b>", new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold });
            GUILayout.Space(5);

            if (_isSolving)
            {
                Color prevC = GUI.color;
                GUI.color = Color.yellow;
                GUILayout.Label($"⟳ {_solverStatus}");
                GUI.color = prevC;
            }

            if (_network != null)
            {
                GUILayout.Label($"网络: {_network.Nodes.Count}节点 | {_network.Edges.Count}巷道 | {_network.Loops.Count}回路");
                GUILayout.Label($"解算模式: {SolverMode}");

                if (_lastResult != null)
                {
                    GUILayout.Label(_lastResult.Diagnostics);
                }

                GUILayout.Space(3);

                if (_hazardManager != null && _hazardManager.SimulationActive)
                {
                    Color prevC = GUI.color;
                    GUI.color = _hazardManager.AlarmEdgeCount > 0 ? Color.red : Color.yellow;
                    GUILayout.Label($"⚠ 瓦斯弥散中 | 最高浓度: {_hazardManager.GlobalMaxConcentration:P1} | 报警巷道: {_hazardManager.AlarmEdgeCount} | 活跃源: {_hazardManager.ActiveSourceCount}");
                    GUI.color = prevC;
                }

                GUILayout.Space(3);

                if (_selectedNodeId >= 0)
                {
                    var selNode = _network.GetNode(_selectedNodeId);
                    if (selNode != null)
                    {
                        GUILayout.Label($"▶ 选中: {selNode.Name} (ID:{_selectedNodeId})");
                    }
                }

                GUILayout.Space(3);
                GUILayout.Label("─── 巷道风量 & 瓦斯浓度 ───", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });

                int maxDisplay = Mathf.Min(_network.Edges.Count, 25);
                for (int i = 0; i < maxDisplay; i++)
                {
                    var edge = _network.Edges[i];
                    float flow = _lastResult != null && i < _lastResult.BranchFlows.Count
                        ? _lastResult.BranchFlows[i]
                        : 0f;

                    string direction = flow >= 0 ? "→" : "←";
                    string fanLabel = edge.FanId >= 0 ? $" [风机{edge.FanId}]" : "";
                    string resistLabel = edge.Resistance < 0.01f ? " ⚠短路" : "";

                    float ch4 = _hazardManager != null ? _hazardManager.GetEdgeConcentration(edge.Id) : 0f;
                    bool isAlarm = _hazardManager != null && _hazardManager.IsEdgeInAlarm(edge.Id);
                    string ch4Label = ch4 > 0.001f ? $" | CH₄:{ch4:P1}" : "";
                    string alarmLabel = isAlarm ? " 💥超限!" : "";

                    Color prevColor = GUI.color;
                    if (isAlarm) GUI.color = new Color(1f, 0.2f, 0f);
                    else if (ch4 >= MethaneWarningLimit) GUI.color = Color.yellow;
                    else if (Mathf.Abs(flow) < 4f) GUI.color = Color.red;
                    else if (Mathf.Abs(flow) > 50f) GUI.color = Color.cyan;
                    else GUI.color = Color.white;

                    GUILayout.Label($"{edge.Name}{fanLabel}{resistLabel}: {direction} {Mathf.Abs(flow):F1}m³/s{ch4Label}{alarmLabel}");
                    GUI.color = prevColor;
                }

                if (_network.Edges.Count > maxDisplay)
                {
                    GUILayout.Label($"... 还有{_network.Edges.Count - maxDisplay}条巷道");
                }

                GUILayout.Space(5);
                GUILayout.Label("─── 操作 ───", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
                GUILayout.Label("[Space] 重新解算  [R] 重置  [D] 演示  [T] 压力测试");
                GUILayout.Label("[左键] 选中节点  [M] 触发瓦斯突出  [C] 清除瓦斯");
                GUILayout.Label("[6] 采区A突出  [7] 采区B突出");
                GUILayout.Label("[右键拖拽] 旋转  [滚轮] 缩放");
            }

            GUILayout.EndArea();
        }
    }
}
