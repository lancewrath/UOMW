// Unity Vector type aliases for niflib.net
// This allows niflib.net to compile in Unity without OpenTK/SharpDX/MonoGame
#if UNITY_5_3_OR_NEWER || UNITY_2017_1_OR_NEWER || UNITY_2018_1_OR_NEWER || UNITY_2019_1_OR_NEWER || UNITY_2020_1_OR_NEWER || UNITY_2021_1_OR_NEWER || UNITY_2022_1_OR_NEWER || UNITY_2023_1_OR_NEWER
#define UNITY
#endif

#if UNITY && !OpenTK && !SharpDX && !MonoGame
using UnityEngine;
namespace Niflib
{
    // Type aliases for Unity
    using Vector3 = UnityEngine.Vector3;
    using Vector2 = UnityEngine.Vector2;
    using Vector4 = UnityEngine.Vector4;
    using Color4 = UnityEngine.Color;
    using Matrix = UnityEngine.Matrix4x4;
}
#endif

