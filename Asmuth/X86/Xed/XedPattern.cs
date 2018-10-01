﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Asmuth.X86.Xed
{
	public readonly struct XedPatternCase
	{
		public ImmutableArray<XedBlot> Conditions { get; }
		public ImmutableArray<XedBlot> Actions { get; }
		public bool Reset { get; }

		public XedPatternCase(ImmutableArray<XedBlot> conditions,
			ImmutableArray<XedBlot> actions, bool reset)
		{
			this.Conditions = conditions;
			this.Actions = actions;
			this.Reset = reset;
		}

		public XedAssignmentBlot? OutRegBlot
		{
			get
			{
				foreach (var blot in Actions)
				{
					if (blot.Type != XedBlotType.Assignment) continue;
					var assignment = blot.Assignment;
					if (assignment.Field != "OUTREG") continue;
					return assignment;
				}
				return null;
			}
		}
	}

	// XED patterns aka rules
	public sealed class XedPattern : XedSymbol
	{
		public bool ReturnsRegister { get; }
		public ImmutableArray<XedPatternCase> Cases { get; }

		public XedPattern(string name, bool returnsRegister, ImmutableArray<XedPatternCase> rules)
			: base(name)
		{
			this.ReturnsRegister = returnsRegister;
			this.Cases = rules;

			foreach (var @case in rules)
			{
				var outRegBlot = @case.OutRegBlot;
				if (outRegBlot.HasValue != ReturnsRegister) throw new ArgumentException();
					
			}
		}
	}

}