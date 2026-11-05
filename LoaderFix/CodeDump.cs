using System;
using System.IO;
using System.Runtime.InteropServices;
using MelonLoader;

namespace AAIAccessLoaderFix
{
    /// <summary>The game keeps its code (GameAssembly.dll) inside GK12.exe and shows only its own process a fake
    /// copy. MelonLoader needs the real code to build its helper files. Once the game has started, the real code
    /// is in memory: copy it to GK12CodeDump.dll (a name the game doesn't fake), in the normal file layout, with
    /// the type table turned back into the form it has on disk. Reads memory only; changes nothing in the game.</summary>
    internal static class CodeDump
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string name);
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] private static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);

        public const string DumpName = "GK12CodeDump.dll";
        private const string StampName = "GK12CodeDump.txt";

        /// <summary>What the copy was made from: if the game is updated, a new copy is made.</summary>
        private static string Stamp(string gameDir)
        {
            string S(string p) { var f = new FileInfo(p); return f.Exists ? $"{f.Length}:{f.LastWriteTimeUtc.Ticks}" : "-"; }
            return S(Path.Combine(gameDir, "GK12.exe")) + "|" + S(Path.Combine(gameDir, "GK12_Data", "il2cpp_data", "Metadata", "global-metadata.dat"));
        }

        /// <summary>Makes GK12CodeDump.dll if it is missing or was made from another version of the game.
        /// Returns its path, or null when there is no copy to use.</summary>
        public static string Ensure(string gameDir)
        {
            string dump = Path.Combine(gameDir, DumpName), stampPath = Path.Combine(gameDir, StampName);
            string stamp = Stamp(gameDir);
            try
            {
                if (File.Exists(dump) && File.Exists(stampPath) && File.ReadAllText(stampPath).Trim() == stamp) return dump;
                if (File.Exists(dump) && !File.Exists(stampPath))
                {
                    // Made earlier by the separate copying tool: keep it, and remember what it belongs to.
                    File.WriteAllText(stampPath, stamp);
                    MelonLogger.Msg("Using the existing copy of the game's code.");
                    return dump;
                }
                MelonLogger.Msg("Copying the game's code out of memory (one time only)...");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                byte[] file = Make(Path.Combine(gameDir, "GK12_Data", "il2cpp_data", "Metadata", "global-metadata.dat"));
                if (file == null) return File.Exists(dump) ? dump : null;
                File.WriteAllBytes(dump, file);
                File.WriteAllText(stampPath, stamp);
                MelonLogger.Msg($"Saved {DumpName} ({file.Length / 1024 / 1024} MB) in {sw.ElapsedMilliseconds} ms.");
                return dump;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Could not copy the game's code: " + e.Message);
                return File.Exists(dump) ? dump : null;
            }
        }

        private static uint U32(byte[] b, long o) => BitConverter.ToUInt32(b, (int)o);
        private static ulong U64(byte[] b, long o) => BitConverter.ToUInt64(b, (int)o);

        private static byte[] Make(string metaPath)
        {
            IntPtr mod = GetModuleHandleW("GameAssembly.dll");
            if (mod == IntPtr.Zero) { MelonLogger.Warning("GameAssembly.dll is not loaded."); return null; }
            ulong baseAddr = (ulong)mod.ToInt64();
            // Headers first, to learn the image size.
            byte[] hdr = new byte[0x1000];
            ReadProcessMemory(GetCurrentProcess(), mod, hdr, (IntPtr)hdr.Length, out _);
            int peOff = (int)U32(hdr, 0x3c);
            uint sizeOfImage = U32(hdr, peOff + 24 + 56);
            byte[] mem = new byte[sizeOfImage];
            byte[] page = new byte[0x1000];
            long ok = 0;
            for (long off = 0; off < sizeOfImage; off += 0x1000)
            {
                int n = (int)Math.Min(0x1000, sizeOfImage - off);
                if (ReadProcessMemory(GetCurrentProcess(), (IntPtr)(long)(baseAddr + (ulong)off), page, (IntPtr)n, out var got))
                { Buffer.BlockCopy(page, 0, mem, (int)off, (int)got); ok += (long)got; }
            }
            MelonLogger.Msg($"Read {ok} of {sizeOfImage} bytes.");

            int nsec = BitConverter.ToUInt16(mem, peOff + 6);
            int optSize = BitConverter.ToUInt16(mem, peOff + 20);
            int secOff = peOff + 24 + optSize;
            uint sizeOfHeaders = U32(mem, peOff + 24 + 60);
            long fileSize = sizeOfHeaders;
            var sva = new uint[nsec]; var srs = new uint[nsec]; var srp = new uint[nsec];
            for (int i = 0; i < nsec; i++)
            {
                int s = secOff + i * 40;
                sva[i] = U32(mem, s + 12); srs[i] = U32(mem, s + 16); srp[i] = U32(mem, s + 20);
                fileSize = Math.Max(fileSize, (long)srp[i] + srs[i]);
            }
            byte[] outb = new byte[fileSize];
            Buffer.BlockCopy(mem, 0, outb, 0, (int)sizeOfHeaders);
            for (int i = 0; i < nsec; i++)
            {
                long n = srs[i];
                if (sva[i] + n > sizeOfImage) n = sizeOfImage - sva[i];
                if (srp[i] != 0 && n > 0) Buffer.BlockCopy(mem, (int)sva[i], outb, (int)srp[i], (int)n);
            }
            BitConverter.GetBytes(baseAddr).CopyTo(outb, peOff + 24 + 24);   // ImageBase = where it was loaded

            long VaToOff(ulong va)
            {
                ulong rva = va - baseAddr;
                for (int i = 0; i < nsec; i++)
                {
                    int s = secOff + i * 40; uint vs = U32(mem, s + 8);
                    ulong span = Math.Max(vs, srs[i]);
                    if (rva >= sva[i] && rva < sva[i] + span) return rva - sva[i] < srs[i] ? (long)(srp[i] + rva - sva[i]) : 0;
                }
                return 0;
            }

            // The type table: found by its shape (fieldOffsetsCount and typeDefinitionsSizesCount both equal the
            // number of types in global-metadata.dat), so this works for any build of the game.
            byte[] mh = new byte[66 * 4];
            using (var mf = File.OpenRead(metaPath)) mf.Read(mh, 0, mh.Length);
            uint tdOff = U32(mh, (2 + 38) * 4), tdSize = U32(mh, (2 + 39) * 4), gpOff = U32(mh, (2 + 24) * 4);
            ulong NT = tdSize / 88;
            ulong metaReg = 0;
            for (int i = 0; i < nsec && metaReg == 0; i++)
                for (long o = 0; o + 128 <= srs[i]; o += 8)
                {
                    long p = srp[i] + o;
                    if (U64(outb, p + 80) == NT && U64(outb, p + 96) == NT && U64(outb, p + 48) > 1000 && U64(outb, p + 48) < 1000000
                        && U64(outb, p) < 1000000 && U64(outb, p + 16) < 1000000)
                    { metaReg = baseAddr + sva[i] + (ulong)o; break; }
                }
            if (metaReg == 0) { MelonLogger.Warning("Could not find the metadata registration."); return null; }

            long o1 = VaToOff(metaReg + 48), o2 = VaToOff(metaReg + 56);
            ulong typesCount = U64(outb, o1), typesPtr = U64(outb, o2);
            long tOff = VaToOff(typesPtr);
            if (tOff == 0) { MelonLogger.Warning("Unexpected type table."); return null; }
            byte[] td = new byte[tdSize];
            using (var mf = File.OpenRead(metaPath)) { mf.Seek(tdOff, SeekOrigin.Begin); mf.Read(td, 0, td.Length); }
            // Where the metadata was in memory: a type definition whose type entry holds a pointer to it.
            ulong tdBase = 0;
            for (ulong i = 0; i < NT && tdBase == 0; i++)
            {
                int b = BitConverter.ToInt32(td, (int)(i * 88 + 8));
                if (b < 0 || (ulong)b >= typesCount) continue;
                long po = VaToOff(U64(outb, tOff + 8 * b));
                if (po == 0) continue;
                ulong data = U64(outb, po); int t = (int)((U32(outb, po + 8) >> 16) & 0xff);
                if ((t == 0x11 || t == 0x12) && data > 0x10000) tdBase = data - i * 88;
            }
            int fixedN = 0;
            if (tdBase != 0)
            {
                ulong gpBase = tdBase - tdOff + gpOff;
                for (ulong i = 0; i < typesCount; i++)
                {
                    long po = VaToOff(U64(outb, tOff + 8 * (long)i));
                    if (po == 0) continue;
                    ulong data = U64(outb, po); int t = (int)((U32(outb, po + 8) >> 16) & 0xff);
                    if (data >= tdBase && data < tdBase + NT * 88 && (data - tdBase) % 88 == 0)
                    { BitConverter.GetBytes((data - tdBase) / 88).CopyTo(outb, po); fixedN++; }
                    else if ((t == 0x13 || t == 0x1e) && data >= gpBase && (data - gpBase) % 16 == 0 && (data - gpBase) / 16 < 1000000)
                    { BitConverter.GetBytes((data - gpBase) / 16).CopyTo(outb, po); fixedN++; }
                }
            }
            MelonLogger.Msg($"Metadata registration at RVA 0x{metaReg - baseAddr:X}; restored {fixedN} type entries.");
            return outb;
        }
    }
}
