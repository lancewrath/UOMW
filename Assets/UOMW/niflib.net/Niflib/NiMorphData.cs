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
	using System;
	using System.IO;

    /// <summary>
    /// Class NiMorphData.
    /// </summary>
    public class NiMorphData : NiObject
	{
        /// <summary>
        /// The number morphs
        /// </summary>
        public uint NumMorphs;

        /// <summary>
        /// The number vertices
        /// </summary>
        public uint NumVertices;

        /// <summary>
        /// The relative targets
        /// </summary>
        public byte RelativeTargets;

        /// <summary>
        /// The morphs
        /// </summary>
        public Morph[] Morphs;

        /// <summary>
        /// Initializes a new instance of the <see cref="NiMorphData" /> class.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="reader">The reader.</param>
		public NiMorphData(NiFile file, BinaryReader reader) : base(file, reader)
		{
			long positionBeforeRead = reader.BaseStream.Position;
			this.NumMorphs = reader.ReadUInt32();
			this.NumVertices = reader.ReadUInt32();
			this.RelativeTargets = reader.ReadByte();
			
			// Sanity check: NumMorphs and NumVertices should be reasonable
			if (this.NumMorphs > 1000u)
			{
				UnityEngine.Debug.LogError($"niflib.net: Suspicious NumMorphs value in NiMorphData at position {positionBeforeRead}: {this.NumMorphs} (0x{this.NumMorphs:X8}). This suggests file misalignment.");
			}
			if (this.NumVertices > 100000u)
			{
				UnityEngine.Debug.LogError($"niflib.net: Suspicious NumVertices value in NiMorphData at position {positionBeforeRead + 4}: {this.NumVertices} (0x{this.NumVertices:X8}). This suggests file misalignment.");
			}
			
			// UnityEngine.Debug.Log($"niflib.net: NiMorphData: NumMorphs={this.NumMorphs}, NumVertices={this.NumVertices}, RelativeTargets={this.RelativeTargets}, position={positionBeforeRead}"); // Commented out for performance
			
			this.Morphs = new Morph[this.NumMorphs];
			int num = 0;
			while ((long)num < (long)((ulong)this.NumMorphs))
			{
				long positionBeforeMorph = reader.BaseStream.Position;
				this.Morphs[num] = new Morph(file, reader, this.NumVertices);
				long positionAfterMorph = reader.BaseStream.Position;
				// UnityEngine.Debug.Log($"niflib.net: NiMorphData: Read morph {num + 1} of {this.NumMorphs} from position {positionBeforeMorph} to {positionAfterMorph} (size: {positionAfterMorph - positionBeforeMorph} bytes)"); // Commented out for performance
				num++;
			}
		}
	}
}
