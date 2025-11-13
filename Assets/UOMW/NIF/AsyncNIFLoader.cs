using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ESMSharp.TES3;

namespace ESMSharp.NIF
{
    /// <summary>
    /// Handles async multithreaded loading of NIF models
    /// When multithreading is enabled, file I/O happens on background threads
    /// NIF parsing and GameObject creation happens on main thread via coroutines
    /// </summary>
    public class AsyncNIFLoader : MonoBehaviour
    {
        private static AsyncNIFLoader _instance = null;
        private static readonly object _lock = new object();
        
        /// <summary>
        /// Singleton instance for processing async load requests
        /// </summary>
        public static AsyncNIFLoader Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            GameObject loaderObj = new GameObject("AsyncNIFLoader");
                            _instance = loaderObj.AddComponent<AsyncNIFLoader>();
                            DontDestroyOnLoad(loaderObj);
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Queue of pending model load requests
        /// </summary>
        private Queue<ModelLoadRequest> _loadQueue = new Queue<ModelLoadRequest>();
        private bool _isProcessing = false;

        /// <summary>
        /// Model load request
        /// </summary>
        public class ModelLoadRequest
        {
            public NIFLoader Loader { get; set; }
            public string ModelFilename { get; set; }
            public bool CombineMeshes { get; set; }
            public Action<GameObject> OnComplete { get; set; }
        }

        /// <summary>
        /// Queues a model load request for async processing
        /// </summary>
        public void QueueLoad(ModelLoadRequest request)
        {
            lock (_loadQueue)
            {
                _loadQueue.Enqueue(request);
            }

            if (!_isProcessing)
            {
                StartCoroutine(ProcessLoadQueue());
            }
        }

        /// <summary>
        /// Processes the load queue using coroutines
        /// File I/O happens on background thread, GameObject creation on main thread
        /// </summary>
        private IEnumerator ProcessLoadQueue()
        {
            _isProcessing = true;

            while (true)
            {
                ModelLoadRequest request = null;
                lock (_loadQueue)
                {
                    if (_loadQueue.Count == 0)
                    {
                        _isProcessing = false;
                        yield break;
                    }
                    request = _loadQueue.Dequeue();
                }

                if (request == null)
                    continue;

                // Check cache first (thread-safe read)
                string baseFilenameNoExt = Path.GetFileNameWithoutExtension(request.ModelFilename);
                TESNifLibrary.NifEntry cachedEntry = TESNifLibrary.GetModelByBaseFilename(baseFilenameNoExt);
                if (cachedEntry != null)
                {
                    // Return cached model immediately
                    GameObject instance = GameObject.Instantiate(cachedEntry.Model);
                    instance.name = cachedEntry.Model.name;
                    request.OnComplete?.Invoke(instance);
                    continue;
                }

                // Load file on background thread
                byte[] nifData = null;
                string actualFilename = null;
                System.Exception loadError = null;

                Task loadTask = Task.Run(() =>
                {
                    try
                    {
                        string cachePath = FindNIFInCache(request.Loader.ESMName, request.ModelFilename);
                        if (string.IsNullOrEmpty(cachePath) || !File.Exists(cachePath))
                        {
                            loadError = new FileNotFoundException($"NIF file not found: {request.ModelFilename}");
                            return;
                        }

                        nifData = File.ReadAllBytes(cachePath);
                        actualFilename = Path.GetFileName(cachePath);
                    }
                    catch (System.Exception ex)
                    {
                        loadError = ex;
                    }
                });

                // Wait for file load to complete
                while (!loadTask.IsCompleted)
                {
                    yield return null;
                }

                if (loadError != null || nifData == null)
                {
                    UnityEngine.Debug.LogError($"AsyncNIFLoader: Failed to load {request.ModelFilename}: {loadError?.Message ?? "Unknown error"}");
                    request.OnComplete?.Invoke(null);
                    continue;
                }

                // Parse NIF and create GameObject on main thread
                // Note: niflib.net parsing and GameObject creation must be on main thread
                GameObject model = null;
                try
                {
                    model = request.Loader.LoadNIFFromBytes(nifData, actualFilename, request.CombineMeshes);
                }
                catch (System.Exception ex)
                {
                    UnityEngine.Debug.LogError($"AsyncNIFLoader: Failed to parse NIF {request.ModelFilename}: {ex.Message}");
                    model = null;
                }

                request.OnComplete?.Invoke(model);
                
                // Yield to next frame to avoid blocking
                yield return null;
            }
        }

        /// <summary>
        /// Finds NIF file in cache directory with case-insensitive search
        /// </summary>
        private string FindNIFInCache(string esm, string modelFilename)
        {
            // Normalize filename
            string normalizedFilename = modelFilename?.Replace('\\', '/');
            normalizedFilename = Path.GetFileName(normalizedFilename);
            
            string cachePath = Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Models", esm, normalizedFilename);
            
            if (File.Exists(cachePath))
                return cachePath;

            // Try original filename
            string originalPath = Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Models", esm, modelFilename);
            if (File.Exists(originalPath))
                return originalPath;

            // Try case-insensitive search
            string cacheDir = Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Models", esm);
            if (Directory.Exists(cacheDir))
            {
                string[] files = Directory.GetFiles(cacheDir, "*.nif", SearchOption.TopDirectoryOnly);
                string searchFilename = Path.GetFileName(modelFilename);
                foreach (string file in files)
                {
                    string fileName = Path.GetFileName(file);
                    if (string.Equals(fileName, searchFilename, StringComparison.OrdinalIgnoreCase))
                    {
                        return file;
                    }
                }
            }

            return null;
        }

        void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }

    /// <summary>
    /// Extension methods for async NIF loading
    /// </summary>
    public static class NIFLoaderAsyncExtensions
    {
        /// <summary>
        /// Loads a NIF model asynchronously
        /// When multithreading is enabled, file I/O happens on background thread
        /// When disabled, loads synchronously
        /// </summary>
        public static void LoadNIFFromCacheAsync(this NIFLoader loader, string modelFilename, bool combineMeshes, Action<GameObject> onComplete)
        {
            if (!TESGlobals.EnableMultithreadedModelLoading)
            {
                // Synchronous fallback
                GameObject result = loader.LoadNIFFromCache(modelFilename, combineMeshes);
                onComplete?.Invoke(result);
                return;
            }

            // Queue async load
            AsyncNIFLoader.Instance.QueueLoad(new AsyncNIFLoader.ModelLoadRequest
            {
                Loader = loader,
                ModelFilename = modelFilename,
                CombineMeshes = combineMeshes,
                OnComplete = onComplete
            });
        }
    }
}
