﻿using Asmuth.X86;
using Asmuth.X86.Asm;
using Asmuth.X86.Asm.Nasm;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Asmuth.X86.Asm.Nasm
{
	[TestClass]
	public sealed class NasmEncodingToOpcodeEncodingTests
	{
		[TestMethod]
		public void TestMainByte()
		{
			AssertEncoding("37", new OpcodeEncoding.Builder
			{
				MainByte = 0x37
			}); // AAA

			AssertEncoding("48+r", new OpcodeEncoding.Builder
			{
				MainByte = 0x48,
				ModRM = ModRMEncoding.MainByteReg
			}); // DEC reg
		}

		[TestMethod]
		public void TestModRM()
		{
			AssertEncoding("00 /r", new OpcodeEncoding.Builder
			{
				MainByte = 0x00,
				ModRM = ModRMEncoding.Any
			}); // ADD r/m8, r8

			AssertEncoding("f6 /3", NasmOperandType.RM8, new OpcodeEncoding.Builder
			{
				MainByte = 0xF6,
				ModRM = ModRMEncoding.FromFixedRegAnyRM(3)
			}); // NEG r/m8

			AssertEncoding("d8 /0", NasmOperandType.Mem32, new OpcodeEncoding.Builder
			{
				MainByte = 0xD8,
				ModRM = ModRMEncoding.FromFixedRegMemRM(0)
			}); // FADD m32

			AssertEncoding("d8 c0+r", new OpcodeEncoding.Builder
			{
				MainByte = 0xD8,
				ModRM = ModRMEncoding.FromFixedRegDirectRM(0)
			}); // FADD

			AssertEncoding("d9 f2", new OpcodeEncoding.Builder
			{
				MainByte = 0xD9,
				ModRM = ModRMEncoding.FromFixedValue(0xF2)
			}); // FPTAN
		}

		[TestMethod]
		public void TestImm()
		{
			AssertEncoding("04 ib", new OpcodeEncoding.Builder
			{
				MainByte = 0x04,
				ImmediateSizeInBytes = sizeof(sbyte)
			}); // ADD reg_al, imm

			AssertEncoding("05 iw", new OpcodeEncoding.Builder
			{
				MainByte = 0x05,
				ImmediateSizeInBytes = sizeof(short)
			}); // ADD reg_ax, imm

			AssertEncoding("05 id", new OpcodeEncoding.Builder
			{
				MainByte = 0x05,
				ImmediateSizeInBytes = sizeof(int)
			}); // ADD reg_eax, imm
		}

		[TestMethod]
		public void TestFixedModRMAndImm()
		{
			AssertEncoding("d5 0a", new OpcodeEncoding.Builder
			{
				MainByte = 0xD5,
				ImmediateSizeInBytes = sizeof(sbyte),
				Imm8Ext = 0x0A
			}); // AAD

			AssertEncoding("dd d1", new OpcodeEncoding.Builder
			{
				MainByte = 0xDD,
				ModRM = ModRMEncoding.FromFixedValue(0xD1)
			}); // FST
		}

		[TestMethod]
		public void TestEscapes()
		{
			AssertEncoding("0f a2", new OpcodeEncoding.Builder
			{
				Map = OpcodeMap.Escape0F,
				MainByte = 0xA2
			}); // CPUID

			AssertEncoding("0f 38 c9 /r", new OpcodeEncoding.Builder
			{
				Map = OpcodeMap.Escape0F38,
				MainByte = 0xC9,
				ModRM = ModRMEncoding.Any
			}); // SHA1MSG1

			AssertEncoding("0f 3a cc /r ib", new OpcodeEncoding.Builder
			{
				Map = OpcodeMap.Escape0F3A,
				MainByte = 0xCC,
				ModRM = ModRMEncoding.Any,
				ImmediateSizeInBytes = sizeof(sbyte)
			}); // SHA1RNDS4
		}

		[TestMethod]
		public void TestSimdPrefixes()
		{
			AssertEncoding("np 0f 10 /r", new OpcodeEncoding.Builder
			{
				SimdPrefix = SimdPrefix.None,
				Map = OpcodeMap.Escape0F,
				MainByte = 0x10,
				ModRM = ModRMEncoding.Any
			}); // MOVUPS

			AssertEncoding("66 0f 10 /r", new OpcodeEncoding.Builder
			{
				SimdPrefix = SimdPrefix._66,
				Map = OpcodeMap.Escape0F,
				MainByte = 0x10,
				ModRM = ModRMEncoding.Any
			}); // MOVUPD

			AssertEncoding("f2 0f 10 /r", new OpcodeEncoding.Builder
			{
				SimdPrefix = SimdPrefix._F2,
				Map = OpcodeMap.Escape0F,
				MainByte = 0x10,
				ModRM = ModRMEncoding.Any
			}); // MOVSD

			AssertEncoding("f3 0f 10 /r", new OpcodeEncoding.Builder
			{
				SimdPrefix = SimdPrefix._F3,
				Map = OpcodeMap.Escape0F,
				MainByte = 0x10,
				ModRM = ModRMEncoding.Any
			}); // MOVSS
		}

		[TestMethod]
		public void TestVex()
		{
			// Test with different L, pp, mm and w bits
			AssertEncoding("vex.128.0f 10 /r", new OpcodeEncoding.Builder
			{
				VexType = VexType.Vex,
				VectorSize = SseVectorSize._128,
				SimdPrefix = SimdPrefix.None,
				Map = OpcodeMap.Escape0F,
				MainByte = 0x10,
				ModRM = ModRMEncoding.Any
			}); // VMOVUPS

			AssertEncoding("vex.nds.lig.f2.0f 10 /r", new OpcodeEncoding.Builder
			{
				VexType = VexType.Vex,
				SimdPrefix = SimdPrefix._F2,
				Map = OpcodeMap.Escape0F,
				MainByte = 0x10,
				ModRM = ModRMEncoding.Any
			}); // VMOVSS

			AssertEncoding("vex.dds.256.66.0f38.w1 98 /r", new OpcodeEncoding.Builder
			{
				VexType = VexType.Vex,
				VectorSize = SseVectorSize._256,
				SimdPrefix = SimdPrefix._66,
				RexW = true,
				Map = OpcodeMap.Escape0F38,
				MainByte = 0x98,
				ModRM = ModRMEncoding.Any
			}); // VFMADD132PD

			AssertEncoding("xop.m8.w0.nds.l0.p0 a2 /r /is4", new OpcodeEncoding.Builder
			{
				VexType = VexType.Xop,
				Map = OpcodeMap.Xop8,
				RexW = false,
				VectorSize = SseVectorSize._128,
				SimdPrefix = SimdPrefix.None,
				MainByte = 0xA2,
				ModRM = ModRMEncoding.Any,
				ImmediateSizeInBytes = 1
			}); // VPCMOV
		}

		private static void AssertEncoding(string nasmEncodingStr,
			NasmOperandType? rmOperandType, OpcodeEncoding expectedEncoding)
		{
			var nasmEncodingTokens = NasmInsns.ParseEncoding(
				nasmEncodingStr, out VexEncoding? vexEncoding);

			var actualEncoding = NasmInsnsEntry.ToOpcodeEncoding(nasmEncodingTokens, vexEncoding, longMode: null, rmOperandType);
			Assert.AreEqual(SetComparisonResult.Equal, OpcodeEncoding.Compare(actualEncoding, expectedEncoding));
		}

		private static void AssertEncoding(string nasmEncodingStr, OpcodeEncoding expectedEncoding)
			=> AssertEncoding(nasmEncodingStr, null, expectedEncoding);
	}
}
