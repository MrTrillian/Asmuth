﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Asmuth.X86.Encoding.Nasm
{
	/// <summary>
	/// Provides methods for manipulating instruction definitions in NASM's insns.dat format.
	/// </summary>
	public static class NasmInsns
	{
		private static readonly Regex instructionLineColumnRegex = new Regex(
			@"(\[[^\]]*\]|\S.*?)(?=(\s|\Z))", RegexOptions.CultureInvariant);

		private static readonly Regex codeStringColumnRegex = new Regex(
			@"\A\[
				(
					(?<operand_fields>[a-z-+]+):
					((?<evex_tuple_type>[a-z0-9]+):)?
				)?
				\s*
				(?<encoding>[^\]\r\n\t]+?)
			\s*\]\Z", RegexOptions.IgnorePatternWhitespace | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant);

		public static ICollection<string> PseudoInstructionMnemonics = new[]
		{
			"DB", "DW", "DD", "DQ", "DT", "DO", "DY", "DZ",
			"RESB", "RESW", "RESD", "RESQ", "REST", "RESO", "RESY", "RESZ",
		};

		public static IEnumerable<NasmInsnsEntry> Read(TextReader textReader)
		{
			if (textReader == null) throw new ArgumentNullException(nameof(textReader));

			while (true)
			{
				var line = textReader.ReadLine();
				if (line == null) break;

				if (IsIgnoredLine(line)) continue;
				yield return ParseLine(line);
			}
		}

		public static OperandField? ParseOperandField(char c)
		{
			switch (c)
			{
				case '-': return null;
				case 'r': return OperandField.ModReg;
				case 'm': return OperandField.BaseReg;
				case 'x': return OperandField.IndexReg;
				case 'i': return OperandField.Immediate;
				case 'j': return OperandField.SecondImmediate;
				case 'v': return OperandField.NonDestructiveReg;
				case 's': return OperandField.IS4;
				default: throw new ArgumentOutOfRangeException(nameof(c));
			}
		}

		public static bool IsIgnoredLine(string line)
		{
			if (line == null) throw new ArgumentNullException(nameof(line));
			// Blank or with comment
			return Regex.IsMatch(line, @"\A\s*(;.*)?\Z", RegexOptions.CultureInvariant);
		}

		public static NasmInsnsEntry ParseLine(string line)
		{
			if (line == null) throw new ArgumentNullException(nameof(line));

			var columnMatches = instructionLineColumnRegex.Matches(line);
			if (columnMatches.Count != 4) throw new FormatException();
			
			var entryData = new NasmInsnsEntry.Data();

			// Mnemonic
			var mnemonicColumn = columnMatches[0].Value;
			if (!Regex.IsMatch(mnemonicColumn, @"\A[A-Z_0-9]+(cc)?\Z", RegexOptions.CultureInvariant))
				throw new FormatException("Invalid mnemonic column format.");
			entryData.Mnemonic = mnemonicColumn;

			// Encoding
			var codeStringColumn = columnMatches[2].Value;
			var operandFieldsString = string.Empty;
			if (codeStringColumn != "ignore")
			{
				var codeStringMatch = codeStringColumnRegex.Match(codeStringColumn);
				if (!codeStringMatch.Success) throw new FormatException("Invalid code string column format.");

				operandFieldsString = codeStringMatch.Groups["operand_fields"].Value;
				var evexTupleTypesString = codeStringMatch.Groups["evex_tuple_type"].Value;
				var encodingString = codeStringMatch.Groups["encoding"].Value;

				entryData.EncodingTokens = ParseEncoding(encodingString, out entryData.VexEncoding);

				if (evexTupleTypesString.Length > 0)
				{
					entryData.EVexTupleType = (NasmEVexTupleType)Enum.Parse(
						typeof(NasmEVexTupleType), evexTupleTypesString, ignoreCase: true);
				}
			}

			// Operands
			var operandsColumn = columnMatches[1].Value;
			entryData.Operands = ParseOperands(operandsColumn, operandFieldsString);

			// Flags
			var flagsColumn = columnMatches[3].Value;
			entryData.Flags = ParseFlags(flagsColumn);

			return new NasmInsnsEntry(in entryData);
		}

		public static IReadOnlyList<NasmEncodingToken> ParseEncoding(string str, out VexEncoding? vex)
		{
			// In most of the cases 5 tokens should be enough.
			var tokens = new List<NasmEncodingToken>(5);
			vex = null;
			
			foreach (string tokenStr in Regex.Split(str, @"\s+"))
			{
				var tokenType = NasmEncodingToken.TryParseType(tokenStr);
				if (tokenType != NasmEncodingTokenType.None)
				{
					tokens.Add(tokenType);
					continue;
				}

				byte @byte;
				if (Regex.IsMatch(tokenStr, @"\A[0-9a-f]{2}(\+[rc])?\Z")
					&& byte.TryParse(tokenStr.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out @byte))
				{
					var type = NasmEncodingTokenType.Byte;
					if (tokenStr.Length == 4)
					{
						type = tokenStr[tokenStr.Length - 1] == 'r'
							? NasmEncodingTokenType.Byte_PlusRegister
							: NasmEncodingTokenType.Byte_PlusConditionCode;
					}
					tokens.Add(new NasmEncodingToken(type, @byte));
					continue;
				}

				if (Regex.IsMatch(tokenStr, @"\A/[0-7]\Z"))
				{
					tokens.Add(new NasmEncodingToken(NasmEncodingTokenType.ModRM_FixedReg,
						(byte)(tokenStr[1] - '0')));
					continue;
				}

				if (Regex.IsMatch(tokenStr, @"\A(vex|xop|evex)\."))
				{
					if (vex.HasValue) throw new FormatException("Multiple VEX prefixes.");
					tokens.Add(NasmEncodingTokenType.Vex);
					vex = VexEncoding.Parse(tokenStr);
					continue;
				}

				throw new FormatException("Unexpected NASM encoding token '{0}'".FormatInvariant(tokenStr));
			}

			return tokens;
		}

		private static IReadOnlyList<NasmOperand> ParseOperands(string operandsString, string fieldsString)
		{
			var operands = new List<NasmOperand>();

			if (operandsString == "void" || operandsString == "ignore")
			{
				Debug.Assert(fieldsString.Length == 0);
				return operands;
			}

			if (fieldsString.Length == 0)
			{
				// This only happens for pseudo-instructions
				return operands;
			}
			
			// ':' delimits operands like ',' but indicates that the syntax uses the ':' delimiter,
			// for example, CALL 42:666
			var operandStrings = Regex.Split(operandsString, "[,:]");
			var operandSeparators = Regex.Replace(operandsString, "[^,:]", string.Empty);
			Debug.Assert(operandSeparators.Length == operandStrings.Length - 1);
			
			if (fieldsString == "r+mi")
			{
				// Hack around the IMUL special case
				// IMUL reg32,imm8 [r+mi: o32 6b /r ib,s] 386
				fieldsString = "rmi";
				operandStrings = new[] { operandStrings[0], operandStrings[0].Replace("reg", "rm"), operandStrings[1] };
				operandSeparators = ',' + operandSeparators;
			}

			if (operandStrings.Length != fieldsString.Length)
				throw new FormatException("Not all operands have associated opcode fields.");

			for (int i = 0; i < operandStrings.Length; ++i)
			{
				var field = ParseOperandField(fieldsString[i]);

				var flags = NasmOperandFlags.None;

				// Handle trailing *
				var operandString = operandStrings[i];
				if (operandString.EndsWith("*"))
				{
					flags |= NasmOperandFlags.Relaxed;
					operandString = operandString.Substring(0, operandString.Length - 1);
				}

				var operandParts = operandString.Split('|');
				var type = (NasmOperandType)Enum.Parse(typeof(NasmOperandType), operandParts[0], ignoreCase: true);

				for (int j = 1; j < operandParts.Length; ++j)
				{
					var flagStr = operandParts[j];
					var flag = NasmEnumNameAttribute.GetEnumerantOrNull<NasmOperandFlags>(flagStr);
					if (!flag.HasValue) throw new FormatException($"Unsupported operand flag: {flagStr}.");
					flags |= flag.Value;
				}

				// Take into account ':' separators.
				if (i < operandStrings.Length - 1 && operandSeparators[i] == ':')
					flags |= NasmOperandFlags.Colon;

				operands.Add(new NasmOperand(field, type, flags));
			}

			return operands;
		}

		private static IReadOnlyCollection<string> ParseFlags(string str)
		{
			var flags = new List<string>();

			if (str != "ignore")
			{
				foreach (var flagStr in str.Split(','))
					flags.Add(flagStr);
			}

			return flags;
		}
    }
}
