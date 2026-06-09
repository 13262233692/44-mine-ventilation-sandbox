#if UNITY_EDITOR
using MineVentilation;
using MineVentilation.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MineVentilation.Editor
{
    public class DemoSceneSetup : MonoBehaviour
    {
        [MenuItem("矿井通风/创建演示场景", false, 1)]
        public static void CreateDemoScene()
        {
            var existing = FindObjectOfType<VentilationSimulator>();
            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog("提示", "场景中已存在通风模拟器，是否重新创建？", "是", "否"))
                    return;
                DestroyImmediate(existing.gameObject);
            }

            var simulatorGO = new GameObject("VentilationSimulator");
            simulatorGO.AddComponent<VentilationSimulator>();

            SetupCamera();

            SetupLighting();

            Selection.activeGameObject = simulatorGO;

            Debug.Log("[矿井通风] 演示场景创建完成，按Play运行模拟");
        }

        [MenuItem("矿井通风/创建复杂网络场景", false, 2)]
        public static void CreateComplexNetworkScene()
        {
            var existing = FindObjectOfType<VentilationSimulator>();
            if (existing != null)
            {
                DestroyImmediate(existing.gameObject);
            }

            var simulatorGO = new GameObject("VentilationSimulator");
            var simulator = simulatorGO.AddComponent<VentilationSimulator>();

            SetupCamera();
            SetupLighting();

            BuildComplexNetwork(simulator);

            Selection.activeGameObject = simulatorGO;

            Debug.Log("[矿井通风] 复杂网络场景创建完成");
        }

        static void SetupCamera()
        {
            var mainCam = Camera.main;
            if (mainCam == null)
            {
                var camGO = new GameObject("Main Camera");
                camGO.tag = "MainCamera";
                mainCam = camGO.AddComponent<Camera>();
                camGO.AddComponent<AudioListener>();
            }

            mainCam.transform.position = new Vector3(0f, 80f, -120f);
            mainCam.transform.LookAt(Vector3.down * 40f);
            mainCam.clearFlags = CameraClearFlags.SolidColor;
            mainCam.backgroundColor = new Color(0.05f, 0.05f, 0.1f);

            mainCam.nearClipPlane = 0.3f;
            mainCam.farClipPlane = 1000f;
        }

        static void SetupLighting()
        {
            var dirLight = FindObjectOfType<Light>();
            if (dirLight == null)
            {
                var lightGO = new GameObject("Directional Light");
                dirLight = lightGO.AddComponent<Light>();
            }

            dirLight.type = LightType.Directional;
            dirLight.color = new Color(0.95f, 0.92f, 0.85f);
            dirLight.intensity = 1.2f;
            dirLight.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            dirLight.shadows = LightShadows.Soft;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.15f, 0.15f, 0.2f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.05f, 0.05f, 0.1f);
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.003f;
        }

        static void BuildComplexNetwork(VentilationSimulator simulator)
        {
            var network = new VentilationNetwork();

            network.AddNode(0, "主井口", new Vector3(0f, 0f, 0f));
            network.AddNode(1, "副井口", new Vector3(15f, 0f, 0f));
            network.AddNode(2, "井底车场A", new Vector3(0f, -60f, 0f));
            network.AddNode(3, "井底车场B", new Vector3(15f, -60f, 0f));
            network.AddNode(4, "中央变电所", new Vector3(7f, -60f, -20f));
            network.AddNode(5, "东翼大巷起点", new Vector3(7f, -60f, 20f));
            network.AddNode(6, "西翼大巷起点", new Vector3(7f, -60f, -50f));
            network.AddNode(7, "东一采区上山口", new Vector3(50f, -60f, 20f));
            network.AddNode(8, "东二采区上山口", new Vector3(90f, -60f, 20f));
            network.AddNode(9, "西一采区上山口", new Vector3(-40f, -60f, -50f));
            network.AddNode(10, "西二采区上山口", new Vector3(-80f, -60f, -50f));
            network.AddNode(11, "东一工作面", new Vector3(50f, -90f, 20f));
            network.AddNode(12, "东二工作面", new Vector3(90f, -90f, 20f));
            network.AddNode(13, "西一工作面", new Vector3(-40f, -90f, -50f));
            network.AddNode(14, "西二工作面", new Vector3(-80f, -90f, -50f));
            network.AddNode(15, "东翼回风汇合", new Vector3(70f, -90f, -20f));
            network.AddNode(16, "西翼回风汇合", new Vector3(-60f, -90f, -20f));
            network.AddNode(17, "总回风巷", new Vector3(0f, -90f, -20f));
            network.AddNode(18, "回风井底", new Vector3(0f, -90f, -40f));
            network.AddNode(19, "回风井口", new Vector3(0f, 0f, -40f));

            network.AddFan(0, "主通风机", 3500f, -35f, -0.6f);
            network.AddFan(1, "东一局扇", 800f, -15f, -0.3f);
            network.AddFan(2, "东二局扇", 750f, -14f, -0.25f);
            network.AddFan(3, "西一局扇", 700f, -13f, -0.25f);
            network.AddFan(4, "西二局扇", 650f, -12f, -0.2f);

            network.AddEdge(0, "主井", 0, 2, 0.04f, 60f, 14f, 15f, 35f);
            network.AddEdge(1, "副井", 1, 3, 0.045f, 60f, 12f, 13f, 25f);
            network.AddEdge(2, "井底联络巷", 2, 3, 0.01f, 15f, 10f, 12f, 10f);
            network.AddEdge(3, "车场A→中央", 2, 4, 0.015f, 20f, 10f, 12f, 15f);
            network.AddEdge(4, "车场B→中央", 3, 4, 0.015f, 20f, 10f, 12f, 15f);
            network.AddEdge(5, "中央→东翼", 4, 5, 0.02f, 40f, 10f, 13f, 20f);
            network.AddEdge(6, "中央→西翼", 4, 6, 0.025f, 30f, 10f, 13f, 18f);
            network.AddEdge(7, "东翼大巷", 5, 7, 0.015f, 43f, 10f, 13f, 22f);
            network.AddEdge(8, "东翼大巷延伸", 7, 8, 0.02f, 40f, 10f, 13f, 18f);
            network.AddEdge(9, "西翼大巷", 6, 9, 0.02f, 33f, 10f, 13f, 18f);
            network.AddEdge(10, "西翼大巷延伸", 9, 10, 0.025f, 40f, 10f, 13f, 15f);
            network.AddEdge(11, "东一上山", 7, 11, 0.1f, 30f, 6f, 9f, 6f, fanId: 1);
            network.AddEdge(12, "东二上山", 8, 12, 0.12f, 30f, 6f, 9f, 5f, fanId: 2);
            network.AddEdge(13, "西一上山", 9, 13, 0.11f, 30f, 6f, 9f, 5f, fanId: 3);
            network.AddEdge(14, "西二上山", 10, 14, 0.13f, 30f, 6f, 9f, 4f, fanId: 4);
            network.AddEdge(15, "东一回风", 11, 15, 0.08f, 40f, 7f, 10f, 6f);
            network.AddEdge(16, "东二回风", 12, 15, 0.09f, 40f, 7f, 10f, 5f);
            network.AddEdge(17, "西一回风", 13, 16, 0.085f, 40f, 7f, 10f, 5f);
            network.AddEdge(18, "西二回风", 14, 16, 0.095f, 40f, 7f, 10f, 4f);
            network.AddEdge(19, "东翼回风巷", 15, 17, 0.03f, 70f, 8f, 11f, 12f);
            network.AddEdge(20, "西翼回风巷", 16, 17, 0.035f, 60f, 8f, 11f, 10f);
            network.AddEdge(21, "总回风巷", 17, 18, 0.02f, 20f, 10f, 13f, 25f);
            network.AddEdge(22, "回风井", 18, 19, 0.05f, 50f, 12f, 14f, 20f, fanId: 0);

            network.AddEdge(23, "东翼联络巷", 7, 15, 0.4f, 25f, 4f, 7f, 2f);
            network.AddEdge(24, "西翼联络巷", 9, 16, 0.5f, 25f, 4f, 7f, 1.5f);
            network.AddEdge(25, "东西翼联络", 5, 6, 0.6f, 30f, 5f, 8f, 2f);

            network.FindIndependentLoops();

            Debug.Log($"[矿井通风] 复杂网络: {network.Nodes.Count}节点, {network.Edges.Count}巷道, {network.Loops.Count}回路");
        }

        [MenuItem("矿井通风/模拟火灾场景", false, 3)]
        public static void SimulateFireScenario()
        {
            var simulator = FindObjectOfType<VentilationSimulator>();
            if (simulator == null)
            {
                EditorUtility.DisplayDialog("错误", "请先创建演示场景", "确定");
                return;
            }

            var network = simulator.Network;
            if (network == null) return;

            var fireEdge = network.GetEdge(7);
            if (fireEdge != null)
            {
                float originalR = fireEdge.Resistance;
                fireEdge.Resistance = originalR * 5f;
                Debug.Log($"[火灾模拟] 巷道'{fireEdge.Name}'风阻从{originalR:F3}升至{fireEdge.Resistance:F3}(火灾阻塞)");

                var fanEdge = network.Edges.Find(e => e.FanId == 1);
                if (fanEdge != null)
                {
                    network.GetFan(1).A0 = 1200f;
                    Debug.Log("[火灾模拟] 东一局扇增压至1200Pa(应急通风)");
                }

                simulator.RunSolver();
                simulator.BuildVisualization();
            }
        }

        [MenuItem("矿井通风/恢复正常通风", false, 4)]
        public static void RestoreNormalVentilation()
        {
            var simulator = FindObjectOfType<VentilationSimulator>();
            if (simulator == null) return;

            simulator.BuildDemoNetwork();
            simulator.RunSolver();
            simulator.BuildVisualization();
            Debug.Log("[矿井通风] 已恢复正常通风状态");
        }

        [MenuItem("矿井通风/压力测试(含短路风门)", false, 5)]
        public static void StressTestWithShortCircuit()
        {
            var simulator = FindObjectOfType<VentilationSimulator>();
            if (simulator == null)
            {
                CreateDemoScene();
                simulator = FindObjectOfType<VentilationSimulator>();
            }

            if (simulator != null)
            {
                simulator.SolverMode = SolverMethod.HybridSequential;
                simulator.BuildStressTestNetwork();
                simulator.RunSolverAsync();
                Debug.Log("[矿井通风] 压力测试网络已加载(含短路风门,混合解算模式)");
            }
        }

        [MenuItem("矿井通风/强制Newton-Raphson解算", false, 6)]
        public static void ForceNewtonRaphson()
        {
            var simulator = FindObjectOfType<VentilationSimulator>();
            if (simulator == null) return;

            simulator.SolverMode = SolverMethod.NewtonRaphsonOnly;
            simulator.RunSolver();
            simulator.BuildVisualization();
            Debug.Log("[矿井通风] 已切换为Newton-Raphson解算模式");
        }

        [MenuItem("矿井通风/导出网络数据", false, 10)]
        public static void ExportNetworkData()
        {
            var simulator = FindObjectOfType<VentilationSimulator>();
            if (simulator == null || simulator.Network == null) return;

            var network = simulator.Network;
            var result = simulator.LastResult;
            string exportPath = EditorUtility.SaveFilePanel("导出网络数据", "", "ventilation_data", "csv");

            if (string.IsNullOrEmpty(exportPath)) return;

            using (var writer = new System.IO.StreamWriter(exportPath, false, System.Text.Encoding.UTF8))
            {
                writer.WriteLine("巷道ID,巷道名称,起点,终点,风阻(Ns²/m⁸),风量(m³/s),压降(Pa),风向,风机ID");

                for (int i = 0; i < network.Edges.Count; i++)
                {
                    var edge = network.Edges[i];
                    float flow = result != null && i < result.BranchFlows.Count ? result.BranchFlows[i] : 0f;
                    float pressure = result != null && i < result.BranchPressures.Count ? result.BranchPressures[i] : 0f;
                    string direction = flow >= 0 ? "正向" : "反向";
                    string fanId = edge.FanId >= 0 ? edge.FanId.ToString() : "无";

                    writer.WriteLine($"{edge.Id},{edge.Name},{edge.FromNodeId},{edge.ToNodeId},{edge.Resistance:F4},{flow:F2},{pressure:F2},{direction},{fanId}");
                }
            }

            Debug.Log($"[矿井通风] 数据已导出至: {exportPath}");
        }
    }
}
#endif
