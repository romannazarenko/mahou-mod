// Trains the trigram models from Mahou's own word lists, validates detection
// quality on held-out words, and writes the runtime model file (ngram.bin).
//
// Run in CI before the app build:
//   dotnet run --project Tools/ModelGen -- Dictionaries-origin Mahou/ngram.bin
//
// Exits non-zero if detection rate or false-switch rate leaves the safe band,
// so a regression in the models fails the build.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Mahou;

namespace Mahou.Tools {
	static class Program {
		// Primary LCID (lcid & 0x3FF) -> source word list. Matches NgramScorer.LangOf.
		static readonly (int lang, string file, string keyRow)[] Langs = {
			(0x09, "!English-big.txt",  EnRow), // English  (en, 0x0409)
			(0x19, "!Russian-big.txt",  RuRow), // Russian  (ru, 0x0419)
			(0x22, "!Ukrainian.txt",    RuRow), // Ukrainian(uk, 0x0422), Cyrillic ЙЦУКЕН
		};

		// Physical key rows used to simulate "typed in the wrong layout".
		const string EnRow = "qwertyuiop[]asdfghjkl;'zxcvbnm,.`";
		const string RuRow = "йцукенгшщзхъфывапролджэячсмитьбюё";

		static readonly Regex WordRx = new Regex(@"====>(.+?)<====", RegexOptions.Compiled);

		static int Main(string[] args) {
			if (args.Length < 2) {
				Console.Error.WriteLine("usage: ModelGen <dictionaries-dir> <out.bin>");
				return 2;
			}
			string dir = args[0], outPath = args[1];

			var words = new Dictionary<int, List<string>>();
			var models = new List<NgramModel>();
			foreach (var (lang, file, _) in Langs) {
				var w = LoadWords(Path.Combine(dir, file));
				if (w.Count == 0) { Console.Error.WriteLine($"no words in {file}"); return 2; }
				words[lang] = w;
				Console.WriteLine($"lang 0x{lang:X2}: {w.Count} words from {file}");
			}

			// Hold out 5000 words per language for validation, train on the rest.
			var rng = new Random(42);
			int ok = 0, checks = 0;
			foreach (var (lang, _, _) in Langs) {
				var shuffled = words[lang].OrderBy(_ => rng.Next()).ToList();
				words[lang] = shuffled;
			}
			var train = new Dictionary<int, List<string>>();
			var test = new Dictionary<int, List<string>>();
			foreach (var (lang, _, _) in Langs) {
				int hold = Math.Min(5000, words[lang].Count / 5);
				test[lang] = words[lang].Take(hold).ToList();
				train[lang] = words[lang].Skip(hold).ToList();
				models.Add(NgramModel.Train(lang, train[lang]));
			}
			NgramScorer.Load(models);

			// Validate every ordered language pair that shares a key row (same script
			// pair, e.g. EN<->RU). Ukrainian and Russian share ЙЦУКЕН so we only test
			// the Latin<->Cyrillic crossings, which is what auto-switch actually faces.
			bool fail = false;
			foreach (var (a, fa, rowA) in Langs)
			foreach (var (b, fb, rowB) in Langs) {
				if (a == b || rowA == rowB) continue; // only cross-script pairs
				var (det, fp) = Evaluate(a, rowA, b, rowB, test);
				checks++;
				string tag = $"detect 0x{a:X2}-in-0x{b:X2}: {det:P2}  false-switch of real 0x{b:X2}: {fp:P3}";
				bool good = det > 0.95 && fp < 0.01;
				Console.WriteLine((good ? "  OK  " : " FAIL ") + tag);
				if (!good) fail = true; else ok++;
			}

			NgramScorer.Save(outPath);
			var size = new FileInfo(outPath).Length;
			Console.WriteLine($"wrote {outPath} ({size / 1024} KB), {ok}/{checks} pair checks passed");
			return fail ? 1 : 0;
		}

		// Words of language `srcLang` typed on `srcRow` while `dstLang` layout is
		// active look like `remap(word, srcRow->dstRow)`. We want a switch. Genuine
		// `dstLang` words must NOT switch.
		static (double det, double fp) Evaluate(int srcLang, string srcRow,
		                                         int dstLang, string dstRow,
		                                         Dictionary<int, List<string>> test) {
			var toDst = Remap(srcRow, dstRow);
			var toSrc = Remap(dstRow, srcRow);
			int detHit = 0, detTot = 0, fpHit = 0, fpTot = 0;
			foreach (var w in test[srcLang]) {
				var typedWrong = Apply(w, toDst);          // src word shown in dst layout
				var corrected = Apply(typedWrong, toSrc);  // == w, the reading in src layout
				detTot++;
				if (NgramScorer.ShouldSwitch(typedWrong, corrected, dstLang, srcLang)) detHit++;
			}
			foreach (var w in test[dstLang]) {
				var corrected = Apply(w, toSrc);           // genuine dst word read as src
				fpTot++;
				if (NgramScorer.ShouldSwitch(w, corrected, dstLang, srcLang)) fpHit++;
			}
			return ((double)detHit / detTot, (double)fpHit / fpTot);
		}

		static Dictionary<char, char> Remap(string from, string to) {
			var d = new Dictionary<char, char>();
			for (int i = 0; i < from.Length && i < to.Length; i++) d[from[i]] = to[i];
			return d;
		}

		static string Apply(string w, Dictionary<char, char> map) {
			var sb = new StringBuilder(w.Length);
			foreach (var c in w) sb.Append(map.TryGetValue(c, out var m) ? m : c);
			return sb.ToString();
		}

		static List<string> LoadWords(string path) {
			var list = new List<string>();
			if (!File.Exists(path)) return list;
			foreach (var line in File.ReadLines(path, Encoding.UTF8)) {
				var m = WordRx.Match(line);
				if (!m.Success) continue;
				var w = m.Groups[1].Value.Trim().ToLowerInvariant();
				if (w.Length < 2) continue;
				if (w.All(c => char.IsLetter(c) || c == '-' || c == '\'')) list.Add(w);
			}
			return list;
		}
	}
}
