﻿using Asmuth.X86;
using Asmuth.X86.Nasm;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Asmuth.Disassembler
{
	using static WinNT;

	class Program
	{
		static void Main(string[] args)
		{
			var filePath = Path.GetFullPath(args[0]);
			using (var memoryMappedFile = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read))
			using (var fileViewAccessor = memoryMappedFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
			{
				// DOS header
				var dosHeader = fileViewAccessor.Read<IMAGE_DOS_HEADER>(0);
				if (dosHeader.e_magic != IMAGE_DOS_SIGNATURE) throw new InvalidDataException();

				// NT headers
				var ntHeaderPosition = (long)dosHeader.e_lfanew;
				if (fileViewAccessor.ReadUInt32(ntHeaderPosition) != IMAGE_NT_SIGNATURE)
					throw new InvalidDataException();
				var fileHeader = fileViewAccessor.Read<IMAGE_FILE_HEADER>(ntHeaderPosition + IMAGE_NT_HEADERS.FileHeaderOffset);
				if (fileHeader.Machine != IMAGE_FILE_MACHINE_I386 && fileHeader.Machine != IMAGE_FILE_MACHINE_AMD64)
					throw new InvalidDataException();

				bool is32Bit = fileHeader.Machine == IMAGE_FILE_MACHINE_I386;

				// NT optional header
				var optionalHeaderMagic = fileViewAccessor.ReadUInt16(
					ntHeaderPosition + IMAGE_NT_HEADERS.OptionalHeaderOffset + IMAGE_OPTIONAL_HEADER.MagicOffset);
				
				IMAGE_OPTIONAL_HEADER64 optionalHeader;
				if (is32Bit)
				{
					if (optionalHeaderMagic != IMAGE_NT_OPTIONAL_HDR32_MAGIC
						|| fileHeader.SizeOfOptionalHeader != Marshal.SizeOf(typeof(IMAGE_OPTIONAL_HEADER32)))
						throw new InvalidDataException();
					optionalHeader = fileViewAccessor.Read<IMAGE_OPTIONAL_HEADER32>(
						ntHeaderPosition + IMAGE_NT_HEADERS.OptionalHeaderOffset)
						.As64();
				}
				else
				{
					if (optionalHeaderMagic != IMAGE_NT_OPTIONAL_HDR64_MAGIC
						|| fileHeader.SizeOfOptionalHeader != Marshal.SizeOf(typeof(IMAGE_OPTIONAL_HEADER64)))
						throw new InvalidDataException();
					optionalHeader = fileViewAccessor.Read<IMAGE_OPTIONAL_HEADER64>(
						ntHeaderPosition + IMAGE_NT_HEADERS.OptionalHeaderOffset);
				}

				Contract.Assert(optionalHeader.NumberOfRvaAndSizes == IMAGE_NUMBEROF_DIRECTORY_ENTRIES);

				// Sections
				var sectionHeaders = new IMAGE_SECTION_HEADER[fileHeader.NumberOfSections];
				var firstSectionHeaderPosition = ntHeaderPosition + IMAGE_NT_HEADERS.OptionalHeaderOffset + fileHeader.SizeOfOptionalHeader;
				for (int i = 0; i < fileHeader.NumberOfSections; ++i)
				{
					var sectionHeaderPosition = firstSectionHeaderPosition + Marshal.SizeOf(typeof(IMAGE_SECTION_HEADER)) * i;
					fileViewAccessor.Read(sectionHeaderPosition, out sectionHeaders[i]);
				}

				// Disassemble executable sections
				var instructionDecoder = new InstructionDecoder(
					is32Bit ? CodeSegmentType._32Bits : CodeSegmentType._64Bits);
				foreach (var sectionHeader in sectionHeaders)
				{
					if ((sectionHeader.Characteristics & IMAGE_SCN_CNT_CODE) == 0) continue;

					var sectionBaseAddress = optionalHeader.ImageBase + sectionHeader.VirtualAddress;
					Console.Write($"{sectionBaseAddress:X8}");
					if (sectionHeader.Name.Char0 != 0)
						Console.Write($" <{sectionHeader.Name}>");
					Console.WriteLine(":");

					long codeOffset = 0;
					while (true)
					{
						Console.Write($"{sectionBaseAddress + (ulong)codeOffset:X}".PadLeft(8));
						Console.Write(":       ");
						while (true)
						{
							var @byte = fileViewAccessor.ReadByte(sectionHeader.PointerToRawData + codeOffset);
							if (instructionDecoder.State != InstructionDecodingState.Initial)
								Console.Write(' ');
							Console.Write($"{@byte:X2}");
							codeOffset++;
							if (!instructionDecoder.Feed(@byte)) break;
						}

						while (Console.CursorLeft < 40) Console.Write(' ');

						var instruction = instructionDecoder.GetInstruction();
						instructionDecoder.Reset();

						Console.Write('\t');
						Console.Write("{0:X2}", instruction.MainByte);
						
						if (instruction.ModRM.HasValue)
						{
							var modRM = instruction.ModRM.Value;
							Console.Write(" /{0} ", modRM.GetReg() + (instruction.Xex.ModRegExtension ? 8 : 0));
							if (modRM.IsMemoryRM())
							{
								var effectiveAddress = instruction.GetRMEffectiveAddress();
								Console.Write(effectiveAddress.ToString(
									vectorSib: false, rip: sectionBaseAddress + (ulong)codeOffset));
							}
							else Console.Write("r{0}", modRM.GetRM() + (instruction.Xex.BaseRegExtension ? 8 : 0));
						}

						Console.WriteLine();

						if (instruction.MainByte == KnownOpcodes.RetNear || instruction.MainByte == KnownOpcodes.RetNearAndPop)
							break;
					}
				}
			}
		}
	}

	internal static class UnmanagedMemoryAccessorExtensions
	{
		public static T Read<T>(this UnmanagedMemoryAccessor accessor, long position) where T : struct
		{
			accessor.Read(position, out T value);
			return value;
		}
	}
}
