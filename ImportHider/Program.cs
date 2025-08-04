using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ImportHider
{
    internal unsafe class Program
    {
        private static readonly bool Is64Bit = IntPtr.Size == 8;

        [StructLayout(LayoutKind.Sequential)]
        private struct LIST_ENTRY
        {
            internal IntPtr Flink;
            internal IntPtr Blink;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UNICODE_STRING
        {
            internal ushort Length;
            internal ushort MaximumLength;
            internal IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGE_EXPORT_DIRECTORY
        {
            internal uint Characteristics;
            internal uint TimeDateStamp;
            internal ushort MajorVersion;
            internal ushort MinorVersion;
            internal uint Name;
            internal uint Base;
            internal uint NumberOfFunctions;
            internal uint NumberOfNames;
            internal uint AddressOfFunctions;
            internal uint AddressOfNames;
            internal uint AddressOfNameOrdinals;
        }

        [DllImport("ntdll.dll")]
        private static extern IntPtr RtlGetCurrentPeb();

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr LoadLib([MarshalAs(UnmanagedType.LPStr)] string lpLibFileName);

        public static T ResolveImport<T>(string dllName, string methodName) where T : Delegate
        {
            IntPtr moduleBase = GetModuleBase(dllName.ToUpperInvariant());

            if (moduleBase == IntPtr.Zero)
            {
                IntPtr kernel32Base = GetModuleBase("KERNEL32.DLL");
                if (kernel32Base == IntPtr.Zero)
                    throw new Win32Exception();

                IntPtr loadLibraryPtr = GetProcAddress(kernel32Base, "LoadLibraryA");
                if (loadLibraryPtr == IntPtr.Zero)
                    throw new Win32Exception();

                var loadLibrary = (LoadLib)Marshal.GetDelegateForFunctionPointer(loadLibraryPtr, typeof(LoadLib));
                moduleBase = loadLibrary(dllName);
                if (moduleBase == IntPtr.Zero)
                    throw new Win32Exception();
            }

            IntPtr procAddress = GetProcAddress(moduleBase, methodName);
            if (procAddress == IntPtr.Zero)
                throw new Win32Exception();

            return (T)Marshal.GetDelegateForFunctionPointer(procAddress, typeof(T));
        }

        private static IntPtr GetModuleBase(string targetModule)
        {
            IntPtr peb = RtlGetCurrentPeb();

            IntPtr ldr = Is64Bit
                ? *(IntPtr*)((byte*)peb + 0x18)
                : *(IntPtr*)((byte*)peb + 0x0C);

            IntPtr listHead = Is64Bit
                ? (IntPtr)((byte*)ldr + 0x10)
                : (IntPtr)((byte*)ldr + 0x0C);

            IntPtr currentEntry = Marshal.ReadIntPtr(listHead);

            while (currentEntry != listHead)
            {
                IntPtr dllBase = Is64Bit
                    ? *(IntPtr*)((byte*)currentEntry + 0x30)
                    : *(IntPtr*)((byte*)currentEntry + 0x18);

                UNICODE_STRING baseDllName = Is64Bit
                    ? *(UNICODE_STRING*)((byte*)currentEntry + 0x58)
                    : *(UNICODE_STRING*)((byte*)currentEntry + 0x2C);

                if (baseDllName.Buffer != IntPtr.Zero)
                {
                    string moduleName = Marshal.PtrToStringUni(baseDllName.Buffer, baseDllName.Length / 2);
                    if (moduleName.ToUpperInvariant() == targetModule)
                        return dllBase;
                }

                currentEntry = Marshal.ReadIntPtr(currentEntry);
            }

            return IntPtr.Zero;
        }

        private static IntPtr GetProcAddress(IntPtr moduleBase, string functionName)
        {
            byte* basePtr = (byte*)moduleBase;
            uint e_lfanew = *(uint*)(basePtr + 0x3C);

            uint exportDirRVA = *(uint*)(basePtr + e_lfanew + (Is64Bit ? 0x88 : 0x78));
            IMAGE_EXPORT_DIRECTORY* exportDir = (IMAGE_EXPORT_DIRECTORY*)(basePtr + exportDirRVA);

            uint* names = (uint*)(basePtr + exportDir->AddressOfNames);
            ushort* ordinals = (ushort*)(basePtr + exportDir->AddressOfNameOrdinals);
            uint* functions = (uint*)(basePtr + exportDir->AddressOfFunctions);

            for (uint i = 0; i < exportDir->NumberOfNames; i++)
            {
                string currentName = Marshal.PtrToStringAnsi((IntPtr)(basePtr + names[i]));
                if (currentName == functionName)
                {
                    return (IntPtr)(basePtr + functions[ordinals[i]]);
                }
            }

            return IntPtr.Zero;
        }

        static void Main()
        {
            try
            {
                var messageBox = ResolveImport<Mbox>("user32.dll", "MessageBoxW");
                messageBox(IntPtr.Zero, "Hello World".ToCharArray(), "Test".ToCharArray(), 0);
            }
            catch { }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private delegate int Mbox(IntPtr hWnd, char[] text, char[] caption, int type);
    }
}