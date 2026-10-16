// User auto-switch dictionary (AS_user.txt): rules learned from manual
// corrections, checked before AS_dict.txt and the n-gram models.
//
// Unlike AS_dict it is never "too big" to edit, survives dictionary downloads,
// and can express "never switch this word", which neither AS_dict nor the
// n-gram models can.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Mahou {
	public static class UserDict {
		public const string FileName = "AS_user.txt";
		const string Arrow = " => ";
		const char NeverMark = '!';
		static readonly string[] Header = {
			"# Mahou user auto-switch dictionary. Rules here win over AS_dict.txt and n-gram.",
			"# One rule per line, words are matched case-insensitively:",
			"#   ghbdtn => привет    switch: typed word => correct word",
			"#   !ckjy               never auto-switch this typed word",
			"# The file is rewritten when Mahou learns a word, so comments below are not kept.",
		};
		static readonly object locky = new object();
		static readonly Dictionary<string, string> switches = new Dictionary<string, string>();
		static readonly HashSet<string> nevers = new HashSet<string>();
		static string loadedPath;

		public static string FilePath { get { return Path.Combine(MahouUI.nPath, FileName); } }

		/// <summary>(Re)reads the file, so manual edits are picked up on Apply.</summary>
		public static void Load() {
			lock (locky) {
				switches.Clear();
				nevers.Clear();
				loadedPath = FilePath;
				if (!File.Exists(loadedPath)) return;
				foreach (var raw in File.ReadAllLines(loadedPath, Encoding.UTF8)) {
					var line = raw.Trim();
					if (line.Length == 0 || line[0] == '#') continue;
					if (line[0] == NeverMark) {
						var w = line.Substring(1).Trim().ToLowerInvariant();
						if (w.Length > 0) nevers.Add(w);
						continue;
					}
					var a = line.IndexOf(Arrow.Trim(), StringComparison.Ordinal);
					if (a <= 0) continue;
					var typed = line.Substring(0, a).Trim().ToLowerInvariant();
					var correct = line.Substring(a + Arrow.Trim().Length).Trim().ToLowerInvariant();
					if (typed.Length > 0 && correct.Length > 0) switches[typed] = correct;
				}
				Logging.Log("[ASU] > Loaded " + switches.Count + " switch and " + nevers.Count + " never rules from " + loadedPath);
			}
		}
		static void EnsureLoaded() {
			if (loadedPath != FilePath) Load();
		}
		public static bool Any {
			get { lock (locky) return switches.Count > 0 || nevers.Count > 0; }
		}
		public static bool IsNever(string typed) {
			lock (locky) return nevers.Contains(typed);
		}
		public static bool TryGetSwitch(string typed, out string correct) {
			lock (locky) return switches.TryGetValue(typed, out correct);
		}
		/// <summary>Adds "typed => correct". Returns false if the rule already existed.</summary>
		public static bool AddSwitch(string typed, string correct) {
			lock (locky) {
				EnsureLoaded();
				string old;
				if (switches.TryGetValue(typed, out old) && old == correct) return false;
				switches[typed] = correct;
				nevers.Remove(typed);
				Save();
				return true;
			}
		}
		/// <summary>Adds "!typed". Returns false if the rule already existed.</summary>
		public static bool AddNever(string typed) {
			lock (locky) {
				EnsureLoaded();
				if (nevers.Contains(typed)) return false;
				nevers.Add(typed);
				switches.Remove(typed);
				Save();
				return true;
			}
		}
		/// <summary>Removes any rule for the typed word.</summary>
		public static void Remove(string typed) {
			lock (locky) {
				EnsureLoaded();
				if (switches.Remove(typed) | nevers.Remove(typed)) Save();
			}
		}
		static void Save() {
			var lines = new List<string>(Header);
			lines.AddRange(switches.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + Arrow + p.Value));
			lines.AddRange(nevers.OrderBy(w => w, StringComparer.Ordinal).Select(w => NeverMark + w));
			try {
				File.WriteAllLines(loadedPath, lines, Encoding.UTF8);
			} catch (Exception e) {
				Logging.Log("[ASU] > Failed to save " + loadedPath + ": " + e.Message, 1);
			}
		}
	}
}
