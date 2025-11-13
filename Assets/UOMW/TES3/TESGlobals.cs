using UnityEngine;

namespace ESMSharp.TES3
{
    /// <summary>
    /// Global constants for TES3 (Morrowind) coordinate and height conversions
    /// </summary>
    public static class TESGlobals
    {
        // Morrowind coordinate system constants
        // Morrowind uses 8192 units per cell in its coordinate system
        // Unity game world uses 64 units per cell (for statics and terrain positioning)
        // Unity heightmap generation uses 65 units per cell (for seamless neighbor alignment)
        
        /// <summary>
        /// Scale factor for converting Morrowind coordinates to Unity static/terrain coordinates
        /// Used for statics, terrain positioning, and height calculations
        /// 64 units per cell / 8192 Morrowind units per cell = 0.0078125
        /// </summary>
        public const float MORROWIND_TO_STATIC_SCALE = 64f / 8192f; // 0.0078125
        
        /// <summary>
        /// Scale factor for converting Morrowind coordinates to Unity terrain heightmap coordinates
        /// Used for height calculations that need to account for the 65-unit heightmap cells
        /// 65 units per cell / 8192 Morrowind units per cell = 0.0079345703125
        /// </summary>
        public const float MORROWIND_TO_TERRAIN_SCALE = 65f / 8192f; // 0.0079345703125
        
        /// <summary>
        /// Height scale factor used in Morrowind heightmap generation
        /// VHGT deltas are accumulated as integers, then multiplied by this factor
        /// Matches OpenMW's Land::sHeightScale = 8
        /// </summary>
        public const float HEIGHT_MAP_SCALE_FACTOR = 8.0f;
        
        /// <summary>
        /// Cell size in Unity world units (game world uses 64 units per cell)
        /// </summary>
        public const float CELL_SIZE = 64f;
        
        /// <summary>
        /// Heightmap cell size (65 units per cell for seamless neighbor alignment)
        /// </summary>
        public const float HEIGHTMAP_CELL_SIZE = 65f;
        
        /// <summary>
        /// Morrowind character height in Morrowind units (standard NPC height)
        /// Characters in Morrowind are typically 128 units tall
        /// </summary>
        public const float MORROWIND_CHARACTER_HEIGHT = 128f;
        
        /// <summary>
        /// Scale factor for NPC/character models
        /// Characters are 128 Morrowind units tall and should be 1 Unity unit tall
        /// Since body part models are already in Morrowind units, we need to scale them to Unity scale
        /// Scale = 1 Unity unit / 128 Morrowind units = 1.0 (models are already correctly sized)
        /// Actually, models loaded from NIF are already in their native scale, so we use 1.0
        /// </summary>
        public const float MORROWIND_TO_CHARACTER_SCALE = 1f; // Models are already at correct scale when loaded
        
        /// <summary>
        /// Enable multithreaded model loading for cells
        /// When enabled, NIF files are parsed on background threads and GameObjects are created on main thread
        /// Set to false for debugging model loading issues
        /// </summary>
        public static bool EnableMultithreadedModelLoading { get; set; } = true;
    }
}
