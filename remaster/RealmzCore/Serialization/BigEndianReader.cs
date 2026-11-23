using System;
using System.Text;

namespace RealmzCore.Serialization
{
    /// <summary>
    /// Reads big-endian primitive values and MacRoman strings from a byte buffer.
    /// Zero external dependencies for direct Unity netstandard2.1 compatibility.
    /// </summary>
    public sealed class BigEndianReader
    {
        // High-byte MacRoman mapping table for bytes 0x80..0xFF.
        private static readonly char[] MacRomanHighChars = new char[]
        {
            '\u00C4', '\u00C5', '\u00C7', '\u00C9', '\u00D1', '\u00D6', '\u00DC', '\u00E1',
            '\u00E0', '\u00E2', '\u00E4', '\u00E3', '\u00E5', '\u00E7', '\u00E9', '\u00E8',
            '\u00EA', '\u00EB', '\u00ED', '\u00EC', '\u00EE', '\u00EF', '\u00F1', '\u00F3',
            '\u00F2', '\u00F4', '\u00F6', '\u00F5', '\u00FA', '\u00F9', '\u00FB', '\u00FC',
            '\u2020', '\u00B0', '\u00A2', '\u00A3', '\u00A7', '\u2022', '\u00B6', '\u00DF',
            '\u00AE', '\u00A9', '\u2122', '\u00B4', '\u00A8', '\u2260', '\u00C6', '\u00D8',
            '\u221E', '\u00B1', '\u2264', '\u2265', '\u00A5', '\u00B5', '\u2202', '\u2211',
            '\u220F', '\u03C0', '\u222B', '\u00AA', '\u00BA', '\u03A9', '\u00E6', '\u00F8',
            '\u00BF', '\u00A1', '\u00AC', '\u221A', '\u0192', '\u2248', '\u2206', '\u00AB',
            '\u00BB', '\u2026', '\u00A0', '\u00C0', '\u00C3', '\u00D5', '\u0152', '\u0153',
            '\u002D', '\u002D', '\u201C', '\u201D', '\u2018', '\u2019', '\u00F7', '\u25CA',
            '\u00FF', '\u0178', '\u2044', '\u20AC', '\u2039', '\u203A', '\uFB01', '\uFB02',
            '\u2021', '\u00B7', '\u201A', '\u201E', '\u2030', '\u00C2', '\u00CA', '\u00C1',
            '\u00CB', '\u00C8', '\u00CD', '\u00CE', '\u00CF', '\u00CC', '\u00D3', '\u00D4',
            '\uF8FF', '\u00D2', '\u00DA', '\u00DB', '\u00D9', '\u0131', '\u02C6', '\u02DC',
            '\u00AF', '\u02D8', '\u02D9', '\u02DA', '\u00B8', '\u02DD', '\u02DB', '\u02C7'
        };

        private readonly byte[] _buffer;
        private readonly int _start;
        private readonly int _length;
        private int _offset;

        public BigEndianReader(byte[] buffer)
            : this(buffer, 0, buffer != null ? buffer.Length : 0)
        {
        }

        public BigEndianReader(byte[] buffer, int offset, int length)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0 || length < 0 || offset + length > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            _buffer = buffer;
            _start = offset;
            _length = length;
            _offset = 0;
        }

        public int Position => _offset;
        public int Length => _length;
        public int Remaining => _length - _offset;

        public void Seek(int position)
        {
            if (position < 0 || position > _length)
            {
                throw new ArgumentOutOfRangeException(nameof(position));
            }

            _offset = position;
        }

        public void Skip(int byteCount)
        {
            if (byteCount < 0 || _offset + byteCount > _length)
            {
                throw new ArgumentOutOfRangeException(nameof(byteCount));
            }

            _offset += byteCount;
        }

        public byte ReadByte()
        {
            EnsureAvailable(1);
            byte value = _buffer[_start + _offset];
            _offset += 1;
            return value;
        }

        public sbyte ReadSByte()
        {
            return unchecked((sbyte)ReadByte());
        }

        public bool ReadBool()
        {
            return ReadByte() != 0;
        }

        public short ReadInt16()
        {
            EnsureAvailable(2);
            short value = ReadInt16(_buffer.AsSpan(_start + _offset, 2));
            _offset += 2;
            return value;
        }

        public ushort ReadUInt16()
        {
            EnsureAvailable(2);
            ushort value = ReadUInt16(_buffer.AsSpan(_start + _offset, 2));
            _offset += 2;
            return value;
        }

        public int ReadInt32()
        {
            EnsureAvailable(4);
            int value = ReadInt32(_buffer.AsSpan(_start + _offset, 4));
            _offset += 4;
            return value;
        }

        public ReadOnlySpan<byte> ReadBytesSpan(int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            EnsureAvailable(count);
            ReadOnlySpan<byte> span = _buffer.AsSpan(_start + _offset, count);
            _offset += count;
            return span;
        }

        public string ReadMacRomanFixedString(int fixedLength)
        {
            if (fixedLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fixedLength));
            }

            ReadOnlySpan<byte> span = ReadBytesSpan(fixedLength);
            int nullIndex = span.IndexOf((byte)0);
            if (nullIndex >= 0)
            {
                span = span.Slice(0, nullIndex);
            }

            return DecodeMacRoman(span);
        }

        public string ReadPascalString()
        {
            int length = ReadByte();
            ReadOnlySpan<byte> span = ReadBytesSpan(length);
            return DecodeMacRoman(span);
        }

        public string ReadPascalString(int fixedBufferLength)
        {
            if (fixedBufferLength < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(fixedBufferLength));
            }

            EnsureAvailable(fixedBufferLength);
            int declaredLength = _buffer[_start + _offset];
            int maxContent = fixedBufferLength - 1;
            int actualLength = declaredLength <= maxContent ? declaredLength : maxContent;
            ReadOnlySpan<byte> span = _buffer.AsSpan(_start + _offset + 1, actualLength);
            _offset += fixedBufferLength;
            return DecodeMacRoman(span);
        }

        public static short ReadInt16(ReadOnlySpan<byte> span, int offset = 0)
        {
            return unchecked((short)ReadUInt16(span, offset));
        }

        public static ushort ReadUInt16(ReadOnlySpan<byte> span, int offset = 0)
        {
            if ((uint)(offset + 2) > (uint)span.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            return (ushort)((span[offset] << 8) | span[offset + 1]);
        }

        public static int ReadInt32(ReadOnlySpan<byte> span, int offset = 0)
        {
            if ((uint)(offset + 4) > (uint)span.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            return (span[offset] << 24)
                 | (span[offset + 1] << 16)
                 | (span[offset + 2] << 8)
                 | span[offset + 3];
        }

        public static string DecodeMacRoman(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(bytes.Length);
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                if (b < 0x80)
                {
                    sb.Append((char)b);
                }
                else
                {
                    sb.Append(MacRomanHighChars[b - 0x80]);
                }
            }

            return sb.ToString();
        }

        private void EnsureAvailable(int count)
        {
            if (_offset + count > _length)
            {
                throw new InvalidOperationException(
                    $"Unexpected end of buffer at offset {_offset}; requested {count} bytes with {Remaining} remaining.");
            }
        }
    }
}
