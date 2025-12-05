using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// PORTAL SYSTEM V3 - HDRP Optimized
/// 
/// Valve Portal-style "window into another world" effect.
/// 
/// Key features:
/// - Screen-space UV rendering (no parallax issues)
/// - Proper HDRP integration with endCameraRendering
/// - Oblique near clip plane alignment
/// - FOV reset protection
/// 
/// Setup:
/// 1. Create Quad (portal surface)
/// 2. Add this script
/// 3. Assign linkedPortal to the other portal
/// 4. Done!
/// </summary>
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
    
    // Components
    private Camera _mainCam;
    private Camera _portalCam;
    private RenderTexture _renderTexture;
    private MeshRenderer _meshRenderer;
    private Material _portalMaterial;
    
    // State
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
        
        // Subscribe to HDRP render events
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
        // Clean up old camera
        var existingCam = transform.Find("_PortalCamera");
        if (existingCam != null)
        {
            if (Application.isPlaying)
                Destroy(existingCam.gameObject);
            else
                DestroyImmediate(existingCam.gameObject);
        }
        
        // Create new camera
        var camGO = new GameObject("_PortalCamera");
        camGO.hideFlags = HideFlags.HideAndDontSave;
        camGO.transform.SetParent(transform, false);
        
        _portalCam = camGO.AddComponent<Camera>();
        _portalCam.enabled = false; // Manual render only
        _portalCam.depth = -100;
        
        // HDRP camera data - copy from main camera
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
        // Use HDRP/Unlit shader - guaranteed to work in HDRP
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
        
        // HDRP/Unlit uses _UnlitColorMap for the main texture
        _portalMaterial.SetTexture("_UnlitColorMap", _renderTexture);
        
        // Set color to white so texture is not tinted
        _portalMaterial.SetColor("_UnlitColor", Color.white);
        
        // Enable emission for better visibility
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
        // Find main camera if lost
        if (_mainCam == null)
        {
            _mainCam = Camera.main;
            if (_mainCam != null)
                _originalFOV = _mainCam.fieldOfView;
        }
        
        // Handle screen resize
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
    
    /// <summary>
    /// Render portal view AFTER main camera finishes.
    /// This ensures we don't interfere with HDRP's rendering pipeline
    /// and also fixes the FOV distortion issue.
    /// </summary>
    void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        // Only process after main camera renders
        if (cam != _mainCam) return;
        
        // Prevent recursion
        if (_isRendering) return;
        
        // Skip if no linked portal
        if (linkedPortal == null) return;
        
        // Skip if components missing
        if (_portalCam == null || _renderTexture == null) return;
        
        // Distance culling
        float dist = Vector3.Distance(cam.transform.position, transform.position);
        if (dist > maxRenderDistance) return;
        
        // Frustum culling
        if (!IsVisibleFrom(cam)) return;
        
        // Backface culling - don't render if viewing from behind
        Vector3 toPortal = (transform.position - cam.transform.position).normalized;
        if (Vector3.Dot(transform.forward, toPortal) > 0.01f) return;
        
        // Render!
        RenderPortalView(cam);
    }
    
    void RenderPortalView(Camera playerCam)
    {
        _isRendering = true;
        
        // =============================================
        // CAMERA POSITION (Mirror through portal)
        // =============================================
        // 1. Get player position in THIS portal's local space
        Vector3 localPos = transform.InverseTransformPoint(playerCam.transform.position);
        
        // 2. Mirror through portal (flip Z)
        localPos = new Vector3(localPos.x, localPos.y, -localPos.z);
        
        // 3. Transform to LINKED portal's world space
        Vector3 worldPos = linkedPortal.transform.TransformPoint(localPos);
        
        // =============================================
        // CAMERA ROTATION (Relative rotation with 180° flip)
        // =============================================
        // Step 1: Get rotation difference between portals
        Quaternion portalRotDiff = linkedPortal.transform.rotation * Quaternion.Inverse(transform.rotation);
        
        // Step 2: 180° flip (we're looking "through" the portal)
        Quaternion flip = Quaternion.Euler(0, 180, 0);
        
        // Step 3: Apply to player rotation
        // Order matters: portalRotDiff * flip * playerRotation
        Quaternion worldRot = portalRotDiff * flip * playerCam.transform.rotation;
        
        // Apply transform
        _portalCam.transform.SetPositionAndRotation(worldPos, worldRot);
        
        // =============================================
        // CAMERA SETTINGS (Copy from main camera)
        // =============================================
        _portalCam.fieldOfView = playerCam.fieldOfView;
        _portalCam.aspect = playerCam.aspect;
        _portalCam.nearClipPlane = playerCam.nearClipPlane;
        _portalCam.farClipPlane = playerCam.farClipPlane;
        
        // Reset projection matrix before oblique calculation
        _portalCam.ResetProjectionMatrix();
        
        // =============================================
        // OBLIQUE NEAR CLIP PLANE
        // =============================================
        SetObliqueClipPlane();
        
        // =============================================
        // RENDER
        // =============================================
        _portalCam.Render();
        
        // Reset projection after render (important!)
        _portalCam.ResetProjectionMatrix();
        
        _isRendering = false;
        
        if (showDebugInfo && Time.frameCount % 60 == 0)
        {
            Debug.Log($"[Portal] {name} -> Cam at {worldPos:F1}, rot {worldRot.eulerAngles:F1}");
        }
    }
    
    /// <summary>
    /// Sets oblique near clip plane aligned with the linked portal surface.
    /// This hides everything behind the portal.
    /// 
    /// Based on Eric Lengyel's technique:
    /// http://www.terathon.com/code/oblique.html
    /// </summary>
    void SetObliqueClipPlane()
    {
        // The clip plane is the linked portal's surface
        Transform clipTransform = linkedPortal.transform;
        
        // Plane normal pointing TOWARDS camera (negative forward)
        Vector3 normal = -clipTransform.forward;
        Vector3 pos = clipTransform.position;
        
        // Build plane equation: dot(normal, point) + d = 0
        // So d = -dot(normal, pointOnPlane)
        float d = -Vector3.Dot(normal, pos);
        
        // Create world-space plane vector
        Vector4 planeWorld = new Vector4(normal.x, normal.y, normal.z, d);
        
        // Transform to camera space using inverse-transpose of worldToCameraMatrix
        Matrix4x4 worldToCam = _portalCam.worldToCameraMatrix;
        Matrix4x4 invTranspose = Matrix4x4.Transpose(Matrix4x4.Inverse(worldToCam));
        Vector4 planeCam = invTranspose * planeWorld;
        
        // Safety check: if plane is behind camera, skip
        // (This can happen when player is very close to portal)
        if (planeCam.w > 0)
        {
            if (showDebugInfo)
                Debug.LogWarning($"[Portal] {name}: Clip plane behind camera, skipping oblique");
            return;
        }
        
        // Offset the plane slightly to prevent z-fighting
        planeCam.w -= nearClipOffset;
        
        // Apply oblique projection matrix
        _portalCam.projectionMatrix = _portalCam.CalculateObliqueMatrix(planeCam);
    }
    
    // Pre-allocated frustum planes array (avoids per-frame allocation)
    private readonly Plane[] _frustumPlanes = new Plane[6];

    bool IsVisibleFrom(Camera cam)
    {
        if (_meshRenderer == null) return false;
        GeometryUtility.CalculateFrustumPlanes(cam, _frustumPlanes);
        return GeometryUtility.TestPlanesAABB(_frustumPlanes, _meshRenderer.bounds);
    }
    
    // =============================================
    // PUBLIC API (for teleportation)
    // =============================================
    
    /// <summary>
    /// Transforms a world point through the portal
    /// </summary>
    public Vector3 TransformPoint(Vector3 worldPoint)
    {
        if (linkedPortal == null) return worldPoint;
        
        Vector3 localPos = transform.InverseTransformPoint(worldPoint);
        localPos = new Vector3(localPos.x, localPos.y, -localPos.z);
        return linkedPortal.transform.TransformPoint(localPos);
    }
    
    /// <summary>
    /// Transforms a world rotation through the portal
    /// </summary>
    public Quaternion TransformRotation(Quaternion worldRot)
    {
        if (linkedPortal == null) return worldRot;
        
        Quaternion portalRotDiff = linkedPortal.transform.rotation * Quaternion.Inverse(transform.rotation);
        Quaternion flip = Quaternion.Euler(0, 180, 0);
        return portalRotDiff * flip * worldRot;
    }
    
    /// <summary>
    /// Transforms a world direction/velocity through the portal
    /// </summary>
    public Vector3 TransformDirection(Vector3 worldDir)
    {
        if (linkedPortal == null) return worldDir;
        
        Quaternion portalRotDiff = linkedPortal.transform.rotation * Quaternion.Inverse(transform.rotation);
        Quaternion flip = Quaternion.Euler(0, 180, 0);
        return portalRotDiff * flip * worldDir;
    }
    
    // =============================================
    // DEBUG
    // =============================================
    
    void OnDrawGizmosSelected()
    {
        // Draw this portal
        Gizmos.color = Color.cyan;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(1, 1, 0.05f));
        
        // Draw forward direction
        Gizmos.color = Color.blue;
        Gizmos.DrawRay(Vector3.zero, Vector3.forward * 2f);
        
        // Draw link to other portal
        if (linkedPortal != null)
        {
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, linkedPortal.transform.position);
            
            // Draw linked portal
            Gizmos.color = Color.magenta;
            Gizmos.matrix = linkedPortal.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(1, 1, 0.05f));
        }
    }
}
