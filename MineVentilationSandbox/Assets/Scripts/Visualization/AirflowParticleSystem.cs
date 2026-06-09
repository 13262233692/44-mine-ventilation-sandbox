using UnityEngine;

namespace MineVentilation.Visualization
{
    [RequireComponent(typeof(ParticleSystem))]
    public class AirflowParticleSystem : MonoBehaviour
    {
        [Header("Particle Appearance")]
        public Gradient NormalFlowColor = CreateDefaultGradient();
        public Gradient HighFlowColor = CreateHighFlowGradient();
        public Gradient ReverseFlowColor = CreateReverseGradient();
        public float MinParticleSize = 0.15f;
        public float MaxParticleSize = 0.4f;

        [Header("Particle Behavior")]
        public float BaseSpeed = 2f;
        public float SpeedFlowMultiplier = 0.15f;
        public float ParticleLifetime = 4f;
        public int BaseEmissionRate = 50;
        public float EmissionFlowMultiplier = 3f;
        public float MaxEmissionRate = 300f;

        [Header("Flow Data")]
        public float CurrentFlowRate;
        public bool FlowReversed;
        public float NormalizedFlowSpeed;

        [Header("Hazard State")]
        public bool InAlarmMode;
        public bool InWarningMode;
        public float AlarmIntensity;
        public float MethaneConcentration;

        private ParticleSystem _particleSystem;
        private ParticleSystem.MainModule _mainModule;
        private ParticleSystem.EmissionModule _emissionModule;
        private ParticleSystem.ShapeModule _shapeModule;
        private ParticleSystem.VelocityOverLifetimeModule _velocityModule;
        private ParticleSystem.ColorOverLifetimeModule _colorModule;

        private MineTunnelSegment _tunnelSegment;
        private bool _initialized;

        void Awake()
        {
            _particleSystem = GetComponent<ParticleSystem>();
            SetupParticleSystem();
        }

        void SetupParticleSystem()
        {
            _mainModule = _particleSystem.main;
            _mainModule.simulationSpace = ParticleSystemSimulationSpace.World;
            _mainModule.maxParticles = 5000;
            _mainModule.startLifetime = ParticleLifetime;
            _mainModule.startSpeed = 0f;
            _mainModule.startSize = new ParticleSystem.MinMaxCurve(MinParticleSize, MaxParticleSize);
            _mainModule.gravityModifier = 0f;
            _mainModule.playOnAwake = true;

            _emissionModule = _particleSystem.emission;
            _emissionModule.rateOverTime = BaseEmissionRate;

            _shapeModule = _particleSystem.shape;
            _shapeModule.shapeType = ParticleSystemShapeType.Box;
            _shapeModule.scale = new Vector3(1f, 1f, 1f);

            _velocityModule = _particleSystem.velocityOverLifetime;
            _velocityModule.enabled = true;
            _velocityModule.space = ParticleSystemSimulationSpace.World;

            _colorModule = _particleSystem.colorOverLifetime;
            _colorModule.enabled = true;
            _colorModule.color = new ParticleSystem.MinMaxGradient(NormalFlowColor);

            var noiseModule = _particleSystem.noise;
            noiseModule.enabled = true;
            noiseModule.strength = 0.2f;
            noiseModule.frequency = 0.5f;

            var sizeModule = _particleSystem.sizeOverLifetime;
            sizeModule.enabled = true;
            sizeModule.size = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.EaseInOut(0f, 0.3f, 1f, 1f));
        }

        public void Initialize(MineTunnelSegment tunnelSegment)
        {
            _tunnelSegment = tunnelSegment;
            _initialized = true;
            ConfigureShape();
        }

        void ConfigureShape()
        {
            if (_tunnelSegment == null) return;

            Vector3 direction = _tunnelSegment.GetTunnelDirection();
            float length = _tunnelSegment.GetTunnelLength();

            transform.position = Vector3.Lerp(_tunnelSegment.StartPosition, _tunnelSegment.EndPosition, 0.5f);

            _shapeModule.shapeType = ParticleSystemShapeType.Box;
            float radius = _tunnelSegment.Radius * 0.6f;
            _shapeModule.scale = new Vector3(radius * 2f, radius * 2f, length * 0.8f);

            if (direction != Vector3.zero)
            {
                transform.rotation = Quaternion.LookRotation(direction);
            }
        }

        public void UpdateFlowVisualization(float flowRate)
        {
            CurrentFlowRate = flowRate;
            FlowReversed = flowRate < 0;
            float absFlow = Mathf.Abs(flowRate);
            float maxFlow = 80f;
            NormalizedFlowSpeed = Mathf.Clamp01(absFlow / maxFlow);

            UpdateEmissionRate(absFlow);
            UpdateVelocity(flowRate);
            UpdateColor();
            UpdateParticleSize(absFlow);
        }

        void UpdateEmissionRate(float absFlow)
        {
            float rate = BaseEmissionRate + absFlow * EmissionFlowMultiplier;
            rate = Mathf.Clamp(rate, 10f, MaxEmissionRate);
            _emissionModule.rateOverTime = rate;
        }

        void UpdateVelocity(float flowRate)
        {
            if (_tunnelSegment == null) return;

            Vector3 direction = _tunnelSegment.GetTunnelDirection();
            if (flowRate < 0)
            {
                direction = -direction;
            }

            float speed = BaseSpeed + Mathf.Abs(flowRate) * SpeedFlowMultiplier;

            _velocityModule.x = new ParticleSystem.MinMaxCurve(direction.x * speed);
            _velocityModule.y = new ParticleSystem.MinMaxCurve(direction.y * speed);
            _velocityModule.z = new ParticleSystem.MinMaxCurve(direction.z * speed);
        }

        void UpdateColor()
        {
            Gradient colorGradient;
            if (FlowReversed)
            {
                colorGradient = ReverseFlowColor;
            }
            else if (NormalizedFlowSpeed > 0.6f)
            {
                colorGradient = HighFlowColor;
            }
            else
            {
                colorGradient = NormalFlowColor;
            }

            _colorModule.color = new ParticleSystem.MinMaxGradient(colorGradient);
        }

        void UpdateParticleSize(float absFlow)
        {
            float t = Mathf.Clamp01(absFlow / 80f);
            float size = Mathf.Lerp(MinParticleSize, MaxParticleSize, t);
            _mainModule.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size * 1.3f);
        }

        public void SetAlarmMode(bool alarm, float intensity = 1f, float concentration = 0f)
        {
            InAlarmMode = alarm;
            AlarmIntensity = intensity;
            MethaneConcentration = concentration;

            if (!alarm) return;

            _colorModule.color = new ParticleSystem.MinMaxGradient(CreateAlarmGradient(intensity, concentration));

            float alarmEmission = BaseEmissionRate * 4f * intensity;
            _emissionModule.rateOverTime = Mathf.Min(alarmEmission, 600f);

            _mainModule.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
            _mainModule.startLifetime = ParticleLifetime * 0.6f;

            var noiseModule = _particleSystem.noise;
            noiseModule.strength = 0.5f * intensity;
            noiseModule.frequency = 1.5f;
        }

        public void SetWarningMode(bool warning, float concentration = 0f)
        {
            InWarningMode = warning;
            if (InAlarmMode) return;

            if (!warning) return;

            _colorModule.color = new ParticleSystem.MinMaxGradient(CreateWarningGradient(concentration));

            float warnEmission = BaseEmissionRate * 2f;
            _emissionModule.rateOverTime = Mathf.Min(warnEmission, 400f);
        }

        static Gradient CreateAlarmGradient(float intensity, float concentration)
        {
            float t = Mathf.Clamp01(concentration / 0.15f);

            var g = new Gradient();
            g.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(Color.Lerp(
                        new Color(1.0f, 0.4f, 0.0f),
                        new Color(1.0f, 0.1f, 0.0f), t), 0f),
                    new GradientColorKey(Color.Lerp(
                        new Color(1.0f, 0.2f, 0.0f),
                        new Color(0.9f, 0.05f, 0.0f), t), 0.4f),
                    new GradientColorKey(Color.Lerp(
                        new Color(0.8f, 0.1f, 0.0f),
                        new Color(0.5f, 0.0f, 0.0f), t), 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0.3f * intensity, 0f),
                    new GradientAlphaKey(1.0f * intensity, 0.15f),
                    new GradientAlphaKey(0.9f * intensity, 0.5f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            return g;
        }

        static Gradient CreateWarningGradient(float concentration)
        {
            float t = Mathf.Clamp01(concentration / 0.05f);

            var g = new Gradient();
            g.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(Color.Lerp(
                        new Color(0.4f, 0.85f, 1.0f),
                        new Color(1.0f, 0.8f, 0.0f), t), 0f),
                    new GradientColorKey(Color.Lerp(
                        new Color(0.3f, 0.7f, 0.95f),
                        new Color(1.0f, 0.6f, 0.0f), t), 0.5f),
                    new GradientColorKey(Color.Lerp(
                        new Color(0.2f, 0.5f, 0.8f),
                        new Color(0.8f, 0.4f, 0.0f), t), 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0.1f, 0f),
                    new GradientAlphaKey(0.8f, 0.2f),
                    new GradientAlphaKey(0.9f, 0.6f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            return g;
        }

        static Gradient CreateDefaultGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(0.4f, 0.85f, 1.0f, 1f), 0f),
                    new GradientColorKey(new Color(0.3f, 0.7f, 0.95f, 1f), 0.5f),
                    new GradientColorKey(new Color(0.2f, 0.5f, 0.8f, 0.6f), 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0.1f, 0f),
                    new GradientAlphaKey(0.8f, 0.2f),
                    new GradientAlphaKey(0.9f, 0.6f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            return g;
        }

        static Gradient CreateHighFlowGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(0.2f, 1.0f, 0.8f, 1f), 0f),
                    new GradientColorKey(new Color(0.1f, 0.9f, 0.6f, 1f), 0.5f),
                    new GradientColorKey(new Color(0.05f, 0.7f, 0.4f, 0.6f), 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0.2f, 0f),
                    new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(1f, 0.7f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            return g;
        }

        static Gradient CreateReverseGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(1.0f, 0.3f, 0.2f, 1f), 0f),
                    new GradientColorKey(new Color(0.9f, 0.2f, 0.15f, 1f), 0.5f),
                    new GradientColorKey(new Color(0.7f, 0.1f, 0.1f, 0.6f), 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0.1f, 0f),
                    new GradientAlphaKey(0.9f, 0.2f),
                    new GradientAlphaKey(0.8f, 0.6f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            return g;
        }

    }
}
