﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HuaweiUpdateLibrary.Streams;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;

namespace HuaweiUpdateLibrary.Core
{
    public class UpdateFile : IEnumerable<UpdateEntry>
    {
        public const int CrcBlockSize = 32768; 

        private enum Mode
        {
            Open,
            Create
        }
        
        private const long SkipBytes = 92;
        private readonly string _fileName;

        public override string ToString()
        {
            return _fileName;
        }

        private UpdateFile(string fileName, Mode mode, bool checksum = true)
        {
            // Store filename
            _fileName = fileName;

            switch (mode)
            {
                case Mode.Open:
                {
                    // Load entries
                    LoadEntries(checksum);
                    break;
                }

                case Mode.Create:
                {
                    // Create file
                    CreateFile();
                    break;
                }
            }
        }

        private List<UpdateEntry> _entries;

        private List<UpdateEntry> Entries
        {
            get { return _entries ?? (_entries = new List<UpdateEntry>()); }
        }

        /// <summary>
        /// Access <see cref="UpdateEntry"/> on index
        /// </summary>
        /// <param name="index">Index</param>
        /// <returns><see cref="UpdateEntry"/></returns>
        public UpdateEntry this[int index]
        {
            get { return Entries[index]; }
        }

        /// <summary>
        /// Returns number of <see cref="UpdateEntry"/>
        /// </summary>
        public int Count
        {
            get { return Entries.Count; }
        }

        private void LoadEntries(bool checksum)
        {
            using (var stream = new FileStream(_fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                // Skip first 92 bytes
                stream.Seek(SkipBytes, SeekOrigin.Begin);

                // Read file
                while (stream.Position < stream.Length)
                {
                    // Read entry
                    var entry = UpdateEntry.Open(stream, checksum);

                    // Add to list
                    Entries.Add(entry);

                    // Skip file data
                    stream.Seek(entry.FileSize, SeekOrigin.Current);

                    // Read remainder
                    var remainder = Utilities.UintSize - (int)(stream.Position % Utilities.UintSize);
                    if (remainder < Utilities.UintSize)
                        stream.Seek(remainder, SeekOrigin.Current);
                }
            }
        }

        private void CreateFile()
        {
            using (var stream = new FileStream(_fileName, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[SkipBytes];

                // Write SkipBytes
                stream.Write(buffer, 0, buffer.Length);
            }
        }

        /// <summary>
        /// Open an existing update file
        /// </summary>
        /// <param name="fileName">Filename</param>
        /// <param name="checksum">Verify header checksum</param>
        /// <returns><see cref="UpdateFile"/></returns>
        public static UpdateFile Open(string fileName, bool checksum = true)
        {
            return new UpdateFile(fileName, Mode.Open, checksum);
        }

        /// <summary>
        /// Create an <see cref="UpdateFile"/>
        /// </summary>
        /// <param name="fileName">Filename</param>
        /// <returns><see cref="UpdateFile"/></returns>
        public static UpdateFile Create(string fileName)
        {
            return new UpdateFile(fileName, Mode.Create);
        }

        /// <summary>
        /// Extract <see cref="UpdateEntry"/>
        /// </summary>
        /// <param name="index"><see cref="UpdateEntry"/> index</param>
        /// <param name="output">Output file</param>
        /// <param name="checksum">Verify checksum</param>
        public void Extract(int index, string output, bool checksum = true)
        {
            // Extract entry
            Extract(Entries[index], output, checksum);
        }

        /// <summary>
        /// Extract <see cref="UpdateEntry"/>
        /// </summary>
        /// <param name="entry"><see cref="UpdateEntry"/></param>
        /// <param name="output">Output file</param>
        /// <param name="checksum">Verify checksum</param>
        public void Extract(UpdateEntry entry, string output, bool checksum = true)
        {
            // Extract entry
            entry.Extract(_fileName, output, checksum);
        }

        /// <summary>
        /// Extract <see cref="UpdateEntry"/>
        /// </summary>
        /// <param name="index"><see cref="UpdateEntry"/> index</param>
        /// <param name="output">Output file</param>
        /// <param name="checksum">Verify checksum</param>
        public void Extract(int index, Stream output, bool checksum = true)
        {
            // Extract entry
            Extract(Entries[index], output, checksum);
        }

        /// <summary>
        /// Extract <see cref="UpdateEntry"/>
        /// </summary>
        /// <param name="entry"><see cref="UpdateEntry"/></param>
        /// <param name="output">Output file</param>
        /// <param name="checksum">Verify checksum</param>
        public void Extract(UpdateEntry entry, Stream output, bool checksum = true)
        {
            // Extract entry
            entry.Extract(_fileName, output, checksum);
        }

        /// <summary>
        /// Add <see cref="UpdateEntry"/>
        /// </summary>
        /// <param name="entry"><see cref="UpdateEntry"/></param>
        /// <param name="stream"><see cref="Stream"/> to input data</param>
        public void Add(UpdateEntry entry, Stream stream)
        {
            // Set size
            entry.FileSize = (uint) stream.Length;
            
            // Calculate checksum table size
            var checksumTableSize = entry.FileSize / entry.BlockSize;
            if (entry.FileSize % entry.BlockSize != 0) 
                checksumTableSize++;

            // Allocate checksum table
            entry.CheckSumTable = new ushort[checksumTableSize];

            // Set headersize
            entry.HeaderSize = (uint) (FileHeader.Size + (checksumTableSize*Utilities.UshortSize));

            // Compute header checksum
            entry.ComputeHeaderChecksum();

            using (var output = new FileStream(_fileName, FileMode.Append, FileAccess.Write, FileShare.None))
            {
                // Get header
                var header = entry.GetHeader();

                // Write header
                output.Write(header, 0, header.Length);

                // Skip checksum table
                output.Seek(checksumTableSize * Utilities.UshortSize, SeekOrigin.Current);

                // Set offset
                entry.DataOffset = output.Position;

                // Read data
                var buffer = new byte[entry.BlockSize];
                var blockNumber = 0;
                int size;

                // Calculate checksum
                while ((size = stream.Read(buffer, 0, entry.BlockSize)) > 0)
                {
                    // Calculate checksum
                    entry.CheckSumTable[blockNumber] = Utilities.Crc.ComputeSum(buffer, 0, size);

                    // Write data
                    output.Write(buffer, 0, size);

                    // Increase blocknumber
                    blockNumber++;
                }

                // Jump back 
                output.Seek(-(stream.Length + (checksumTableSize * Utilities.UshortSize)), SeekOrigin.Current);

                // Write checksum table
                var writer = new BinaryWriter(output);

                // Write
                for (var count = 0; count < entry.CheckSumTable.Length; count++) writer.Write(entry.CheckSumTable[count]);

                // Jump further
                output.Seek(stream.Length, SeekOrigin.Current);

                // Write remainder
                var remainder = Utilities.UintSize - (int)(writer.BaseStream.Position % Utilities.UintSize);
                if (remainder < Utilities.UintSize)
                {
                    // Write remainder bytes
                    writer.Write(new byte[remainder]);
                }
            }

            // Add entry
            Entries.Add(entry);
        }

        /// <summary>
        /// Add <see cref="UpdateEntry"/>
        /// </summary>
        /// <param name="entry"><see cref="UpdateEntry"/></param>
        /// <param name="fileName">File to add</param>
        public void Add(UpdateEntry entry, string fileName)
        {
            using (var input = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Add(entry, input);
            }
        }

        /// <summary>
        /// Add checkum <see cref="UpdateEntry"/> (CRC)
        /// </summary>
        /// <param name="entry"><see cref="UpdateEntry"/></param>
        /// <param name="blockSize">Block size</param>
        public void AddChecksum(UpdateEntry entry, int blockSize = CrcBlockSize)
        {
            // TODO: Remove already existing ?

            // Set entry type
            entry.Type = EntryType.Checksum;

            // Result checksum list
            var result = new List<ushort>();

            // Allocate buffer
            var buffer = new byte[CrcBlockSize];

            using (var stream = new FileStream(_fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                // Skip checksum and Signature
                foreach (var item in Entries.Where(e => e.Type != EntryType.Checksum && e.Type != EntryType.Checksum))
                {
                    // Seek to filedata
                    stream.Seek(item.DataOffset, SeekOrigin.Begin);

                    var partial = new PartialStream(stream, item.FileSize);
                    int size;

                    // Process data
                    while ((size = partial.Read(buffer, 0, CrcBlockSize)) > 0)
                    {
                        // Compute crc
                        result.Add(Utilities.Crc.ComputeSum(buffer, 0, size));
                    }
                }
            }

            // Add entry
            using (var stream = new MemoryStream(result.SelectMany(BitConverter.GetBytes).ToArray()))
            {
                Add(entry, stream);
            }
        }

        /// <summary>
        /// Add signature <see cref="UpdateEntry"/> (MD5RSA)
        /// </summary>
        /// <param name="entry"><see cref="UpdateEntry"/></param>
        /// <param name="algorithm">Algorithm to use</param>
        /// <param name="keyfile">Key file</param>
        public void AddSignature(UpdateEntry entry, string algorithm, string keyfile)
        {
            // TODO: Remove already existing ?

            // Set entry type
            entry.Type = EntryType.Signature;

            // Get signer
            var signer = SignerUtilities.GetSigner(algorithm);

            // Load key
            using (var reader = new StreamReader(keyfile))
            {
                var pemReader = new PemReader(reader);
                var key = (AsymmetricCipherKeyPair)pemReader.ReadObject();
                signer.Init(true, key.Private);
            }

            // Allocate buffer
            var buffer = new byte[CrcBlockSize];

            using (var stream = new FileStream(_fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                // Skip checksum and Signature
                foreach (var item in Entries.Where(e => e.Type != EntryType.Checksum && e.Type != EntryType.Checksum))
                {
                    // Seek to filedata
                    stream.Seek(item.DataOffset, SeekOrigin.Begin);

                    var partial = new PartialStream(stream, item.FileSize);
                    int size;

                    // Process data
                    while ((size = partial.Read(buffer, 0, CrcBlockSize)) > 0)
                    {
                        signer.BlockUpdate(buffer, 0, size);
                    }
                }
            }

            // Add entry
            using (var stream = new MemoryStream(signer.GenerateSignature()))
            {
                Add(entry, stream);
            }
        }

        /// <summary>
        /// Remove <see cref="UpdateEntry"/> from <see cref="UpdateFile"/>
        /// </summary>
        /// <param name="entry"><see cref="UpdateEntry"/></param>
        public void Remove(UpdateEntry entry)
        {
            var size = entry.HeaderSize + entry.FileSize;
            var offset = entry.DataOffset - entry.HeaderSize;



            
            // Remove entry
            Entries.Remove(entry);
        }

        /// <summary>
        /// Remove <see cref="UpdateEntry"/> at index
        /// </summary>
        /// <param name="index"><see cref="UpdateEntry"/> index</param>
        public void Remove(int index)
        {
            Remove(Entries[index]);
        }

        /// <summary>
        /// Returns enumerator
        /// </summary>
        /// <returns><see cref="IEnumerator"/></returns>
        public IEnumerator<UpdateEntry> GetEnumerator()
        {
            return Entries.GetEnumerator();
        }

        /// <summary>
        /// Returns enumerator
        /// </summary>
        /// <returns><see cref="IEnumerator"/></returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
