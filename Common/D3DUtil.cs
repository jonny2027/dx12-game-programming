﻿using System;
using System.IO;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D12;
using Device = SharpDX.Direct3D12.Device;
using Resource = SharpDX.Direct3D12.Resource;
using ShaderBytecode = SharpDX.Direct3D12.ShaderBytecode;

namespace DX12GameProgramming
{
    public static class D3DUtil
    {
        public static Resource CreateDefaultBuffer<T>(
            Device device,
            GraphicsCommandList cmdList,
            T[] initData,
            long byteSize,
            out Resource uploadBuffer) where T : struct
        {
            // Create the actual default buffer resource.
            Resource defaultBuffer = device.CreateCommittedResource(
                new HeapProperties(HeapType.Default),
                HeapFlags.None,
                ResourceDescription.Buffer(byteSize),
                ResourceStates.Common);

            // In order to copy CPU memory data into our default buffer, we need to create
            // an intermediate upload heap.
            uploadBuffer = device.CreateCommittedResource(
                new HeapProperties(HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer(byteSize),
                ResourceStates.GenericRead);

            // Copy the data to the upload buffer.
            IntPtr ptr = uploadBuffer.Map(0);
            Utilities.Write(ptr, initData, 0, initData.Length);
            uploadBuffer.Unmap(0);         

            // Schedule to copy the data to the default buffer resource.
            cmdList.ResourceBarrierTransition(defaultBuffer, ResourceStates.Common, ResourceStates.CopyDestination);
            cmdList.CopyResource(defaultBuffer, uploadBuffer);
            cmdList.ResourceBarrierTransition(defaultBuffer, ResourceStates.CopyDestination, ResourceStates.GenericRead);

            // Note: uploadBuffer has to be kept alive after the above function calls because
            // the command list has not been executed yet that performs the actual copy.
            // The caller can Release the uploadBuffer after it knows the copy has been executed.

            return defaultBuffer;
        }

        // Constant buffers must be a multiple of the minimum hardware
        // allocation size (usually 256 bytes). So round up to nearest
        // multiple of 256. We do this by adding 255 and then masking off
        // the lower 2 bytes which store all bits < 256.
        // Example: Suppose byteSize = 300.
        // (300 + 255) & ~255
        // 555 & ~255
        // 0x022B & ~0x00ff
        // 0x022B & 0xff00
        // 0x0200
        // 512
        public static int CalcConstantBufferByteSize<T>() where T : struct => (Utilities.SizeOf<T>() + 255) & ~255;

        public static ShaderBytecode CompileShader(string fileName, string entryPoint, string profile, ShaderMacro[] defines = null)
        {
            var shaderFlags = ShaderFlags.None;
#if DEBUG
            shaderFlags |= ShaderFlags.Debug | ShaderFlags.SkipOptimization;
#endif
            CompilationResult result = SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile(
                fileName, 
                entryPoint, 
                profile, 
                shaderFlags, 
                include: FileIncludeHandler.Default,
                defines: defines);
            return new ShaderBytecode(result);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct Light
    {
        public const int Size = 48;

        public Vector3 Strength;
        public float FalloffStart;  // Point/spot light only.
        public Vector3 Direction;   // Directional/spot light only.
        public float FalloffEnd;    // Point/spot light only.
        public Vector3 Position;    // Point/spot light only.
        public float SpotPower;     // Spot light only.

        public static Light Default => new Light
        {
            Strength = new Vector3(0.5f),
            FalloffStart = 1.0f,
            Direction = -Vector3.UnitY,
            FalloffEnd = 10.0f,
            Position = Vector3.Zero,
            SpotPower = 64.0f
        };
    }

    // C# does not allow fixed custom struct buffers, hence we manually unroll the array.
    // MarshalAs does not seem to work correctly through SharpDX.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct Lights
    {
        public const int MaxLights = 16;

        public Light Light1;
        public Light Light2;
        public Light Light3;
        public Light Light4;
        public Light Light5;
        public Light Light6;
        public Light Light7;
        public Light Light8;
        public Light Light9;
        public Light Light10;
        public Light Light11;
        public Light Light12;
        public Light Light13;
        public Light Light14;
        public Light Light15;
        public Light Light16;

        public static Lights Default => new Lights
        {
            Light1 = Light.Default,
            Light2 = Light.Default,
            Light3 = Light.Default,
            Light4 = Light.Default,
            Light5 = Light.Default,
            Light6 = Light.Default,
            Light7 = Light.Default,
            Light8 = Light.Default,
            Light9 = Light.Default,
            Light10 = Light.Default,
            Light11 = Light.Default,
            Light12 = Light.Default,
            Light13 = Light.Default,
            Light14 = Light.Default,
            Light15 = Light.Default,
            Light16 = Light.Default
        };

        public Light this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return Light1;
                    case 1: return Light2;
                    case 2: return Light3;
                    case 3: return Light4;
                    case 4: return Light5;
                    case 5: return Light6;
                    case 6: return Light7;
                    case 7: return Light8;
                    case 8: return Light9;
                    case 9: return Light10;
                    case 10: return Light11;
                    case 11: return Light12;
                    case 12: return Light13;
                    case 13: return Light14;
                    case 14: return Light15;
                    default: return Light16;
                }
            }
            set
            {
                switch (index)
                {
                    case 0: Light1 = value; break;
                    case 1: Light2 = value; break;
                    case 2: Light3 = value; break;
                    case 3: Light4 = value; break;
                    case 4: Light5 = value; break;
                    case 5: Light6 = value; break;
                    case 6: Light7 = value; break;
                    case 7: Light8 = value; break;
                    case 8: Light9 = value; break;
                    case 9: Light10 = value; break;
                    case 10: Light11 = value; break;
                    case 11: Light12 = value; break;
                    case 12: Light13 = value; break;
                    case 13: Light14 = value; break;
                    case 14: Light15 = value; break;
                    default: Light16 = value; break;
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct MaterialConstants
    {
        public Vector4 DiffuseAlbedo;
        public Vector3 FresnelR0;
        public float Roughness;

        // Used in texture mapping.
        public Matrix MatTransform;

        public static MaterialConstants Default => new MaterialConstants 
        {
            DiffuseAlbedo = Vector4.One,
            FresnelR0 = new Vector3(0.01f),
            Roughness = 0.25f,
            MatTransform = Matrix.Identity
        };
    };

    // Simple struct to represent a material for our demos. A production 3D engine
    // would likely create a class hierarchy of Materials.
    public class Material
    {
        // Unique material name for lookup.
        public string Name { get; set; }

        // Index into constant buffer corresponding to this material.
        public int MatCBIndex { get; set; } = -1;

        // Index into SRV heap for diffuse texture.
        public int DiffuseSrvHeapIndex { get; set; } = -1;

        // Index into SRV heap for normal texture.
        public int NormalSrvHeapIndex { get; set; } = -1;

        // Dirty flag indicating the material has changed and we need to update the constant buffer.
        // Because we have a material constant buffer for each FrameResource, we have to apply the
        // update to each FrameResource. Thus, when we modify a material we should set 
        // NumFramesDirty = NumFrameResources so that each frame resource gets the update.
        public int NumFramesDirty { get; set; } = D3DApp.NumFrameResources;

        // Material constant buffer data used for shading.
        public Vector4 DiffuseAlbedo { get; set; } = Vector4.One;
        public Vector3 FresnelR0 { get; set; } = new Vector3(0.01f);
        public float Roughness { get; set; } = 0.25f;
        public Matrix MatTransform { get; set; } = Matrix.Identity;
    }

    public class Texture : IDisposable
    {
        // Unique material name for lookup.
        public string Name { get; set; }

        public string Filename { get; set; }

        public Resource Resource { get; set; }
        public Resource UploadHeap { get; set; }

        public void Dispose()
        {
            Resource?.Dispose();
            UploadHeap?.Dispose();
        }
    }

    // Required for ShaderBytecode.CompileFromFile API in order to resolve #includes in shader files.
    // Equivalent for D3D_COMPILE_STANDARD_FILE_INCLUDE.
    internal class FileIncludeHandler : CallbackBase, Include
    {
        public static FileIncludeHandler Default { get; } = new FileIncludeHandler();

        public Stream Open(IncludeType type, string fileName, Stream parentStream)
        {
            string filePath = fileName;

            if (!Path.IsPathRooted(filePath))
            {
                string selectedFile = Path.Combine(Environment.CurrentDirectory, fileName);
                if (File.Exists(selectedFile))
                    filePath = selectedFile;
            }

            return new FileStream(filePath, FileMode.Open, FileAccess.Read);
        }

        public void Close(Stream stream) => stream.Close();
    }

    // We define the enums below to provide the same values for mouse and keyboard input
    // as System.Windows.Forms does. This is done in order to prevent direct dependencies
    // from samples to System.Windows.Forms and System.Drawing.

    // Ref: System.Windows.Forms.MouseButtons
    [Flags]
    public enum MouseButtons
    {
        Left = 1048576,
        None = 0,
        Right = 2097152,
        Middle = 4194304,
        XButton1 = 8388608,
        XButton2 = 16777216
    }

    // Ref: System.Windows.Forms.Keys
    public enum Keys
    {
        KeyCode = 65535,
        Modifiers = -65536,
        None = 0,
        LButton = 1,
        RButton = 2,
        Cancel = 3,
        MButton = 4,
        XButton1 = 5,
        XButton2 = 6,
        Back = 8,
        Tab = 9,
        LineFeed = 10,
        Clear = 12,
        Return = 13,
        Enter = 13,
        ShiftKey = 16,
        ControlKey = 17,
        Menu = 18,
        Pause = 19,
        Capital = 20,
        CapsLock = 20,
        KanaMode = 21,
        HanguelMode = 21,
        HangulMode = 21,
        JunjaMode = 23,
        FinalMode = 24,
        HanjaMode = 25,
        KanjiMode = 25,
        Escape = 27,
        IMEConvert = 28,
        IMENonconvert = 29,
        IMEAccept = 30,
        IMEAceept = 30,
        IMEModeChange = 31,
        Space = 32,
        Prior = 33,
        PageUp = 33,
        Next = 34,
        PageDown = 34,
        End = 35,
        Home = 36,
        Left = 37,
        Up = 38,
        Right = 39,
        Down = 40,
        Select = 41,
        Print = 42,
        Execute = 43,
        Snapshot = 44,
        PrintScreen = 44,
        Insert = 45,
        Delete = 46,
        Help = 47,
        D0 = 48,
        D1 = 49,
        D2 = 50,
        D3 = 51,
        D4 = 52,
        D5 = 53,
        D6 = 54,
        D7 = 55,
        D8 = 56,
        D9 = 57,
        A = 65,
        B = 66,
        C = 67,
        D = 68,
        E = 69,
        F = 70,
        G = 71,
        H = 72,
        I = 73,
        J = 74,
        K = 75,
        L = 76,
        M = 77,
        N = 78,
        O = 79,
        P = 80,
        Q = 81,
        R = 82,
        S = 83,
        T = 84,
        U = 85,
        V = 86,
        W = 87,
        X = 88,
        Y = 89,
        Z = 90,
        LWin = 91,
        RWin = 92,
        Apps = 93,
        Sleep = 95,
        NumPad0 = 96,
        NumPad1 = 97,
        NumPad2 = 98,
        NumPad3 = 99,
        NumPad4 = 100,
        NumPad5 = 101,
        NumPad6 = 102,
        NumPad7 = 103,
        NumPad8 = 104,
        NumPad9 = 105,
        Multiply = 106,
        Add = 107,
        Separator = 108,
        Subtract = 109,
        Decimal = 110,
        Divide = 111,
        F1 = 112,
        F2 = 113,
        F3 = 114,
        F4 = 115,
        F5 = 116,
        F6 = 117,
        F7 = 118,
        F8 = 119,
        F9 = 120,
        F10 = 121,
        F11 = 122,
        F12 = 123,
        F13 = 124,
        F14 = 125,
        F15 = 126,
        F16 = 127,
        F17 = 128,
        F18 = 129,
        F19 = 130,
        F20 = 131,
        F21 = 132,
        F22 = 133,
        F23 = 134,
        F24 = 135,
        NumLock = 144,
        Scroll = 145,
        LShiftKey = 160,
        RShiftKey = 161,
        LControlKey = 162,
        RControlKey = 163,
        LMenu = 164,
        RMenu = 165,
        BrowserBack = 166,
        BrowserForward = 167,
        BrowserRefresh = 168,
        BrowserStop = 169,
        BrowserSearch = 170,
        BrowserFavorites = 171,
        BrowserHome = 172,
        VolumeMute = 173,
        VolumeDown = 174,
        VolumeUp = 175,
        MediaNextTrack = 176,
        MediaPreviousTrack = 177,
        MediaStop = 178,
        MediaPlayPause = 179,
        LaunchMail = 180,
        SelectMedia = 181,
        LaunchApplication1 = 182,
        LaunchApplication2 = 183,
        OemSemicolon = 186,
        Oem1 = 186,
        Oemplus = 187,
        Oemcomma = 188,
        OemMinus = 189,
        OemPeriod = 190,
        OemQuestion = 191,
        Oem2 = 191,
        Oemtilde = 192,
        Oem3 = 192,
        OemOpenBrackets = 219,
        Oem4 = 219,
        OemPipe = 220,
        Oem5 = 220,
        OemCloseBrackets = 221,
        Oem6 = 221,
        OemQuotes = 222,
        Oem7 = 222,
        Oem8 = 223,
        OemBackslash = 226,
        Oem102 = 226,
        ProcessKey = 229,
        Packet = 231,
        Attn = 246,
        Crsel = 247,
        Exsel = 248,
        EraseEof = 249,
        Play = 250,
        Zoom = 251,
        NoName = 252,
        Pa1 = 253,
        OemClear = 254,
        Shift = 65536,
        Control = 131072,
        Alt = 262144
    }
}