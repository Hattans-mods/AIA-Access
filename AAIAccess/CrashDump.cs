#if DEVTOOLS
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace AAIAccess
{
    // Crash test helper: when the game hits a stack overflow, write a small crash dump to
    // UserData/AAIAccess_crash.dmp so we can see which code was looping. Turned on by
    // UserData/AAIAccess_dump.txt. The handler is a few bytes of native code, so it still
    // runs when the stack is full; the dump itself is written from a separate thread.
    internal static class CrashDump
    {
        [DllImport("kernel32")] static extern IntPtr GetModuleHandle(string n);
        [DllImport("kernel32")] static extern IntPtr GetProcAddress(IntPtr m, string n);
        [DllImport("kernel32")] static extern IntPtr VirtualAlloc(IntPtr a, UIntPtr s, uint t, uint p);
        [DllImport("kernel32")] static extern IntPtr CreateEvent(IntPtr a, bool manual, bool init, IntPtr name);
        [DllImport("kernel32")] static extern bool SetEvent(IntPtr h);
        [DllImport("kernel32")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32")] static extern IntPtr AddVectoredExceptionHandler(uint first, IntPtr h);
        [DllImport("kernel32")] static extern bool SetThreadStackGuarantee(ref uint size);
        [DllImport("kernel32")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32")] static extern uint GetCurrentProcessId();
        [DllImport("dbghelp", SetLastError = true)]
        static extern bool MiniDumpWriteDump(IntPtr proc, uint pid, IntPtr file, uint type, ref MINIDUMP_EXCEPTION_INFORMATION ex, IntPtr user, IntPtr cb);

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct MINIDUMP_EXCEPTION_INFORMATION { public uint ThreadId; public IntPtr ExceptionPointers; public int ClientPointers; }

        static IntPtr _ev, _done, _data;
        static string _path;

        internal static void Install()
        {
            try
            {
                string ud = MelonLoader.Utils.MelonEnvironment.UserDataDirectory;
                string flag = Path.Combine(ud, "AAIAccess_dump.txt");
                if (!File.Exists(flag) || !File.ReadAllText(flag).Trim().Equals("on", StringComparison.OrdinalIgnoreCase)) return;
                _path = Path.Combine(ud, "AAIAccess_crash.dmp");
                uint g = 0x10000; SetThreadStackGuarantee(ref g);
                _ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                _done = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                IntPtr k = GetModuleHandle("kernel32.dll");
                IntPtr pTid = GetProcAddress(k, "GetCurrentThreadId");
                IntPtr pSet = GetProcAddress(k, "SetEvent");
                IntPtr pWait = GetProcAddress(k, "WaitForSingleObject");
                IntPtr mem = VirtualAlloc(IntPtr.Zero, (UIntPtr)4096, 0x3000, 0x40);
                _data = mem + 0x800; // [0] exception pointers, [8] thread id
                var c = new System.Collections.Generic.List<byte>();
                void B(params byte[] b) => c.AddRange(b);
                void Q(long v) => c.AddRange(BitConverter.GetBytes(v));
                B(0x48, 0x8B, 0x01);                         // mov rax,[rcx]
                B(0x81, 0x38, 0xFD, 0x00, 0x00, 0xC0);       // cmp dword [rax],0xC00000FD
                B(0x74, 0x03);                               // je +3
                B(0x31, 0xC0, 0xC3);                         // xor eax,eax ; ret
                B(0x53);                                     // push rbx
                B(0x48, 0x83, 0xEC, 0x20);                   // sub rsp,20h
                B(0x48, 0x89, 0xCB);                         // mov rbx,rcx
                B(0x48, 0xB8); Q((long)_data);               // mov rax,&data
                B(0x48, 0x89, 0x18);                         // mov [rax],rbx
                B(0x48, 0xB8); Q((long)pTid);                // mov rax,GetCurrentThreadId
                B(0xFF, 0xD0);                               // call rax
                B(0x48, 0xBA); Q((long)_data + 8);           // mov rdx,&data+8
                B(0x89, 0x02);                               // mov [rdx],eax
                B(0x48, 0xB9); Q((long)_ev);                 // mov rcx,ev
                B(0x48, 0xB8); Q((long)pSet);                // mov rax,SetEvent
                B(0xFF, 0xD0);                               // call rax
                B(0x48, 0xB9); Q((long)_done);               // mov rcx,done
                B(0xBA, 0x60, 0xEA, 0x00, 0x00);             // mov edx,60000
                B(0x48, 0xB8); Q((long)pWait);               // mov rax,WaitForSingleObject
                B(0xFF, 0xD0);                               // call rax
                B(0x48, 0x83, 0xC4, 0x20);                   // add rsp,20h
                B(0x5B);                                     // pop rbx
                B(0x31, 0xC0, 0xC3);                         // xor eax,eax ; ret
                Marshal.Copy(c.ToArray(), 0, mem, c.Count);
                var t = new Thread(Watch) { IsBackground = true, Name = "AAIAccess crash dump" };
                t.Start();
                AddVectoredExceptionHandler(1, mem);
                Main.Log.Msg("Crash dump helper ready.");
            }
            catch (Exception e) { Main.Log.Warning("Crash dump helper: " + e.Message); }
        }

        static void Watch()
        {
            WaitForSingleObject(_ev, 0xFFFFFFFF);
            try
            {
                var info = new MINIDUMP_EXCEPTION_INFORMATION
                {
                    ThreadId = (uint)Marshal.ReadInt32(_data + 8),
                    ExceptionPointers = Marshal.ReadIntPtr(_data),
                    ClientPointers = 0
                };
                using (var fs = new FileStream(_path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    // MiniDumpWithIndirectlyReferencedMemory | MiniDumpWithThreadInfo | MiniDumpWithUnloadedModules
                    MiniDumpWriteDump(GetCurrentProcess(), GetCurrentProcessId(), fs.SafeFileHandle.DangerousGetHandle(), 0x40 | 0x1000 | 0x20, ref info, IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch { }
            SetEvent(_done);
        }
    }
}
#endif
