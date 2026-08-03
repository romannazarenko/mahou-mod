using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Diagnostics;

namespace Mahou
{
	static class Locales {
		[DllImport("getconkbl.dll")]
		public static extern uint GetConsoleAppKbLayout(uint ActiveConsolePID);
		[DllImport("getconkbl.dll")]
		public static extern bool Initialize();
		/// <summary>
		/// Returns current layout id in foreground window.
		/// </summary>
		/// <returns>uint</returns>
		public static uint GetCurrentLocale(IntPtr byHwnd = default(IntPtr)) { //Gets current locale in active window, or in specified hwnd.
			IntPtr actv = IntPtr.Zero;
			if (byHwnd == IntPtr.Zero)
				actv = ActiveWindow();
			else 
				actv = byHwnd;
			uint pid = 0;
			uint tid = WinAPI.GetWindowThreadProcessId(actv, out pid);
			IntPtr layout = WinAPI.GetKeyboardLayout(tid);
			try {
				IfCmdExe(actv, out pid); 
				if (pid != 0) 
					return pid;
			} catch (Exception e) { Logging.Log("Error in IfCmdExe (getconkbl.dll), details: \r\n" + e.Message + e.StackTrace +"\r\n", 1); }
			//Produces TOO much logging, disabled.
            //Logging.Log("Current locale id is [" + (uint)(layout.ToInt32() & 0xFFFF) + "].");
            //Debug.WriteLine(layout + " A " + actv);
			return (uint)layout;
		}
		public static void IfCmdExe(IntPtr hwnd, out uint layoutId) {
			uint pid;
			var strb = new StringBuilder(256);
			WinAPI.GetClassName(hwnd, strb, strb.Capacity);
			if (strb.ToString() == "ConsoleWindowClass") {
				var imewnd = WinAPI.ImmGetDefaultIMEWnd(hwnd);  // https://www.autohotkey.com/boards/viewtopic.php?t=84140
				if (imewnd != IntPtr.Zero) {
					hwnd = imewnd;
				}
				var tid = WinAPI.GetWindowThreadProcessId(hwnd, out pid);
				IntPtr layout = WinAPI.GetKeyboardLayout(tid);
				if (layout != IntPtr.Zero) {
					layoutId = (uint)layout;
					return;
				}
				uint lid = 0;
				try {
					var init = Initialize();
					Logging.Log("INIT: "+init);
					lid = GetConsoleAppKbLayout(pid);
				} catch {
					Logging.Log("getconkbl.dll not found, console layout get will not be right.", 2);
				}
				Logging.Log("Tried to get console layout id, return ["+lid+"], pid ["+pid+"].");
				layoutId = lid;
			} else layoutId = 0;
		}
		/// <summary>
		/// Returns focused or foreground window.
		/// </summary>
		/// <returns>IntPtr(HWND)</returns>
		public static IntPtr ActiveWindow() {
			IntPtr awHandle = IntPtr.Zero;
			var gui = new WinAPI.GUITHREADINFO();
			gui.cbSize = Marshal.SizeOf(gui);
			WinAPI.GetGUIThreadInfo(WinAPI.GetWindowThreadProcessId(WinAPI.GetForegroundWindow(), IntPtr.Zero), ref gui);
			awHandle = gui.hwndFocus;
			if (awHandle == IntPtr.Zero) {
				awHandle = WinAPI.GetForegroundWindow();
			} 
			return awHandle;
		}
		public static Process ActiveWindowProcess() {
			uint pid = 0;
			WinAPI.GetWindowThreadProcessId(Locales.ActiveWindow(), out pid);
			Process prc = null;
			try { prc = Process.GetProcessById((int)pid); } catch { Logging.Log("Process with id ["+pid+"] not exist...", 1); }
			return prc;
			
		}
		public static string ActiveWindowClassName(int len, IntPtr hwnd = default(IntPtr)) {
			var _fw = hwnd == default(IntPtr) ? ActiveWindow() : hwnd;
			var _clsNMb = new StringBuilder(len);
			WinAPI.GetClassName(_fw, _clsNMb, _clsNMb.Capacity);
			return _clsNMb.ToString();
			
		}
		/// <summary>
		/// Returns all installed in system layouts. 
		/// </summary>
		/// <returns></returns>
		public static Locale[] AllList() {
			var locs = new List<Locale>();
			var PHl = new List<uint>();
			string[] usrord = null;
			using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\International\User Profile")) {
				if (key != null) { usrord = key.GetValue("Languages") as string[]; }
			}
			if (usrord == null || usrord.Length == 0) {
				usrord = new string[16];
				for (int i = 0; i != usrord.Length; i++) { usrord[i] = "?"; }
			}
			string[] usrord2 = null;
			using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Keyboard Layout\Preload")) {
				if (key != null) {
					int i = 1;
					var vl = new List<string>();
					while (true) {
						var v = key.GetValue(i.ToString()) as string;
						if (string.IsNullOrEmpty(v)) break;
						i++;
						vl.Add(v);
					}
					usrord2 = vl.ToArray();
				}
			}
			Logging.Log("[Locales] Locales installed: " + usrord.Length);
			var subs = new Dictionary<uint, uint>();
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Keyboard Layout\Substitutes")) {
                if (key != null) {
                    foreach (var n in key.GetValueNames()) {
						var vr = key.GetValue(n);
						var v = ""; if (vr != null) v = vr.ToString();
                        if (!string.IsNullOrEmpty(v)) {
							uint hex = 0, hex2 = 0;
							uint.TryParse(n, System.Globalization.NumberStyles.HexNumber, null, out hex);
							uint.TryParse(v, System.Globalization.NumberStyles.HexNumber, null, out hex2);
                            subs[hex] = hex2;
                        }
                    }
                }
            }
			Logging.Log("[Locales] Substitutes: " + subs.Count);
			for(var i = 0; i < usrord2.Length; i++) {
				foreach (InputLanguage lang in InputLanguage.InstalledInputLanguages) {
					uint u = (uint)lang.Handle;
					if (locs.Exists(x => x.uId == u)) continue;
					Debug.WriteLine("Testing: " + lang.LayoutName);
					var matches = false;
					uint hex = 0;
					// In case there is non-standard layout which is missing data
					var null_or_empty_layoutname = string.IsNullOrEmpty(lang.LayoutName);
					var likely_custom_layout = string.IsNullOrEmpty(lang.Culture.Name) ||
						string.IsNullOrEmpty(lang.Culture.TwoLetterISOLanguageName) ||
						null_or_empty_layoutname;
					if (!likely_custom_layout) {
						matches = usrord[i].Contains("-") ?
							string.Equals(usrord[i], lang.Culture.Name, StringComparison.OrdinalIgnoreCase) :
						    string.Equals(usrord[i], lang.Culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase);
					}
					if (uint.TryParse(usrord2[i], System.Globalization.NumberStyles.HexNumber, null, out hex)) {
						Logging.Log("[Locales] Using HKCU\\Keyboard Layout\\Preload order...");
						foreach(var h in subs.Keys) {
							if (hex == h) {
								Logging.Log("[Locales] Layout substitute: " + hex.ToString("X") + " => " + subs[h].ToString("X"));
								hex = subs[h];
								break;
							}
						}
						matches = (hex == u) || ((hex & 0xffff) == (u & 0xffff));
					}
					if (matches || likely_custom_layout) {
						uint shc = u >> 16;
						if (!PHl.Contains(shc))
							PHl.Add(shc);
						Logging.Log("[Locales] Adding " + usrord[i] + "/" + u.ToString("X") + " as #" + (i+1));
						locs.Add(new Locale {
							Lang = null_or_empty_layoutname ? "0x" + u.ToString("X") : lang.LayoutName,
							uId = u
						});
					}
				}
			}
			var locsstr = "";
			for (var i = 0; i < locs.Count; i++) {
				locsstr += "[" + locs[i].uId + " " + locs[i].Lang + "]" + (i+1 != locs.Count ? "," : "");
			}
			Logging.Log("[Locales] Final layouts: " + locs.Count + ": " + locsstr);
			MMain.PHLayouts = PHl.Count;
			return locs.ToArray();
		}
		/// <summary>
		/// Gets Locale from localeString.
		/// </summary>
		/// <param name="localeString">String which contains layout name and uid.</param>
		/// <returns>Locale</returns>
		public static Locale GetLocaleFromString(string localeString) {
			var getLocale = new Regex(@"^(.+)\((\d+)");
			var lang = getLocale.Match(localeString).Groups[1].Value;
			uint id = 0;
			if (!UInt32.TryParse(getLocale.Match(localeString).Groups[2].Value, out id)) {
				Logging.Log("Locale string ["+localeString+"] does not contain a layout uID.");
				return new Locale();
			}
			return new Locale() { Lang = lang, uId = id};
		}
		/// <summary>
		/// Check if you have enough layouts(>2).
		/// </summary>
		public static void IfLessThan2() {
			if (MMain.MyConfs.ReadBool("Hidden", "disablelessthan2layoutsnotification")) return;
			if (AllList().Length < 2) {
            	MMain.mahou.icon.trIcon.BalloonTipClicked += (_, __) => Process.Start(@"C:\Windows\system32\rundll32.exe", "shell32.dll,Control_RunDLL input.dll");
				MMain.mahou.icon.trIcon.ShowBalloonTip(4500, "You have too less layouts(locales/languages)!!", "This program switches texts by system's layouts(locales/languages), please add at least 2!", ToolTipIcon.Warning);
			}
		}
		/// <summary>
		/// Contains layout name [Lang], and layout id [uId].  
		/// </summary>
		public struct Locale {
			public string Lang { get; set; }
			public uint uId { get; set; }
		}
	}
}
