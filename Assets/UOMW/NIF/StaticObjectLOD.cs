using UnityEngine;

namespace ESMSharp.NIF
{
    /// <summary>
    /// Simple LOD component that disables static objects beyond a certain distance from the camera
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class StaticObjectLOD : MonoBehaviour
    {
        [Tooltip("Distance at which the object will be culled (disabled)")]
        public float cullDistance = 100f;

        [Tooltip("Check interval in seconds (0 = every frame)")]
        public float checkInterval = 0.1f;

        private Renderer _renderer;
        private float _lastCheckTime = 0f;
        private bool _isVisible = true;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            if (_renderer == null)
            {
                _renderer = GetComponentInChildren<Renderer>();
            }
        }

        private void Start()
        {
            // Initial check
            UpdateVisibility();
        }

        private void Update()
        {
            // Only check at specified interval to reduce CPU usage
            if (Time.time - _lastCheckTime >= checkInterval)
            {
                UpdateVisibility();
                _lastCheckTime = Time.time;
            }
        }

        private void UpdateVisibility()
        {
            if (_renderer == null || Camera.main == null)
                return;

            float distance = Vector3.Distance(transform.position, Camera.main.transform.position);
            bool shouldBeVisible = distance <= cullDistance;

            // Only update if visibility state changed
            if (shouldBeVisible != _isVisible)
            {
                _isVisible = shouldBeVisible;
                _renderer.enabled = _isVisible;

                // Optionally disable/enable the entire GameObject for better performance
                // This disables all components including colliders, scripts, etc.
                // Uncomment if you want more aggressive culling:
                // gameObject.SetActive(_isVisible);
            }
        }

        /// <summary>
        /// Force an immediate visibility update
        /// </summary>
        public void ForceUpdate()
        {
            UpdateVisibility();
        }

        /// <summary>
        /// Get the current visibility state
        /// </summary>
        public bool IsVisible()
        {
            return _isVisible;
        }

        private void OnDrawGizmosSelected()
        {
            // Draw a wireframe sphere showing the cull distance
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, cullDistance);
        }
    }
}

