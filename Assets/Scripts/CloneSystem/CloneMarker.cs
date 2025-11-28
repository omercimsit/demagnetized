using UnityEngine;

namespace CloneSystem
{
    /// <summary>
    /// Marks a GameObject as a clone instance. Used by the portal system
    /// to prevent clones from passing through portals.
    /// Automatically added by AAACloneSystem on clone creation.
    /// </summary>
    public class CloneMarker : MonoBehaviour
    {
        [Header("Clone Identity")]
        public AAACloneSystem.CloneType cloneType;
        public float spawnTime;
        public bool isActive = true;

        public bool IsClone => true;

        public Color CloneColor
        {
            get
            {
                if (AAACloneSystem.Instance != null)
                {
                    return AAACloneSystem.Instance.CloneTypes[(int)cloneType].color;
                }
                return Color.cyan;
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
