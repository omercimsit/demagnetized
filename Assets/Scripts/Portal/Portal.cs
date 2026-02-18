using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// Portal system - Valve-style "window into another world" effect for HDRP
//
// Setup:
// 1. Create Quad (portal surface)
// 2. Add this script
// 3. Assign linkedPortal to the other portal
// 4. Done!
[ExecuteAlways]
public class Portal : MonoBehaviour
{
    [Header("Portal Link")]
    [Tooltip("The portal on the other side")]
    public Portal linkedPortal;

    [Header("Rendering")]
    [Range(0.25f, 2f)]
    public float renderScale = 1f;

    [Tooltip("Offset for near clip plane to prevent z-fighting")]
    [Range(0.01f, 0.2f)]
    public float nearClipOffset = 0.05f;

    [Header("Culling")]
    [Tooltip("Maximum render distance")]
    public float maxRenderDistance = 100f;

    [Header("Debug")]
    public bool showDebugInfo = false;

    private Camera _mainCam;
    private Camera _portalCam;
    private RenderTexture _renderTexture;
    private MeshRenderer _meshRenderer;
    private Material _portalMaterial;

    private static bool _isRendering = false;
    private int _lastScreenWidth, _lastScreenHeight;
    private float _originalFOV;

    void OnEnable()
    {
        _meshRenderer = GetComponent<MeshRenderer>();
        if (_meshRenderer == null)
        {
            Debug.LogError($"[Portal] {name}: MeshRenderer required!");
            enabled = false;
            return;
        }

        Setup();

        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        Cleanup();
    }

    void Setup()
    {
        _mainCam = Camera.main;
        if (_mainCam == null)
        {
            Debug.LogError($"[Portal] Main Camera not found!");
            return;
        }

        _originalFOV = _mainCam.fieldOfView;

        CreatePortalCamera();
        CreateRenderTexture();
        CreateMaterial();

        _lastScreenWidth = Screen.width;
        _lastScreenHeight = Screen.height;

        if (showDebugInfo)
            Debug.Log($"[Portal] {name} initialized. Linked to: {(linkedPortal != null ? linkedPortal.name : "NONE")}");
    }

    void CreatePortalCamera()
    {
        var existingCam = transform.Find("_PortalCamera");
        if (existingCam != null)
        {
            if (Application.isPlaying)
                Destroy(existingCam.gameObject);
            else
                DestroyImmediate(existingCam.gameObject);
        }

        var camGO = new GameObject("_PortalCamera");
        camGO.hideFlags = HideFlags.HideAndDontSave;
        camGO.transform.SetParent(transform, false);

        _portalCam = camGO.AddComponent<Camera>();
        _portalCam.enabled = false; // we call Render() manually
        _portalCam.depth = -100;

        // copy HDRP settings from main camera so it renders the same volumes/AA
        var hdData = camGO.AddComponent<HDAdditionalCameraData>();
        var mainHdData = _mainCam.GetComponent<HDAdditionalCameraData>();
        if (mainHdData != null)
        {
            hdData.volumeLayerMask = mainHdData.volumeLayerMask;
            hdData.antialiasing = mainHdData.antialiasing;
            hdData.dithering = mainHdData.dithering;
        }

        _portalCam.cullingMask = _mainCam.cullingMask;
    }

    void CreateRenderTexture()
    {
        if (_renderTexture != null)
        {
            _renderTexture.Release();
            if (Application.isPlaying)
                Destroy(_renderTexture);
            else
                DestroyImmediate(_renderTexture);
        }

        int w = Mathf.Max(64, Mathf.RoundToInt(Screen.width * renderScale));
        int h = Mathf.Max(64, Mathf.RoundToInt(Screen.height * renderScale));

        _renderTexture = new RenderTexture(w, h, 32, RenderTextureFormat.DefaultHDR);
        _renderTexture.name = $"PortalRT_{name}";
        _renderTexture.antiAliasing = 1;
        _renderTexture.filterMode = FilterMode.Bilinear;
        _renderTexture.Create();

        if (_portalCam != null)
            _portalCam.targetTexture = _renderTexture;

        if (showDebugInfo)
            Debug.Log($"[Portal] RenderTexture created: {w}x{h}");
    }

    void CreateMaterial()
    {
        // HDRP/Unlit is the most reliable choice here - Lit adds unnecessary lighting calculations
        var shader = Shader.Find("HDRP/Unlit");

        if (shader == null)
        {
            Debug.LogError($"[Portal] HDRP/Unlit shader not found!");
            return;
        }

        if (_portalMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(_portalMaterial);
            else
                DestroyImmediate(_portalMaterial);
        }

        _portalMaterial = new Material(shader);
        _portalMaterial.hideFlags = HideFlags.HideAndDontSave;

        _portalMaterial.SetTexture("_UnlitColorMap", _renderTexture);
        _portalMaterial.SetColor("_UnlitColor", Color.white);

        _portalMaterial.EnableKeyword("_EMISSIVE_COLOR_MAP");
        _portalMaterial.SetTexture("_EmissiveColorMap", _renderTexture);
        _portalMaterial.SetColor("_EmissiveColor", Color.white);

        _meshRenderer.sharedMaterial = _portalMaterial;

        if (showDebugInfo)
            Debug.Log($"[Portal] Material created with HDRP/Unlit shader");
    }

    void Cleanup()
    {
        if (_portalCam != null)
        {
            _portalCam.targetTexture = null;
            if (Application.isPlaying)
                Destroy(_portalCam.gameObject);
            else
                DestroyImmediate(_portalCam.gameObject);
            _portalCam = null;
        }

        if (_renderTexture != null)
        {
            _renderTexture.Release();
            if (Application.isPlaying)
                Destroy(_renderTexture);
            else
                DestroyImmediate(_renderTexture);
            _renderTexture = null;
        }

        if (_portalMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(_portalMaterial);
            else
                DestroyImmediate(_portalMaterial);
            _portalMaterial = null;
        }
    }

    void Update()
    {
        if (_mainCam == null)
        {
            _mainCam = Camera.main;
            if (_mainCam != null)
                _originalFOV = _mainCam.fieldOfView;
        }

        // recreate RT when resolution changes (e.g. fullscreen toggle)
        if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight)
        {
            CreateRenderTexture();
            if (_portalMaterial != null)
            {
                _portalMaterial.SetTexture("_UnlitColorMap", _renderTexture);
                _portalMaterial.SetTexture("_EmissiveColorMap", _renderTexture);
            }
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
        }
    }

    // render the portal view AFTER the main camera is done so we don't mess up HDRP's pipeline
    // this also fixes the FOV distortion that showed up when rendering before main cam
    void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (cam != _mainCam) return;
        if (_isRendering) return;
        if (linkedPortal == null) return;
        if (_portalCam == null || _renderTexture == null) return;

        float dist = Vector3.Distance(cam.transform.position, transform.position);
        if (dist > maxRenderDistance) return;

        if (!IsVisibleFrom(cam)) return;

        // skip rendering if player is looking at the back side of the portal
        Vector3 toPortal = (transform.position - cam.transform.position).normalized;
        if (Vector3.Dot(transform.forward, toPortal) > 0.01f) return;

        RenderPortalView(cam);
    }

    void RenderPortalView(Camera playerCam)
    {
        _isRendering = true;

        // position: mirror player through this portal into linked portal's space
        Vector3 localPos = transform.InverseTransformPoint(playerCam.transform.position);
        localPos = new Vector3(localPos.x, localPos.y, -localPos.z); // flip Z
        Vector3 worldPos = linkedPortal.transform.TransformPoint(localPos);

        // rotation: portal delta + 180 degree flip + player rotation
        // order matters a lot here - got this wrong originally and the view was mirrored
        Quaternion portalRotDiff = linkedPortal.transform.rotation * Quaternion.Inverse(transform.rotation);
        Quaternion flip = Quaternion.Euler(0, 180, 0);
        Quaternion worldRot = portalRotDiff * flip * playerCam.transform.rotation;

        _portalCam.transform.SetPositionAndRotation(worldPos, worldRot);

        _portalCam.fieldOfView = playerCam.fieldOfView;
        _portalCam.aspect = playerCam.aspect;
        _portalCam.nearClipPlane = playerCam.nearClipPlane;
        _portalCam.farClipPlane = playerCam.farClipPlane;

        _portalCam.ResetProjectionMatrix();

        SetObliqueClipPlane();

        _portalCam.Render();

        // FIXME: resetting projection after render might not be necessary, need to test
        _portalCam.ResetProjectionMatrix();

        _isRendering = false;

        if (showDebugInfo && Time.frameCount % 60 == 0)
        {
            Debug.Log($"[Portal] {name} -> Cam at {worldPos:F1}, rot {worldRot.eulerAngles:F1}");
        }
    }

    // Eric Lengyel's oblique projection technique - hides everything behind the portal surface
    // ref: http://www.terathon.com/code/oblique.html
    void SetObliqueClipPlane()
    {
        Transform clipTransform = linkedPortal.transform;

        // plane normal points toward the camera (away from portal face)
        Vector3 normal = -clipTransform.forward;
        Vector3 pos = clipTransform.position;

        float d = -Vector3.Dot(normal, pos);

        Vector4 planeWorld = new Vector4(normal.x, normal.y, normal.z, d);

        // transform plane to camera space using inverse-transpose
        Matrix4x4 worldToCam = _portalCam.worldToCameraMatrix;
        Matrix4x4 invTranspose = Matrix4x4.Transpose(Matrix4x4.Inverse(worldToCam));
        Vector4 planeCam = invTranspose * planeWorld;

        // w > 0 means the clip plane is behind the camera - skip to avoid inverting the view
        if (planeCam.w > 0)
        {
            if (showDebugInfo)
                Debug.LogWarning($"[Portal] {name}: Clip plane behind camera, skipping oblique");
            return;
        }

        planeCam.w -= nearClipOffset;

        _portalCam.projectionMatrix = _portalCam.CalculateObliqueMatrix(planeCam);
    }

    // reuse array across frames to avoid allocating 6 Plane structs every frame
    private readonly Plane[] _frustumPlanes = new Plane[6];

    bool IsVisibleFrom(Camera cam)
    {
        if (_meshRenderer == null) return false;
        GeometryUtility.CalculateFrustumPlanes(cam, _frustumPlanes);
        return GeometryUtility.TestPlanesAABB(_frustumPlanes, _meshRenderer.bounds);
    }

    // --- Public API for teleportation ---

    // transforms a world point through the portal - used by teleport scripts
    public Vector3 TransformPoint(Vector3 worldPoint)
    {
        if (linkedPortal == null) return worldPoint;

        Vector3 localPos = transform.InverseTransformPoint(worldPoint);
        localPos = new Vector3(localPos.x, localPos.y, -localPos.z);
        return linkedPortal.transform.TransformPoint(localPos);
    }

    public Quaternion TransformRotation(Quaternion worldRot)
    {
        if (linkedPortal == null) return worldRot;

        Quaternion portalRotDiff = linkedPortal.transform.rotation * Quaternion.Inverse(transform.rotation);
        Quaternion flip = Quaternion.Euler(0, 180, 0);
        return portalRotDiff * flip * worldRot;
    }

    public Vector3 TransformDirection(Vector3 worldDir)
    {
        if (linkedPortal == null) return worldDir;

        Quaternion portalRotDiff = linkedPortal.transform.rotation * Quaternion.Inverse(transform.rotation);
        Quaternion flip = Quaternion.Euler(0, 180, 0);
        return portalRotDiff * flip * worldDir;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(1, 1, 0.05f));

        Gizmos.color = Color.blue;
        Gizmos.DrawRay(Vector3.zero, Vector3.forward * 2f);

        if (linkedPortal != null)
        {
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, linkedPortal.transform.position);

            Gizmos.color = Color.magenta;
            Gizmos.matrix = linkedPortal.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(1, 1, 0.05f));
        }
    }
}
