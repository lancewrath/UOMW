using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace BSASharp
{
    /// <summary>
    /// BSA version enumeration matching OpenMW's BsaVersion
    /// </summary>
    public enum BSAVersion : uint
    {
        Unknown = 0x0,
        Uncompressed = 0x100,        // Morrowind BSA format
        Compressed = 0x415342,       // "BSA" - Oblivion/Fallout compressed format
        BA2GNRL,                     // Fallout 4 general files
        BA2DX10                      // Fallout 4 textures
    }

    /// <summary>
    /// Hash structure for BSA file lookup (64-bit hash split into two 32-bit values)
    /// </summary>
    [Serializable]
    public struct BSAHash
    {
        public uint Low;
        public uint High;

        public BSAHash(uint low, uint high)
        {
            Low = low;
            High = high;
        }

        public override bool Equals(object obj)
        {
            if (obj is BSAHash hash)
                return Low == hash.Low && High == hash.High;
            return false;
        }

        public override int GetHashCode()
        {
            return Low.GetHashCode() ^ High.GetHashCode();
        }
    }

    /// <summary>
    /// Represents a file entry in the BSA archive
    /// </summary>
    public class BSAFileEntry
    {
        public uint FileSize { get; set; }
        public uint Offset { get; set; }
        public BSAHash Hash { get; set; }
        public string Name { get; set; }

        public BSAFileEntry()
        {
            Name = string.Empty;
        }
    }

    /// <summary>
    /// Main class for reading and extracting files from Morrowind BSA archives
    /// Based on OpenMW's BSAFile implementation
    /// </summary>
    public class BSA : IDisposable
    {
        private bool _isLoaded = false;
        private string _filepath = string.Empty;
        private FileStream _fileStream = null;
        private List<BSAFileEntry> _files = new List<BSAFileEntry>();
        private Dictionary<string, BSAFileEntry> _fileLookup = new Dictionary<string, BSAFileEntry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gets whether the BSA archive is currently loaded
        /// </summary>
        public bool IsLoaded => _isLoaded;

        /// <summary>
        /// Gets the filepath of the loaded BSA archive
        /// </summary>
        public string Filepath => _filepath;

        /// <summary>
        /// Gets a list of all files in the archive
        /// </summary>
        public IReadOnlyList<BSAFileEntry> Files => _files.AsReadOnly();

        /// <summary>
        /// Gets the number of files in the archive
        /// </summary>
        public int FileCount => _files.Count;

        /// <summary>
        /// Detects the BSA version from a file without fully loading it
        /// </summary>
        public static BSAVersion DetectVersion(string filePath)
        {
            if (!File.Exists(filePath))
                return BSAVersion.Unknown;

            try
            {
                using (var fs = File.OpenRead(filePath))
                using (var reader = new BinaryReader(fs))
                {
                    if (fs.Length < 12)
                        return BSAVersion.Unknown;

                    uint head0 = reader.ReadUInt32();
                    uint head1 = reader.ReadUInt32();
                    uint head2 = reader.ReadUInt32();

                    if (head0 == (uint)BSAVersion.Uncompressed)
                        return BSAVersion.Uncompressed;

                    if (head0 == (uint)BSAVersion.Compressed)
                        return BSAVersion.Compressed;

                    // Check for BA2 formats
                    fs.Position = 0;
                    byte[] fourcc = reader.ReadBytes(4);
                    if (Encoding.ASCII.GetString(fourcc) == "BTDX")
                    {
                        fs.Position = 8;
                        byte[] type = reader.ReadBytes(4);
                        string typeStr = Encoding.ASCII.GetString(type);
                        if (typeStr == "GNRL")
                            return BSAVersion.BA2GNRL;
                        if (typeStr == "DX10")
                            return BSAVersion.BA2DX10;
                    }
                }
            }
            catch
            {
                return BSAVersion.Unknown;
            }

            return BSAVersion.Unknown;
        }

        /// <summary>
        /// Opens and loads a BSA archive file
        /// </summary>
        public void Open(string filePath)
        {
            if (_isLoaded)
                Close();

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"BSA file not found: {filePath}");

            _filepath = filePath;
            _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            // Detect version
            BSAVersion version = DetectVersion(filePath);
            if (version != BSAVersion.Uncompressed)
                throw new NotSupportedException($"BSA version {version} is not supported. Only uncompressed Morrowind BSA (0x100) is currently supported.");

            ReadHeader();
            _isLoaded = true;
        }

        /// <summary>
        /// Closes the BSA archive and releases resources
        /// </summary>
        public void Close()
        {
            if (_fileStream != null)
            {
                _fileStream.Dispose();
                _fileStream = null;
            }

            _files.Clear();
            _fileLookup.Clear();
            _isLoaded = false;
            _filepath = string.Empty;
        }

        /// <summary>
        /// Reads the BSA header and directory structure
        /// Based on OpenMW's BSAFile::readHeader implementation
        /// </summary>
        private void ReadHeader()
        {
            if (_fileStream == null)
                throw new InvalidOperationException("File stream is not open");

            long fileSize = _fileStream.Length;

            if (fileSize < 12)
                throw new InvalidDataException("File too small to be a valid BSA archive");

            using (var reader = new BinaryReader(_fileStream, Encoding.UTF8, leaveOpen: true))
            {
                // Read header (12 bytes)
                uint version = reader.ReadUInt32();
                uint dirSize = reader.ReadUInt32();
                uint fileCount = reader.ReadUInt32();

                if (version != (uint)BSAVersion.Uncompressed)
                    throw new InvalidDataException($"Unrecognized BSA header: expected 0x100, got 0x{version:X8}");

                // Validate archive size
                if (fileCount * 21 > fileSize - 12 || dirSize + 8 * fileCount > fileSize - 12)
                    throw new InvalidDataException("Directory information larger than entire archive");

                // Read file size and offset pairs (8 bytes per file)
                uint[] offsets = new uint[3 * fileCount];
                for (int i = 0; i < offsets.Length; i++)
                {
                    offsets[i] = reader.ReadUInt32();
                }

                // Read string table (name buffer)
                int stringBufferSize = (int)(dirSize - 12 * fileCount);
                byte[] stringBuffer = reader.ReadBytes(stringBufferSize);

                // Verify position
                long expectedPos = 12 + dirSize;
                if (_fileStream.Position != expectedPos)
                    throw new InvalidDataException($"Position mismatch after reading directory: expected {expectedPos}, got {_fileStream.Position}");

                // Read hash table (8 bytes per file)
                BSAHash[] hashes = new BSAHash[fileCount];
                for (int i = 0; i < fileCount; i++)
                {
                    uint low = reader.ReadUInt32();
                    uint high = reader.ReadUInt32();
                    hashes[i] = new BSAHash(low, high);
                }

                // Calculate data buffer offset
                long fileDataOffset = 12 + dirSize + 8 * fileCount;

                // Build file list
                _files.Capacity = (int)fileCount;
                for (uint i = 0; i < fileCount; i++)
                {
                    uint entryFileSize = offsets[i * 2];
                    uint relativeOffset = offsets[i * 2 + 1];
                    long offset = relativeOffset + fileDataOffset;

                    // Validate file size and offset
                    if (entryFileSize == 0)
                    {
                        //UnityEngine.Debug.LogWarning($"BSA: File {i} has zero size");
                    }
                    
                    if (entryFileSize + offset > fileSize)
                        throw new InvalidDataException($"Archive contains offsets outside itself: file {i}, size {entryFileSize} + offset {offset} > fileSize {fileSize}");

                    uint nameOffset = offsets[2 * fileCount + i];

                    if (nameOffset >= stringBufferSize)
                        throw new InvalidDataException("Archive contains name offset outside string buffer");

                    // Extract null-terminated string
                    int nameLength = 0;
                    for (int j = (int)nameOffset; j < stringBufferSize; j++)
                    {
                        if (stringBuffer[j] == 0)
                        {
                            nameLength = j - (int)nameOffset;
                            break;
                        }
                    }

                    if (nameLength == 0 && nameOffset < stringBufferSize - 1)
                        throw new InvalidDataException("Archive contains non-zero terminated string");

                    string fileName = Encoding.UTF8.GetString(stringBuffer, (int)nameOffset, nameLength);

                    BSAFileEntry entry = new BSAFileEntry
                    {
                        FileSize = entryFileSize,
                        Offset = (uint)offset,
                        Hash = hashes[i],
                        Name = fileName
                    };

                    _files.Add(entry);
                    _fileLookup[fileName] = entry;
                }

                // Sort files by offset for efficient access
                _files = _files.OrderBy(f => f.Offset).ToList();
            }
        }

        /// <summary>
        /// Gets a file entry by name (case-insensitive)
        /// </summary>
        public BSAFileEntry GetFileEntry(string fileName)
        {
            if (!_isLoaded)
                throw new InvalidOperationException("BSA archive is not loaded");

            // Normalize path separators
            fileName = fileName.Replace('/', '\\');

            if (_fileLookup.TryGetValue(fileName, out BSAFileEntry entry))
                return entry;

            return null;
        }

        /// <summary>
        /// Extracts a file from the BSA archive by name
        /// </summary>
        public byte[] ExtractFile(string fileName)
        {
            BSAFileEntry entry = GetFileEntry(fileName);
            if (entry == null)
                throw new FileNotFoundException($"File not found in BSA: {fileName}");

            return ExtractFile(entry);
        }

        /// <summary>
        /// Extracts a file from the BSA archive using a file entry
        /// </summary>
        public byte[] ExtractFile(BSAFileEntry entry)
        {
            if (!_isLoaded)
                throw new InvalidOperationException("BSA archive is not loaded");

            if (_fileStream == null)
                throw new InvalidOperationException("File stream is not open");

            // Validate entry
            if (entry.FileSize == 0)
                throw new InvalidDataException($"File entry has zero size: {entry.Name}");

            if (entry.Offset >= _fileStream.Length)
                throw new InvalidDataException($"File offset beyond file size: {entry.Name} at offset {entry.Offset}, file size {_fileStream.Length}");

            if (entry.Offset + entry.FileSize > _fileStream.Length)
                throw new InvalidDataException($"File extends beyond archive: {entry.Name} at offset {entry.Offset}, size {entry.FileSize}, archive size {_fileStream.Length}");

            byte[] data = new byte[entry.FileSize];
            _fileStream.Seek(entry.Offset, SeekOrigin.Begin);
            
            // Read in chunks to ensure we get all data
            int totalBytesRead = 0;
            int remaining = (int)entry.FileSize;
            while (remaining > 0)
            {
                int bytesRead = _fileStream.Read(data, totalBytesRead, remaining);
                if (bytesRead == 0)
                    break; // End of stream
                totalBytesRead += bytesRead;
                remaining -= bytesRead;
            }

            if (totalBytesRead != entry.FileSize)
                throw new IOException($"Failed to read complete file '{entry.Name}': expected {entry.FileSize} bytes, got {totalBytesRead}");

            return data;
        }

        /// <summary>
        /// Gets a stream for reading a file from the BSA archive
        /// </summary>
        public Stream GetFileStream(string fileName)
        {
            BSAFileEntry entry = GetFileEntry(fileName);
            if (entry == null)
                throw new FileNotFoundException($"File not found in BSA: {fileName}");

            return GetFileStream(entry);
        }

        /// <summary>
        /// Gets a stream for reading a file from the BSA archive using a file entry
        /// </summary>
        public Stream GetFileStream(BSAFileEntry entry)
        {
            if (!_isLoaded)
                throw new InvalidOperationException("BSA archive is not loaded");

            if (_fileStream == null)
                throw new InvalidOperationException("File stream is not open");

            return new BSAFileStream(_fileStream, entry.Offset, entry.FileSize);
        }

        /// <summary>
        /// Checks if a file exists in the archive
        /// </summary>
        public bool FileExists(string fileName)
        {
            if (!_isLoaded)
                return false;

            fileName = fileName.Replace('/', '\\');
            return _fileLookup.ContainsKey(fileName);
        }

        /// <summary>
        /// Gets all file names in the archive
        /// </summary>
        public IEnumerable<string> GetFileNames()
        {
            return _files.Select(f => f.Name);
        }

        /// <summary>
        /// Generates a BSA hash for a filename (used for file lookup in some BSA formats)
        /// Based on OpenMW's getHash function
        /// </summary>
        public static BSAHash GenerateHash(string name)
        {
            if (string.IsNullOrEmpty(name))
                return new BSAHash(0, 0);

            int l = name.Length >> 1;
            uint sum, off, temp, n;

            // Calculate low hash
            sum = off = 0;
            for (int i = 0; i < l; i++)
            {
                sum ^= ((uint)name[i]) << (int)(off & 0x1F);
                off += 8;
            }
            uint low = sum;

            // Calculate high hash
            sum = off = 0;
            for (int i = l; i < name.Length; i++)
            {
                temp = ((uint)name[i]) << (int)(off & 0x1F);
                sum ^= temp;
                n = temp & 0x1F;
                sum = (sum << (int)(32 - n)) | (sum >> (int)n); // binary "rotate right"
                off += 8;
            }
            uint high = sum;

            return new BSAHash(low, high);
        }

        public void Dispose()
        {
            Close();
        }
    }

    /// <summary>
    /// Stream wrapper for reading a specific file from a BSA archive
    /// </summary>
    internal class BSAFileStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly long _startOffset;
        private readonly long _length;
        private long _position;

        public BSAFileStream(Stream baseStream, long startOffset, long length)
        {
            _baseStream = baseStream;
            _startOffset = startOffset;
            _length = length;
            _position = 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0 || value > _length)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _position = value;
            }
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0)
                throw new ArgumentOutOfRangeException();
            if (offset + count > buffer.Length)
                throw new ArgumentException();

            long remaining = _length - _position;
            if (remaining <= 0)
                return 0;

            if (count > remaining)
                count = (int)remaining;

            long oldPos = _baseStream.Position;
            _baseStream.Position = _startOffset + _position;
            int bytesRead = _baseStream.Read(buffer, offset, count);
            _position += bytesRead;
            return bytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPos;
            switch (origin)
            {
                case SeekOrigin.Begin:
                    newPos = offset;
                    break;
                case SeekOrigin.Current:
                    newPos = _position + offset;
                    break;
                case SeekOrigin.End:
                    newPos = _length + offset;
                    break;
                default:
                    throw new ArgumentException();
            }

            if (newPos < 0 || newPos > _length)
                throw new ArgumentOutOfRangeException();

            _position = newPos;
            return _position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
