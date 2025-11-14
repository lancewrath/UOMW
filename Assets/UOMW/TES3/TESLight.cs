using ESMSharp.TES3;
using ESMSharp.TES3.Records;
using System;
using System.Collections;
using UnityEngine;

namespace ESMSharp.TES3Terrain
{
    /// <summary>
    /// MonoBehaviour component attached to light objects loaded from Morrowind LIGH records.
    /// Stores light properties from LHDT subrecord and controls Unity Light component behavior.
    /// </summary>
    public class TESLight : MonoBehaviour
    {
        [Header("Morrowind Light Data")]
        [Tooltip("Light data from LHDT subrecord")]
        public LightData lightData;
        
        [Header("Light Properties")]
        [Tooltip("Light ID from NAME subrecord")]
        public string lightId;
        
        [Tooltip("Display name from FNAM subrecord")]
        public string displayName;
        
        [Tooltip("Sound name from SNAM subrecord")]
        public string soundName;
        
        [Tooltip("Script name from SCRI subrecord")]
        public string scriptName;
        
        // Private references
        private Light _unityLight;
        private float _baseIntensity;
        private Color _baseColor;
        private float _flickerTimer = 0f;
        private float _pulseTimer = 0f;
        private bool _isInitialized = false;
        
        // Light flags from LHDT
        private const uint FLAG_DYNAMIC = 0x0001;
        private const uint FLAG_CAN_CARRY = 0x0002;
        private const uint FLAG_NEGATIVE = 0x0004;
        private const uint FLAG_FLICKER = 0x0008;
        private const uint FLAG_FIRE = 0x0010;
        private const uint FLAG_OFF_BY_DEFAULT = 0x0020;
        private const uint FLAG_FLICKER_SLOW = 0x0040;
        private const uint FLAG_PULSE = 0x0080;
        private const uint FLAG_PULSE_SLOW = 0x0100;
        
        /// <summary>
        /// Initializes the light component with data from a LightEntry
        /// </summary>
        public void Initialize(TESLightManager.LightEntry lightEntry)
        {
            if (lightEntry == null)
            {
                Debug.LogWarning("TESLight: Attempted to initialize with null light entry");
                return;
            }
            
            lightId = lightEntry.LightId;
            displayName = lightEntry.DisplayName;
            soundName = lightEntry.SoundName;
            scriptName = lightEntry.ScriptName;
            lightData = lightEntry.LightData;
            
            // Get or add Unity Light component
            _unityLight = GetComponent<Light>();
            if (_unityLight == null)
            {
                _unityLight = gameObject.AddComponent<Light>();
            }
            
            // Configure Unity Light based on LHDT data
            ConfigureUnityLight();
            
            _isInitialized = true;
        }
        
        /// <summary>
        /// Configures the Unity Light component based on LHDT data
        /// </summary>
        private void ConfigureUnityLight()
        {
            if (_unityLight == null || lightData == null)
                return;
            
            // Set light type (most Morrowind lights are point lights)
            _unityLight.type = LightType.Point;
            
            // Set range based on radius (convert Morrowind units to Unity units)
            // Morrowind radius is in game units, Unity range is in meters
            // Using a conversion factor similar to static scale
            float range = lightData.radius * 0.0078125f; // 64/8192 = 0.0078125 (same as MORROWIND_TO_STATIC_SCALE)
            _unityLight.range = Mathf.Max(range, 0.1f); // Ensure minimum range
            
            // Set color from RGB bytes
            Color lightColor = Color.white;
            if (lightData.color != null && lightData.color.Length >= 3)
            {
                lightColor = new Color(
                    lightData.color[0] / 255f,
                    lightData.color[1] / 255f,
                    lightData.color[2] / 255f,
                    1f
                );
            }
            _baseColor = lightColor;
            _unityLight.color = lightColor;
            
            // Set intensity using weight (light weight/brightness) as primary factor, with value as fallback
            // Weight is typically a float representing light intensity/brightness
            // Unity intensity is typically 0-8 for point lights
            if (lightData.weight > 0f)
            {
                // Use weight directly, scaled to Unity intensity range (weight is typically 0-1 or similar)
                _baseIntensity = lightData.weight * 8f; // Scale weight to Unity intensity range
            }
            else
            {
                // Fallback to value if weight is 0 or negative
                // Morrowind light value is typically 0-255, Unity intensity is typically 0-8 for point lights
                _baseIntensity = (lightData.value / 255f) * 2f; // Scale to reasonable Unity intensity
            }
            _unityLight.intensity = Mathf.Max(_baseIntensity, 0.1f); // Ensure minimum intensity
            
            // Handle negative light flag (inverted lighting)
            if ((lightData.flags & FLAG_NEGATIVE) != 0)
            {
                // Negative lights could be implemented as subtractive lighting
                // For now, we'll just reduce intensity significantly
                _unityLight.intensity *= -0.5f;
            }
            
            // Set initial enabled state based on "Off by default" flag
            if ((lightData.flags & FLAG_OFF_BY_DEFAULT) != 0)
            {
                _unityLight.enabled = false;
            }
            else
            {
                _unityLight.enabled = true;
            }
            
            // Configure shadows
            _unityLight.shadows = LightShadows.Soft;
        }
        
        void Start()
        {
            // If not initialized via Initialize(), try to configure anyway
            if (!_isInitialized && lightData != null)
            {
                _unityLight = GetComponent<Light>();
                if (_unityLight == null)
                {
                    _unityLight = gameObject.AddComponent<Light>();
                }
                ConfigureUnityLight();
                _isInitialized = true;
            }
        }
        
        void Update()
        {
            if (!_isInitialized || _unityLight == null || lightData == null)
                return;
            
            // Handle flicker effect
            if ((lightData.flags & FLAG_FLICKER) != 0)
            {
                float flickerSpeed = (lightData.flags & FLAG_FLICKER_SLOW) != 0 ? 2f : 5f;
                _flickerTimer += Time.deltaTime * flickerSpeed;
                
                // Random flicker intensity variation
                float flickerAmount = 0.7f + (Mathf.PerlinNoise(_flickerTimer, 0f) * 0.3f);
                _unityLight.intensity = _baseIntensity * flickerAmount;
            }
            
            // Handle pulse effect
            if ((lightData.flags & FLAG_PULSE) != 0)
            {
                float pulseSpeed = (lightData.flags & FLAG_PULSE_SLOW) != 0 ? 1f : 2f;
                _pulseTimer += Time.deltaTime * pulseSpeed;
                
                // Smooth sine wave pulse
                float pulseAmount = 0.5f + (Mathf.Sin(_pulseTimer) * 0.5f);
                _unityLight.intensity = _baseIntensity * (0.5f + pulseAmount * 0.5f);
            }
            
            // Fire effect (similar to flicker but more intense)
            if ((lightData.flags & FLAG_FIRE) != 0)
            {
                float fireSpeed = 8f;
                _flickerTimer += Time.deltaTime * fireSpeed;
                
                // More intense variation for fire
                float fireAmount = 0.5f + (Mathf.PerlinNoise(_flickerTimer, _flickerTimer * 0.5f) * 0.5f);
                _unityLight.intensity = _baseIntensity * fireAmount;
                
                // Slight color variation for fire effect
                float colorVariation = 0.1f * Mathf.Sin(_flickerTimer);
                _unityLight.color = _baseColor * (1f + colorVariation);
            }
        }
        
        /// <summary>
        /// Gets whether this light is dynamic (can be moved/carried)
        /// </summary>
        public bool IsDynamic()
        {
            return lightData != null && (lightData.flags & FLAG_DYNAMIC) != 0;
        }
        
        /// <summary>
        /// Gets whether this light can be carried
        /// </summary>
        public bool CanCarry()
        {
            return lightData != null && (lightData.flags & FLAG_CAN_CARRY) != 0;
        }
        
        /// <summary>
        /// Gets whether this light is negative (subtractive lighting)
        /// </summary>
        public bool IsNegative()
        {
            return lightData != null && (lightData.flags & FLAG_NEGATIVE) != 0;
        }
        
        /// <summary>
        /// Gets the Unity Light component
        /// </summary>
        public Light GetUnityLight()
        {
            return _unityLight;
        }
    }
}
