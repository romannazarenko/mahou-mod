using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mahou {
	public static class miniaudio {
	    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	    delegate int InitEngine(IntPtr cfg, IntPtr eng);
	    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	    delegate int InitDecoderMem(IntPtr data, UIntPtr sz, IntPtr cfg, IntPtr dec);
	    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	    delegate int InitSound(IntPtr eng, IntPtr dataSource, uint flags, IntPtr group, IntPtr snd);
	    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	    delegate int PlaySound(IntPtr snd);
	    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		delegate bool IsAtEnd(IntPtr snd);
	    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	    delegate void FreeNative(IntPtr ptr);
	    static IntPtr hDll,
			eng = Marshal.AllocHGlobal(4096),
			dec = Marshal.AllocHGlobal(4096),
			snd = Marshal.AllocHGlobal(4096);
	    static GCHandle pin;
	    static FreeNative freeSnd, freeDec, freeEng;
	    static readonly uint MA_SOUND_FLAG_DECODE = 1; // (auto-decodes MP3/WAV/FLAC from pointer)
	    static bool isPlaying = false;
	    public static string DllPath() {
			var dll = "miniaudio_x86.dll";
			if (Environment.Is64BitProcess) dll = "miniaudio_x64.dll";
			var dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dll);
			if (!File.Exists(dllPath)) dllPath = Path.Combine(MahouUI.nPath, dll);
			return dllPath;
	    }
	    public static bool Play(byte[] bytes) {
			var dll = DllPath();
			if (hDll == IntPtr.Zero) hDll = WinAPI.LoadLibrary(dll);
	        if (hDll == IntPtr.Zero) { Logging.Log("[miniaudio] Can't find [" + dll + "].", 2); return false; }
	        CleanupNative();
	        var initE = (InitEngine)Bind("ma_engine_init", typeof(InitEngine));
	        var initD = (InitDecoderMem)Bind("ma_decoder_init_memory", typeof(InitDecoderMem));
	        var initS = (InitSound)Bind("ma_sound_init_from_data_source", typeof(InitSound));
	        var playS = (PlaySound)Bind("ma_sound_start", typeof(PlaySound));
	        var atEnd = (IsAtEnd)Bind("ma_sound_at_end", typeof(IsAtEnd));
	        freeSnd   = (FreeNative)Bind("ma_sound_uninit", typeof(FreeNative));
	        freeDec = (FreeNative)Bind("ma_decoder_uninit", typeof(FreeNative));
	        freeEng   = (FreeNative)Bind("ma_engine_uninit", typeof(FreeNative));
	        Zero(eng, 4096);
	        Zero(dec, 4096);
	        Zero(snd, 4096);
	        var rr = initE(IntPtr.Zero, eng);
	        if (rr != 0) { Logging.Log("[miniaudio] init engine returned: " + rr + ".", 1); return false; }
	        pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
	        rr = initD(pin.AddrOfPinnedObject(), (UIntPtr)bytes.Length, IntPtr.Zero, dec);
	        if (rr != 0) { Logging.Log("[miniaudio] init decoder returned: " + rr + ".", 1); return false; }
	        rr = initS(eng, dec, MA_SOUND_FLAG_DECODE, IntPtr.Zero, snd);
	        if (rr != 0) { Logging.Log("[miniaudio] init source returned: " + rr + ".", 1); return false; }
	        rr = playS(snd);
	        if (rr == 0) isPlaying = true;
	        System.Threading.Tasks.Task.Run(() => {
				while (isPlaying && !atEnd(snd)) System.Threading.Thread.Sleep(10);
		        Logging.Log("[miniaudio] Played sound using " + dll + " result code: " + rr);
            });
	        return true;
	    }
	    static void CleanupNative() {
			isPlaying = false;
            if (freeSnd != null) { freeSnd(snd); freeSnd = null; }
            if (freeDec != null) { freeDec(dec); freeDec = null; }
            if (freeEng != null) { freeEng(eng); freeEng = null; }
            if (pin.IsAllocated) { pin.Free(); pin = default(GCHandle); }
        }
	    static object Bind(string n, Type t) {
	        return Marshal.GetDelegateForFunctionPointer(WinAPI.GetProcAddress(hDll, n), t);
	    }
		static void Zero(IntPtr p, int sz) {
			for (int k = 0; k < sz; k++) Marshal.WriteByte(p, k, 0);
		}
	}
}