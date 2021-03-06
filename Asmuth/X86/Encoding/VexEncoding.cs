﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Asmuth.X86.Encoding
{
	public enum VexRegOperand : byte
	{
		Invalid,
		Source,
		SecondSource,
		Dest,
	}

	/// <summary>
	/// Defines the vex-based encoding of an opcode,
	/// using the syntax from intel's ISA manuals.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Size = 2)]
	public readonly partial struct VexEncoding : IEquatable<VexEncoding>
	{
		public struct Builder
		{
			public VexType Type;
			public VexRegOperand RegOperand;
			public AvxVectorSize? VectorSize;
			public SimdPrefix SimdPrefix;
			public OpcodeMap OpcodeMap;
			public bool? OperandSizePromotion;

			public void Validate()
			{
				if (Type == VexType.None)
					throw new ArgumentException("VEX instructions must have a VEX type.", nameof(Type));
				if (OpcodeMap == OpcodeMap.Default)
					throw new ArgumentException("VEX instructions cannot encode the default opcode map.", nameof(OpcodeMap));
			}

			public VexEncoding Build() => new VexEncoding(ref this);
		}

		private const int TypeShift = 0;
		private const int RegOperandShift = TypeShift + 2;
		private const int VectorSizeShift = RegOperandShift + 2;
		private const int SimdPrefixShift = VectorSizeShift + 2;
		private const int OpcodeMapShift = SimdPrefixShift + 2;
		private const int OperandSizePromotionShift = OpcodeMapShift + 4;
		private const ushort TestOverflow = 3 << OperandSizePromotionShift;

		private readonly ushort data;

		private VexEncoding(ref Builder data)
		{
			data.Validate();

			this.data = (ushort)(
				((data.Type - VexType.Vex) << TypeShift)
				| ((int)data.RegOperand << RegOperandShift)
				| ((data.VectorSize.HasValue ? (int)data.VectorSize.Value + 1 : 0) << VectorSizeShift)
				| ((int)data.SimdPrefix << SimdPrefixShift)
				| ((int)data.OpcodeMap << OpcodeMapShift)
				| ((data.OperandSizePromotion.HasValue ? (data.OperandSizePromotion.Value ? 2 : 1) : 0) << OperandSizePromotionShift));
		}

		public VexType Type => (VexType)((int)VexType.Vex + ((data >> TypeShift) & 3));
		public VexRegOperand RegOperand => (VexRegOperand)((data >> RegOperandShift) & 3);
		public AvxVectorSize? VectorSize => (AvxVectorSize?)AsZeroIsNullInt((data >> VectorSizeShift) & 3);
		public SimdPrefix SimdPrefix => (SimdPrefix)((data >> SimdPrefixShift) & 3);
		public OpcodeMap OpcodeMap => (OpcodeMap)((data >> OpcodeMapShift) & 0xF);
		public bool? OperandSizePromotion => AsZeroIsNullBool((data >> OperandSizePromotionShift) & 3);

		public string ToIntelStyleString()
		{
			// Encoded length = 12-24:
			// VEX.L0.0F 42
			// EVEX.NDS.512.F3.0F3A.WIG
			var str = new StringBuilder(24);

			switch (Type)
			{
				case VexType.Vex: str.Append("vex"); break;
				case VexType.Xop: str.Append("xop"); break;
				case VexType.EVex: str.Append("evex"); break;
				default: throw new ArgumentException();
			}

			switch (RegOperand)
			{
				case VexRegOperand.Invalid: break;
				case VexRegOperand.Source: str.Append(".nds"); break;
				case VexRegOperand.Dest: str.Append(".ndd"); break;
				case VexRegOperand.SecondSource: str.Append(".dds"); break;
				default: throw new UnreachableException();
			}

			bool isEVex = Type == VexType.EVex;
			switch (VectorSize)
			{
				case null: str.Append(".lig"); break;
				case AvxVectorSize._128: str.Append(isEVex ? ".128" : ".l0"); break;
				case AvxVectorSize._256: str.Append(isEVex ? ".256" : ".l1"); break;
				case AvxVectorSize._512:
					if (!isEVex) throw new ArgumentException();
					str.Append(".512");
					break;
				default: throw new UnreachableException();
			}

			switch (SimdPrefix)
			{
				case SimdPrefix.None: break;
				case SimdPrefix._66: str.Append(".66"); break;
				case SimdPrefix._F3: str.Append(".f3"); break;
				case SimdPrefix._F2: str.Append(".f2"); break;
				default: throw new UnreachableException();
			}

			switch (OpcodeMap)
			{
				case OpcodeMap.Escape0F: str.Append(".0f"); break;
				case OpcodeMap.Escape0F38: str.Append(".0f38"); break;
				case OpcodeMap.Escape0F3A: str.Append(".0f3a"); break;
				case OpcodeMap.Xop8: str.Append(".m8"); break;
				case OpcodeMap.Xop9: str.Append(".m9"); break;
				case OpcodeMap.Xop10: str.Append(".m10"); break;
				default: throw new ArgumentException();
			}

			str.Append(OperandSizePromotion.HasValue ? (OperandSizePromotion.Value ? ".w1" : ".w0") : ".wig");

			return str.ToString();
		}

		public bool Equals(VexEncoding other) => data == other.data;
		public override bool Equals(object obj) => obj is VexEncoding && Equals((VexEncoding)obj);
		public override int GetHashCode() => data;
		public override string ToString() => ToIntelStyleString();

		public static bool Equals(VexEncoding lhs, VexEncoding rhs) => lhs.Equals(rhs);
		public static bool operator ==(VexEncoding lhs, VexEncoding rhs) => Equals(lhs, rhs);
		public static bool operator !=(VexEncoding lhs, VexEncoding rhs) => !Equals(lhs, rhs);

		public static VexEncoding FromVex(Vex3Xop vex)
		{
			return new Builder
			{
				Type = vex.VexType,
				VectorSize = vex.VectorSize,
				SimdPrefix = vex.SimdPrefix,
				OpcodeMap = vex.OpcodeMap,
				OperandSizePromotion = vex.OperandSizePromotion
			}.Build();
		}

		private static int? AsZeroIsNullInt(int value) => value == 0 ? null : (int?)(value - 1);
		private static bool? AsZeroIsNullBool(int value) => value == 0 ? null : (bool?)(value != 1);
	}
}
