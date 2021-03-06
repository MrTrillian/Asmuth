﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Asmuth.X86.Encoding.Xed
{
	public static class XedBitPattern
	{
		public readonly struct Span
		{
			private readonly byte @char;
			private readonly byte startIndex;
			private readonly byte length;

			public Span(char @char, int startIndex, int length)
			{
				this.@char = (byte)@char;
				this.startIndex = (byte)startIndex;
				this.length = (byte)length;
			}

			public char Char => (char)@char;
			public int StartIndex => startIndex;
			public int Length => length;
			public int EndIndex => startIndex + length;

			public bool IsConstant => Char == '0' || Char == '1';
			public bool IsVariable => !IsConstant;
		}

		public static XedBitsValue? TryAsConstant(string pattern)
		{
			ulong bits = 0;
			int length = 0;
			foreach (var c in pattern)
			{
				if (IsZeroOrOne(c))
				{
					bits <<= 1;
					bits |= (uint)(c - '0');
					length++;
				}
				else if (c == '_') continue;
				else return null;
			}

			return new XedBitsValue(bits, length);
		}

		public static bool IsConstant(string pattern) => TryAsConstant(pattern).HasValue;

		public static string Normalize(string pattern)
		{
			if (pattern == null) throw new ArgumentNullException(nameof(pattern));
			if (pattern.Length == 0) throw new ArgumentException();

			char[] normalizedChars = null;
			int normalizedLength = 0;
			for (int i = 0; i < pattern.Length; ++i)
			{
				char c = pattern[i];
				if (c == '_')
				{
					if (normalizedChars == null)
					{
						normalizedChars = new char[pattern.Length - 1];
						normalizedLength = i;
						pattern.CopyTo(0, normalizedChars, 0, normalizedLength);
					}
					continue;
				}

				if (normalizedChars != null)
				{
					normalizedChars[normalizedLength] = c;
					normalizedLength++;
				}

				if (c == '0' || c == '1') continue;
				if (c >= 'a' && c <= 'z')
				{
					if (i == 0 || pattern[i - 1] == c) continue;
					if (pattern.IndexOf(c) != i)
						throw new FormatException("Duplicate bit pattern variable.");
					continue;
				}

				throw new FormatException("Invalid bit pattern character.");
			}

			return normalizedChars == null ? pattern
				: new string(normalizedChars, 0, normalizedLength);
		}

		public static string Prettify(string pattern)
		{
			if (IsConstant(pattern))
			{
				if (pattern.Length == 8)
				{
					return "0x" + Convert.ToByte(pattern, fromBase: 2)
						.ToString("x2", CultureInfo.InvariantCulture);
				}

				return "0b" + pattern;
			}

			var str = new StringBuilder(pattern.Length + pattern.Length / 4); // Heuristic
			for (int i = 0; i < pattern.Length; ++i)
			{
				char c = pattern[i];
				if (i > 0 && (IsZeroOrOne(pattern[i - 1])
					? !IsZeroOrOne(c)
					: (IsZeroOrOne(c) || c != pattern[i - 1])))
					str.Append('_');
				str.Append(c);
			}

			return str.ToString();
		}

		private static bool IsZeroOrOne(char c) => c == '0' || c == '1';

		public static Span GetSpanAt(string pattern, int startIndex)
		{
			if (startIndex < 0 || startIndex >= pattern.Length)
				throw new ArgumentOutOfRangeException(nameof(startIndex));

			char c = pattern[startIndex];
			if ((c < 'a' || c > 'z') && c != '0' && c != '1')
				throw new FormatException();

			int length = 1;
			while (startIndex + length < pattern.Length && pattern[startIndex + length] == c)
				length++;

			return new Span(c, startIndex, length);
		}
		
		public static XedBitsValue Evaluate(string pattern, Func<char, XedBitsValue> variableResolver)
		{
			ulong bits = 0;
			int length = 0;

			int startIndex = 0;
			while (startIndex < pattern.Length)
			{
				if (pattern[startIndex] == '_') continue;
				var span = GetSpanAt(pattern, startIndex);
				
				if (IsZeroOrOne(span.Char))
				{
					bits <<= 1;
					bits |= (uint)(span.Char - '0');
					length++;
				}
				else
				{
					var variableValue = variableResolver(span.Char);
					if (variableValue.Length != span.Length)
						throw new InvalidOperationException("Mismatched bit variable size.");
					bits <<= variableValue.Length;
					bits |= variableValue.Mask;
					length += variableValue.Length;
				}
			}

			return new XedBitsValue(bits, length);
		}
	}
}
