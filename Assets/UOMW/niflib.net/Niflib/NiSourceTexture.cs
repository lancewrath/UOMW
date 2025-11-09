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
    /// Class NiSourceTexture.
    /// </summary>
    public class NiSourceTexture : NiTexture
	{
        /// <summary>
        /// The use external
        /// </summary>
        public bool UseExternal;

        /// <summary>
        /// The file name
        /// </summary>
        public NiString FileName;

        /// <summary>
        /// The pixel layout
        /// </summary>
        public ePixelLayout PixelLayout;

        /// <summary>
        /// The use mipmaps
        /// </summary>
        public eMipMapFormat UseMipmaps;

        /// <summary>
        /// The alpha format
        /// </summary>
        public eAlphaFormat AlphaFormat;

        /// <summary>
        /// The is static
        /// </summary>
        public bool IsStatic;

        /// <summary>
        /// The direct render
        /// </summary>
        public bool DirectRender;

        /// <summary>
        /// The persistent render data
        /// </summary>
        public bool PersistentRenderData;

        /// <summary>
        /// The internal texture
        /// </summary>
        public NiRef<ATextureRenderData> InternalTexture;

        /// <summary>
        /// Initializes a new instance of the <see cref="NiSourceTexture" /> class.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="reader">The reader.</param>
        public NiSourceTexture(NiFile file, BinaryReader reader) : base(file, reader)
		{
			this.IsStatic = true;
			
			// useExternal is a byte, not a bool, so always read as byte
			// In C++: byte useExternal; NifStream( useExternal, in, info ); reads a single byte
			long positionBeforeUseExternal = reader.BaseStream.Position;
			byte useExternalByte = reader.ReadByte();
			this.UseExternal = (useExternalByte != 0);
			
			if (this.UseExternal)
			{
				long positionBeforeFileName = reader.BaseStream.Position;
				try
				{
					this.FileName = new NiString(file, reader);
				}
				catch (System.Exception ex)
				{
					// For version 4.0.0.2, if string reading fails, try to skip it and continue
					// This is a workaround for misaligned files
					if (base.Version <= eNifVersion.VER_4_0_0_2)
					{
						// Reset to before the failed string read attempt
						reader.BaseStream.Position = positionBeforeFileName;
						
						// Skip 4 bytes (the uint32 we tried to read as string length)
						// This is a simple workaround - the file is already misaligned, so we just skip forward
						reader.BaseStream.Position += 4;
						
						// Create an empty filename as fallback
						this.FileName = new NiString();
						this.FileName.Value = ""; // Empty string as fallback
						UnityEngine.Debug.LogWarning($"NiSourceTexture: Failed to read FileName at position {positionBeforeFileName}, skipped 4 bytes and using empty string. " +
						                              $"UseExternal={this.UseExternal} (byte={useExternalByte}), Version={base.Version}. " +
						                              $"Error: {ex.Message}. File may be misaligned. New position: {reader.BaseStream.Position}");
					}
					else
					{
						// For newer versions, throw the error
						string errorMsg = $"Failed to read FileName in NiSourceTexture at position {positionBeforeFileName}. " +
						                 $"UseExternal={this.UseExternal} (byte={useExternalByte}), Version={base.Version}. " +
						                 $"Error: {ex.Message}";
						throw new System.Exception(errorMsg, ex);
					}
				}
				if (base.Version >= eNifVersion.VER_10_1_0_0)
				{
					reader.ReadUInt32();
				}
			}
			if (!this.UseExternal)
			{
				if (base.Version <= eNifVersion.VER_10_0_1_0)
				{
					reader.ReadByte();
				}
				if (base.Version >= eNifVersion.VER_10_1_0_0)
				{
					this.FileName = new NiString(file, reader);
				}
				this.InternalTexture = new NiRef<ATextureRenderData>(reader);
			}
			this.PixelLayout = (ePixelLayout)reader.ReadUInt32();
			this.UseMipmaps = (eMipMapFormat)reader.ReadUInt32();
			this.AlphaFormat = (eAlphaFormat)reader.ReadUInt32();
			// isStatic is a byte, not a bool
			byte isStaticByte = reader.ReadByte();
			this.IsStatic = (isStaticByte != 0);
			if (base.Version >= eNifVersion.VER_10_1_0_106)
			{
				this.DirectRender = reader.ReadBoolean(Version);
			}
			if (base.Version >= eNifVersion.VER_20_2_0_7)
			{
				this.PersistentRenderData = reader.ReadBoolean(Version);
			}
		}
	}
}
