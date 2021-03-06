﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Asmuth.X86.Encoding
{
	public enum OperandSizeEncoding : byte
	{
		Any, // 66, REX.W, VEX.W*, are all fair game
		Word, // No REX.W, 66 in 32-bit mode
		Dword, // No REX.W, 66 in 16-bit mode
		NoPromotion, // No REX.W / VEX.W0
		Promotion // REX.W / VEX.W1
	}

	public static class OperandSizeEncodingEnum
	{
		public static IntegerSize? AsIntegerSize(this OperandSizeEncoding value)
		{
			switch (value)
			{
				case OperandSizeEncoding.Any: return null;
				case OperandSizeEncoding.NoPromotion: return null;
				case OperandSizeEncoding.Word: return IntegerSize.Word;
				case OperandSizeEncoding.Dword: return IntegerSize.Dword;
				case OperandSizeEncoding.Promotion: return IntegerSize.Qword;
				default: throw new ArgumentOutOfRangeException(nameof(value));
			}
		}

		public static OperandSizeEncoding FromPromotion(bool? promotion)
		{
			if (!promotion.HasValue) return OperandSizeEncoding.Any;
			return promotion.Value ? OperandSizeEncoding.Promotion : OperandSizeEncoding.NoPromotion;
		}

		public static bool? GetPromotion(this OperandSizeEncoding value)
		{
			if (value == OperandSizeEncoding.Any) return null;
			if (value == OperandSizeEncoding.Promotion) return true;
			return false;
		}

		public static bool IsVexCompatible(this OperandSizeEncoding value)
			=> value != OperandSizeEncoding.Word && value != OperandSizeEncoding.Dword;
	}

	public readonly partial struct OpcodeEncoding
	{
		#region Builder Struct
		public struct Builder
		{
			public bool? X64;
			public AddressSize? AddressSize;
			public OperandSizeEncoding OperandSize;
			public VexType VexType;
			public AvxVectorSize? VectorSize;
			public SimdPrefix? SimdPrefix;
			public OpcodeMap Map;
			public byte MainByte;
			public AddressingFormEncoding AddressingForm;
			public ImmediateSizeEncoding ImmediateSize;
			public byte? Imm8Ext;

			public void Validate()
			{
				if (AddressSize == X86.AddressSize._64 && X64 != true)
					throw new ArgumentException("64-bit addresses imply X64 mode.");
				if (AddressSize == X86.AddressSize._16 && X64 != false)
					throw new ArgumentException("16-bit addresses imply IA32 mode.");
				if (OperandSize == OperandSizeEncoding.Promotion && VexType == VexType.None && X64 != true)
					throw new ArgumentException("REX.W implies X64.");
				if (!OperandSize.IsVexCompatible() && VexType != VexType.None)
					throw new ArgumentException("Word/dword operand size implies no VEX encoding.");
				if (VexType != VexType.None && !SimdPrefix.HasValue)
					throw new ArgumentException("Vex encoding implies SIMD prefixes.");
				if (VexType == VexType.None && VectorSize.HasValue)
					throw new ArgumentException("Escape-based non-legacy prefixes implies ignored VEX.L.");
				if (AddressingForm.IsMainByteEmbeddedRegister && MainOpcodeByte.GetEmbeddedReg(MainByte) != 0)
					throw new ArgumentException("Main byte-embedded reg implies multiple-of-8 main byte.");
				if (Imm8Ext.HasValue && ImmediateSize.FixedInBytes != 1)
					throw new ArgumentException("imm8 opcode extension implies 8-bit immediate.");
			}

			public OpcodeEncoding Build() => new OpcodeEncoding(ref this);

			public static implicit operator OpcodeEncoding(Builder builder) => builder.Build();
		}
		#endregion

		#region Packed Fields
		// Lead bitfield: everything up to and including the main byte
		private static readonly Bitfield32.Builder leadBitfieldBuilder = new Bitfield32.Builder();
		private readonly Bitfield32 leadBitfield;

		private static readonly Bitfield32.NullableBool x64Field = leadBitfieldBuilder;
		public bool? X64 => leadBitfield[x64Field];

		private static readonly Bitfield32.NullableUIntMaxValue2 addressSizeField = leadBitfieldBuilder;
		public AddressSize? AddressSize => (AddressSize?)leadBitfield[addressSizeField];
		
		private static readonly Bitfield32.UInt3 operandSizeField = leadBitfieldBuilder;
		public OperandSizeEncoding OperandSize => (OperandSizeEncoding)leadBitfield[operandSizeField];

		private static readonly Bitfield32.UInt2 vexTypeField = leadBitfieldBuilder;
		public VexType VexType => (VexType)leadBitfield[vexTypeField];

		private static readonly Bitfield32.NullableUIntMaxValue2 vectorSizeField = leadBitfieldBuilder;
		public AvxVectorSize? VectorSize => (AvxVectorSize?)leadBitfield[vectorSizeField];

		private static readonly Bitfield32.NullableUIntMaxValue6 simdPrefixField = leadBitfieldBuilder;
		public SimdPrefix? SimdPrefix => (SimdPrefix?)leadBitfield[simdPrefixField];

		private static readonly Bitfield32.UInt4 mapField = leadBitfieldBuilder;
		public OpcodeMap Map => (OpcodeMap)leadBitfield[mapField];

		private static readonly Bitfield32.Byte mainByteField = leadBitfieldBuilder;
		public byte MainByte => leadBitfield[mainByteField];
		
		public AddressingFormEncoding AddressingForm { get; }

		// 0bABBBCCCC: HasImm8Ext, Nullable<VariableSize>, BaseSizeInBytes
		private readonly byte immFields;
		private readonly byte imm8Ext;
		private ImmediateVariableSize? ImmediateVariableSize => (immFields & 0b111_0000) > 0
			? (ImmediateVariableSize?)(((immFields >> 4) & 0b111) - 1) : null;
		public ImmediateSizeEncoding ImmediateSize => ImmediateSizeEncoding.FromBytes(
			@base: immFields & 0xF, variable: ImmediateVariableSize);
		public byte? Imm8Ext => immFields == 0b1000_0001 ? (byte?)imm8Ext : null;
		private static byte MakeImmFields(ImmediateSizeEncoding size, byte? imm8Ext)
			=> (byte)((imm8Ext.HasValue ? 0x80 : 0)
				| (size.IsFixed ? 0 : (((int)size.Variable + 1) << 4))
				| size.BaseInBytes);
		#endregion

		public OpcodeEncoding(ref Builder builder)
		{
			builder.Validate();
			leadBitfield = new Bitfield32();
			leadBitfield[x64Field] = builder.X64;
			leadBitfield[addressSizeField] = (byte?)builder.AddressSize;
			leadBitfield[operandSizeField] = (byte)builder.OperandSize;
			leadBitfield[vexTypeField] = (byte)builder.VexType;
			leadBitfield[vectorSizeField] = (byte?)builder.VectorSize;
			leadBitfield[simdPrefixField] = (byte?)builder.SimdPrefix;
			leadBitfield[mapField] = (byte)builder.Map;
			leadBitfield[mainByteField] = builder.MainByte;
			AddressingForm = builder.AddressingForm;
			immFields = MakeImmFields(builder.ImmediateSize, builder.Imm8Ext);
			imm8Ext = builder.Imm8Ext.GetValueOrDefault();
		}

		public byte MainByteMask => AddressingForm.MainByteMask;
		public bool HasModRM => AddressingForm.HasModRM;

		public bool IsValidInCodeSegment(CodeSegmentType codeSegmentType)
		{
			if (!X64.HasValue) return true;
			return X64.Value
				? codeSegmentType == CodeSegmentType.X64
				: codeSegmentType != CodeSegmentType.X64;
		}

		#region Matching
		public bool IsMatchUpToMainByte(in InstructionPrefixes prefixes, byte mainByte)
		{
			if (!IsValidInCodeSegment(prefixes.CodeSegmentType)) return false;

			var effectiveAddressSize = prefixes.EffectiveAddressSize;
			if (effectiveAddressSize != AddressSize.GetValueOrDefault(effectiveAddressSize)) return false;

			if (prefixes.VexType != VexType) return false;
			if (prefixes.VectorSize != VectorSize.GetValueOrDefault(prefixes.VectorSize)) return false;

			var integerSize = prefixes.IntegerOperandSize;
			if (integerSize != OperandSize.AsIntegerSize().GetValueOrDefault(integerSize)) return false;

			var potentialSimdPrefix = prefixes.PotentialSimdPrefix;
			if (potentialSimdPrefix != SimdPrefix.GetValueOrDefault(potentialSimdPrefix)) return false;

			if (prefixes.OpcodeMap != Map) return false;
			if ((mainByte & MainByteMask) != MainByte) return false;

			return true;
		}

		public bool IsMatch(in InstructionPrefixes prefixes,
			byte mainByte, ModRM? modRM, byte? imm8)
		{
			if (!IsMatchUpToMainByte(prefixes, mainByte)) return false;
			if (!AddressingForm.IsValid(modRM)) return false;
			if (imm8.HasValue != (ImmediateSize.FixedInBytes == 1)) return false;
			if (Imm8Ext.HasValue && imm8.Value != Imm8Ext.Value) return false;
			return true;
		}

		public bool IsMatch(in Instruction instruction)
		{
			var imm8 = instruction.ImmediateSizeInBytes == 1 ? instruction.ImmediateData.GetByte(0) : (byte?)null;
			return IsMatch(instruction.Prefixes, instruction.MainOpcodeByte, instruction.ModRM, imm8);
		}
		#endregion
		
		public VexEncoding ToVexEncoding()
		{
			if (VexType == VexType.None) throw new InvalidOperationException();
			return new VexEncoding.Builder
			{
				Type = VexType,
				RegOperand = VexRegOperand.Invalid, // Lost in translation
				VectorSize = VectorSize,
				SimdPrefix = SimdPrefix.Value,
				OpcodeMap = Map,
				OperandSizePromotion = OperandSize.GetPromotion()
			}.Build();
		}

		#region ToString
		public override string ToString()
		{
			var str = new StringBuilder(30);
			
			if (X64.HasValue)
				str.Append(X64.Value ? "x64 " : "ia32 ");

			if (AddressSize.HasValue)
				str.AppendFormat(CultureInfo.InvariantCulture, "a{0} ",
					AddressSize.Value.InBits());
			
			if (VexType == VexType.None) AppendNonVexPrefixes(str);
			else
			{
				str.Append(ToVexEncoding().ToIntelStyleString());
				str.Append(' ');
			}

			// String tail: opcode byte and what follows: 0B /r ib

			// The opcode itself
			str.AppendFormat(CultureInfo.InvariantCulture, "{0:x2}", MainByte);

			if (AddressingForm.IsMainByteEmbeddedRegister)
				str.Append("+r");
			else if (AddressingForm.HasModRM)
				str.Append(' ').Append(AddressingForm.ModRM.Value.ToString());

			if (Imm8Ext.HasValue)
				str.Append(' ').AppendFormat(CultureInfo.InvariantCulture, "{0:x2}", Imm8Ext.Value);
			else if (ImmediateSize.IsNonZero)
				str.Append(' ').Append(ImmediateSize);

			return str.ToString();
		}

		private void AppendNonVexPrefixes(StringBuilder str)
		{
			if (OperandSize == OperandSizeEncoding.Word) str.Append("o16 ");
			else if (OperandSize == OperandSizeEncoding.Dword) str.Append("o32 ");

			// 66 REX.W 0F 38
			if (SimdPrefix.HasValue)
			{
				switch (SimdPrefix.Value)
				{
					case X86.SimdPrefix.None: str.Append("np "); break;
					case X86.SimdPrefix._66: str.Append("66 "); break;
					case X86.SimdPrefix._F3: str.Append("f3 "); break;
					case X86.SimdPrefix._F2: str.Append("f2 "); break;
					default: throw new UnreachableException();
				}
			}

			if (OperandSize == OperandSizeEncoding.Promotion)
				str.Append("rex.w ");

			switch (Map)
			{
				case OpcodeMap.Default: break;
				case OpcodeMap.Escape0F: str.Append("0f "); break;
				case OpcodeMap.Escape0F38: str.Append("0f 38 "); break;
				case OpcodeMap.Escape0F3A: str.Append("0f 3a "); break;
				default: throw new UnreachableException();
			}
		}
		#endregion

		private static int AsZeroIsNull(bool? value)
			=> value.HasValue ? (value.Value ? 2 : 1) : 0;

		private static int AsZeroIsNull(int? value)
			=> value.GetValueOrDefault(-1) + 1;

		private static bool? AsBool_ZeroIsNull(int value)
			=> value == 0 ? (bool?)null : value > 1;

		private static int? AsInt_ZeroIsNull(int value)
			=> value == 0 ? (int?)null : value - 1;
	}
}
