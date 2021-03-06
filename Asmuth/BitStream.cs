﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Asmuth
{
	public sealed class BitStream : IDisposable
	{
		private readonly Stream underlying;
		private readonly bool owned;
		private byte bitSubPosition;
		private byte trailingBits; // 0-7 bits written past the end of the stream
		private byte trailingBitCount;

		public BitStream(Stream underlying, bool owned = true)
		{
			this.underlying = underlying ?? throw new ArgumentNullException(nameof(underlying));
			this.owned = owned;
		}

		public bool CanRead => underlying.CanRead;
		public bool CanWrite => underlying.CanWrite; // Although if you can't read, you can only append
		public bool CanSeek => underlying.CanSeek;
		public long Length => (underlying.Length << 3) + trailingBitCount;
		public bool IsByteAligned => bitSubPosition == 0;

		public long Position
		{
			get => (underlying.Position << 3) + bitSubPosition;
			set
			{
				if (value < 0 || value > Length) throw new ArgumentOutOfRangeException(nameof(Position));
				underlying.Position = Position >> 3;
				bitSubPosition = (byte)(Position & 0x7);
			}
		}

		public bool ReadBit() => ReadRightAlignedBits(1) != 0;

		public ulong ReadRightAlignedBits(byte count)
		{
			throw new NotImplementedException();
		}

		public byte ReadAlignedByte()
		{
			CheckByteAligned();
			return (byte)ReadRightAlignedBits(8);
		}

		public void WriteBit(bool bit) => WriteRightAlignedBits(bit ? 1UL : 0UL, count: 1);

		public void WriteRightAlignedBits(ulong bits, byte count)
		{
			if (!CanWrite) throw new InvalidOperationException();

			var position = Position;
			var underlyingLength = underlying.Length;
			if (position >= underlyingLength)
			{
				while (count > 1)
				{
					if (bitSubPosition == 0 && count >= 8)
					{
						underlying.WriteByte((byte)(bits >> (count - 8)));
						count -= 8;
						continue;
					}

					var writeCount = (byte)Math.Min(count, 8 - trailingBitCount);
					var shift = 7 - bitSubPosition - writeCount;
					trailingBits &= (byte)~(((1 << writeCount) - 1) << shift);
					trailingBits |= (byte)((bits >> (count - writeCount)) << shift);
					count -= writeCount;
					bitSubPosition += writeCount;
					if (bitSubPosition == 8)
					{
						underlying.WriteByte(trailingBits);
						bitSubPosition = 0;
						trailingBitCount = 0;
						trailingBits = 0;
					}
					else if (bitSubPosition > trailingBitCount)
						trailingBitCount = bitSubPosition;
				}
			}
			else throw new NotImplementedException();
		}

		public void WriteAlignedByte(byte value)
		{
			CheckByteAligned();
			WriteRightAlignedBits(value, 8);
		}

		public void Flush(bool underlying = true)
		{
			FlushTrailingBits();
			if (underlying) this.underlying.Flush();
		}

		public void Dispose()
		{
			FlushTrailingBits();
			if (owned) underlying.Dispose();
		}

		private void CheckByteAligned()
		{
			if (!IsByteAligned) throw new InvalidOperationException();
		}

		private void FlushTrailingBits()
		{
			if (trailingBitCount == 0) return;
			throw new NotImplementedException();
		}
	}
}
