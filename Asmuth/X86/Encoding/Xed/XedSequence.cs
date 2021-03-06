﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Asmuth.X86.Encoding.Xed
{
	public sealed class XedSequence : XedSymbol
	{
		public ImmutableArray<XedSequenceEntry> Entries { get; }

		public XedSequence(string name, ImmutableArray<XedSequenceEntry> entries)
			: base(name)
		{
			this.Entries = entries;
		}

		public override string ToString() => "SEQUENCE " + Name;
	}

	public enum XedSequenceEntryType : byte
	{
		Sequence,
		Pattern
	}

	public readonly struct XedSequenceEntry
	{
		public string TargetName { get; }
		public XedSequenceEntryType Type { get; }

		public XedSequenceEntry(string targetName, XedSequenceEntryType type)
		{
			this.TargetName = targetName;
			this.Type = type;
		}

		public override string ToString() => TargetName;
	}
}
