using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using DamianGonzalez.Portals;

namespace CloneProject
{
    // Adds visual polish to portals: point light, glowing rim edges, and particles
    // attach this to the same GameObject as PortalSetup
    public class PortalGlowEffects : MonoBehaviour
    {
        [Header("Glow Rim Settings")]
        [Tooltip("Enable the glowing rim edge effect")]
        public bool enableGlowRim = true;

        [Tooltip("Rim color (HDR supported - increase brightness for intensity)")]
        [ColorUsage(true, true)]
        public Color rimColor = new Color(0.5f, 1.5f, 2f, 1f);

        [Tooltip("Rim thickness")]
        [Range(0.02f, 0.3f)]
        public float rimThickness = 0.08f;

        [Tooltip("Rim brightness multiplier")]
        [Range(1f, 20f)]
        public float rimIntensity = 5f;

        [Tooltip("Rim pulse effect")]
        public bool enableRimPulse = true;

        [Tooltip("Pulse speed")]
        [Range(0.5f, 3f)]
        public float rimPulseSpeed = 1.5f;

        [Tooltip("Pulse amount")]
        [Range(0f, 0.5f)]
        public float rimPulseAmount = 0.3f;

        [Header("Light Settings")]
        [Tooltip("Enable the portal light")]
        public bool enablePortalLight = true;

        [Tooltip("Light color")]
        public Color lightColor = new Color(0.3f, 0.7f, 1f, 1f);

        [Tooltip("Light intensity (lumens for HDRP)")]
        [Range(100f, 10000f)]
        public float lightIntensity = 2000f;

        [Tooltip("Light range (meters)")]
        [Range(1f, 20f)]
        public float lightRange = 8f;

        [Tooltip("Light offset from the portal surface")]
        [Range(0f, 2f)]
        public float lightOffset = 0.5f;

        [Header("Light Flicker")]
        [Tooltip("Enable light flickering")]
        public bool enableFlicker = true;

        [Tooltip("Flicker speed")]
        [Range(0.5f, 5f)]
        public float flickerSpeed = 2f;

        [Tooltip("Flicker amount (0-1)")]
        [Range(0f, 0.5f)]
        public float flickerAmount = 0.15f;

        [Header("Particle Settings")]
        [Tooltip("Enable particle effects")]
        public bool enableParticles = true;

        [Tooltip("Particle color")]
        [ColorUsage(true, true)]
        public Color particleColor = new Color(0.5f, 1.5f, 2f, 1f);

        [Tooltip("Particles emitted per second")]
        [Range(5f, 100f)]
        public float particleRate = 30f;

        [Tooltip("Particle lifetime (seconds)")]
        [Range(0.5f, 3f)]
        public float particleLifetime = 1.5f;

        [Tooltip("Particle size")]
        [Range(0.01f, 0.2f)]
        public float particleSize = 0.05f;

        [Tooltip("Particle speed")]
        [Range(0.1f, 2f)]
        public float particleSpeed = 0.5f;

        [Tooltip("Particle spread angle")]
        [Range(0f, 90f)]
        public float particleSpread = 30f;

        [Header("Debug")]
        [SerializeField] private bool verboseDebug = false;

        private PortalSetup _portalSetup;
        private Light _lightA;
        private Light _lightB;
        private HDAdditionalLightData _hdLightA;
        private HDAdditionalLightData _hdLightB;
        private float _baseIntensity;
        private bool _configured = false;

        private GameObject _rimA;
        private GameObject _rimB;
        private Material _rimMaterial;
        private float _baseRimIntensity;

        private ParticleSystem _particlesA;
        private ParticleSystem _particlesB;
        private Material _particleMaterial;

        private void Start()
        {
            _portalSetup = GetComponent<PortalSetup>();
            if (_portalSetup == null)
            {
                Debug.LogError("[PortalGlowEffects] PortalSetup not found!");
                enabled = false;
                return;
            }

            _baseIntensity = lightIntensity;

            // subscribe to setup event - portal may not be ready yet when we Start()
            PortalEvents.setupComplete += OnPortalSetupComplete;

            // if already setup (e.g. hot reload in editor), run immediately
            if (_portalSetup.setupComplete)
            {
                OnPortalSetupComplete(_portalSetup.groupId, _portalSetup);
            }

            if (verboseDebug)
                Debug.Log("[PortalGlowEffects] Initialized, waiting for portal setup...");
        }

        private void OnDestroy()
        {
            PortalEvents.setupComplete -= OnPortalSetupComplete;

            if (_lightA != null)
                Destroy(_lightA.gameObject);
            if (_lightB != null)
                Destroy(_lightB.gameObject);

            if (_rimA != null)
                Destroy(_rimA);
            if (_rimB != null)
                Destroy(_rimB);
            if (_rimMaterial != null)
                Destroy(_rimMaterial);

            if (_particlesA != null)
                Destroy(_particlesA.gameObject);
            if (_particlesB != null)
                Destroy(_particlesB.gameObject);
            if (_particleMaterial != null)
                Destroy(_particleMaterial);
        }

        private void OnPortalSetupComplete(string groupId, PortalSetup setup)
        {
            if (setup != _portalSetup) return;
            if (_configured) return;

            _configured = true;

            if (enableGlowRim)
                CreateGlowRims();

            if (enablePortalLight)
                CreatePortalLights();

            if (enableParticles)
                CreateParticles();

            if (verboseDebug)
                Debug.Log("[PortalGlowEffects] Portal effects created");
        }

        private void CreateGlowRims()
        {
            _rimMaterial = new Material(Shader.Find("HDRP/Unlit"));
            _rimMaterial.name = "PortalRimMaterial";
            _rimMaterial.SetColor("_UnlitColor", rimColor * rimIntensity);
            _rimMaterial.EnableKeyword("_EMISSION");
            _rimMaterial.SetColor("_EmissiveColor", rimColor * rimIntensity);
            _rimMaterial.renderQueue = 3001; // render in front of the portal surface

            _baseRimIntensity = rimIntensity;

            if (_portalSetup.refs.rendererA != null)
                _rimA = CreateRimForPortal(_portalSetup.refs.rendererA.transform, "PortalRim_A");

            if (_portalSetup.refs.rendererB != null)
                _rimB = CreateRimForPortal(_portalSetup.refs.rendererB.transform, "PortalRim_B");

            if (verboseDebug)
                Debug.Log($"[PortalGlowEffects] Glow rim created: Color={rimColor}, Thickness={rimThickness}");
        }

        private GameObject CreateRimForPortal(Transform portalPlane, string rimName)
        {
            GameObject rimContainer = new GameObject(rimName);
            rimContainer.transform.SetParent(portalPlane);
            rimContainer.transform.localPosition = new Vector3(0, 0, -0.01f);
            rimContainer.transform.localRotation = Quaternion.identity;
            rimContainer.transform.localScale = Vector3.one;

            // build 4 edge quads to form the glowing border
            // hacky but works - a proper approach would use a custom shader
            Vector3 portalScale = portalPlane.localScale;
            float width = portalScale.x;
            float height = portalScale.y;

            CreateRimEdge(rimContainer.transform, "Top",
                new Vector3(0, height / 2f + rimThickness / 2f, 0),
                new Vector3(width + rimThickness * 2, rimThickness, 0.01f));

            CreateRimEdge(rimContainer.transform, "Bottom",
                new Vector3(0, -height / 2f - rimThickness / 2f, 0),
                new Vector3(width + rimThickness * 2, rimThickness, 0.01f));

            CreateRimEdge(rimContainer.transform, "Left",
                new Vector3(-width / 2f - rimThickness / 2f, 0, 0),
                new Vector3(rimThickness, height, 0.01f));

            CreateRimEdge(rimContainer.transform, "Right",
                new Vector3(width / 2f + rimThickness / 2f, 0, 0),
                new Vector3(rimThickness, height, 0.01f));

            return rimContainer;
        }

        private void CreateRimEdge(Transform parent, string edgeName, Vector3 localPos, Vector3 scale)
        {
            GameObject edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            edge.name = edgeName;
            edge.transform.SetParent(parent);
            edge.transform.localPosition = localPos;
            edge.transform.localRotation = Quaternion.identity;
            edge.transform.localScale = scale;

            // remove collider - the rim is purely visual
            var collider = edge.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            var renderer = edge.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material = _rimMaterial;
        }

        private void CreatePortalLights()
        {
            if (_portalSetup.refs.rendererA != null)
            {
                _lightA = CreateLight(_portalSetup.refs.rendererA.transform, "PortalLight_A");
                _hdLightA = _lightA.GetComponent<HDAdditionalLightData>();
            }

            if (_portalSetup.refs.rendererB != null)
            {
                _lightB = CreateLight(_portalSetup.refs.rendererB.transform, "PortalLight_B");
                _hdLightB = _lightB.GetComponent<HDAdditionalLightData>();
            }

            if (verboseDebug)
                Debug.Log($"[PortalGlowEffects] Portal lights created: Color={lightColor}, Intensity={lightIntensity}, Range={lightRange}");
        }

        private Light CreateLight(Transform portalPlane, string lightName)
        {
            GameObject lightObj = new GameObject(lightName);
            lightObj.transform.SetParent(portalPlane);
            lightObj.transform.localPosition = new Vector3(0, 0, -lightOffset);
            lightObj.transform.localRotation = Quaternion.identity;

            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = lightColor;
            light.range = lightRange;

            HDAdditionalLightData hdLight = lightObj.AddComponent<HDAdditionalLightData>();

            // HDRP uses physical units - lumens for point lights
            light.intensity = lightIntensity;
            light.lightUnit = UnityEngine.Rendering.LightUnit.Lumen;
            hdLight.affectsVolumetric = true;
            hdLight.volumetricDimmer = 0.5f;

            if (verboseDebug)
                Debug.Log($"[PortalGlowEffects] Light created: {lightName} at {lightObj.transform.position}");

            return light;
        }

        private void CreateParticles()
        {
            // additive transparent material - TODO: switch to a proper VFX Graph effect at some point
            _particleMaterial = new Material(Shader.Find("HDRP/Unlit"));
            _particleMaterial.name = "PortalParticleMaterial";
            _particleMaterial.SetColor("_UnlitColor", particleColor);
            _particleMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _particleMaterial.EnableKeyword("_BLENDMODE_ADD");
            _particleMaterial.SetFloat("_SurfaceType", 1);
            _particleMaterial.SetFloat("_BlendMode", 1);
            _particleMaterial.renderQueue = 3100;

            if (_portalSetup.refs.rendererA != null)
                _particlesA = CreateParticleSystemForPortal(_portalSetup.refs.rendererA.transform, "PortalParticles_A");

            if (_portalSetup.refs.rendererB != null)
                _particlesB = CreateParticleSystemForPortal(_portalSetup.refs.rendererB.transform, "PortalParticles_B");

            if (verboseDebug)
                Debug.Log($"[PortalGlowEffects] Particles created: Color={particleColor}, Rate={particleRate}");
        }

        private ParticleSystem CreateParticleSystemForPortal(Transform portalPlane, string particleName)
        {
            GameObject particleObj = new GameObject(particleName);
            particleObj.transform.SetParent(portalPlane);
            particleObj.transform.localPosition = new Vector3(0, 0, -0.05f);
            particleObj.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            particleObj.transform.localScale = Vector3.one;

            ParticleSystem ps = particleObj.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = true;
            main.startLifetime = particleLifetime;
            main.startSpeed = particleSpeed;
            main.startSize = particleSize;
            main.startColor = particleColor;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = particleRate;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Rectangle;

            Vector3 portalScale = portalPlane.localScale;
            shape.scale = new Vector3(portalScale.x * 0.9f, portalScale.y * 0.9f, 0.1f);
            shape.randomDirectionAmount = particleSpread / 90f;

            // velocity module - all axes must use same mode or Unity throws an error at runtime
            var velocityOverLifetime = ps.velocityOverLifetime;
            velocityOverLifetime.enabled = true;
            velocityOverLifetime.space = ParticleSystemSimulationSpace.Local;
            velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(0f);
            velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(0f);
            velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(-particleSpeed * 0.8f);

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 0.7f),
                    new GradientColorKey(Color.white, 1f)
                },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            colorOverLifetime.color = gradient;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, 0.5f);
            sizeCurve.AddKey(0.3f, 1f);
            sizeCurve.AddKey(1f, 0f);
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            var renderer = particleObj.GetComponent<ParticleSystemRenderer>();
            renderer.material = _particleMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;

            ps.Play();

            return ps;
        }

        private void Update()
        {
            if (!_configured) return;

            if (enableGlowRim && enableRimPulse && _rimMaterial != null)
            {
                float pulse = 1f + Mathf.Sin(Time.time * rimPulseSpeed * Mathf.PI * 2f) * rimPulseAmount;
                float currentIntensity = _baseRimIntensity * pulse;
                Color pulsedColor = rimColor * currentIntensity;
                _rimMaterial.SetColor("_UnlitColor", pulsedColor);
                _rimMaterial.SetColor("_EmissiveColor", pulsedColor);
            }

            UpdateRimParameters();

            if (!enablePortalLight) return;

            if (enableFlicker)
            {
                // combine sine + perlin for a less regular flicker pattern
                float flicker = 1f + Mathf.Sin(Time.time * flickerSpeed * Mathf.PI * 2f) * flickerAmount;
                flicker += Mathf.PerlinNoise(Time.time * flickerSpeed * 0.5f, 0) * flickerAmount * 0.5f;

                float currentIntensity = _baseIntensity * flicker;

                if (_lightA != null)
                    _lightA.intensity = currentIntensity;
                if (_lightB != null)
                    _lightB.intensity = currentIntensity;
            }

            UpdateLightParameters();
        }

        private void UpdateRimParameters()
        {
            if (_rimMaterial == null) return;

            _baseRimIntensity = rimIntensity;

            if (!enableRimPulse)
            {
                Color finalColor = rimColor * rimIntensity;
                _rimMaterial.SetColor("_UnlitColor", finalColor);
                _rimMaterial.SetColor("_EmissiveColor", finalColor);
            }
        }

        private void UpdateLightParameters()
        {
            if (_lightA != null)
            {
                _lightA.color = lightColor;
                _lightA.range = lightRange;
                _lightA.transform.localPosition = new Vector3(0, 0, -lightOffset);
            }

            if (_lightB != null)
            {
                _lightB.color = lightColor;
                _lightB.range = lightRange;
                _lightB.transform.localPosition = new Vector3(0, 0, -lightOffset);
            }

            _baseIntensity = lightIntensity;

            if (!enableFlicker)
            {
                if (_lightA != null)
                    _lightA.intensity = lightIntensity;
                if (_lightB != null)
                    _lightB.intensity = lightIntensity;
            }
        }

        public void SetLightColor(Color color)
        {
            lightColor = color;
        }

        public void SetLightIntensity(float intensity)
        {
            lightIntensity = intensity;
            _baseIntensity = intensity;
        }

        [ContextMenu("Recreate Effects")]
        public void RecreateEffects()
        {
            if (_lightA != null)
                Destroy(_lightA.gameObject);
            if (_lightB != null)
                Destroy(_lightB.gameObject);

            if (_rimA != null)
                Destroy(_rimA);
            if (_rimB != null)
                Destroy(_rimB);
            if (_rimMaterial != null)
                Destroy(_rimMaterial);

            if (_particlesA != null)
                Destroy(_particlesA.gameObject);
            if (_particlesB != null)
                Destroy(_particlesB.gameObject);
            if (_particleMaterial != null)
                Destroy(_particleMaterial);

            _configured = false;

            if (_portalSetup != null && _portalSetup.setupComplete)
            {
                OnPortalSetupComplete(_portalSetup.groupId, _portalSetup);
            }
        }

        public void SetRimColor(Color color)
        {
            rimColor = color;
            if (_rimMaterial != null)
            {
                Color finalColor = color * rimIntensity;
                _rimMaterial.SetColor("_UnlitColor", finalColor);
                _rimMaterial.SetColor("_EmissiveColor", finalColor);
            }
        }

        public void SetParticleColor(Color color)
        {
            particleColor = color;
            if (_particleMaterial != null)
            {
                _particleMaterial.SetColor("_UnlitColor", color);
            }

            if (_particlesA != null)
            {
                var main = _particlesA.main;
                main.startColor = color;
            }
            if (_particlesB != null)
            {
                var main = _particlesB.main;
                main.startColor = color;
            }
        }

        // sets light, rim, and particle all at once - handy for color theme changes
        public void SetAllColors(Color color)
        {
            SetLightColor(color);
            SetRimColor(color);
            SetParticleColor(color);
        }
    }
}
