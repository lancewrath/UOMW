using ESMSharp.TES3;
using ESMSharp.TES3Terrain;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ESMSharp.TES3
{
    /// <summary>
    /// Main game controller for TES3 (Morrowind) content loading and management.
    /// Handles initialization, terrain generation, and dynamic cell loading based on player position.
    /// </summary>
    public class TESGame : MonoBehaviour
    {
        [Header("Initialization Settings")]
        [Tooltip("Data folder path (defaults to StreamingAssets/Data)")]
        public string dataFolder = null;
        
        [Header("Player Settings")]
        [Tooltip("Tag for player-controlled objects (default: 'Player')")]
        public string playerTag = "Player";
        
        [Header("Dynamic Cell Loading")]
        [Tooltip("Number of cells to load around the player (default: 2)")]
        [Range(1, 5)]
        public int cellLoadRadius = 2;
        
        [Tooltip("How often to check player position for cell loading (seconds)")]
        [Range(0.1f, 5f)]
        public float cellCheckInterval = 1f;
        
        [Header("Status")]
        [SerializeField] private bool _isInitialized = false;
        [SerializeField] private bool _isGenerating = false;
        [SerializeField] private int _loadedCellCount = 0;
        
        // References
        private TES3Master _tes3Master = null;
        private CellManager _cellManager = null;
        private GameObject _playerObject = null;
        private List<GameObject> _playerObjects = new List<GameObject>(); // Store references to player objects before disabling
        private HashSet<string> _loadedCellKeys = new HashSet<string>();
        private HashSet<string> _loadingCellKeys = new HashSet<string>(); // Cells currently being loaded
        private Dictionary<string, Coroutine> _activeCellLoadingCoroutines = new Dictionary<string, Coroutine>(); // Track active loading coroutines per cell
        private int _currentPlayerCellX = int.MaxValue;
        private int _currentPlayerCellY = int.MaxValue;
        private Coroutine _cellLoadingCoroutine = null;
        
        /// <summary>
        /// Whether the game content has been initialized
        /// </summary>
        public bool IsInitialized => _isInitialized;
        
        /// <summary>
        /// Whether content is currently being generated
        /// </summary>
        public bool IsGenerating => _isGenerating;
        
        void Start()
        {
            // Start initialization coroutine
            StartCoroutine(InitializeGameCoroutine());
        }
        
        /// <summary>
        /// Initializes the game content (ESM loading, terrain generation, etc.)
        /// </summary>
        private IEnumerator InitializeGameCoroutine()
        {
            if (_isInitialized)
            {
                Debug.LogWarning("TESGame: Already initialized!");
                yield break;
            }
            
            _isGenerating = true;
            Debug.Log("TESGame: Starting initialization...");
            
            // Step 1: Disable all player objects
            DisablePlayerObjects();
            
            // Step 2: Initialize ESM and BSA libraries
            Debug.Log("TESGame: Initializing ESM/BSA libraries...");

            _tes3Master = new TES3Master();

            //Use Null or empty because unity will initialize as a blank string
            if (string.IsNullOrEmpty(dataFolder))
            {
                dataFolder = Path.Combine(Application.dataPath, "StreamingAssets", "Data");
            }
            if (!TES3Master.InitializeLibraries(dataFolder))
            {
                Debug.LogError("TESGame: Failed to initialize ESM/BSA libraries!");
                _isGenerating = false;
                yield break;
            }
            yield return null; // Yield a frame
            
            // Step 3: Generate heightmaps
            Debug.Log("TESGame: Generating heightmaps...");
            _tes3Master.GenerateTerrainMaps_MergedLands();
            yield return null; // Yield a frame
            
            // Step 4: Generate terrain
            Debug.Log("TESGame: Generating terrain...");
            _tes3Master.GenerateTerrain();
            yield return null; // Yield a frame
            
            // Step 5: Generate all cells
            Debug.Log("TESGame: Generating cells...");
            _tes3Master.GenerateStatics();
            _cellManager = _tes3Master.CreateCells(transform);
            if (_cellManager == null)
            {
                Debug.LogError("TESGame: Failed to create CellManager!");
                _isGenerating = false;
                yield break;
            }
            yield return null; // Yield a frame
            
            // Step 6: Re-enable player objects
            EnablePlayerObjects();
            
            // Step 7: Start dynamic cell loading
            _isInitialized = true;
            _isGenerating = false;
            Debug.Log("TESGame: Initialization complete!");
            
            // Wait a frame to ensure player object is active
            yield return null;
            
            // Try to find player object if not found yet
            if (_playerObject == null)
            {
                GameObject[] playerObjects = GameObject.FindGameObjectsWithTag(playerTag);
                if (playerObjects.Length > 0)
                {
                    _playerObject = playerObjects[0];
                    Debug.Log($"TESGame: Found player object after initialization: {_playerObject.name}");
                }
                else
                {
                    // Try alternative methods
                    _playerObject = GameObject.Find("Player");
                    if (_playerObject == null)
                    {
                        _playerObject = GameObject.FindGameObjectWithTag("MainCamera")?.transform.parent?.gameObject;
                    }
                    if (_playerObject != null)
                    {
                        Debug.Log($"TESGame: Found player object by alternative method: {_playerObject.name}");
                    }
                }
            }
            
            // Start dynamic cell loading system
            StartDynamicCellLoading();
            
            // Force initial cell load check
            if (_playerObject != null)
            {
                Vector3 playerPos = _playerObject.transform.position;
                int playerCellX = Mathf.FloorToInt(playerPos.x / TESGlobals.CELL_SIZE);
                int playerCellY = Mathf.FloorToInt(playerPos.z / TESGlobals.CELL_SIZE);
                _currentPlayerCellX = playerCellX;
                _currentPlayerCellY = playerCellY;
                Debug.Log($"TESGame: Player at cell ({playerCellX}, {playerCellY}), starting initial cell load...");
                yield return StartCoroutine(UpdateLoadedCellsCoroutine(playerCellX, playerCellY));
            }
            else
            {
                Debug.LogWarning("TESGame: Player object not found! Dynamic cell loading will not work until player is found.");
            }
        }
        
        /// <summary>
        /// Disables all objects tagged as player (stores references before disabling)
        /// </summary>
        private void DisablePlayerObjects()
        {
            _playerObjects.Clear();
            
            // Find all player objects BEFORE disabling them (FindGameObjectsWithTag only finds active objects)
            GameObject[] playerObjects = GameObject.FindGameObjectsWithTag(playerTag);
            
            // Also try to find by name as fallback
            if (playerObjects.Length == 0)
            {
                GameObject playerByName = GameObject.Find("Player");
                if (playerByName != null)
                {
                    playerObjects = new GameObject[] { playerByName };
                }
            }
            
            // Store references before disabling
            foreach (GameObject obj in playerObjects)
            {
                _playerObjects.Add(obj);
                obj.SetActive(false);
            }
            
            Debug.Log($"TESGame: Disabled {_playerObjects.Count} player object(s) (stored references)");
        }
        
        /// <summary>
        /// Enables all objects tagged as player (uses stored references since FindGameObjectsWithTag doesn't find disabled objects)
        /// </summary>
        private void EnablePlayerObjects()
        {
            // Use stored references (FindGameObjectsWithTag doesn't find disabled objects)
            if (_playerObjects.Count > 0)
            {
                foreach (GameObject obj in _playerObjects)
                {
                    if (obj != null)
                    {
                        obj.SetActive(true);
                        if (_playerObject == null)
                        {
                            _playerObject = obj; // Store first player object as reference
                            Debug.Log($"TESGame: Stored player object reference: {obj.name} at position {obj.transform.position}");
                        }
                    }
                }
                Debug.Log($"TESGame: Enabled {_playerObjects.Count} player object(s) from stored references");
            }
            else
            {
                // Fallback: try to find player by name (might work if object is still active)
                Debug.LogWarning($"TESGame: No stored player object references. Trying to find player by name...");
                GameObject playerByName = GameObject.Find("Player");
                if (playerByName == null)
                {
                    playerByName = GameObject.FindGameObjectWithTag("MainCamera")?.transform.parent?.gameObject;
                }
                if (playerByName != null)
                {
                    playerByName.SetActive(true);
                    _playerObject = playerByName;
                    _playerObjects.Add(playerByName);
                    Debug.Log($"TESGame: Found and enabled player object by name: {playerByName.name}");
                }
                else
                {
                    Debug.LogError($"TESGame: Could not find player object to enable! Make sure player is tagged as '{playerTag}' or named 'Player'.");
                }
            }
        }
        
        /// <summary>
        /// Starts the dynamic cell loading system
        /// </summary>
        private void StartDynamicCellLoading()
        {
            if (_cellLoadingCoroutine != null)
            {
                StopCoroutine(_cellLoadingCoroutine);
            }
            _cellLoadingCoroutine = StartCoroutine(DynamicCellLoadingCoroutine());
        }
        
        /// <summary>
        /// Coroutine that monitors player position and loads/unloads cells dynamically
        /// </summary>
        private IEnumerator DynamicCellLoadingCoroutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(cellCheckInterval);
                
                if (!_isInitialized || _playerObject == null)
                    continue;
                
                // Get player's current cell coordinates
                Vector3 playerPos = _playerObject.transform.position;
                int playerCellX = Mathf.FloorToInt(playerPos.x / TESGlobals.CELL_SIZE);
                int playerCellY = Mathf.FloorToInt(playerPos.z / TESGlobals.CELL_SIZE);
                
                // Check if player moved to a new cell
                if (playerCellX != _currentPlayerCellX || playerCellY != _currentPlayerCellY)
                {
                    _currentPlayerCellX = playerCellX;
                    _currentPlayerCellY = playerCellY;
                    
                    // Update loaded cells around player
                    yield return StartCoroutine(UpdateLoadedCellsCoroutine(playerCellX, playerCellY));
                }
            }
        }
        
        /// <summary>
        /// Updates which cells are loaded around the player
        /// Prioritizes the player's current cell first, then neighbors
        /// Stops ongoing neighbor loading when player moves to a new cell
        /// </summary>
        private IEnumerator UpdateLoadedCellsCoroutine(int centerCellX, int centerCellY)
        {
            string newPlayerCellKey = $"{centerCellX}_{centerCellY}";
            
            // Stop any ongoing neighbor cell loading coroutines (but keep the player's current cell if it's loading)
            List<string> cellsToStop = new List<string>();
            foreach (var kvp in _activeCellLoadingCoroutines)
            {
                string cellKey = kvp.Key;
                // Stop if it's not the new player cell and it's currently loading
                if (cellKey != newPlayerCellKey && _loadingCellKeys.Contains(cellKey))
                {
                    cellsToStop.Add(cellKey);
                }
            }
            
            // Stop the coroutines
            foreach (string cellKey in cellsToStop)
            {
                if (_activeCellLoadingCoroutines.TryGetValue(cellKey, out Coroutine coroutine))
                {
                    if (coroutine != null)
                    {
                        StopCoroutine(coroutine);
                        Debug.Log($"TESGame: Stopped loading cell {cellKey} (player moved to new cell)");
                    }
                    _activeCellLoadingCoroutines.Remove(cellKey);
                    _loadingCellKeys.Remove(cellKey);
                }
            }
            
            HashSet<string> cellsToLoad = new HashSet<string>();
            HashSet<string> cellsToUnload = new HashSet<string>(_loadedCellKeys);
            
            // Determine which cells should be loaded (within radius)
            for (int x = centerCellX - cellLoadRadius; x <= centerCellX + cellLoadRadius; x++)
            {
                for (int y = centerCellY - cellLoadRadius; y <= centerCellY + cellLoadRadius; y++)
                {
                    string cellKey = $"{x}_{y}";
                    cellsToLoad.Add(cellKey);
                    
                    // Remove from unload list if it should stay loaded
                    cellsToUnload.Remove(cellKey);
                }
            }
            
            // Unload cells that are too far away
            foreach (string cellKey in cellsToUnload)
            {
                UnloadCell(cellKey);
                yield return null; // Yield between unloads
            }
            
            // Priority 1: Load the player's current cell first (or wait if already loading)
            if (cellsToLoad.Contains(newPlayerCellKey) && !_loadedCellKeys.Contains(newPlayerCellKey))
            {
                if (_loadingCellKeys.Contains(newPlayerCellKey))
                {
                    // Already loading, wait for it to complete
                    Debug.Log($"TESGame: Player's current cell ({centerCellX}, {centerCellY}) is already loading, waiting...");
                    while (_loadingCellKeys.Contains(newPlayerCellKey))
                    {
                        yield return null;
                    }
                }
                else
                {
                    // Start loading the player's current cell
                    Debug.Log($"TESGame: Prioritizing player's current cell ({centerCellX}, {centerCellY})...");
                    Coroutine playerCellCoroutine = StartCoroutine(LoadCellCoroutine(newPlayerCellKey));
                    _activeCellLoadingCoroutines[newPlayerCellKey] = playerCellCoroutine; // Store the coroutine reference
                    yield return playerCellCoroutine; // Wait for it to complete
                }
                cellsToLoad.Remove(newPlayerCellKey); // Remove from list so we don't load it again
            }
            
            // Priority 2: Load neighboring cells (cells adjacent to player's cell)
            List<string> neighborCells = new List<string>();
            for (int x = centerCellX - 1; x <= centerCellX + 1; x++)
            {
                for (int y = centerCellY - 1; y <= centerCellY + 1; y++)
                {
                    if (x == centerCellX && y == centerCellY)
                        continue; // Skip player's cell (already loaded)
                    
                    string cellKey = $"{x}_{y}";
                    if (cellsToLoad.Contains(cellKey) && !_loadedCellKeys.Contains(cellKey) && !_loadingCellKeys.Contains(cellKey))
                    {
                        neighborCells.Add(cellKey);
                    }
                }
            }
            
            // Load immediate neighbors (start them all, but don't wait - they'll run in parallel)
            foreach (string cellKey in neighborCells)
            {
                Coroutine neighborCoroutine = StartCoroutine(LoadCellCoroutine(cellKey));
                _activeCellLoadingCoroutines[cellKey] = neighborCoroutine; // Store the outer coroutine reference
                cellsToLoad.Remove(cellKey);
            }
            
            // Wait a frame to let neighbor loading start
            yield return null;
            
            // Priority 3: Load remaining cells (further away cells within radius)
            foreach (string cellKey in cellsToLoad)
            {
                if (!_loadedCellKeys.Contains(cellKey) && !_loadingCellKeys.Contains(cellKey))
                {
                    Coroutine remainingCoroutine = StartCoroutine(LoadCellCoroutine(cellKey));
                    _activeCellLoadingCoroutines[cellKey] = remainingCoroutine; // Store the outer coroutine reference
                }
            }
        }
        
        /// <summary>
        /// Loads a cell and generates its statics
        /// </summary>
        private IEnumerator LoadCellCoroutine(string cellKey)
        {
            // Mark as loading (coroutine reference is stored by caller)
            _loadingCellKeys.Add(cellKey);
            
            try
            {
                if (_cellManager == null)
                {
                    Debug.LogWarning($"TESGame: Cannot load cell {cellKey} - CellManager is null");
                    yield break;
                }
                
                // Parse cell coordinates from key
                string[] parts = cellKey.Split('_');
                if (parts.Length != 2)
                {
                    Debug.LogWarning($"TESGame: Invalid cell key format: {cellKey}");
                    yield break;
                }
                
                if (!int.TryParse(parts[0], out int cellX) || !int.TryParse(parts[1], out int cellY))
                {
                    Debug.LogWarning($"TESGame: Failed to parse cell coordinates from key: {cellKey}");
                    yield break;
                }
                
                // Get the cell GameObject from CellManager
                GameObject cellObj = _cellManager.GetCell(cellX, cellY);
                if (cellObj == null)
                {
                    // Cell doesn't exist (might be outside world bounds or interior)
                    Debug.Log($"TESGame: Cell ({cellX}, {cellY}) does not exist (may be outside world bounds or interior)");
                    yield break;
                }
                
                // Get TESCell component and generate statics if not already generated
                TESCell tesCell = cellObj.GetComponent<TESCell>();
                if (tesCell != null && !tesCell.staticsGenerated)
                {
                    Debug.Log($"TESGame: Loading statics for cell ({cellX}, {cellY})...");
                    
                    // Wait for statics to generate
                    yield return StartCoroutine(tesCell.GenerateStatics());
                    
                    Debug.Log($"TESGame: Finished loading statics for cell ({cellX}, {cellY})");
                }
                else if (tesCell == null)
                {
                    Debug.LogWarning($"TESGame: Cell ({cellX}, {cellY}) does not have TESCell component!");
                }
                else if (tesCell.staticsGenerated)
                {
                    Debug.Log($"TESGame: Cell ({cellX}, {cellY}) statics already generated");
                }
                
                // Mark as loaded
                _loadedCellKeys.Add(cellKey);
                _loadedCellCount = _loadedCellKeys.Count;
            }
            finally
            {
                // Clean up loading state
                _loadingCellKeys.Remove(cellKey);
                _activeCellLoadingCoroutines.Remove(cellKey);
            }
        }
        
        /// <summary>
        /// Unloads a cell (currently just marks it as unloaded, doesn't destroy it)
        /// Also stops any ongoing loading coroutine for this cell
        /// Future: Could implement actual unloading/destruction of cell statics
        /// </summary>
        private void UnloadCell(string cellKey)
        {
            // Stop any ongoing loading coroutine
            if (_activeCellLoadingCoroutines.TryGetValue(cellKey, out Coroutine coroutine))
            {
                if (coroutine != null)
                {
                    StopCoroutine(coroutine);
                }
                _activeCellLoadingCoroutines.Remove(cellKey);
            }
            
            _loadingCellKeys.Remove(cellKey);
            _loadedCellKeys.Remove(cellKey);
            _loadedCellCount = _loadedCellKeys.Count;
            // Note: We don't destroy the cell GameObject or its statics here
            // This could be implemented later if memory management is needed
        }
        
        /// <summary>
        /// Gets the cell coordinates for a world position
        /// </summary>
        public static (int cellX, int cellY) GetCellCoordinates(Vector3 worldPosition)
        {
            int cellX = Mathf.FloorToInt(worldPosition.x / TESGlobals.CELL_SIZE);
            int cellY = Mathf.FloorToInt(worldPosition.z / TESGlobals.CELL_SIZE);
            return (cellX, cellY);
        }
        
        /// <summary>
        /// Gets the cell GameObject at the specified coordinates
        /// </summary>
        public GameObject GetCellAt(int cellX, int cellY)
        {
            if (_cellManager == null)
                return null;
            return _cellManager.GetCell(cellX, cellY);
        }
        
        /// <summary>
        /// Gets the cell GameObject at the specified world position
        /// </summary>
        public GameObject GetCellAt(Vector3 worldPosition)
        {
            var (cellX, cellY) = GetCellCoordinates(worldPosition);
            return GetCellAt(cellX, cellY);
        }
    }
}
