using UnityEngine;

namespace CloneSystem
{
    // slapped on every clone instance by AAACloneSystem at creation time
    // the portal system uses this to stop clones from walking through portals
    public class CloneMarker : MonoBehaviour
    {
        [Header("Clone Identity")]
        public AAACloneSystem.CloneType cloneType;
        public float spawnTime;
        public bool isActive = true;

        // always true - but it's useful to have the property for readability at call sites
        public bool IsClone => true;

        public Color CloneColor
        {
            get
            {
                if (AAACloneSystem.Instance != null)
                {
                    return AAACloneSystem.Instance.CloneTypes[(int)cloneType].color;
                }
                return Color.cyan; // fallback if the system is gone
            }
        }

        private void OnEnable()
        {
            spawnTime = Time.time;
            isActive = true;
        }

        private void OnDisable()
        {
            isActive = false;
        }

        // checks self and parents - clones can have child colliders
        public static bool IsObjectClone(Transform target)
        {
            if (target == null) return false;

            var marker = target.GetComponent<CloneMarker>();
            if (marker != null) return true;

            marker = target.GetComponentInParent<CloneMarker>();
            return marker != null;
        }

        public static bool IsColliderClone(Collider col)
        {
            if (col == null) return false;
            return IsObjectClone(col.transform);
        }
    }
}
