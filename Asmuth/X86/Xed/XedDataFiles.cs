﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Asmuth.X86.Xed
{
	public static class XedDataFiles
	{
		private static readonly Regex commentRegex = new Regex(@"\s*#.*$");
		private static readonly Regex xtypesLineRegex = new Regex(@"^\s*(\w+)\s+(\w+)\s+(\d+)\s*$");
		private static readonly Regex widthsLineRegex = new Regex(
			@"^\s*(\w+)\s+(\w+)\s+
			(?<size16>\d+)(?<bits16>bits)?
			(
				\s+(?<size32>\d+)(?<bits32>bits)?
				\s+(?<size64>\d+)(?<bits64>bits)?
			)?
			\s*$", RegexOptions.IgnorePatternWhitespace);
		private static readonly Regex stateMacroLineRegex = new Regex(@"^\s*(\w+)\s+(.+?)\s*$");

		public static IReadOnlyDictionary<string, XedXType> ParseXTypes(TextReader reader)
		{
			var xtypes = new Dictionary<string, XedXType>();
			foreach (var lineMatch in ParseLineBased(reader, xtypesLineRegex))
			{
				if (!Enum.TryParse<XedType>(lineMatch.Groups[2].Value, ignoreCase: true, out var type))
					throw new FormatException();
				if (!ushort.TryParse(lineMatch.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var bitsPerElement))
					throw new FormatException();
				xtypes.Add(lineMatch.Groups[1].Value, new XedXType(type, bitsPerElement));
			}
			return xtypes;
		}

		public static IReadOnlyDictionary<string, XedOperandWidth> ParseOperandWidths(
			TextReader reader, Func<string, XedXType> xtypeLookup)
		{
			var widths = new Dictionary<string, XedOperandWidth>();
			foreach (var lineMatch in ParseLineBased(reader, widthsLineRegex))
			{
				var xtype = xtypeLookup(lineMatch.Groups[2].Value);

				if (!ushort.TryParse(lineMatch.Groups["size16"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var width16))
					throw new FormatException();
				if (!lineMatch.Groups["bits16"].Success) width16 *= 8;

				XedOperandWidth width;
				if (lineMatch.Groups["size32"].Success)
				{
					if (!ushort.TryParse(lineMatch.Groups["size32"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var width32))
						throw new FormatException();
					if (!lineMatch.Groups["bits32"].Success) width32 *= 8;

					if (!ushort.TryParse(lineMatch.Groups["size64"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var width64))
						throw new FormatException();
					if (!lineMatch.Groups["bits64"].Success) width64 *= 8;

					width = new XedOperandWidth(xtype, width16, width32, width64);
				}
				else
				{
					width = new XedOperandWidth(xtype, width16);
				}

				widths.Add(lineMatch.Groups[1].Value, width);
			}
			return widths;
		}

		public static IReadOnlyDictionary<string, string> ParseStateMacros(TextReader reader)
		{
			var xtypes = new Dictionary<string, string>();
			foreach (var lineMatch in ParseLineBased(reader, stateMacroLineRegex))
				xtypes.Add(lineMatch.Groups[1].Value, lineMatch.Groups[2].Value);
			return xtypes;
		}

		private static IEnumerable<Match> ParseLineBased(TextReader reader, Regex lineRegex)
		{
			while (true)
			{
				var line = reader.ReadLine();
				if (line == null) yield break;

				line = commentRegex.Replace(line, string.Empty);
				if (line.Length == 0) continue;

				var match = lineRegex.Match(line);
				if (!match.Success) throw new FormatException("Badly formatted xed data file.");

				yield return match;
			}
		}
	}
}