/*
 * DAWN OF LIGHT - The first free open source DAoC server emulator
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
 *
 */

namespace Niflib
{
	#if OpenTK
	using OpenTK;
	#elif SharpDX
	using SharpDX;
	#elif MonoGame
	using Microsoft.Xna.Framework;
	#else
	using UnityEngine;
	using Vector3 = UnityEngine.Vector3;
	#endif
	using System;
	using System.IO;

    /// <summary>
    /// Class Morph.
    /// </summary>
    public class Morph
	{
        /// <summary>
        /// The frame name
        /// </summary>
        public NiString FrameName;

        /// <summary>
        /// The number keys
        /// </summary>
        public uint NumKeys;

        /// <summary>
        /// The interpolation
        /// </summary>
        public uint Interpolation;

        /// <summary>
        /// The keys
        /// </summary>
        public KeyGroup<FloatKey> Keys;

        /// <summary>
        /// The unkown int
        /// </summary>
        public uint UnkownInt;

        /// <summary>
        /// The vectors
        /// </summary>
        public Vector3[] Vectors;

        /// <summary>
        /// Initializes a new instance of the <see cref="Morph"/> class.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="reader">The reader.</param>
        /// <param name="numVertices">The number vertices.</param>
		public Morph(NiFile file, BinaryReader reader, uint numVertices)
		{
			if (file.Version >= eNifVersion.VER_10_1_0_106)
			{
				this.FrameName = new NiString(file, reader);
			}
			if (file.Version <= eNifVersion.VER_10_1_0_0)
			{
				// For Morrowind and older versions, read numKeys and interpolation first
				// KeyGroup expects to read the count first, which matches the format
				// But we need to add a sanity check to prevent OverflowException
				long positionBeforeKeys = reader.BaseStream.Position;
				uint numKeys = reader.ReadUInt32();
				
				// Sanity check: numKeys should be reasonable (not millions)
				// If it's huge, we probably read from the wrong position
				if (numKeys > 10000u)
				{
					// This is probably garbage data - log and try to recover
					UnityEngine.Debug.LogError($"niflib.net: Suspicious numKeys value in Morph at position {positionBeforeKeys}: {numKeys} (0x{numKeys:X8}). This suggests file misalignment. Value looks like: '{System.Text.Encoding.ASCII.GetString(BitConverter.GetBytes(numKeys))}'");
					
					// Seek back and skip this morph's keys
					reader.BaseStream.Position = positionBeforeKeys;
					this.Keys = null;
					this.NumKeys = 0;
					this.Interpolation = 0;
				}
				else
				{
					// Seek back to read with KeyGroup
					reader.BaseStream.Position = positionBeforeKeys;
					this.Keys = new KeyGroup<FloatKey>(reader);
					this.NumKeys = (uint)this.Keys.Values.Length;
					this.Interpolation = (uint)this.Keys.Interpolation;
				}
			}
			if (file.Version >= eNifVersion.VER_10_1_0_106 && file.Version <= eNifVersion.VER_10_2_0_0)
			{
				this.UnkownInt = reader.ReadUInt32();
			}
			if (file.Version >= eNifVersion.VER_20_0_0_4 && file.Version <= eNifVersion.VER_20_1_0_3)
			{
				this.UnkownInt = reader.ReadUInt32();
			}
			this.Vectors = new Vector3[numVertices];
			int num = 0;
			while ((long)num < (long)((ulong)numVertices))
			{
				this.Vectors[num] = reader.ReadVector3();
				num++;
			}
		}
	}
}
