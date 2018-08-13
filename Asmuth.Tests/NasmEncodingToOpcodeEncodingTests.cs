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
	using OEF = OpcodeEncodingFlags;

	[TestClass]
	public sealed class NasmEncodingToOpcodeEncodingTests
	{
		[TestMethod]
		public void TestMainByte()
		{
			AssertEncoding("37", default, 0x37); // AAA
			AssertEncoding("48+r", OEF.HasMainByteReg, 0x48); // DEC reg
		}

		[TestMethod]
		public void TestModRM()
		{
			AssertEncoding("00 /r", OEF.ModRM_Present, 0x10, default(ModRM)); // ADD r/m8, r8
			AssertEncoding("d8 /0", OEF.ModRM_Present | OEF.ModRM_FixedReg, 0xD8, ModRM.Reg_0); // FADD m32
			AssertEncoding("d8 c0+r", OEF.ModRM_Present | OEF.ModRM_FixedReg | OEF.ModRM_RM_Direct, 0xD8, (ModRM)0xC0); // FADD
			AssertEncoding("d9 f2", OEF.ModRM_Present | OEF.ModRM_FixedReg | OEF.ModRM_RM_Fixed, 0xD9, (ModRM)0xF2); // FPTAN
		}

		[TestMethod]
		public void TestImm()
		{
			AssertEncoding("04 ib", OEF.ImmediateSize_8, 0x04); // ADD reg_al, imm
			AssertEncoding("05 iw", OEF.ImmediateSize_16, 0x05); // ADD reg_ax, imm
			AssertEncoding("05 id", OEF.ImmediateSize_32, 0x05); // ADD reg_eax, imm
		}

		[TestMethod]
		public void TestFixedModRMAndImm()
		{
			AssertEncoding("d5 0a", OEF.Imm8Ext_Fixed | OEF.ImmediateSize_8, 0xD5, (ModRM?)null, 0x0A); // AAD
			AssertEncoding("dd d1", OEF.ModRM_Present | OEF.ModRM_FixedReg | OEF.ModRM_RM_Fixed, 0xDD, (ModRM)0xD1); // FST
		}

		[TestMethod]
		public void TestEscapes()
		{
			AssertEncoding("0f a2", OEF.Map_0F, 0xA2); // CPUID
			AssertEncoding("0f 38 c9 /r", OEF.Map_0F38 | OEF.ModRM_Present, 0xC9); // SHA1MSG1
			AssertEncoding("0f 3a cc /r ib", OEF.Map_0F3A | OEF.ModRM_Present | OEF.ImmediateSize_8, 0xCC); // SHA1RNDS4
		}

		[TestMethod]
		public void TestSimdPrefixes()
		{
			AssertEncoding("np 0f 10 /r", OEF.SimdPrefix_None | OEF.Map_0F | OEF.ModRM_Present, 0x10); // MOVUPS
			AssertEncoding("66 0f 10 /r", OEF.SimdPrefix_66 | OEF.Map_0F | OEF.ModRM_Present, 0x10); // MOVUPD
			AssertEncoding("f2 0f 10 /r", OEF.SimdPrefix_F2 | OEF.Map_0F | OEF.ModRM_Present, 0x10); // MOVSD
			AssertEncoding("f3 0f 10 /r", OEF.SimdPrefix_F3 | OEF.Map_0F | OEF.ModRM_Present, 0x10); // MOVSS
		}

		[TestMethod]
		public void TestVex()
		{
			// Test with different L, pp, mm and w bits
			AssertEncoding("vex.128.0f 10 /r",
				OEF.XexType_Vex | OEF.VexL_128 | OEF.SimdPrefix_None | OEF.Map_0F | OEF.ModRM_Present, 0x10); // VMOVUPS
			AssertEncoding("vex.nds.lig.f2.0f 10 /r",
				OEF.XexType_Vex | OEF.VexL_Ignored | OEF.SimdPrefix_F2 | OEF.Map_0F | OEF.ModRM_Present, 0x10); // VMOVSS
			AssertEncoding("vex.dds.256.66.0f38.w1 98 /r",
				OEF.XexType_Vex | OEF.VexL_256 | OEF.SimdPrefix_66 | OEF.RexW_1 | OEF.Map_0F38 | OEF.ModRM_Present, 0x98); // VFMADD132PD
		}

		private static void AssertEncoding(string nasmEncodingStr,
			OpcodeEncodingFlags expectedFlags, byte expectedOpcode,
			ModRM? expectedModRM = null, byte? expectedFixedImm8 = null)
		{
			var nasmEncodingTokens = NasmInsns.ParseEncoding(
				nasmEncodingStr, out VexEncoding? vexEncoding);

			var opcodeEncoding = NasmInsnsEntry.ToOpcodeEncoding(nasmEncodingTokens, vexEncoding, longMode: null);
			Assert.AreEqual(expectedFlags, opcodeEncoding.Flags);

			bool hasFixedModRMBits = opcodeEncoding.HasModRM
				&& ((opcodeEncoding.Flags & OEF.ModRM_FixedReg) != 0
				|| (opcodeEncoding.Flags & OEF.ModRM_RM_Mask) == OEF.ModRM_RM_Fixed);
			Assert.IsTrue(expectedModRM.HasValue ? opcodeEncoding.HasModRM : !hasFixedModRMBits,
				"Expected ModRM provided when not needed or vice-versa.");
			if (expectedModRM.HasValue)
				Assert.AreEqual(expectedModRM.GetValueOrDefault(), opcodeEncoding.ModRM);

			Assert.AreEqual(expectedFixedImm8.HasValue, opcodeEncoding.Flags.HasFixedImm8());
			if (expectedFixedImm8.HasValue)
				Assert.AreEqual(expectedFixedImm8.Value, opcodeEncoding.Imm8);
		}
	}
}
