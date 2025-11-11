using System;
using System.Collections.Generic;
using UnityEngine;

namespace ESMSharp.TES3
{
    /// <summary>
    /// Global NIF model library for caching and reusing loaded NIF models during static placement.
    /// Stores model information including static ID, name, model filename, and the GameObject itself.
    /// </summary>
    public static class TESNifLibrary
    {
        /// <summary>
        /// NIF model entry containing all relevant model information
        /// </summary>
        public class NifEntry
        {
            public string StaticId { get; set; }              // Static object ID (from SubRecordCellObjectID)
            public string StaticName { get; set; }            // Static object name
            public string ModelFilename { get; set; }         // Full model filename (e.g., "tree_01.nif")
            public string BaseFilename { get; set; }          // Filename without extension (e.g., "tree_01")
            public GameObject Model { get; set; }             // The actual GameObject (template model)
            public bool CombineMeshes { get; set; }           // Whether this model was loaded with combineMeshes option
            
            public NifEntry(string staticId, string staticName, string modelFilename, string baseFilename, GameObject model, bool combineMeshes = false)
            {
                StaticId = staticId;
                StaticName = staticName;
                ModelFilename = modelFilename;
                BaseFilename = baseFilename;
                Model = model;
                CombineMeshes = combineMeshes;
            }
        }
        
        // Cache dictionaries for fast lookups
        private static Dictionary<string, NifEntry> _cacheByBaseFilename = new Dictionary<string, NifEntry>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, NifEntry> _cacheByModelFilename = new Dictionary<string, NifEntry>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, NifEntry> _cacheByStaticId = new Dictionary<string, NifEntry>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, NifEntry> _cacheByStaticName = new Dictionary<string, NifEntry>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>
        /// Adds a NIF model to the library. If it already exists (by baseFilename), returns the existing entry.
        /// </summary>
        /// <param name="staticId">Static object ID (from SubRecordCellObjectID)</param>
        /// <param name="staticName">Static object name</param>
        /// <param name="modelFilename">Full model filename (e.g., "tree_01.nif")</param>
        /// <param name="baseFilename">Filename without extension (e.g., "tree_01")</param>
        /// <param name="model">The GameObject template model</param>
        /// <param name="combineMeshes">Whether this model was loaded with combineMeshes option</param>
        /// <returns>The model entry (existing if found, or newly created)</returns>
        public static NifEntry AddModel(string staticId, string staticName, string modelFilename, string baseFilename, GameObject model, bool combineMeshes = false)
        {
            if (model == null)
            {
                Debug.LogWarning($"TESNifLibrary: Attempted to add null model for {baseFilename}");
                return null;
            }
            
            // Normalize baseFilename for consistent lookups
            string normalizedBaseFilename = baseFilename.ToLowerInvariant();
            
            // Check if already exists by baseFilename
            if (_cacheByBaseFilename.TryGetValue(normalizedBaseFilename, out NifEntry existing))
            {
                // Update the entry if needed (in case model object changed)
                if (existing.Model != model)
                {
                    existing.Model = model;
                }
                return existing;
            }
            
            // Create new entry
            NifEntry entry = new NifEntry(staticId, staticName, modelFilename, baseFilename, model, combineMeshes);
            
            // Add to all cache dictionaries
            _cacheByBaseFilename[normalizedBaseFilename] = entry;
            
            if (!string.IsNullOrEmpty(modelFilename))
            {
                _cacheByModelFilename[modelFilename.ToLowerInvariant()] = entry;
            }
            
            if (!string.IsNullOrEmpty(staticId))
            {
                _cacheByStaticId[staticId] = entry;
            }
            
            if (!string.IsNullOrEmpty(staticName))
            {
                _cacheByStaticName[staticName] = entry;
            }
            
            return entry;
        }
        
        /// <summary>
        /// Gets a model by base filename (without extension). Returns null if not found.
        /// </summary>
        public static NifEntry GetModelByBaseFilename(string baseFilename)
        {
            if (string.IsNullOrEmpty(baseFilename))
                return null;
                
            string normalizedBaseFilename = baseFilename.ToLowerInvariant();
            _cacheByBaseFilename.TryGetValue(normalizedBaseFilename, out NifEntry entry);
            return entry;
        }
        
        /// <summary>
        /// Gets a model by full model filename. Returns null if not found.
        /// </summary>
        public static NifEntry GetModelByFilename(string modelFilename)
        {
            if (string.IsNullOrEmpty(modelFilename))
                return null;
                
            _cacheByModelFilename.TryGetValue(modelFilename.ToLowerInvariant(), out NifEntry entry);
            return entry;
        }
        
        /// <summary>
        /// Gets a model by static ID. Returns null if not found.
        /// </summary>
        public static NifEntry GetModelByStaticId(string staticId)
        {
            if (string.IsNullOrEmpty(staticId))
                return null;
                
            _cacheByStaticId.TryGetValue(staticId, out NifEntry entry);
            return entry;
        }
        
        /// <summary>
        /// Gets a model by static name. Returns null if not found.
        /// </summary>
        public static NifEntry GetModelByStaticName(string staticName)
        {
            if (string.IsNullOrEmpty(staticName))
                return null;
                
            _cacheByStaticName.TryGetValue(staticName, out NifEntry entry);
            return entry;
        }
        
        /// <summary>
        /// Gets the GameObject model by base filename. Returns null if not found.
        /// This is a convenience method for quick model lookups.
        /// </summary>
        public static GameObject GetModel(string baseFilename)
        {
            NifEntry entry = GetModelByBaseFilename(baseFilename);
            return entry?.Model;
        }
        
        /// <summary>
        /// Checks if a model exists in the library by base filename.
        /// </summary>
        public static bool HasModel(string baseFilename)
        {
            if (string.IsNullOrEmpty(baseFilename))
                return false;
                
            string normalizedBaseFilename = baseFilename.ToLowerInvariant();
            return _cacheByBaseFilename.ContainsKey(normalizedBaseFilename);
        }
        
        /// <summary>
        /// Checks if a model exists in the library by static ID.
        /// </summary>
        public static bool HasModelByStaticId(string staticId)
        {
            if (string.IsNullOrEmpty(staticId))
                return false;
            return _cacheByStaticId.ContainsKey(staticId);
        }
        
        /// <summary>
        /// Gets or loads a model. If the model exists in the library, returns it.
        /// Otherwise, loads it using the provided load function and adds it to the library.
        /// </summary>
        /// <param name="baseFilename">Base filename without extension</param>
        /// <param name="staticId">Optional static object ID</param>
        /// <param name="staticName">Optional static object name</param>
        /// <param name="modelFilename">Optional full model filename</param>
        /// <param name="loadModelFunc">Function to load the model if not cached (returns GameObject or null)</param>
        /// <param name="combineMeshes">Whether to load with combineMeshes option</param>
        /// <returns>The model entry, or null if loading failed</returns>
        public static NifEntry GetOrLoadModel(string baseFilename, string staticId = null, string staticName = null, string modelFilename = null, Func<GameObject> loadModelFunc = null, bool combineMeshes = false)
        {
            // Check if already cached
            NifEntry existing = GetModelByBaseFilename(baseFilename);
            if (existing != null)
            {
                return existing;
            }
            
            // Try to load if load function provided
            if (loadModelFunc != null)
            {
                GameObject model = loadModelFunc();
                if (model != null)
                {
                    // Use provided modelFilename or construct from baseFilename
                    string finalModelFilename = modelFilename ?? (baseFilename + ".nif");
                    return AddModel(staticId, staticName, finalModelFilename, baseFilename, model, combineMeshes);
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Clears all cached models from the library.
        /// </summary>
        public static void Clear()
        {
            _cacheByBaseFilename.Clear();
            _cacheByModelFilename.Clear();
            _cacheByStaticId.Clear();
            _cacheByStaticName.Clear();
        }
        
        /// <summary>
        /// Gets the total number of models in the library.
        /// </summary>
        public static int Count => _cacheByBaseFilename.Count;
        
        /// <summary>
        /// Gets all model entries in the library.
        /// </summary>
        public static IEnumerable<NifEntry> GetAllModels()
        {
            return _cacheByBaseFilename.Values;
        }
    }
}
