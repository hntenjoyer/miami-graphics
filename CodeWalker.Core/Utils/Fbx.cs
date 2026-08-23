using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

/*
 Shamelessly stolen and mangled from:
 https://github.com/hamish-milne/FbxWriter
 Under GPL license, for full terms see the above link.

 Copyright (c) 2015 Hamish Milne

 "An FBX library for .NET"
*/

namespace CodeWalker
{

    public static class FbxIO
    {

        public static FbxDocument Read(byte[] data)
        {
            using (var stream = new MemoryStream(data))
            {
                var isbinary = FbxBinary.IsBinary(stream);
                if (isbinary)
                {
                    var reader = new FbxBinaryReader(stream);
                    return reader.Read();
                }
                else
                {
                    var reader = new FbxAsciiReader(stream);
                    return reader.Read();
                }
            }
        }

        public static FbxDocument ReadBinary(string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            using (var stream = new FileStream(path, FileMode.Open))
            {
                var reader = new FbxBinaryReader(stream);
                return reader.Read();
            }
        }

        public static FbxDocument ReadAscii(string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            using (var stream = new FileStream(path, FileMode.Open))
            {
                var reader = new FbxAsciiReader(stream);
                return reader.Read();
            }
        }

        public static void WriteBinary(FbxDocument document, string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            using (var stream = new FileStream(path, FileMode.Create))
            {
                var writer = new FbxBinaryWriter(stream);
                writer.Write(document);
            }
        }

        public static void WriteAscii(FbxDocument document, string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            using (var stream = new FileStream(path, FileMode.Create))
            {
                var writer = new FbxAsciiWriter(stream);
                writer.Write(document);
            }
        }
    }

    public class FbxAsciiReader
    {
        private readonly Stream stream;
        private readonly FbxErrorLevel errorLevel;

        private int line = 1;
        private int column = 1;

        public FbxAsciiReader(Stream stream, FbxErrorLevel errorLevel = FbxErrorLevel.Checked)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            this.stream = stream;
            this.errorLevel = errorLevel;
        }

        public int MaxArrayLength { get; set; } = (1 << 24);

        readonly byte[] singleChar = new byte[1];
        private char? prevChar;
        private bool endStream;
        private bool wasCr;

        char ReadChar()
        {
            if (prevChar != null)
            {
                var c = prevChar.Value;
                prevChar = null;
                return c;
            }
            if (stream.Read(singleChar, 0, 1) < 1)
            {
                endStream = true;
                return '\0';
            }
            var ch = (char)singleChar[0];
            if (ch == '\r')
            {
                wasCr = true;
                line++;
                column = 0;
            }
            else
            {
                if (ch == '\n' && !wasCr)
                {
                    line++;
                    column = 0;
                }
                wasCr = false;
            }
            column++;
            return ch;
        }

        static bool IsDigit(char c, bool first)
        {
            if (char.IsDigit(c))
                return true;
            switch (c)
            {
                case '-':
                case '+':
                    return true;
                case '.':
                case 'e':
                case 'E':
                case 'X':
                case 'x':
                    return !first;
            }
            return false;
        }

        static bool IsLineEnd(char c)
        {
            return c == '\r' || c == '\n';
        }

        class EndOfStream
        {
            public override string ToString()
            {
                return "end of stream";
            }
        }

        class Identifier
        {
            public readonly string String;

            public override bool Equals(object obj)
            {
                var id = obj as Identifier;
                if (id != null)
                    return String == id.String;
                return false;
            }

            public override int GetHashCode()
            {
                return String?.GetHashCode() ?? 0;
            }

            public Identifier(string str)
            {
                String = str;
            }

            public override string ToString()
            {
                return String + ":";
            }
        }

        private object prevTokenSingle;

        object ReadTokenSingle()
        {
            if (prevTokenSingle != null)
            {
                var ret = prevTokenSingle;
                prevTokenSingle = null;
                return ret;
            }
            var c = ReadChar();
            if (endStream)
                return new EndOfStream();
            switch (c)
            {
                case ';':
                    while (!IsLineEnd(ReadChar()) && !endStream) { }
                    return null;
                case '{':
                case '}':
                case '*':
                case ':':
                case ',':
                    return c;
                case '"':
                    var sb1 = new StringBuilder();
                    while ((c = ReadChar()) != '"')
                    {
                        if (endStream)
                            throw new FbxException(line, column,
                                "Unexpected end of stream; expecting end quote");
                        sb1.Append(c);
                    }
                    return sb1.ToString();
                default:
                    if (char.IsWhiteSpace(c))
                    {
                        while (char.IsWhiteSpace(c = ReadChar()) && !endStream) { }
                        if (!endStream)
                            prevChar = c;
                        return null;
                    }
                    if (IsDigit(c, true))
                    {
                        var sb2 = new StringBuilder();
                        do
                        {
                            sb2.Append(c);
                            c = ReadChar();
                        } while (IsDigit(c, false) && !endStream);
                        if (!endStream)
                            prevChar = c;
                        var str = sb2.ToString();
                        if (str.Contains("."))
                        {
                            if (str.Split('.', 'e', 'E')[1].Length > 6)
                            {
                                double d;
                                if (!double.TryParse(str, out d))
                                    throw new FbxException(line, column,
                                        "Invalid number");
                                return d;
                            }
                            else
                            {
                                float f;
                                if (!float.TryParse(str, out f))
                                    throw new FbxException(line, column,
                                        "Invalid number");
                                return f;
                            }
                        }
                        long l;
                        if (!long.TryParse(str, out l))
                            throw new FbxException(line, column,
                                "Invalid integer");
                        if (l >= byte.MinValue && l <= byte.MaxValue)
                            return (byte)l;
                        if (l >= int.MinValue && l <= int.MaxValue)
                            return (int)l;
                        return l;
                    }
                    if (char.IsLetter(c) || c == '_')
                    {
                        var sb3 = new StringBuilder();
                        do
                        {
                            sb3.Append(c);
                            c = ReadChar();
                        } while ((char.IsLetterOrDigit(c) || c == '_') && !endStream);
                        if (!endStream)
                            prevChar = c;
                        return new Identifier(sb3.ToString());
                    }
                    break;
            }
            throw new FbxException(line, column,
                "Unknown character " + c);
        }

        private object prevToken;

        object ReadToken()
        {
            object ret;
            if (prevToken != null)
            {
                ret = prevToken;
                prevToken = null;
                return ret;
            }
            do
            {
                ret = ReadTokenSingle();
            } while (ret == null);
            var id = ret as Identifier;
            if (id != null)
            {
                object colon;
                do
                {
                    colon = ReadTokenSingle();
                } while (colon == null);
                if (!':'.Equals(colon))
                {
                    if (id.String.Length > 1)
                        throw new FbxException(line, column,
                            "Unexpected '" + colon + "', expected ':' or a single-char literal");
                    ret = id.String[0];
                    prevTokenSingle = colon;
                }
            }
            return ret;
        }

        void ExpectToken(object token)
        {
            var t = ReadToken();
            if (!token.Equals(t))
                throw new FbxException(line, column,
                    "Unexpected '" + t + "', expected " + token);
        }

        private enum ArrayType
        {
            Byte = 0,
            Int = 1,
            Long = 2,
            Float = 3,
            Double = 4,
        };

        Array ReadArray()
        {
            var len = ReadToken();
            long l;
            if (len is long)
                l = (long)len;
            else if (len is int)
                l = (int)len;
            else if (len is byte)
                l = (byte)len;
            else
                throw new FbxException(line, column,
                    "Unexpected '" + len + "', expected an integer");
            if (l < 0)
                throw new FbxException(line, column,
                    "Invalid array length " + l);
            if (l > MaxArrayLength)
                throw new FbxException(line, column,
                    "Array length " + l + " higher than permitted maximum " + MaxArrayLength);
            ExpectToken('{');
            ExpectToken(new Identifier("a"));
            var array = new double[l];

            bool expectComma = false;
            object token;
            var arrayType = ArrayType.Byte;
            long pos = 0;
            while (!'}'.Equals(token = ReadToken()))
            {
                if (expectComma)
                {
                    if (!','.Equals(token))
                        throw new FbxException(line, column,
                            "Unexpected '" + token + "', expected ','");
                    expectComma = false;
                    continue;
                }
                if (pos >= array.Length)
                {
                    if (errorLevel >= FbxErrorLevel.Checked)
                        throw new FbxException(line, column,
                            "Too many elements in array");
                    continue;
                }

                double d;
                if (token is byte)
                {
                    d = (byte)token;
                }
                else if (token is int)
                {
                    d = (int)token;
                    if (arrayType < ArrayType.Int)
                        arrayType = ArrayType.Int;
                }
                else if (token is long)
                {
                    d = (long)token;
                    if (arrayType < ArrayType.Long)
                        arrayType = ArrayType.Long;
                }
                else if (token is float)
                {
                    d = (float)token;
                    arrayType = arrayType < ArrayType.Long
                        ? ArrayType.Float : ArrayType.Double;
                }
                else if (token is double)
                {
                    d = (double)token;
                    if (arrayType < ArrayType.Double)
                        arrayType = ArrayType.Double;
                }
                else
                    throw new FbxException(line, column,
                            "Unexpected '" + token + "', expected a number");
                array[pos++] = d;
                expectComma = true;
            }
            if (pos < array.Length && errorLevel >= FbxErrorLevel.Checked)
                throw new FbxException(line, column,
                    "Too few elements in array - expected " + (array.Length - pos) + " more");

            Array ret;
            switch (arrayType)
            {
                case ArrayType.Byte:
                    var bArray = new byte[array.Length];
                    for (int i = 0; i < bArray.Length; i++)
                        bArray[i] = (byte)array[i];
                    ret = bArray;
                    break;
                case ArrayType.Int:
                    var iArray = new int[array.Length];
                    for (int i = 0; i < iArray.Length; i++)
                        iArray[i] = (int)array[i];
                    ret = iArray;
                    break;
                case ArrayType.Long:
                    var lArray = new long[array.Length];
                    for (int i = 0; i < lArray.Length; i++)
                        lArray[i] = (long)array[i];
                    ret = lArray;
                    break;
                case ArrayType.Float:
                    var fArray = new float[array.Length];
                    for (int i = 0; i < fArray.Length; i++)
                        fArray[i] = (long)array[i];
                    ret = fArray;
                    break;
                default:
                    ret = array;
                    break;
            }
            return ret;
        }

        public FbxNode ReadNode()
        {
            var first = ReadToken();
            var id = first as Identifier;
            if (id == null)
            {
                if (first is EndOfStream)
                    return null;
                throw new FbxException(line, column,
                    "Unexpected '" + first + "', expected an identifier");
            }
            var node = new FbxNode { Name = id.String };

            object token;
            bool expectComma = false;
            while (!'{'.Equals(token = ReadToken()) && !(token is Identifier) && !'}'.Equals(token))
            {
                if (expectComma)
                {
                    if (!','.Equals(token))
                        throw new FbxException(line, column,
                            "Unexpected '" + token + "', expected a ','");
                    expectComma = false;
                    continue;
                }
                if (token is char)
                {
                    var c = (char)token;
                    switch (c)
                    {
                        case '*':
                            token = ReadArray();
                            break;
                        case '}':
                        case ':':
                        case ',':
                            throw new FbxException(line, column,
                                "Unexpected '" + c + "' in property list");
                    }
                }
                node.Properties.Add(token);
                expectComma = true;
            }
            if (token is Identifier || '}'.Equals(token))
            {
                prevToken = token;
                return node;
            }
            object endBrace;
            while (!'}'.Equals(endBrace = ReadToken()))
            {
                prevToken = endBrace;
                node.Nodes.Add(ReadNode());
            }
            if (node.Nodes.Count < 1)
                node.Nodes.Add(null);
            return node;
        }

        public FbxDocument Read()
        {
            var ret = new FbxDocument();

            const string versionString = @"; FBX (\d)\.(\d)\.(\d) project file";
            char c;
            while (char.IsWhiteSpace(c = ReadChar()) && !endStream) { }
            bool hasVersionString = false;
            if (c == ';')
            {
                var sb = new StringBuilder();
                do
                {
                    sb.Append(c);
                } while (!IsLineEnd(c = ReadChar()) && !endStream);
                var match = Regex.Match(sb.ToString(), versionString);
                hasVersionString = match.Success;
                if (hasVersionString)
                    ret.Version = (FbxVersion)(
                        int.Parse(match.Groups[1].Value) * 1000 +
                        int.Parse(match.Groups[2].Value) * 100 +
                        int.Parse(match.Groups[3].Value) * 10
                    );
            }
            if (!hasVersionString && errorLevel >= FbxErrorLevel.Strict)
                throw new FbxException(line, column,
                    "Invalid version string; first line must match \"" + versionString + "\"");
            FbxNode node;
            while ((node = ReadNode()) != null)
                ret.Nodes.Add(node);
            return ret;
        }
    }

    public class FbxAsciiWriter
    {
        private readonly Stream stream;

        public FbxAsciiWriter(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            this.stream = stream;
        }

        public int MaxLineLength { get; set; } = 260;

        readonly Stack<string> nodePath = new Stack<string>();

        void BuildString(FbxNode node, StringBuilder sb, bool writeArrayLength, int indentLevel = 0)
        {
            nodePath.Push(node.Name ?? "");
            int lineStart = sb.Length;
            for (int i = 0; i < indentLevel; i++)
                sb.Append('\t');
            sb.Append(node.Name).Append(':');

            var first = true;
            for (int j = 0; j < node.Properties.Count; j++)
            {
                var p = node.Properties[j];
                if (p == null)
                    continue;
                if (!first)
                    sb.Append(',');
                sb.Append(' ');
                if (p is string)
                {
                    sb.Append('"').Append(p).Append('"');
                }
                else if (p is Array)
                {
                    var array = (Array)p;
                    var elementType = p.GetType().GetElementType();
                    if (array.Rank != 1 || !elementType.IsPrimitive)
                        throw new FbxException(nodePath, j,
                            "Invalid array type " + p.GetType());
                    if (writeArrayLength)
                    {
                        sb.Append('*').Append(array.Length).Append(" {\n");
                        lineStart = sb.Length;
                        for (int i = -1; i < indentLevel; i++)
                            sb.Append('\t');
                        sb.Append("a: ");
                    }
                    bool pFirst = true;
                    foreach (var v in (Array)p)
                    {
                        if (!pFirst)
                            sb.Append(',');
                        var vstr = v.ToString();
                        if ((sb.Length - lineStart) + vstr.Length >= MaxLineLength)
                        {
                            sb.Append('\n');
                            lineStart = sb.Length;
                        }
                        sb.Append(vstr);
                        pFirst = false;
                    }
                    if (writeArrayLength)
                    {
                        sb.Append('\n');
                        for (int i = 0; i < indentLevel; i++)
                            sb.Append('\t');
                        sb.Append('}');
                    }
                }
                else if (p is char)
                    sb.Append((char)p);
                else if (p.GetType().IsPrimitive && p is IFormattable)
                    sb.Append(p);
                else
                    throw new FbxException(nodePath, j,
                        "Invalid property type " + p.GetType());
                first = false;
            }

            if (node.Nodes.Count > 0)
            {
                sb.Append(" {\n");
                foreach (var n in node.Nodes)
                {
                    if (n == null)
                        continue;
                    BuildString(n, sb, writeArrayLength, indentLevel + 1);
                }
                for (int i = 0; i < indentLevel; i++)
                    sb.Append('\t');
                sb.Append('}');
            }
            sb.Append('\n');

            nodePath.Pop();
        }

        public void Write(FbxDocument document)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            var sb = new StringBuilder();

            var vMajor = (int)document.Version / 1000;
            var vMinor = ((int)document.Version % 1000) / 100;
            var vRev = ((int)document.Version % 100) / 10;
            sb.Append($"; FBX {vMajor}.{vMinor}.{vRev} project file\n\n");

            nodePath.Clear();
            foreach (var n in document.Nodes)
            {
                if (n == null)
                    continue;
                BuildString(n, sb, document.Version >= FbxVersion.v7_1);
                sb.Append('\n');
            }
            var b = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(b, 0, b.Length);
        }
    }

    public abstract class FbxBinary
    {
        private static readonly byte[] headerString
            = Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0\x1a\0");

        private static readonly byte[] sourceId =
            { 0x58, 0xAB, 0xA9, 0xF0, 0x6C, 0xA2, 0xD8, 0x3F, 0x4D, 0x47, 0x49, 0xA3, 0xB4, 0xB2, 0xE7, 0x3D };
        private static readonly byte[] key =
            { 0xE2, 0x4F, 0x7B, 0x5F, 0xCD, 0xE4, 0xC8, 0x6D, 0xDB, 0xD8, 0xFB, 0xD7, 0x40, 0x58, 0xC6, 0x78 };
        private static readonly byte[] extension =
            { 0xF8, 0x5A, 0x8C, 0x6A, 0xDE, 0xF5, 0xD9, 0x7E, 0xEC, 0xE9, 0x0C, 0xE3, 0x75, 0x8F, 0x29, 0x0B };

        private const int footerZeroes1 = 20;
        private const int footerZeroes2 = 120;

        protected const int footerCodeSize = 16;

        protected const string binarySeparator = "\0\x1";

        protected const string asciiSeparator = "::";

        protected static bool CheckEqual(byte[] data, byte[] original)
        {
            for (int i = 0; i < original.Length; i++)
                if (data[i] != original[i])
                    return false;
            return true;
        }

        public static bool IsBinary(Stream stream)
        {
            var isb = ReadHeader(stream);
            stream.Position = 0;
            return isb;
        }

        protected static void WriteHeader(Stream stream)
        {
            stream.Write(headerString, 0, headerString.Length);
        }

        protected static bool ReadHeader(Stream stream)
        {
            var buf = new byte[headerString.Length];
            stream.Read(buf, 0, buf.Length);
            return CheckEqual(buf, headerString);
        }

        static void Encrypt(byte[] a, byte[] b)
        {
            byte c = 64;
            for (int i = 0; i < footerCodeSize; i++)
            {
                a[i] = (byte)(a[i] ^ (byte)(c ^ b[i]));
                c = a[i];
            }
        }

        const string timePath1 = "FBXHeaderExtension";
        const string timePath2 = "CreationTimeStamp";
        static readonly Stack<string> timePath = new Stack<string>(new[] { timePath1, timePath2 });

        static int GetTimestampVar(FbxNode timestamp, string element)
        {
            var elementNode = timestamp[element];
            if (elementNode != null && elementNode.Properties.Count > 0)
            {
                var prop = elementNode.Properties[0];
                if (prop is int || prop is long)
                    return (int)prop;
            }
            throw new FbxException(timePath, -1, "Timestamp has no " + element);
        }

        protected static byte[] GenerateFooterCode(FbxNodeList document)
        {
            var timestamp = document.GetRelative(timePath1 + "/" + timePath2);
            if (timestamp == null)
                throw new FbxException(timePath, -1, "No creation timestamp");
            try
            {
                return GenerateFooterCode(
                    GetTimestampVar(timestamp, "Year"),
                    GetTimestampVar(timestamp, "Month"),
                    GetTimestampVar(timestamp, "Day"),
                    GetTimestampVar(timestamp, "Hour"),
                    GetTimestampVar(timestamp, "Minute"),
                    GetTimestampVar(timestamp, "Second"),
                    GetTimestampVar(timestamp, "Millisecond")
                    );
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new FbxException(timePath, -1, "Invalid timestamp");
            }
        }

        protected static byte[] GenerateFooterCode(
            int year, int month, int day,
            int hour, int minute, int second, int millisecond)
        {
            if (year < 0 || year > 9999)
                throw new ArgumentOutOfRangeException(nameof(year));
            if (month < 0 || month > 12)
                throw new ArgumentOutOfRangeException(nameof(month));
            if (day < 0 || day > 31)
                throw new ArgumentOutOfRangeException(nameof(day));
            if (hour < 0 || hour >= 24)
                throw new ArgumentOutOfRangeException(nameof(hour));
            if (minute < 0 || minute >= 60)
                throw new ArgumentOutOfRangeException(nameof(minute));
            if (second < 0 || second >= 60)
                throw new ArgumentOutOfRangeException(nameof(second));
            if (millisecond < 0 || millisecond >= 1000)
                throw new ArgumentOutOfRangeException(nameof(millisecond));

            var str = (byte[])sourceId.Clone();
            var mangledTime = $"{second:00}{month:00}{hour:00}{day:00}{(millisecond / 10):00}{year:0000}{minute:00}";
            var mangledBytes = Encoding.ASCII.GetBytes(mangledTime);
            Encrypt(str, mangledBytes);
            Encrypt(str, key);
            Encrypt(str, mangledBytes);
            return str;
        }

        protected void WriteFooter(BinaryWriter stream, int version)
        {
            var zeroes = new byte[Math.Max(footerZeroes1, footerZeroes2)];
            stream.Write(zeroes, 0, footerZeroes1);
            stream.Write(version);
            stream.Write(zeroes, 0, footerZeroes2);
            stream.Write(extension, 0, extension.Length);
        }

        static bool AllZero(byte[] array)
        {
            foreach (var b in array)
                if (b != 0)
                    return false;
            return true;
        }

        protected bool CheckFooter(BinaryReader stream, FbxVersion version)
        {
            var buffer = new byte[Math.Max(footerZeroes1, footerZeroes2)];
            stream.Read(buffer, 0, footerZeroes1);
            bool correct = AllZero(buffer);
            var readVersion = stream.ReadInt32();
            correct &= (readVersion == (int)version);
            stream.Read(buffer, 0, footerZeroes2);
            correct &= AllZero(buffer);
            stream.Read(buffer, 0, extension.Length);
            correct &= CheckEqual(buffer, extension);
            return correct;
        }
    }

    public class FbxBinaryReader : FbxBinary
    {
        private readonly BinaryReader stream;
        private readonly FbxErrorLevel errorLevel;

        private delegate object ReadPrimitive(BinaryReader reader);

        public FbxBinaryReader(Stream stream, FbxErrorLevel errorLevel = FbxErrorLevel.Checked)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (!stream.CanSeek)
                throw new ArgumentException(
                    "The stream must support seeking. Try reading the data into a buffer first");
            this.stream = new BinaryReader(stream, Encoding.ASCII);
            this.errorLevel = errorLevel;
        }

        object ReadProperty()
        {
            var dataType = (char)stream.ReadByte();
            switch (dataType)
            {
                case 'Y':
                    return stream.ReadInt16();
                case 'C':
                    return (char)stream.ReadByte();
                case 'I':
                    return stream.ReadInt32();
                case 'F':
                    return stream.ReadSingle();
                case 'D':
                    return stream.ReadDouble();
                case 'L':
                    return stream.ReadInt64();
                case 'f':
                    return ReadArray(br => br.ReadSingle(), typeof(float));
                case 'd':
                    return ReadArray(br => br.ReadDouble(), typeof(double));
                case 'l':
                    return ReadArray(br => br.ReadInt64(), typeof(long));
                case 'i':
                    return ReadArray(br => br.ReadInt32(), typeof(int));
                case 'b':
                    return ReadArray(br => br.ReadBoolean(), typeof(bool));
                case 'S':
                    var len = stream.ReadInt32();
                    var str = len == 0 ? "" : Encoding.ASCII.GetString(stream.ReadBytes(len));
                    if (str.Contains(binarySeparator))
                    {
                        var tokens = str.Split(new[] { binarySeparator }, StringSplitOptions.None);
                        var sb = new StringBuilder();
                        bool first = true;
                        for (int i = tokens.Length - 1; i >= 0; i--)
                        {
                            if (!first)
                                sb.Append(asciiSeparator);
                            sb.Append(tokens[i]);
                            first = false;
                        }
                        str = sb.ToString();
                    }
                    return str;
                case 'R':
                    return stream.ReadBytes(stream.ReadInt32());
                default:
                    throw new FbxException(stream.BaseStream.Position - 1,
                        "Invalid property data type `" + dataType + "'");
            }
        }

        Array ReadArray(ReadPrimitive readPrimitive, Type arrayType)
        {
            var len = stream.ReadInt32();
            var encoding = stream.ReadInt32();
            var compressedLen = stream.ReadInt32();
            var ret = Array.CreateInstance(arrayType, len);
            var s = stream;
            var endPos = stream.BaseStream.Position + compressedLen;
            if (encoding != 0)
            {
                if (errorLevel >= FbxErrorLevel.Checked)
                {
                    if (encoding != 1)
                        throw new FbxException(stream.BaseStream.Position - 1,
                            "Invalid compression encoding (must be 0 or 1)");
                    var cmf = stream.ReadByte();
                    if ((cmf & 0xF) != 8 || (cmf >> 4) > 7)
                        throw new FbxException(stream.BaseStream.Position - 1,
                            "Invalid compression format " + cmf);
                    var flg = stream.ReadByte();
                    if (errorLevel >= FbxErrorLevel.Strict && ((cmf << 8) + flg) % 31 != 0)
                        throw new FbxException(stream.BaseStream.Position - 1,
                            "Invalid compression FCHECK");
                    if ((flg & (1 << 5)) != 0)
                        throw new FbxException(stream.BaseStream.Position - 1,
                            "Invalid compression flags; dictionary not supported");
                }
                else
                {
                    stream.BaseStream.Position += 2;
                }
                var codec = new FbxDeflateWithChecksum(stream.BaseStream, CompressionMode.Decompress);
                s = new BinaryReader(codec);
            }
            try
            {
                for (int i = 0; i < len; i++)
                    ret.SetValue(readPrimitive(s), i);
            }
            catch (InvalidDataException)
            {
                throw new FbxException(stream.BaseStream.Position - 1,
                    "Compressed data was malformed");
            }
            if (encoding != 0)
            {
                if (errorLevel >= FbxErrorLevel.Checked)
                {
                    stream.BaseStream.Position = endPos - sizeof(int);
                    var checksumBytes = new byte[sizeof(int)];
                    stream.BaseStream.Read(checksumBytes, 0, checksumBytes.Length);
                    int checksum = 0;
                    for (int i = 0; i < checksumBytes.Length; i++)
                        checksum = (checksum << 8) + checksumBytes[i];
                    if (checksum != ((FbxDeflateWithChecksum)s.BaseStream).Checksum)
                        throw new FbxException(stream.BaseStream.Position,
                            "Compressed data has invalid checksum");
                }
                else
                {
                    stream.BaseStream.Position = endPos;
                }
            }
            return ret;
        }

        public FbxNode ReadNode()
        {
            var endOffset = stream.ReadInt32();
            var numProperties = stream.ReadInt32();
            var propertyListLen = stream.ReadInt32();
            var nameLen = stream.ReadByte();
            var name = nameLen == 0 ? "" : Encoding.ASCII.GetString(stream.ReadBytes(nameLen));

            if (endOffset == 0)
            {
                if (errorLevel >= FbxErrorLevel.Checked && (numProperties != 0 || propertyListLen != 0 || !string.IsNullOrEmpty(name)))
                    throw new FbxException(stream.BaseStream.Position,
                        "Invalid node; expected NULL record");
                return null;
            }

            var node = new FbxNode { Name = name };

            var propertyEnd = stream.BaseStream.Position + propertyListLen;
            for (int i = 0; i < numProperties; i++)
                node.Properties.Add(ReadProperty());

            if (errorLevel >= FbxErrorLevel.Checked && stream.BaseStream.Position != propertyEnd)
                throw new FbxException(stream.BaseStream.Position,
                    "Too many bytes in property list, end point is " + propertyEnd);

            var listLen = endOffset - stream.BaseStream.Position;
            if (errorLevel >= FbxErrorLevel.Checked && listLen < 0)
                throw new FbxException(stream.BaseStream.Position,
                    "Node has invalid end point");
            if (listLen > 0)
            {
                FbxNode nested;
                do
                {
                    nested = ReadNode();
                    node.Nodes.Add(nested);
                } while (nested != null);
                if (errorLevel >= FbxErrorLevel.Checked && stream.BaseStream.Position != endOffset)
                    throw new FbxException(stream.BaseStream.Position,
                        "Too many bytes in node, end point is " + endOffset);
            }
            return node;
        }

        public FbxDocument Read()
        {
            bool validHeader = ReadHeader(stream.BaseStream);
            if (errorLevel >= FbxErrorLevel.Strict && !validHeader)
                throw new FbxException(stream.BaseStream.Position,
                    "Invalid header string");
            var document = new FbxDocument { Version = (FbxVersion)stream.ReadInt32() };

            var dataPos = stream.BaseStream.Position;
            FbxNode nested;
            do
            {
                nested = ReadNode();
                if (nested != null)
                    document.Nodes.Add(nested);
            } while (nested != null);

            var footerCode = new byte[footerCodeSize];
            stream.BaseStream.Read(footerCode, 0, footerCode.Length);
            if (errorLevel >= FbxErrorLevel.Strict)
            {
                var validCode = GenerateFooterCode(document);
                if (!CheckEqual(footerCode, validCode))
                    throw new FbxException(stream.BaseStream.Position - footerCodeSize,
                        "Incorrect footer code");
            }

            dataPos = stream.BaseStream.Position;
            var validFooterExtension = CheckFooter(stream, document.Version);
            if (errorLevel >= FbxErrorLevel.Strict && !validFooterExtension)
                throw new FbxException(dataPos, "Invalid footer");
            return document;
        }
    }

    public class FbxBinaryWriter : FbxBinary
    {
        private readonly Stream output;
        private readonly MemoryStream memory;
        private readonly BinaryWriter stream;

        readonly Stack<string> nodePath = new Stack<string>();

        public int CompressionThreshold { get; set; } = 1024;

        public FbxBinaryWriter(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            output = stream;
            memory = new MemoryStream();
            this.stream = new BinaryWriter(memory, Encoding.ASCII);
        }

        private delegate void PropertyWriter(BinaryWriter sw, object obj);

        struct WriterInfo
        {
            public readonly char id;
            public readonly PropertyWriter writer;

            public WriterInfo(char id, PropertyWriter writer)
            {
                this.id = id;
                this.writer = writer;
            }
        }

        private static readonly Dictionary<Type, WriterInfo> writePropertyActions
            = new Dictionary<Type, WriterInfo>
            {
                { typeof(int),  new WriterInfo('I', (sw, obj) => sw.Write((int)obj)) },
                { typeof(short),  new WriterInfo('Y', (sw, obj) => sw.Write((short)obj)) },
                { typeof(long),   new WriterInfo('L', (sw, obj) => sw.Write((long)obj)) },
                { typeof(float),  new WriterInfo('F', (sw, obj) => sw.Write((float)obj)) },
                { typeof(double), new WriterInfo('D', (sw, obj) => sw.Write((double)obj)) },
                { typeof(bool),   new WriterInfo('C', (sw, obj) => sw.Write((byte)(char)obj)) },
                { typeof(byte[]), new WriterInfo('R', WriteRaw) },
                { typeof(string), new WriterInfo('S', WriteString) },
				{ typeof(int[]),    new WriterInfo('i', null) },
                { typeof(long[]),   new WriterInfo('l', null) },
                { typeof(float[]),  new WriterInfo('f', null) },
                { typeof(double[]), new WriterInfo('d', null) },
                { typeof(bool[]),   new WriterInfo('b', null) },
            };

        static void WriteRaw(BinaryWriter stream, object obj)
        {
            var bytes = (byte[])obj;
            stream.Write(bytes.Length);
            stream.Write(bytes);
        }

        static void WriteString(BinaryWriter stream, object obj)
        {
            var str = obj.ToString();
            if (str.Contains(asciiSeparator))
            {
                var tokens = str.Split(new[] { asciiSeparator }, StringSplitOptions.None);
                var sb = new StringBuilder();
                bool first = true;
                for (int i = tokens.Length - 1; i >= 0; i--)
                {
                    if (!first)
                        sb.Append(binarySeparator);
                    sb.Append(tokens[i]);
                    first = false;
                }
                str = sb.ToString();
            }
            var bytes = Encoding.ASCII.GetBytes(str);
            stream.Write(bytes.Length);
            stream.Write(bytes);
        }

        void WriteArray(Array array, Type elementType, PropertyWriter writer)
        {
            stream.Write(array.Length);

            var size = array.Length * Marshal.SizeOf(elementType);
            bool compress = size >= CompressionThreshold;
            stream.Write(compress ? 1 : 0);

            var sw = stream;
            FbxDeflateWithChecksum codec = null;

            var compressLengthPos = stream.BaseStream.Position;
            stream.Write(0);
            var dataStart = stream.BaseStream.Position;
            if (compress)
            {
                stream.Write(new byte[] { 0x58, 0x85 }, 0, 2);
                codec = new FbxDeflateWithChecksum(stream.BaseStream, CompressionMode.Compress, true);
                sw = new BinaryWriter(codec);
            }
            foreach (var obj in array)
                writer(sw, obj);
            if (compress)
            {
                codec.Close();
                var checksum = codec.Checksum;
                byte[] bytes =
                {
                    (byte)((checksum >> 24) & 0xFF),
                    (byte)((checksum >> 16) & 0xFF),
                    (byte)((checksum >> 8) & 0xFF),
                    (byte)(checksum & 0xFF),
                };
                stream.Write(bytes);
            }

            if (compress)
            {
                var dataEnd = stream.BaseStream.Position;
                stream.BaseStream.Position = compressLengthPos;
                stream.Write((int)(dataEnd - dataStart));
                stream.BaseStream.Position = dataEnd;
            }
        }

        void WriteProperty(object obj, int id)
        {
            if (obj == null)
                return;
            WriterInfo writerInfo;
            if (!writePropertyActions.TryGetValue(obj.GetType(), out writerInfo))
                throw new FbxException(nodePath, id,
                    "Invalid property type " + obj.GetType());
            stream.Write((byte)writerInfo.id);
            if (writerInfo.writer == null)
            {
                var elementType = obj.GetType().GetElementType();
                WriteArray((Array)obj, elementType, writePropertyActions[elementType].writer);
            }
            else
                writerInfo.writer(stream, obj);
        }

        static readonly byte[] nullData = new byte[13];

        void WriteNode(FbxNode node)
        {
            if (node == null)
            {
                stream.BaseStream.Write(nullData, 0, nullData.Length);
            }
            else
            {
                nodePath.Push(node.Name ?? "");
                var name = string.IsNullOrEmpty(node.Name) ? null : Encoding.ASCII.GetBytes(node.Name);
                if (name != null && name.Length > byte.MaxValue)
                    throw new FbxException(stream.BaseStream.Position,
                        "Node name is too long");

                var endOffsetPos = stream.BaseStream.Position;
                stream.Write(0);
                stream.Write(node.Properties.Count);
                var propertyLengthPos = stream.BaseStream.Position;
                stream.Write(0);
                stream.Write((byte)(name?.Length ?? 0));
                if (name != null)
                    stream.Write(name);

                var propertyBegin = stream.BaseStream.Position;
                for (int i = 0; i < node.Properties.Count; i++)
                {
                    WriteProperty(node.Properties[i], i);
                }
                var propertyEnd = stream.BaseStream.Position;
                stream.BaseStream.Position = propertyLengthPos;
                stream.Write((int)(propertyEnd - propertyBegin));
                stream.BaseStream.Position = propertyEnd;

                if (node.Nodes.Count > 0)
                {
                    foreach (var n in node.Nodes)
                    {
                        if (n == null)
                            continue;
                        WriteNode(n);
                    }
                    WriteNode(null);
                }

                var dataEnd = stream.BaseStream.Position;
                stream.BaseStream.Position = endOffsetPos;
                stream.Write((int)dataEnd);
                stream.BaseStream.Position = dataEnd;

                nodePath.Pop();
            }
        }

        public void Write(FbxDocument document)
        {
            stream.BaseStream.Position = 0;
            WriteHeader(stream.BaseStream);
            stream.Write((int)document.Version);
            nodePath.Clear();
            foreach (var node in document.Nodes)
                WriteNode(node);
            WriteNode(null);
            stream.Write(GenerateFooterCode(document));
            WriteFooter(stream, (int)document.Version);
            output.Write(memory.GetBuffer(), 0, (int)memory.Position);
        }
    }

    public class FbxDocument : FbxNodeList
    {
        public FbxVersion Version { get; set; } = FbxVersion.v7_4;

        public List<FbxNode> GetSceneNodes()
        {
            var fobjs = this["Objects"];
            if (fobjs?.Nodes == null)
                return null;

            var fconns = this["Connections"];
            if (fconns?.Nodes == null)
                return null;

            var fobjdict = new Dictionary<long, FbxNode>();
            var rootnodes = new List<FbxNode>();

            foreach (var node in fobjs.Nodes)
            {
                if (node == null) continue;
                long id = 0;
                if (node.Value is long)
                { id = (long)node.Value; }
                if (id == 0)
                { }
                fobjdict[id] = node;
            }

            foreach (var node in fconns.Nodes)
            {
                if (node == null) continue;
                var connType = node.Value as string;
                if ((connType == "OO") || (connType == "OP"))
                {
                    if (node.Properties.Count < 3) { continue; }
                    if (!(node.Properties[1] is long)) { continue; }
                    if (!(node.Properties[2] is long)) { continue; }
                    long cid = (long)node.Properties[1];
                    long pid = (long)node.Properties[2];
                    FbxNode cnode;
                    FbxNode pnode;
                    fobjdict.TryGetValue(cid, out cnode);
                    fobjdict.TryGetValue(pid, out pnode);
                    if (cnode == null) { continue; }
                    if (pnode == null)
                    {
                        rootnodes.Add(cnode);
                    }
                    else
                    {
                        pnode.Connections.Add(cnode);
                    }
                }
                else
                { }
            }

            return rootnodes;
        }

    }

    public class FbxException : Exception
    {
        public FbxException(long position, string message) :
            base($"{message}, near offset {position}")
        {
        }

        public FbxException(int line, int column, string message) :
            base($"{message}, near line {line} column {column}")
        {
        }

        public FbxException(Stack<string> nodePath, int propertyID, string message) :
            base(message + ", at " + string.Join("/", nodePath.ToArray()) + (propertyID < 0 ? "" : $"[{propertyID}]"))
        {
        }
    }

    public class FbxNode : FbxNodeList
    {
        public string Name { get; set; }

        public List<object> Properties { get; } = new List<object>();

        public List<FbxNode> Connections { get; } = new List<FbxNode>();

        public object Value
        {
            get { return Properties.Count < 1 ? null : Properties[0]; }
            set
            {
                if (Properties.Count < 1)
                    Properties.Add(value);
                else
                    Properties[0] = value;
            }
        }

        public bool IsEmpty => string.IsNullOrEmpty(Name) && Properties.Count == 0 && Nodes.Count == 0;

        public override string ToString()
        {
            return Name + ((Value != null) ? (": " + Value.ToString()) : "");
        }
    }

    public abstract class FbxNodeList
    {
        public List<FbxNode> Nodes { get; } = new List<FbxNode>();

        public FbxNode this[string name] { get { return Nodes.Find(n => n != null && n.Name == name); } }

        public FbxNode GetRelative(string path)
        {
            var tokens = path.Split('/');
            FbxNodeList n = this;
            foreach (var t in tokens)
            {
                if (t == "")
                    continue;
                n = n[t];
                if (n == null)
                    break;
            }
            return n as FbxNode;
        }
    }

    public enum FbxVersion
    {
        v6_0 = 6000,

        v6_1 = 6100,

        v7_0 = 7000,

        v7_1 = 7100,

        v7_2 = 7200,

        v7_3 = 7300,

        v7_4 = 7400,
    }

    public enum FbxErrorLevel
    {
        Permissive = 0,

        Checked = 1,

        Strict = 2,
    }

    public class FbxDeflateWithChecksum : DeflateStream
    {
        private const int modAdler = 65521;
        private uint checksumA;
        private uint checksumB;

        public int Checksum
        {
            get
            {
                checksumA %= modAdler;
                checksumB %= modAdler;
                return (int)((checksumB << 16) | checksumA);
            }
        }

        public FbxDeflateWithChecksum(Stream stream, CompressionMode mode) : base(stream, mode)
        {
            ResetChecksum();
        }

        public FbxDeflateWithChecksum(Stream stream, CompressionMode mode, bool leaveOpen) : base(stream, mode, leaveOpen)
        {
            ResetChecksum();
        }

        void CalcChecksum(byte[] array, int offset, int count)
        {
            checksumA %= modAdler;
            checksumB %= modAdler;
            for (int i = offset, c = 0; i < (offset + count); i++, c++)
            {
                checksumA += array[i];
                checksumB += checksumA;
                if (c > 4000)
                {
                    checksumA %= modAdler;
                    checksumB %= modAdler;
                    c = 0;
                }
            }
        }

        public override void Write(byte[] array, int offset, int count)
        {
            base.Write(array, offset, count);
            CalcChecksum(array, offset, count);
        }

        public override int Read(byte[] array, int offset, int count)
        {
            var ret = base.Read(array, offset, count);
            CalcChecksum(array, offset, count);
            return ret;
        }

        public void ResetChecksum()
        {
            checksumA = 1;
            checksumB = 0;
        }
    }

}
