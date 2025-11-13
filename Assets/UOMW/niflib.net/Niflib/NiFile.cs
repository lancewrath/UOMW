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
	#if !UNITY_EDITOR && !UNITY_STANDALONE && !UNITY_IOS && !UNITY_ANDROID && !UNITY_WEBGL
	using Microsoft.CSharp.RuntimeBinder;
	#endif
	using System;
	using System.Collections;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Linq.Expressions;
	using System.Reflection;
	using System.Runtime.CompilerServices;

    /// <summary>
    /// Class NiFile.
    /// </summary>
    public class NiFile
	{
        /// <summary>
        /// Class o__11.
        /// </summary>
        [CompilerGenerated]
		private static class o__11
		{
            /// <summary>
            /// The P__0
            /// </summary>
            public static CallSite<Action<CallSite, object, NiFile>> p__0;

            /// <summary>
            /// The P__1
            /// </summary>
            public static CallSite<Func<CallSite, object, object>> p__1;

            /// <summary>
            /// The P__2
            /// </summary>
            public static CallSite<Func<CallSite, object, bool>> p__2;

            /// <summary>
            /// The P__3
            /// </summary>
            public static CallSite<Func<CallSite, object, object>> p__3;

            /// <summary>
            /// The P__4
            /// </summary>
            public static CallSite<Func<CallSite, object, object, object>> p__4;

            /// <summary>
            /// The P__5
            /// </summary>
            public static CallSite<Func<CallSite, object, bool>> p__5;

            /// <summary>
            /// The P__6
            /// </summary>
            public static CallSite<Action<CallSite, object, NiFile>> p__6;
		}

        /// <summary>
        /// The invali d_ reference
        /// </summary>
        public const uint INVALID_REF = 4294967295u;

        /// <summary>
        /// The cm d_ to p_ leve l_ object
        /// </summary>
        public const string CMD_TOP_LEVEL_OBJECT = "Top Level Object";

        /// <summary>
        /// The cm d_ en d_ o f_ file
        /// </summary>
        public const string CMD_END_OF_FILE = "End Of File";

        /// <summary>
        /// The header
        /// </summary>
        public NiHeader Header;

        /// <summary>
        /// The footer
        /// </summary>
        public NiFooter Footer;

        /// <summary>
        /// The objects by reference
        /// </summary>
        public Dictionary<uint, NiObject> ObjectsByRef;

        /// <summary>
        /// Gets the version.
        /// </summary>
        /// <value>The version.</value>
        public eNifVersion Version
		{
			get
			{
				return this.Header.Version;
			}
		}

        /// <summary>
        /// Initializes a new instance of the <see cref="NiFile"/> class.
        /// </summary>
        /// <param name="reader">The reader.</param>
        public NiFile(BinaryReader reader)
		{
			this.Header = new NiHeader(this, reader);
			long positionBeforeObjects = reader.BaseStream.Position;
			this.ReadNiObjects(reader);
			long positionAfterObjects = reader.BaseStream.Position;
			//UnityEngine.Debug.Log($"niflib.net: Read {this.ObjectsByRef.Count} objects. Header.NumBlocks = {this.Header.NumBlocks}. Position before objects: {positionBeforeObjects}, after: {positionAfterObjects}");
			this.Footer = new NiFooter(this, reader);
			this.FixRefs();
		}

        /// <summary>
        /// Reads the ni objects.
        /// </summary>
        /// <param name="reader">The reader.</param>
        /// <exception cref="Exception">
        /// Check value is not zero! Invalid file?
        /// or
        /// Invalid object type string length!
        /// </exception>
        /// <exception cref="NotImplementedException"></exception>
        private void ReadNiObjects(BinaryReader reader)
		{
			this.ObjectsByRef = new Dictionary<uint, NiObject>();
			int num = 0;
			string text;
			while (true)
			{
				// For versions >= 0x0303000D (like Morrowind 4.0.0.2), check if we've read all blocks BEFORE trying to read another
				// This prevents reading past the end of the objects list
				if (this.Version >= eNifVersion.VER_3_3_0_13 && this.Version < eNifVersion.VER_5_0_0_1)
				{
					if ((long)num >= (long)((ulong)this.Header.NumBlocks))
					{
						// We've read all blocks, stop reading
						//UnityEngine.Debug.Log($"niflib.net: Read all {this.Header.NumBlocks} objects. Stopping read at position {reader.BaseStream.Position}.");
						return;
					}
					// Debug: Log when we're about to read objects near the end
					//if ((long)num >= (long)((ulong)this.Header.NumBlocks) - 2)
					//{
					//	UnityEngine.Debug.Log($"niflib.net: About to read object {num + 1} of {this.Header.NumBlocks} at position {reader.BaseStream.Position}");
					//}
				}
				
				if (this.Version >= eNifVersion.VER_5_0_0_1)
				{
					if (this.Version <= eNifVersion.VER_10_1_0_106 && reader.ReadUInt32() != 0u)
					{
						break;
					}
					text = this.Header.BlockTypes[(int)this.Header.BlockTypeIndex[num]].Value;
				}
				else
				{
					long positionBeforeRead = reader.BaseStream.Position;
					uint num2 = reader.ReadUInt32();
					
					// Check if this looks like ASCII text (common object type names start with 'N' = 0x4E)
					// If the high byte is 0x4D-0x5A (M-Z in ASCII), it's probably not a length but actual data
					// This happens when we've read past the end of the objects list
					byte highByte = (byte)((num2 >> 24) & 0xFF);
					if (highByte >= 0x4D && highByte <= 0x5A) // 'M' to 'Z' in ASCII
					{
						// This looks like ASCII text, not a length - we've probably read all objects
						// Convert the uint32 to ASCII to see what we're reading
						byte[] bytes = BitConverter.GetBytes(num2);
						string asciiText = System.Text.Encoding.ASCII.GetString(bytes);
						UnityEngine.Debug.LogWarning($"niflib.net: Read ASCII-like value at position {positionBeforeRead}: {num2} (0x{num2:X8}) = '{asciiText}'. Expected {this.Header.NumBlocks} objects but read {num}. This suggests we've read past the end. Seeking back and stopping.");
						
						// Seek back 4 bytes since we read past the end
						reader.BaseStream.Position = positionBeforeRead;
						
						// For versions >= 0x0303000D, we should have already checked NumBlocks above
						// But if we get here, it means the NumBlocks count might be wrong or we miscounted
						if (this.Version >= eNifVersion.VER_3_3_0_13)
						{
							return; // Stop reading - we've read all objects (or as many as we can)
						}
					}
					
					// More lenient check: allow 0-1024 characters instead of strict 6-30
					// Some Morrowind NIF files have object type names outside the expected range
					// This matches OpenMW's more lenient approach
					// Also check for obviously invalid values that suggest file misalignment
					if (num2 > 1024u)
					{
						// Log the value we read to help debug
						UnityEngine.Debug.LogError($"niflib.net: Invalid object type string length: {num2} (0x{num2:X8}) at position {positionBeforeRead}. File may be misaligned or corrupted. Expected {this.Header.NumBlocks} objects, read {num}.");
						goto IL_74;
					}
					
					// Allow empty strings (0 length) - some files may have padding or special markers
					if (num2 == 0u)
					{
						// Skip this object and continue - might be padding or alignment
						continue;
					}
					
					// Additional check: if length is very small (< 3), it's probably not a valid object type name
					// But we'll still try to read it to see what we get
					if (num2 < 3u)
					{
						UnityEngine.Debug.LogWarning($"niflib.net: Suspiciously short object type string length: {num2} (at position {positionBeforeRead}). Attempting to read anyway...");
					}
					
					text = new string(reader.ReadChars((int)num2));
					if (this.Header.Version < eNifVersion.VER_3_3_0_13)
					{
						if (text == "Top Level Object")
						{
							continue;
						}
						if (text == "End Of File")
						{
							return;
						}
					}
				}
				uint key;
				if (this.Version < eNifVersion.VER_3_3_0_13)
				{
					key = reader.ReadUInt32();
				}
				else
				{
					key = (uint)num;
				}
				Type expr_E7 = Type.GetType("Niflib." + text);
				if (expr_E7 == null)
				{
					// Log unknown object type
					UnityEngine.Debug.LogWarning($"niflib.net: Unknown object type '{text}' at position {reader.BaseStream.Position - 4 - text.Length}. Expected {this.Header.NumBlocks} objects, read {num} so far. Cannot skip unknown objects safely, so stopping read.");
					
					// For unknown types, we can't continue safely because we don't know the object size
					// But we should still increment num to match the expected count
					// However, this will cause issues with references, so we throw
					goto Block_8;
				}
				
				NiObject value = null;
				long positionBeforeObject = reader.BaseStream.Position;
				try
				{
					//UnityEngine.Debug.Log($"niflib.net: Reading object {num + 1} of {this.Header.NumBlocks}: type='{text}', key={key}, position={positionBeforeObject}");
					value = (NiObject)Activator.CreateInstance(expr_E7, new object[]
					{
						this,
						reader
					});
					long positionAfterObject = reader.BaseStream.Position;
					//UnityEngine.Debug.Log($"niflib.net: Successfully read object {num + 1} of {this.Header.NumBlocks}: '{text}' (key={key}) from position {positionBeforeObject} to {positionAfterObject} (size: {positionAfterObject - positionBeforeObject} bytes)");
				}
				catch (System.Exception ex)
				{
					// Object creation failed - this is a serious error
					long positionAfterError = reader.BaseStream.Position;
					UnityEngine.Debug.LogError($"niflib.net: Failed to create instance of '{text}' (object {num + 1} of {this.Header.NumBlocks}) at position {positionBeforeObject}. Error: {ex.Message}. Position after error: {positionAfterError}. Inner exception: {(ex.InnerException != null ? ex.InnerException.Message : "none")}. Stack trace: {ex.StackTrace}");
					
					// Can't continue safely - the file position is now wrong
					// Re-throw to stop parsing
					throw new System.Exception($"Failed to create object of type '{text}' at position {positionBeforeObject}: {ex.Message}", ex);
				}
				
				this.ObjectsByRef.Add(key, value);
				if (this.Version >= eNifVersion.VER_3_3_0_13)
				{
					num++;
					// Check again after incrementing (for versions < 5.0.0.1, we already checked at the start of the loop)
					// But keep this check for safety
					if (this.Version < eNifVersion.VER_5_0_0_1 && (long)num >= (long)((ulong)this.Header.NumBlocks))
					{
						return;
					}
				}
			}
			throw new Exception("Check value is not zero! Invalid file?");
			IL_74:
			throw new Exception("Invalid object type string length!");
			Block_8:
			throw new NotImplementedException(text);
		}

        /// <summary>
        /// Fixes the refs.
        /// </summary>
        private void FixRefs()
		{
        	// Fix Object Refs
			foreach (NiObject current in this.ObjectsByRef.Values)
			{
				this.FixRefs(current);
			}
			
			// Fix Footer Refs
			foreach (var niRef in Footer.RootNodes)
			{
				niRef.SetRef(this);
			}
		}

        /// <summary>
        /// Fixes the refs.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <exception cref="Exception">no child</exception>
        private void FixRefs(object obj)
		{
			#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID || UNITY_WEBGL
			// Unity-compatible version using reflection
			FieldInfo[] fields = obj.GetType().GetFields();
			for (int i = 0; i < fields.Length; i++)
			{
				FieldInfo fieldInfo = fields[i];
				if (fieldInfo.FieldType.Name.Contains("NiRef"))
				{
					if (fieldInfo.FieldType.IsArray)
					{
						IEnumerable enumerable = (IEnumerable)fieldInfo.GetValue(obj);
						if (enumerable == null)
						{
							continue;
						}
						IEnumerator enumerator = enumerable.GetEnumerator();
						try
						{
							while (enumerator.MoveNext())
							{
								object current = enumerator.Current;
								if (current == null) continue;
								
								// Use reflection to call SetRef
								MethodInfo setRefMethod = current.GetType().GetMethod("SetRef", new Type[] { typeof(NiFile) });
								if (setRefMethod != null)
								{
									setRefMethod.Invoke(current, new object[] { this });
								}
								
								if (fieldInfo.Name == "Children")
								{
									// Use reflection to check IsValid (method) and get Object (property)
									MethodInfo isValidMethod = current.GetType().GetMethod("IsValid");
									PropertyInfo objectProp = current.GetType().GetProperty("Object");
									
									if (isValidMethod != null && objectProp != null)
									{
										bool isValid = (bool)isValidMethod.Invoke(current, null);
										if (isValid)
										{
											object refObj = objectProp.GetValue(current);
											NiAVObject expr_1C2 = refObj as NiAVObject;
											if (expr_1C2 == null)
											{
												throw new Exception("no child");
											}
											expr_1C2.Parent = (NiNode)obj;
										}
									}
								}
							}
						}
						finally
						{
							IDisposable disposable = enumerator as IDisposable;
							if (disposable != null)
							{
								disposable.Dispose();
							}
						}
					}
					else
					{
						object value = fieldInfo.GetValue(obj);
						if (value == null) continue;
						
						// Use reflection to check if value is valid and needs SetRef
						// The original code checks if value != null, then calls SetRef
						// Use reflection to call SetRef
						MethodInfo setRefMethod = value.GetType().GetMethod("SetRef", new Type[] { typeof(NiFile) });
						if (setRefMethod != null)
						{
							setRefMethod.Invoke(value, new object[] { this });
						}
					}
				}
			}
			#else
			// Original RuntimeBinder version for non-Unity platforms
			FieldInfo[] fields = obj.GetType().GetFields();
			for (int i = 0; i < fields.Length; i++)
			{
				FieldInfo fieldInfo = fields[i];
				if (fieldInfo.FieldType.Name.Contains("NiRef"))
				{
					if (fieldInfo.FieldType.IsArray)
					{
						IEnumerable enumerable = (IEnumerable)fieldInfo.GetValue(obj);
						if (enumerable == null)
						{
							goto IL_303;
						}
						IEnumerator enumerator = enumerable.GetEnumerator();
						try
						{
							while (enumerator.MoveNext())
							{
								object current = enumerator.Current;
								if (NiFile.o__11.p__0 == null)
								{
									NiFile.o__11.p__0 = CallSite<Action<CallSite, object, NiFile>>.Create(Microsoft.CSharp.RuntimeBinder.Binder.InvokeMember(CSharpBinderFlags.ResultDiscarded, "SetRef", null, typeof(NiFile), new CSharpArgumentInfo[]
									{
										CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null),
										CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.UseCompileTimeType, null)
									}));
								}
								NiFile.o__11.p__0.Target(NiFile.o__11.p__0, current, this);
								if (fieldInfo.Name == "Children")
								{
									if (NiFile.o__11.p__2 == null)
									{
										NiFile.o__11.p__2 = CallSite<Func<CallSite, object, bool>>.Create(Microsoft.CSharp.RuntimeBinder.Binder.UnaryOperation(CSharpBinderFlags.None, ExpressionType.IsTrue, typeof(NiFile), new CSharpArgumentInfo[]
										{
											CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null)
										}));
									}
									Func<CallSite, object, bool> arg_16A_0 = NiFile.o__11.p__2.Target;
									CallSite arg_16A_1 = NiFile.o__11.p__2;
									if (NiFile.o__11.p__1 == null)
									{
										NiFile.o__11.p__1 = CallSite<Func<CallSite, object, object>>.Create(Microsoft.CSharp.RuntimeBinder.Binder.InvokeMember(CSharpBinderFlags.None, "IsValid", null, typeof(NiFile), new CSharpArgumentInfo[]
										{
											CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null)
										}));
									}
									if (arg_16A_0(arg_16A_1, NiFile.o__11.p__1.Target(NiFile.o__11.p__1, current)))
									{
										if (NiFile.o__11.p__3 == null)
										{
											NiFile.o__11.p__3 = CallSite<Func<CallSite, object, object>>.Create(Microsoft.CSharp.RuntimeBinder.Binder.GetMember(CSharpBinderFlags.None, "Object", typeof(NiFile), new CSharpArgumentInfo[]
											{
												CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null)
											}));
										}
										NiAVObject expr_1C2 = NiFile.o__11.p__3.Target(NiFile.o__11.p__3, current) as NiAVObject;
										if (expr_1C2 == null)
										{
											throw new Exception("no child");
										}
										expr_1C2.Parent = (NiNode)obj;
									}
								}
							}
							goto IL_303;
						}
						finally
						{
							IDisposable disposable = enumerator as IDisposable;
							if (disposable != null)
							{
								disposable.Dispose();
							}
						}
					}
					object value = fieldInfo.GetValue(obj);
					if (NiFile.o__11.p__5 == null)
					{
						NiFile.o__11.p__5 = CallSite<Func<CallSite, object, bool>>.Create(Microsoft.CSharp.RuntimeBinder.Binder.UnaryOperation(CSharpBinderFlags.None, ExpressionType.IsTrue, typeof(NiFile), new CSharpArgumentInfo[]
						{
							CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null)
						}));
					}
					Func<CallSite, object, bool> arg_2A0_0 = NiFile.o__11.p__5.Target;
					CallSite arg_2A0_1 = NiFile.o__11.p__5;
					if (NiFile.o__11.p__4 == null)
					{
						NiFile.o__11.p__4 = CallSite<Func<CallSite, object, object, object>>.Create(Microsoft.CSharp.RuntimeBinder.Binder.BinaryOperation(CSharpBinderFlags.None, ExpressionType.Equal, typeof(NiFile), new CSharpArgumentInfo[]
						{
							CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null),
							CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.Constant, null)
						}));
					}
					if (!arg_2A0_0(arg_2A0_1, NiFile.o__11.p__4.Target(NiFile.o__11.p__4, value, null)))
					{
						if (NiFile.o__11.p__6 == null)
						{
							NiFile.o__11.p__6 = CallSite<Action<CallSite, object, NiFile>>.Create(Microsoft.CSharp.RuntimeBinder.Binder.InvokeMember(CSharpBinderFlags.ResultDiscarded, "SetRef", null, typeof(NiFile), new CSharpArgumentInfo[]
							{
								CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null),
								CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.UseCompileTimeType, null)
							}));
						}
						NiFile.o__11.p__6.Target(NiFile.o__11.p__6, value, this);
					}
				}
				IL_303:;
			}
			#endif
		}

        /// <summary>
        /// Finds the root.
        /// </summary>
        /// <returns>NiAVObject.</returns>
        public NiAVObject FindRoot()
		{
			NiAVObject niAVObject = (from obj in this.ObjectsByRef.Values.OfType<NiAVObject>()
			select obj).FirstOrDefault<NiAVObject>();
			if (niAVObject == null)
			{
				return null;
			}
			while (niAVObject.Parent != null)
			{
				niAVObject = niAVObject.Parent;
			}
			return niAVObject;
		}

        /// <summary>
        /// Prints the nif tree.
        /// </summary>
        private void PrintNifTree()
		{
			NiAVObject niAVObject = this.FindRoot();
			if (niAVObject == null)
			{
				Console.WriteLine("No Root!");
				return;
			}
			int depth = 0;
			this.PrintNifNode(niAVObject, depth);
		}

        /// <summary>
        /// Prints the nif node.
        /// </summary>
        /// <param name="root">The root.</param>
        /// <param name="depth">The depth.</param>
        private void PrintNifNode(NiAVObject root, int depth)
		{
			string text = string.Empty;
			for (int i = 0; i < depth; i++)
			{
				text += "*";
			}
			text += " ";
			Console.WriteLine(text + " " + root.Name);
			NiNode niNode = root as NiNode;
			if (niNode != null)
			{
				NiRef<NiAVObject>[] children = niNode.Children;
				for (int j = 0; j < children.Length; j++)
				{
					NiRef<NiAVObject> niRef = children[j];
					if (niRef.IsValid())
					{
						this.PrintNifNode(niRef.Object, depth + 1);
					}
				}
			}
		}
	}
}
