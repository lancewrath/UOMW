using UnityEngine;
using ESMSharp.TES3;
using ESMSharp.TES3.Records;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BSASharp;

namespace ESMSharp.NIF
{
    public class NIFModels
    {
        private string _esm = "";
        private string _bsa = "";

        public void GatherModels(Record[] _records, string bsa = "Morrowind.bsa", string esm = "Morrowind")
        {
            _bsa = bsa;
            _esm = System.IO.Path.GetFileNameWithoutExtension(esm);

            // Step 1: Collect unique model filenames from STAT records
            HashSet<string> uniqueModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Record rec in _records)
            {
                RecordStat statRecord = rec as RecordStat;
                if (statRecord != null)
                {
                    string modelFilename = null;

                    // Extract MODL (model filename) from subrecords
                    foreach (SubRecords subrec in statRecord.subRecords)
                    {
                        SubRecordStatMODL modlSubrec = subrec as SubRecordStatMODL;
                        if (modlSubrec != null)
                        {
                            modelFilename = modlSubrec.model;
                            if (!string.IsNullOrEmpty(modelFilename))
                            {
                                // Clean null characters and whitespace
                                modelFilename = modelFilename.TrimEnd('\0', ' ', '\t', '\r', '\n');
                                modelFilename = modelFilename.Replace("\0", "");
                                modelFilename = modelFilename.Trim();
                                
                                if (!string.IsNullOrEmpty(modelFilename))
                                {
                                    uniqueModels.Add(modelFilename);
                                }
                            }
                        }
                    }
                }
            }

            UnityEngine.Debug.Log($"Found {uniqueModels.Count} unique model files in STAT records");

            // Step 2: Open BSA archive
            string bsaPath = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", _bsa);
            if (!System.IO.File.Exists(bsaPath))
            {
                UnityEngine.Debug.LogError($"BSA file not found: {bsaPath}");
                return;
            }

            BSA bsaArchive = null;
            try
            {
                bsaArchive = new BSA();
                bsaArchive.Open(bsaPath);
                UnityEngine.Debug.Log($"Opened BSA archive: {bsaPath}");
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"Failed to open BSA archive: {ex.Message}");
                return;
            }

            // Step 3: Create output directory
            string outputDir = System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "Data", "UOMW", "Cache", "Models", _esm);
            System.IO.Directory.CreateDirectory(outputDir);

            // Step 4: Get all file names from BSA for path resolution
            HashSet<string> bsaFileNames = new HashSet<string>(bsaArchive.GetFileNames(), StringComparer.OrdinalIgnoreCase);

            // Step 5: Extract models from BSA
            int extractedCount = 0;
            int failedCount = 0;

            foreach (string modelFilename in uniqueModels)
            {
                try
                {
                    // Check if model is already cached
                    string baseFilename = System.IO.Path.GetFileName(modelFilename);
                    string outputPath = System.IO.Path.Combine(outputDir, baseFilename);
                    
                    if (System.IO.File.Exists(outputPath))
                    {
                        extractedCount++;
                        UnityEngine.Debug.Log($"Model already cached: {baseFilename} (skipping extraction)");
                        continue;
                    }

                    // Try to find the model in BSA with various path variations
                    string foundPath = null;
                    string[] pathVariations = new string[]
                    {
                        modelFilename,
                        modelFilename.Replace('/', '\\'),
                        modelFilename.Replace('\\', '/'),
                        "meshes\\" + modelFilename,
                        "meshes/" + modelFilename,
                        "Meshes\\" + modelFilename,
                        "Meshes/" + modelFilename,
                        "MESHES\\" + modelFilename,
                        "MESHES/" + modelFilename
                    };

                    foreach (string pathVar in pathVariations)
                    {
                        if (bsaFileNames.Contains(pathVar))
                        {
                            foundPath = pathVar;
                            break;
                        }
                    }

                    // Try case-insensitive search if exact match failed
                    if (foundPath == null)
                    {
                        string modelLower = modelFilename.ToLower();
                        foreach (string bsaFileName in bsaFileNames)
                        {
                            if (bsaFileName.ToLower() == modelLower || 
                                bsaFileName.ToLower().EndsWith("\\" + modelLower) ||
                                bsaFileName.ToLower().EndsWith("/" + modelLower))
                            {
                                foundPath = bsaFileName;
                                break;
                            }
                        }
                    }

                    if (foundPath != null)
                    {
                        // Extract model from BSA
                        BSAFileEntry entry = bsaArchive.GetFileEntry(foundPath);
                        if (entry != null)
                        {
                            byte[] modelData = bsaArchive.ExtractFile(entry);
                            System.IO.File.WriteAllBytes(outputPath, modelData);
                            extractedCount++;
                            UnityEngine.Debug.Log($"Extracted model: {foundPath} -> {baseFilename}");
                        }
                        else
                        {
                            UnityEngine.Debug.LogWarning($"Model entry not found in BSA: {foundPath}");
                            failedCount++;
                        }
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"Model not found in BSA: {modelFilename} (tried {pathVariations.Length} variations)");
                        failedCount++;
                    }
                }
                catch (System.Exception ex)
                {
                    UnityEngine.Debug.LogError($"Error extracting model {modelFilename}: {ex.Message}\n{ex.StackTrace}");
                    failedCount++;
                }
            }

            // Clean up
            if (bsaArchive != null)
            {
                bsaArchive.Close();
            }

            UnityEngine.Debug.Log($"Model extraction complete: {extractedCount} extracted, {failedCount} failed");
        }
    }
}
