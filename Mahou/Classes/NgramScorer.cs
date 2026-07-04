// Phonetic/statistical layout detector for Mahou auto-switch.
//
// Replaces the full-word AS_dict lookup with per-language character trigram
// models. Instead of asking "is this exact word in the dictionary?", it asks
// "do these keystrokes read like a real word in the other layout's language?".
// This generalises to inflected forms and neologisms the word list never had.
//
// Pure C# (no WinForms/WinAPI) so it links into a plain test project unchanged.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Mahou {
	/// <summary>Trigram language model for one language.</summary>
	public class NgramModel {
		public int LangId;                       // primary LCID (lcid & 0x3FF)
		public string Alphabet;                  // index 0 is the '^' start/boundary marker
		int[] _idx = new int[char.MaxValue + 1]; // char -> alphabet index, -1 if absent
		int _v;                                  // alphabet size (smoothing vocabulary)
		Dictionary<int, float> _tri;             // packed(a,b,c) -> log P(c | a,b)
		Dictionary<int, float> _bi;              // packed(a,b)   -> log P(unseen c | a,b) backoff
		float _floor;                            // log P for a context never seen at all

		const char Boundary = '^';
		const float OutOfAlphabet = -16.1181f;   // ~log(1e-7): char can't be typed in this layout

		NgramModel() { }

		void BuildIndex() {
			for (int i = 0; i < _idx.Length; i++) _idx[i] = -1;
			for (int i = 0; i < Alphabet.Length; i++) _idx[Alphabet[i]] = i;
			_v = Alphabet.Length;
			_floor = (float)Math.Log(1.0 / _v);
		}

		int Ix(char c) { return c <= char.MaxValue ? _idx[c] : -1; }
		int Pack2(int a, int b) { return a * _v + b; }
		int Pack3(int a, int b, int c) { return (a * _v + b) * _v + c; }

		/// <summary>Average log-probability per character. Higher = more word-like.</summary>
		public float Score(string word) {
			if (string.IsNullOrEmpty(word)) return OutOfAlphabet;
			int a = 0, b = 0; // both start as boundary '^' (index 0)
			double total = 0;
			int n = 0;
			for (int i = 0; i <= word.Length; i++) {
				char ch = i < word.Length ? char.ToLowerInvariant(word[i]) : Boundary;
				int c = Ix(ch);
				if (c < 0) {
					total += OutOfAlphabet;
				} else {
					float lp;
					if (_tri.TryGetValue(Pack3(a, b, c), out lp)) total += lp;
					else if (_bi.TryGetValue(Pack2(a, b), out lp)) total += lp;
					else total += _floor;
				}
				a = b;
				b = c < 0 ? 0 : c;
				n++;
			}
			return (float)(total / n);
		}

		// ---- training -------------------------------------------------------
		const double Smoothing = 0.1;

		/// <summary>Builds a model from a word list. Alphabet is derived from the data.</summary>
		public static NgramModel Train(int langId, IEnumerable<string> words) {
			var tri = new Dictionary<int, int>();
			var bi = new Dictionary<int, int>();
			var alpha = new SortedSet<char> { Boundary };
			var kept = new List<string>();
			foreach (var raw in words) {
				if (string.IsNullOrEmpty(raw)) continue;
				var w = raw.Trim().ToLowerInvariant();
				if (w.Length < 2) continue;
				kept.Add(w);
				foreach (var c in w) alpha.Add(c);
			}
			var alphabet = new string(new List<char>(alpha).ToArray());
			// stable char->index for counting
			var index = new Dictionary<char, int>();
			for (int i = 0; i < alphabet.Length; i++) index[alphabet[i]] = i;
			int v = alphabet.Length;
			Func<int, int, int> p2 = (x, y) => x * v + y;
			Func<int, int, int, int> p3 = (x, y, z) => (x * v + y) * v + z;

			foreach (var w in kept) {
				int a = 0, b = 0;
				for (int i = 0; i <= w.Length; i++) {
					char ch = i < w.Length ? w[i] : Boundary;
					int c = index[ch];
					Inc(tri, p3(a, b, c));
					Inc(bi, p2(a, b));
					a = b; b = c;
				}
			}

			// Precompute log-probabilities so the runtime does no division.
			var triLog = new Dictionary<int, float>(tri.Count);
			foreach (var kv in tri) {
				int ctx = kv.Key / v;                 // strip the third char -> packed(a,b)
				int biCount = bi.TryGetValue(ctx, out int bc) ? bc : 0;
				triLog[kv.Key] = (float)Math.Log((kv.Value + Smoothing) / (biCount + Smoothing * v));
			}
			var biBack = new Dictionary<int, float>(bi.Count);
			foreach (var kv in bi)
				biBack[kv.Key] = (float)Math.Log(Smoothing / (kv.Value + Smoothing * v));

			var m = new NgramModel {
				LangId = langId, Alphabet = alphabet, _tri = triLog, _bi = biBack
			};
			m.BuildIndex();
			return m;
		}

		static void Inc(Dictionary<int, int> d, int k) { d.TryGetValue(k, out int c); d[k] = c + 1; }

		// ---- serialization --------------------------------------------------
		internal void Write(BinaryWriter w) {
			w.Write(LangId);
			w.Write(Alphabet);
			w.Write(_tri.Count);
			foreach (var kv in _tri) { w.Write(kv.Key); w.Write(kv.Value); }
			w.Write(_bi.Count);
			foreach (var kv in _bi) { w.Write(kv.Key); w.Write(kv.Value); }
		}

		internal static NgramModel Read(BinaryReader r) {
			var m = new NgramModel { LangId = r.ReadInt32(), Alphabet = r.ReadString() };
			int tn = r.ReadInt32();
			m._tri = new Dictionary<int, float>(tn);
			for (int i = 0; i < tn; i++) { int k = r.ReadInt32(); m._tri[k] = r.ReadSingle(); }
			int bn = r.ReadInt32();
			m._bi = new Dictionary<int, float>(bn);
			for (int i = 0; i < bn; i++) { int k = r.ReadInt32(); m._bi[k] = r.ReadSingle(); }
			m.BuildIndex();
			return m;
		}
	}

	/// <summary>
	/// Holds all language models and answers the auto-switch question.
	/// </summary>
	public static class NgramScorer {
		const uint Magic = 0x31474E4D; // "MNG1"
		static Dictionary<int, NgramModel> _models = new Dictionary<int, NgramModel>();

		/// <summary>Log-likelihood advantage the corrected reading must have to trigger a switch.</summary>
		public static float Threshold = 1.0f;
		/// <summary>Words shorter than this are left to the dictionary (statistics are unreliable).</summary>
		public static int MinLength = 3;

		public static bool Ready { get { return _models.Count > 0; } }
		public static bool Has(int langId) { return _models.ContainsKey(langId); }

		/// <summary>Primary language id used as the model key: LCID low 10 bits.</summary>
		public static int LangOf(uint layoutId) { return (int)(layoutId & 0xFFFF) & 0x3FF; }

		public static void Load(IEnumerable<NgramModel> models) {
			var d = new Dictionary<int, NgramModel>();
			foreach (var m in models) d[m.LangId] = m;
			_models = d;
		}

		/// <summary>
		/// Decides whether the keystrokes (<paramref name="typed"/> as read in the source
		/// language, <paramref name="corrected"/> as read in the destination language)
		/// should be auto-switched to the destination layout.
		/// </summary>
		public static bool ShouldSwitch(string typed, string corrected, int srcLang, int dstLang) {
			if (typed == null || corrected == null) return false;
			if (typed.Length < MinLength) return false;
			NgramModel src, dst;
			if (!_models.TryGetValue(srcLang, out src)) return false;
			if (!_models.TryGetValue(dstLang, out dst)) return false;
			return dst.Score(corrected) - src.Score(typed) > Threshold;
		}

		// ---- persistence ----------------------------------------------------
		public static void Save(string path) {
			using (var w = new BinaryWriter(File.Create(path), Encoding.UTF8)) {
				w.Write(Magic);
				w.Write(_models.Count);
				foreach (var m in _models.Values) m.Write(w);
			}
		}

		public static bool TryLoadFile(string path) {
			if (!File.Exists(path)) return false;
			using (var r = new BinaryReader(File.OpenRead(path), Encoding.UTF8)) {
				if (r.ReadUInt32() != Magic) return false;
				int n = r.ReadInt32();
				var list = new List<NgramModel>(n);
				for (int i = 0; i < n; i++) list.Add(NgramModel.Read(r));
				Load(list);
			}
			return true;
		}
	}
}
