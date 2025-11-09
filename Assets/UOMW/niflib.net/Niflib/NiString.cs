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
	using System.Collections.Generic;
	using System.IO;

    /// <summary>
    /// Class NiString.
    /// </summary>
    public class NiString
	{
        /// <summary>
        /// The value
        /// </summary>
        public string Value;
        
        /// <summary>
        /// Default constructor for creating empty NiString
        /// </summary>
        public NiString()
        {
            this.Value = "";
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NiString"/> class.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="reader">The reader.</param>
        public NiString(NiFile file, BinaryReader reader)
		{
        	var count = reader.ReadUInt32();
        	
        	// For older NIF versions (4.0.0.2 and earlier), if the count is unreasonably large,
        	// it might indicate misalignment. Try to recover by reading as null-terminated string.
        	if (count > 16384)
        	{
        		// Check if this is an older version that might have different string format
        		if (file.Version <= eNifVersion.VER_4_0_0_2)
        		{
        			// Try to read as null-terminated string instead
        			// First, we need to backtrack 4 bytes (the uint32 we just read)
        			reader.BaseStream.Position -= 4;
        			
        			// Read characters until we hit a null terminator
        			// Limit to 256 chars for safety and to avoid infinite loops
        			List<char> chars = new List<char>();
        			try
        			{
        				char c;
        				while (chars.Count < 256)
        				{
        					c = reader.ReadChar();
        					if (c == '\0')
        						break;
        					// Check for reasonable ASCII characters (printable or common control chars)
        					if (c >= 32 || c == '\t' || c == '\n' || c == '\r')
        					{
        						chars.Add(c);
        					}
        					else
        					{
        						// Non-printable character encountered, might not be a string
        						// Backtrack and throw error
        						reader.BaseStream.Position -= 1;
        						break;
        					}
        				}
        				
        				if (chars.Count > 0 && chars.Count < 256)
        				{
        					this.Value = new string(chars.ToArray());
        					return;
        				}
        			}
        			catch
        			{
        				// If reading fails, fall through to throw exception
        			}
        			
        			// Recovery failed, throw exception with context
        			reader.BaseStream.Position -= 4; // Reset to before the count
        			throw new NotSupportedException($"String too long (count={count}) and null-terminated recovery failed. File may be misaligned at position {reader.BaseStream.Position}.");
        		}
        		throw new NotSupportedException($"String too long (count={count}). Not a NIF file or unsupported format?");
        	}
        	this.Value = new string(reader.ReadChars((int)count));
		}

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>A <see cref="System.String" /> that represents this instance.</returns>
        public override string ToString()
		{
			return this.Value;
		}
	}
}
