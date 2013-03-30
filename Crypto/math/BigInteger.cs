using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Utilities;

namespace Org.BouncyCastle.Math
{
#if !(NETCF_1_0 || NETCF_2_0 || SILVERLIGHT)
    [Serializable]
#endif
    public class BigInteger : IBigInteger
    {
        // The primes b/w 2 and ~2^10
        /*
                3   5   7   11  13  17  19  23  29
            31  37  41  43  47  53  59  61  67  71
            73  79  83  89  97  101 103 107 109 113
            127 131 137 139 149 151 157 163 167 173
            179 181 191 193 197 199 211 223 227 229
            233 239 241 251 257 263 269 271 277 281
            283 293 307 311 313 317 331 337 347 349
            353 359 367 373 379 383 389 397 401 409
            419 421 431 433 439 443 449 457 461 463
            467 479 487 491 499 503 509 521 523 541
            547 557 563 569 571 577 587 593 599 601
            607 613 617 619 631 641 643 647 653 659
            661 673 677 683 691 701 709 719 727 733
            739 743 751 757 761 769 773 787 797 809
            811 821 823 827 829 839 853 857 859 863
            877 881 883 887 907 911 919 929 937 941
            947 953 967 971 977 983 991 997
            1009 1013 1019 1021 1031
        */

        // Each list has a product < 2^31
        private static readonly int[][] primeLists = new int[][]
		{
			new int[]{ 3, 5, 7, 11, 13, 17, 19, 23 },
			new int[]{ 29, 31, 37, 41, 43 },
			new int[]{ 47, 53, 59, 61, 67 },
			new int[]{ 71, 73, 79, 83 },
			new int[]{ 89, 97, 101, 103 },

			new int[]{ 107, 109, 113, 127 },
			new int[]{ 131, 137, 139, 149 },
			new int[]{ 151, 157, 163, 167 },
			new int[]{ 173, 179, 181, 191 },
			new int[]{ 193, 197, 199, 211 },

			new int[]{ 223, 227, 229 },
			new int[]{ 233, 239, 241 },
			new int[]{ 251, 257, 263 },
			new int[]{ 269, 271, 277 },
			new int[]{ 281, 283, 293 },

			new int[]{ 307, 311, 313 },
			new int[]{ 317, 331, 337 },
			new int[]{ 347, 349, 353 },
			new int[]{ 359, 367, 373 },
			new int[]{ 379, 383, 389 },

			new int[]{ 397, 401, 409 },
			new int[]{ 419, 421, 431 },
			new int[]{ 433, 439, 443 },
			new int[]{ 449, 457, 461 },
			new int[]{ 463, 467, 479 },

			new int[]{ 487, 491, 499 },
			new int[]{ 503, 509, 521 },
			new int[]{ 523, 541, 547 },
			new int[]{ 557, 563, 569 },
			new int[]{ 571, 577, 587 },

			new int[]{ 593, 599, 601 },
			new int[]{ 607, 613, 617 },
			new int[]{ 619, 631, 641 },
			new int[]{ 643, 647, 653 },
			new int[]{ 659, 661, 673 },

			new int[]{ 677, 683, 691 },
			new int[]{ 701, 709, 719 },
			new int[]{ 727, 733, 739 },
			new int[]{ 743, 751, 757 },
			new int[]{ 761, 769, 773 },

			new int[]{ 787, 797, 809 },
			new int[]{ 811, 821, 823 },
			new int[]{ 827, 829, 839 },
			new int[]{ 853, 857, 859 },
			new int[]{ 863, 877, 881 },

			new int[]{ 883, 887, 907 },
			new int[]{ 911, 919, 929 },
			new int[]{ 937, 941, 947 },
			new int[]{ 953, 967, 971 },
			new int[]{ 977, 983, 991 },

			new int[]{ 997, 1009, 1013 },
			new int[]{ 1019, 1021, 1031 },
		};

        private static readonly int[] primeProducts;

        private const long IMASK = 0xffffffffL;
        private static readonly ulong UIMASK = (ulong)IMASK;

        private static readonly int[] ZeroMagnitude = new int[0];
        private static readonly byte[] ZeroEncoding = new byte[0];

        public static readonly IBigInteger Zero = new BigInteger(0, ZeroMagnitude, false);
        public static readonly IBigInteger One = createUValueOf(1);
        public static readonly IBigInteger Two = createUValueOf(2);
        public static readonly IBigInteger Three = createUValueOf(3);
        public static readonly IBigInteger Ten = createUValueOf(10);

        private static readonly int chunk2 = 1; // TODO Parse 64 bits at a time
        private static readonly IBigInteger radix2 = ValueOf(2);
        private static readonly IBigInteger radix2E = radix2.Pow(chunk2);

        private static readonly int chunk10 = 19;
        private static readonly IBigInteger radix10 = ValueOf(10);
        private static readonly IBigInteger radix10E = radix10.Pow(chunk10);

        private static readonly int chunk16 = 16;
        private static readonly IBigInteger radix16 = ValueOf(16);
        private static readonly IBigInteger radix16E = radix16.Pow(chunk16);

        private static readonly IRandom RandomSource = new bcpg.Random();

        private const int BitsPerByte = 8;
        private const int BitsPerInt = 32;
        private const int BytesPerInt = 4;

        static BigInteger()
        {
            primeProducts = new int[primeLists.Length];

            for (int i = 0; i < primeLists.Length; ++i)
            {
                int[] primeList = primeLists[i];
                int product = 1;
                for (int j = 0; j < primeList.Length; ++j)
                {
                    product *= primeList[j];
                }
                primeProducts[i] = product;
            }
        }

        private int nBits = -1; // cache BitCount() value
        private int nBitLength = -1; // cache calcBitLength() value
        private long mQuote = -1L; // -m^(-1) mod b, b = 2^32 (see Montgomery mult.)

        private static int GetByteLength(
            int nBits)
        {
            return (nBits + BitsPerByte - 1) / BitsPerByte;
        }

        private BigInteger()
        {
        }

        private BigInteger(
            int signum,
            int[] mag,
            bool checkMag)
        {
            if (checkMag)
            {
                int i = 0;
                while (i < mag.Length && mag[i] == 0)
                {
                    ++i;
                }

                if (i == mag.Length)
                {
                    //					this.sign = 0;
                    this.Magnitude = ZeroMagnitude;
                }
                else
                {
                    this.SignValue = signum;

                    if (i == 0)
                    {
                        this.Magnitude = mag;
                    }
                    else
                    {
                        // strip leading 0 words
                        this.Magnitude = new int[mag.Length - i];
                        Array.Copy(mag, i, this.Magnitude, 0, this.Magnitude.Length);
                    }
                }
            }
            else
            {
                this.SignValue = signum;
                this.Magnitude = mag;
            }
        }

        public BigInteger(
            string value)
            : this(value, 10)
        {
        }

        public BigInteger(
            string str,
            int radix)
        {
            if (str.Length == 0)
                throw new FormatException("Zero length BigInteger");

            NumberStyles style;
            int chunk;
            IBigInteger r;
            IBigInteger rE;

            switch (radix)
            {
                case 2:
                    // Is there anyway to restrict to binary digits?
                    style = NumberStyles.Integer;
                    chunk = chunk2;
                    r = radix2;
                    rE = radix2E;
                    break;
                case 10:
                    // This style seems to handle spaces and minus SignValue already (our processing redundant?)
                    style = NumberStyles.Integer;
                    chunk = chunk10;
                    r = radix10;
                    rE = radix10E;
                    break;
                case 16:
                    // TODO Should this be HexNumber?
                    style = NumberStyles.AllowHexSpecifier;
                    chunk = chunk16;
                    r = radix16;
                    rE = radix16E;
                    break;
                default:
                    throw new FormatException("Only bases 2, 10, or 16 allowed");
            }


            int index = 0;
            SignValue = 1;

            if (str[0] == '-')
            {
                if (str.Length == 1)
                    throw new FormatException("Zero length BigInteger");

                SignValue = -1;
                index = 1;
            }

            // strip leading zeros from the string str
            while (index < str.Length && Int32.Parse(str[index].ToString(), style) == 0)
            {
                index++;
            }

            if (index >= str.Length)
            {
                // zero value - we're done
                SignValue = 0;
                Magnitude = ZeroMagnitude;
                return;
            }

            //////
            // could we work out the max number of ints required to store
            // str.Length digits in the given base, then allocate that
            // storage in one hit?, then Generate the Magnitude in one hit too?
            //////

            IBigInteger b = Zero;


            int next = index + chunk;

            if (next <= str.Length)
            {
                do
                {
                    string s = str.Substring(index, chunk);
                    ulong i = ulong.Parse(s, style);
                    IBigInteger bi = createUValueOf(i);

                    switch (radix)
                    {
                        case 2:
                            // TODO Need this because we are parsing in radix 10 above
                            if (i > 1)
                                throw new FormatException("Bad character in radix 2 string: " + s);

                            // TODO Parse 64 bits at a time
                            b = b.ShiftLeft(1);
                            break;
                        case 16:
                            b = b.ShiftLeft(64);
                            break;
                        default:
                            b = b.Multiply(rE);
                            break;
                    }

                    b = b.Add(bi);

                    index = next;
                    next += chunk;
                }
                while (next <= str.Length);
            }

            if (index < str.Length)
            {
                string s = str.Substring(index);
                ulong i = ulong.Parse(s, style);
                IBigInteger bi = createUValueOf(i);

                if (b.SignValue > 0)
                {
                    if (radix == 2)
                    {
                        // NB: Can't reach here since we are parsing one char at a time
                        Debug.Assert(false);

                        // TODO Parse all bits at once
                        //						b = b.ShiftLeft(s.Length);
                    }
                    else if (radix == 16)
                    {
                        b = b.ShiftLeft(s.Length << 2);
                    }
                    else
                    {
                        b = b.Multiply(r.Pow(s.Length));
                    }

                    b = b.Add(bi);
                }
                else
                {
                    b = bi;
                }
            }

            // Note: This is the previous (slower) algorithm
            //			while (index < value.Length)
            //            {
            //				char c = value[index];
            //				string s = c.ToString();
            //				int i = Int32.Parse(s, style);
            //
            //                b = b.Multiply(r).Add(ValueOf(i));
            //                index++;
            //            }

            Magnitude = b.Magnitude;
        }

        public BigInteger(
            byte[] bytes)
            : this(bytes, 0, bytes.Length)
        {
        }

        public BigInteger(
            byte[] bytes,
            int offset,
            int length)
        {
            if (length == 0)
                throw new FormatException("Zero length BigInteger");

            // TODO Move this processing into MakeMagnitude (provide SignValue argument)
            if ((sbyte)bytes[offset] < 0)
            {
                this.SignValue = -1;

                int end = offset + length;

                int iBval;
                // strip leading SignValue bytes
                for (iBval = offset; iBval < end && ((sbyte)bytes[iBval] == -1); iBval++)
                {
                }

                if (iBval >= end)
                {
                    this.Magnitude = One.Magnitude;
                }
                else
                {
                    int numBytes = end - iBval;
                    byte[] inverse = new byte[numBytes];

                    int index = 0;
                    while (index < numBytes)
                    {
                        inverse[index++] = (byte)~bytes[iBval++];
                    }

                    Debug.Assert(iBval == end);

                    while (inverse[--index] == byte.MaxValue)
                    {
                        inverse[index] = byte.MinValue;
                    }

                    inverse[index]++;

                    this.Magnitude = MakeMagnitude(inverse, 0, inverse.Length);
                }
            }
            else
            {
                // strip leading zero bytes and return Magnitude bytes
                this.Magnitude = MakeMagnitude(bytes, offset, length);
                this.SignValue = this.Magnitude.Length > 0 ? 1 : 0;
            }
        }

        private static int[] MakeMagnitude(
            byte[] bytes,
            int offset,
            int length)
        {
            int end = offset + length;

            // strip leading zeros
            int firstSignificant;
            for (firstSignificant = offset; firstSignificant < end
                && bytes[firstSignificant] == 0; firstSignificant++)
            {
            }

            if (firstSignificant >= end)
            {
                return ZeroMagnitude;
            }

            int nInts = (end - firstSignificant + 3) / BytesPerInt;
            int bCount = (end - firstSignificant) % BytesPerInt;
            if (bCount == 0)
            {
                bCount = BytesPerInt;
            }

            if (nInts < 1)
            {
                return ZeroMagnitude;
            }

            int[] mag = new int[nInts];

            int v = 0;
            int magnitudeIndex = 0;
            for (int i = firstSignificant; i < end; ++i)
            {
                v <<= 8;
                v |= bytes[i] & 0xff;
                bCount--;
                if (bCount <= 0)
                {
                    mag[magnitudeIndex] = v;
                    magnitudeIndex++;
                    bCount = BytesPerInt;
                    v = 0;
                }
            }

            if (magnitudeIndex < mag.Length)
            {
                mag[magnitudeIndex] = v;
            }

            return mag;
        }

        public BigInteger(
            int sign,
            byte[] bytes)
            : this(sign, bytes, 0, bytes.Length)
        {
        }

        public BigInteger(
            int sign,
            byte[] bytes,
            int offset,
            int length)
        {
            if (sign < -1 || sign > 1)
                throw new FormatException("Invalid sign value");

            if (sign == 0)
            {
                //this.sign = 0;
                this.Magnitude = ZeroMagnitude;
            }
            else
            {
                // copy bytes
                this.Magnitude = MakeMagnitude(bytes, offset, length);
                this.SignValue = this.Magnitude.Length < 1 ? 0 : sign;
            }
        }

        public BigInteger(
            int sizeInBits,
            IRandom random)
        {
            if (sizeInBits < 0)
                throw new ArgumentException("sizeInBits must be non-negative");

            this.nBits = -1;
            this.nBitLength = -1;

            if (sizeInBits == 0)
            {
                //				this.sign = 0;
                this.Magnitude = ZeroMagnitude;
                return;
            }

            int nBytes = GetByteLength(sizeInBits);
            byte[] b = new byte[nBytes];
            random.NextBytes(b);

            // strip off any excess bits in the MSB
            b[0] &= rndMask[BitsPerByte * nBytes - sizeInBits];

            this.Magnitude = MakeMagnitude(b, 0, b.Length);
            this.SignValue = this.Magnitude.Length < 1 ? 0 : 1;
        }

        private static readonly byte[] rndMask = { 255, 127, 63, 31, 15, 7, 3, 1 };

        public BigInteger(
            int bitLength,
            int certainty,
            IRandom random)
        {
            if (bitLength < 2)
                throw new ArithmeticException("bitLength < 2");

            this.SignValue = 1;
            this.nBitLength = bitLength;

            if (bitLength == 2)
            {
                this.Magnitude = random.Next(2) == 0
                    ? Two.Magnitude
                    : Three.Magnitude;
                return;
            }

            int nBytes = GetByteLength(bitLength);
            byte[] b = new byte[nBytes];

            int xBits = BitsPerByte * nBytes - bitLength;
            byte mask = rndMask[xBits];

            for (; ; )
            {
                random.NextBytes(b);

                // strip off any excess bits in the MSB
                b[0] &= mask;

                // ensure the leading bit is 1 (to meet the strength requirement)
                b[0] |= (byte)(1 << (7 - xBits));

                // ensure the trailing bit is 1 (i.e. must be odd)
                b[nBytes - 1] |= 1;

                this.Magnitude = MakeMagnitude(b, 0, b.Length);
                this.nBits = -1;
                this.mQuote = -1L;

                if (certainty < 1)
                    break;

                if (CheckProbablePrime(certainty, random))
                    break;

                if (bitLength > 32)
                {
                    for (int rep = 0; rep < 10000; ++rep)
                    {
                        int n = 33 + random.Next(bitLength - 2);
                        this.Magnitude[this.Magnitude.Length - (n >> 5)] ^= (1 << (n & 31));
                        this.Magnitude[this.Magnitude.Length - 1] ^= ((random.Next() + 1) << 1);
                        this.mQuote = -1L;

                        if (CheckProbablePrime(certainty, random))
                            return;
                    }
                }
            }
        }

        public IBigInteger Abs()
        {
            return SignValue >= 0 ? this : Negate();
        }

        /**
         * return a = a + b - b preserved.
         */
        private static int[] AddMagnitudes(
            int[] a,
            int[] b)
        {
            int tI = a.Length - 1;
            int vI = b.Length - 1;
            long m = 0;

            while (vI >= 0)
            {
                m += ((long)(uint)a[tI] + (long)(uint)b[vI--]);
                a[tI--] = (int)m;
                m = (long)((ulong)m >> 32);
            }

            if (m != 0)
            {
                while (tI >= 0 && ++a[tI--] == 0)
                {
                }
            }

            return a;
        }

        public IBigInteger Add(
            IBigInteger value)
        {
            if (this.SignValue == 0)
                return value;

            if (this.SignValue != value.SignValue)
            {
                if (value.SignValue == 0)
                    return this;

                if (value.SignValue < 0)
                    return Subtract(value.Negate());

                return value.Subtract(Negate());
            }

            return AddToMagnitude(value.Magnitude);
        }

        private IBigInteger AddToMagnitude(
            int[] magToAdd)
        {
            int[] big, small;
            if (this.Magnitude.Length < magToAdd.Length)
            {
                big = magToAdd;
                small = this.Magnitude;
            }
            else
            {
                big = this.Magnitude;
                small = magToAdd;
            }

            // Conservatively avoid over-allocation when no overflow possible
            uint limit = uint.MaxValue;
            if (big.Length == small.Length)
                limit -= (uint)small[0];

            bool possibleOverflow = (uint)big[0] >= limit;

            int[] bigCopy;
            if (possibleOverflow)
            {
                bigCopy = new int[big.Length + 1];
                big.CopyTo(bigCopy, 1);
            }
            else
            {
                bigCopy = (int[])big.Clone();
            }

            bigCopy = AddMagnitudes(bigCopy, small);

            return new BigInteger(this.SignValue, bigCopy, possibleOverflow);
        }

        public IBigInteger And(
            IBigInteger value)
        {
            if (this.SignValue == 0 || value.SignValue == 0)
            {
                return Zero;
            }

            int[] aMag = this.SignValue > 0
                ? this.Magnitude
                : Add(One).Magnitude;

            int[] bMag = value.SignValue > 0
                ? value.Magnitude
                : value.Add(One).Magnitude;

            bool resultNeg = SignValue < 0 && value.SignValue < 0;
            int resultLength = System.Math.Max(aMag.Length, bMag.Length);
            int[] resultMag = new int[resultLength];

            int aStart = resultMag.Length - aMag.Length;
            int bStart = resultMag.Length - bMag.Length;

            for (int i = 0; i < resultMag.Length; ++i)
            {
                int aWord = i >= aStart ? aMag[i - aStart] : 0;
                int bWord = i >= bStart ? bMag[i - bStart] : 0;

                if (this.SignValue < 0)
                {
                    aWord = ~aWord;
                }

                if (value.SignValue < 0)
                {
                    bWord = ~bWord;
                }

                resultMag[i] = aWord & bWord;

                if (resultNeg)
                {
                    resultMag[i] = ~resultMag[i];
                }
            }

            IBigInteger result = new BigInteger(1, resultMag, true);

            // TODO Optimise this case
            if (resultNeg)
            {
                result = result.Not();
            }

            return result;
        }

        public IBigInteger AndNot(
            IBigInteger val)
        {
            return And(val.Not());
        }

        public int[] Magnitude { get; set; }

        public int BitCount
        {
            get
            {
                if (nBits == -1)
                {
                    if (SignValue < 0)
                    {
                        // TODO Optimise this case
                        nBits = Not().BitCount;
                    }
                    else
                    {
                        int sum = 0;
                        for (int i = 0; i < Magnitude.Length; i++)
                        {
                            sum += bitCounts[(byte)Magnitude[i]];
                            sum += bitCounts[(byte)(Magnitude[i] >> 8)];
                            sum += bitCounts[(byte)(Magnitude[i] >> 16)];
                            sum += bitCounts[(byte)(Magnitude[i] >> 24)];
                        }
                        nBits = sum;
                    }
                }

                return nBits;
            }
        }

        private readonly static byte[] bitCounts =
		{
			0, 1, 1, 2, 1, 2, 2, 3, 1, 2, 2, 3, 2, 3, 3, 4, 1,
			2, 2, 3, 2, 3, 3, 4, 2, 3, 3, 4, 3, 4, 4, 5, 1, 2, 2, 3, 2, 3, 3, 4, 2, 3, 3, 4, 3, 4,
			4, 5, 2, 3, 3, 4, 3, 4, 4, 5, 3, 4, 4, 5, 4, 5, 5, 6, 1, 2, 2, 3, 2, 3, 3, 4, 2, 3, 3,
			4, 3, 4, 4, 5, 2, 3, 3, 4, 3, 4, 4, 5, 3, 4, 4, 5, 4, 5, 5, 6, 2, 3, 3, 4, 3, 4, 4, 5,
			3, 4, 4, 5, 4, 5, 5, 6, 3, 4, 4, 5, 4, 5, 5, 6, 4, 5, 5, 6, 5, 6, 6, 7, 1, 2, 2, 3, 2,
			3, 3, 4, 2, 3, 3, 4, 3, 4, 4, 5, 2, 3, 3, 4, 3, 4, 4, 5, 3, 4, 4, 5, 4, 5, 5, 6, 2, 3,
			3, 4, 3, 4, 4, 5, 3, 4, 4, 5, 4, 5, 5, 6, 3, 4, 4, 5, 4, 5, 5, 6, 4, 5, 5, 6, 5, 6, 6,
			7, 2, 3, 3, 4, 3, 4, 4, 5, 3, 4, 4, 5, 4, 5, 5, 6, 3, 4, 4, 5, 4, 5, 5, 6, 4, 5, 5, 6,
			5, 6, 6, 7, 3, 4, 4, 5, 4, 5, 5, 6, 4, 5, 5, 6, 5, 6, 6, 7, 4, 5, 5, 6, 5, 6, 6, 7, 5,
			6, 6, 7, 6, 7, 7, 8
		};

        private int calcBitLength(
            int indx,
            int[] mag)
        {
            for (; ; )
            {
                if (indx >= mag.Length)
                    return 0;

                if (mag[indx] != 0)
                    break;

                ++indx;
            }

            // bit length for everything after the first int
            int bitLength = 32 * ((mag.Length - indx) - 1);

            // and determine bitlength of first int
            int firstMag = mag[indx];
            bitLength += BitLen(firstMag);

            // Check for negative powers of two
            if (SignValue < 0 && ((firstMag & -firstMag) == firstMag))
            {
                do
                {
                    if (++indx >= mag.Length)
                    {
                        --bitLength;
                        break;
                    }
                }
                while (mag[indx] == 0);
            }

            return bitLength;
        }

        public int BitLength
        {
            get
            {
                if (nBitLength == -1)
                {
                    nBitLength = SignValue == 0
                        ? 0
                        : calcBitLength(0, Magnitude);
                }

                return nBitLength;
            }
        }

        //
        // BitLen(value) is the number of bits in value.
        //
        private static int BitLen(
            int w)
        {
            // Binary search - decision tree (5 tests, rarely 6)
            return (w < 1 << 15 ? (w < 1 << 7
                ? (w < 1 << 3 ? (w < 1 << 1
                ? (w < 1 << 0 ? (w < 0 ? 32 : 0) : 1)
                : (w < 1 << 2 ? 2 : 3)) : (w < 1 << 5
                ? (w < 1 << 4 ? 4 : 5)
                : (w < 1 << 6 ? 6 : 7)))
                : (w < 1 << 11
                ? (w < 1 << 9 ? (w < 1 << 8 ? 8 : 9) : (w < 1 << 10 ? 10 : 11))
                : (w < 1 << 13 ? (w < 1 << 12 ? 12 : 13) : (w < 1 << 14 ? 14 : 15)))) : (w < 1 << 23 ? (w < 1 << 19
                ? (w < 1 << 17 ? (w < 1 << 16 ? 16 : 17) : (w < 1 << 18 ? 18 : 19))
                : (w < 1 << 21 ? (w < 1 << 20 ? 20 : 21) : (w < 1 << 22 ? 22 : 23))) : (w < 1 << 27
                ? (w < 1 << 25 ? (w < 1 << 24 ? 24 : 25) : (w < 1 << 26 ? 26 : 27))
                : (w < 1 << 29 ? (w < 1 << 28 ? 28 : 29) : (w < 1 << 30 ? 30 : 31)))));
        }

        //		private readonly static byte[] bitLengths =
        //		{
        //			0, 1, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4,
        //			5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6,
        //			6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
        //			7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
        //			7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 8, 8, 8, 8,
        //			8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        //			8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        //			8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        //			8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        //			8, 8, 8, 8, 8, 8, 8, 8
        //		};

        public bool QuickPow2Check()
        {
            return SignValue > 0 && nBits == 1;
        }

        public int CompareTo(
            object obj)
        {
            return CompareTo((IBigInteger)obj);
        }

        /**
         * unsigned comparison on two arrays - note the arrays may
         * start with leading zeros.
         */
        private static int CompareTo(
            int xIndx,
            int[] x,
            int yIndx,
            int[] y)
        {
            while (xIndx != x.Length && x[xIndx] == 0)
            {
                xIndx++;
            }

            while (yIndx != y.Length && y[yIndx] == 0)
            {
                yIndx++;
            }

            return CompareNoLeadingZeroes(xIndx, x, yIndx, y);
        }

        private static int CompareNoLeadingZeroes(
            int xIndx,
            int[] x,
            int yIndx,
            int[] y)
        {
            int diff = (x.Length - y.Length) - (xIndx - yIndx);

            if (diff != 0)
            {
                return diff < 0 ? -1 : 1;
            }

            // lengths of magnitudes the same, test the Magnitude values

            while (xIndx < x.Length)
            {
                uint v1 = (uint)x[xIndx++];
                uint v2 = (uint)y[yIndx++];

                if (v1 != v2)
                    return v1 < v2 ? -1 : 1;
            }

            return 0;
        }

        public int CompareTo(
            IBigInteger value)
        {
            return SignValue < value.SignValue ? -1
                : SignValue > value.SignValue ? 1
                : SignValue == 0 ? 0
                : SignValue * CompareNoLeadingZeroes(0, Magnitude, 0, value.Magnitude);
        }

        /**
         * return z = x / y - done in place (z value preserved, x contains the
         * remainder)
         */
        private int[] Divide(
            int[] x,
            int[] y)
        {
            int xStart = 0;
            while (xStart < x.Length && x[xStart] == 0)
            {
                ++xStart;
            }

            int yStart = 0;
            while (yStart < y.Length && y[yStart] == 0)
            {
                ++yStart;
            }

            Debug.Assert(yStart < y.Length);

            int xyCmp = CompareNoLeadingZeroes(xStart, x, yStart, y);
            int[] count;

            if (xyCmp > 0)
            {
                int yBitLength = calcBitLength(yStart, y);
                int xBitLength = calcBitLength(xStart, x);
                int shift = xBitLength - yBitLength;

                int[] iCount;
                int iCountStart = 0;

                int[] c;
                int cStart = 0;
                int cBitLength = yBitLength;
                if (shift > 0)
                {
                    //					iCount = ShiftLeft(One.Magnitude, shift);
                    iCount = new int[(shift >> 5) + 1];
                    iCount[0] = 1 << (shift % 32);

                    c = ShiftLeft(y, shift);
                    cBitLength += shift;
                }
                else
                {
                    iCount = new int[] { 1 };

                    int len = y.Length - yStart;
                    c = new int[len];
                    Array.Copy(y, yStart, c, 0, len);
                }

                count = new int[iCount.Length];

                for (; ; )
                {
                    if (cBitLength < xBitLength
                        || CompareNoLeadingZeroes(xStart, x, cStart, c) >= 0)
                    {
                        Subtract(xStart, x, cStart, c);
                        AddMagnitudes(count, iCount);

                        while (x[xStart] == 0)
                        {
                            if (++xStart == x.Length)
                                return count;
                        }

                        //xBitLength = calcBitLength(xStart, x);
                        xBitLength = 32 * (x.Length - xStart - 1) + BitLen(x[xStart]);

                        if (xBitLength <= yBitLength)
                        {
                            if (xBitLength < yBitLength)
                                return count;

                            xyCmp = CompareNoLeadingZeroes(xStart, x, yStart, y);

                            if (xyCmp <= 0)
                                break;
                        }
                    }

                    shift = cBitLength - xBitLength;

                    // NB: The case where c[cStart] is 1-bit is harmless
                    if (shift == 1)
                    {
                        uint firstC = (uint)c[cStart] >> 1;
                        uint firstX = (uint)x[xStart];
                        if (firstC > firstX)
                            ++shift;
                    }

                    if (shift < 2)
                    {
                        ShiftRightOneInPlace(cStart, c);
                        --cBitLength;
                        ShiftRightOneInPlace(iCountStart, iCount);
                    }
                    else
                    {
                        ShiftRightInPlace(cStart, c, shift);
                        cBitLength -= shift;
                        ShiftRightInPlace(iCountStart, iCount, shift);
                    }

                    //cStart = c.Length - ((cBitLength + 31) / 32);
                    while (c[cStart] == 0)
                    {
                        ++cStart;
                    }

                    while (iCount[iCountStart] == 0)
                    {
                        ++iCountStart;
                    }
                }
            }
            else
            {
                count = new int[1];
            }

            if (xyCmp == 0)
            {
                AddMagnitudes(count, One.Magnitude);
                Array.Clear(x, xStart, x.Length - xStart);
            }

            return count;
        }

        public IBigInteger Divide(
            IBigInteger val)
        {
            if (val.SignValue == 0)
                throw new ArithmeticException("Division by zero error");

            if (SignValue == 0)
                return Zero;

            if (val.QuickPow2Check()) // val is power of two
            {
                IBigInteger result = this.Abs().ShiftRight(val.Abs().BitLength - 1);
                return val.SignValue == this.SignValue ? result : result.Negate();
            }

            int[] mag = (int[])this.Magnitude.Clone();

            return new BigInteger(this.SignValue * val.SignValue, Divide(mag, val.Magnitude), true);
        }

        public IBigInteger[] DivideAndRemainder(
            IBigInteger val)
        {
            if (val.SignValue == 0)
                throw new ArithmeticException("Division by zero error");

            IBigInteger[] biggies = new IBigInteger[2];

            if (SignValue == 0)
            {
                biggies[0] = Zero;
                biggies[1] = Zero;
            }
            else if (val.QuickPow2Check()) // val is power of two
            {
                int e = val.Abs().BitLength - 1;
                IBigInteger quotient = this.Abs().ShiftRight(e);
                int[] remainder = this.LastNBits(e);

                biggies[0] = val.SignValue == this.SignValue ? quotient : quotient.Negate();
                biggies[1] = new BigInteger(this.SignValue, remainder, true);
            }
            else
            {
                int[] remainder = (int[])this.Magnitude.Clone();
                int[] quotient = Divide(remainder, val.Magnitude);

                biggies[0] = new BigInteger(this.SignValue * val.SignValue, quotient, true);
                biggies[1] = new BigInteger(this.SignValue, remainder, true);
            }

            return biggies;
        }

        public override bool Equals(
            object obj)
        {
            if (obj == this)
                return true;

            IBigInteger biggie = obj as IBigInteger;
            if (biggie == null)
                return false;

            if (biggie.SignValue != SignValue || biggie.Magnitude.Length != Magnitude.Length)
                return false;

            for (int i = 0; i < Magnitude.Length; i++)
            {
                if (biggie.Magnitude[i] != Magnitude[i])
                {
                    return false;
                }
            }

            return true;
        }

        public IBigInteger Gcd(
            IBigInteger value)
        {
            if (value.SignValue == 0)
                return Abs();

            if (SignValue == 0)
                return value.Abs();

            IBigInteger r;
            IBigInteger u = this;
            IBigInteger v = value;

            while (v.SignValue != 0)
            {
                r = u.Mod(v);
                u = v;
                v = r;
            }

            return u;
        }

        public override int GetHashCode()
        {
            int hc = Magnitude.Length;
            if (Magnitude.Length > 0)
            {
                hc ^= Magnitude[0];

                if (Magnitude.Length > 1)
                {
                    hc ^= Magnitude[Magnitude.Length - 1];
                }
            }

            return SignValue < 0 ? ~hc : hc;
        }

        // TODO Make public?
        private IBigInteger Inc()
        {
            if (this.SignValue == 0)
                return One;

            if (this.SignValue < 0)
                return new BigInteger(-1, doSubBigLil(this.Magnitude, One.Magnitude), true);

            return AddToMagnitude(One.Magnitude);
        }

        public int IntValue
        {
            get
            {
                return SignValue == 0 ? 0
                    : SignValue > 0 ? Magnitude[Magnitude.Length - 1]
                    : -Magnitude[Magnitude.Length - 1];
            }
        }

        /**
         * return whether or not a BigInteger is probably prime with a
         * probability of 1 - (1/2)**certainty.
         * <p>From Knuth Vol 2, pg 395.</p>
         */
        public bool IsProbablePrime(
            int certainty)
        {
            if (certainty <= 0)
                return true;

            BigInteger n = (BigInteger)Abs();

            if (!n.TestBit(0))
                return n.Equals(Two);

            if (n.Equals(One))
                return false;

            return n.CheckProbablePrime(certainty, RandomSource);
        }

        private bool CheckProbablePrime(
            int certainty,
            IRandom random)
        {
            Debug.Assert(certainty > 0);
            Debug.Assert(CompareTo(Two) > 0);
            Debug.Assert(TestBit(0));


            // Try to reduce the penalty for really small numbers
            int numLists = System.Math.Min(BitLength - 1, primeLists.Length);

            for (int i = 0; i < numLists; ++i)
            {
                int test = Remainder(primeProducts[i]);

                int[] primeList = primeLists[i];
                for (int j = 0; j < primeList.Length; ++j)
                {
                    int prime = primeList[j];
                    int qRem = test % prime;
                    if (qRem == 0)
                    {
                        // We may find small numbers in the list
                        return BitLength < 16 && IntValue == prime;
                    }
                }
            }


            // TODO Special case for < 10^16 (RabinMiller fixed list)
            //			if (BitLength < 30)
            //			{
            //				RabinMiller against 2, 3, 5, 7, 11, 13, 23 is sufficient
            //			}


            // TODO Is it worth trying to create a hybrid of these two?
            return RabinMillerTest(certainty, random);
            //			return SolovayStrassenTest(certainty, random);

            //			bool rbTest = RabinMillerTest(certainty, random);
            //			bool ssTest = SolovayStrassenTest(certainty, random);
            //
            //			Debug.Assert(rbTest == ssTest);
            //
            //			return rbTest;
        }

        internal bool RabinMillerTest(
            int certainty,
            IRandom random)
        {
            Debug.Assert(certainty > 0);
            Debug.Assert(BitLength > 2);
            Debug.Assert(TestBit(0));

            // let n = 1 + d . 2^s
            IBigInteger n = this;
            IBigInteger nMinusOne = n.Subtract(One);
            int s = nMinusOne.GetLowestSetBit();
            IBigInteger r = nMinusOne.ShiftRight(s);

            Debug.Assert(s >= 1);

            do
            {
                // TODO Make a method for random BigIntegers in range 0 < x < n)
                // - Method can be optimized by only replacing examined bits at each trial
                IBigInteger a;
                do
                {
                    a = new BigInteger(n.BitLength, random);
                }
                while (a.CompareTo(One) <= 0 || a.CompareTo(nMinusOne) >= 0);

                IBigInteger y = a.ModPow(r, n);

                if (!y.Equals(One))
                {
                    int j = 0;
                    while (!y.Equals(nMinusOne))
                    {
                        if (++j == s)
                            return false;

                        y = y.ModPow(Two, n);

                        if (y.Equals(One))
                            return false;
                    }
                }

                certainty -= 2; // composites pass for only 1/4 possible 'a'
            }
            while (certainty > 0);

            return true;
        }

        //		private bool SolovayStrassenTest(
        //			int		certainty,
        //			Random	random)
        //		{
        //			Debug.Assert(certainty > 0);
        //			Debug.Assert(CompareTo(Two) > 0);
        //			Debug.Assert(TestBit(0));
        //
        //			IBigInteger n = this;
        //			IBigInteger nMinusOne = n.Subtract(One);
        //			IBigInteger e = nMinusOne.ShiftRight(1);
        //
        //			do
        //			{
        //				IBigInteger a;
        //				do
        //				{
        //					a = new BigInteger(nBitLength, random);
        //				}
        //				// NB: Spec says 0 < x < n, but 1 is trivial
        //				while (a.CompareTo(One) <= 0 || a.CompareTo(n) >= 0);
        //
        //
        //				// TODO Check this is redundant given the way Jacobi() works?
        ////				if (!a.Gcd(n).Equals(One))
        ////					return false;
        //
        //				int x = Jacobi(a, n);
        //
        //				if (x == 0)
        //					return false;
        //
        //				BigInteger check = a.ModPow(e, n);
        //
        //				if (x == 1 && !check.Equals(One))
        //					return false;
        //
        //				if (x == -1 && !check.Equals(nMinusOne))
        //					return false;
        //
        //				--certainty;
        //			}
        //			while (certainty > 0);
        //
        //			return true;
        //		}
        //
        //		private static int Jacobi(
        //			BigInteger	a,
        //			BigInteger	b)
        //		{
        //			Debug.Assert(a.sign >= 0);
        //			Debug.Assert(b.sign > 0);
        //			Debug.Assert(b.TestBit(0));
        //			Debug.Assert(a.CompareTo(b) < 0);
        //
        //			int totalS = 1;
        //			for (;;)
        //			{
        //				if (a.sign == 0)
        //					return 0;
        //
        //				if (a.Equals(One))
        //					break;
        //
        //				int e = a.GetLowestSetBit();
        //
        //				int bLsw = b.Magnitude[b.Magnitude.Length - 1];
        //				if ((e & 1) != 0 && ((bLsw & 7) == 3 || (bLsw & 7) == 5))
        //					totalS = -totalS;
        //
        //				// TODO Confirm this is faster than later a1.Equals(One) test
        //				if (a.BitLength == e + 1)
        //					break;
        //				BigInteger a1 = a.ShiftRight(e);
        ////				if (a1.Equals(One))
        ////					break;
        //
        //				int a1Lsw = a1.Magnitude[a1.Magnitude.Length - 1];
        //				if ((bLsw & 3) == 3 && (a1Lsw & 3) == 3)
        //					totalS = -totalS;
        //
        ////				a = b.Mod(a1);
        //				a = b.Remainder(a1);
        //				b = a1;
        //			}
        //			return totalS;
        //		}

        public long LongValue
        {
            get
            {
                if (SignValue == 0)
                    return 0;

                long v;
                if (Magnitude.Length > 1)
                {
                    v = ((long)Magnitude[Magnitude.Length - 2] << 32)
                        | (Magnitude[Magnitude.Length - 1] & IMASK);
                }
                else
                {
                    v = (Magnitude[Magnitude.Length - 1] & IMASK);
                }

                return SignValue < 0 ? -v : v;
            }
        }

        public IBigInteger Max(
            IBigInteger value)
        {
            return CompareTo(value) > 0 ? this : value;
        }

        public IBigInteger Min(
            IBigInteger value)
        {
            return CompareTo(value) < 0 ? this : value;
        }

        public IBigInteger Mod(
            IBigInteger m)
        {
            if (m.SignValue < 1)
                throw new ArithmeticException("Modulus must be positive");

            IBigInteger biggie = Remainder(m);

            return (biggie.SignValue >= 0 ? biggie : biggie.Add(m));
        }

        public IBigInteger ModInverse(
            IBigInteger m)
        {
            if (m.SignValue < 1)
                throw new ArithmeticException("Modulus must be positive");

            // TODO Too slow at the moment
            //			// "Fast Key Exchange with Elliptic Curve Systems" R.Schoeppel
            //			if (m.TestBit(0))
            //			{
            //				//The Almost Inverse Algorithm
            //				int k = 0;
            //				BigInteger B = One, C = Zero, F = this, G = m, tmp;
            //
            //				for (;;)
            //				{
            //					// While F is even, do F=F/u, C=C*u, k=k+1.
            //					int zeroes = F.GetLowestSetBit();
            //					if (zeroes > 0)
            //					{
            //						F = F.ShiftRight(zeroes);
            //						C = C.ShiftLeft(zeroes);
            //						k += zeroes;
            //					}
            //
            //					// If F = 1, then return B,k.
            //					if (F.Equals(One))
            //					{
            //						BigInteger half = m.Add(One).ShiftRight(1);
            //						BigInteger halfK = half.ModPow(BigInteger.ValueOf(k), m);
            //						return B.Multiply(halfK).Mod(m);
            //					}
            //
            //					if (F.CompareTo(G) < 0)
            //					{
            //						tmp = G; G = F; F = tmp;
            //						tmp = B; B = C; C = tmp;
            //					}
            //
            //					F = F.Add(G);
            //					B = B.Add(C);
            //				}
            //			}

            IBigInteger x = new BigInteger();
            IBigInteger gcd = ExtEuclid(this.Mod(m), m, x, null);

            if (!gcd.Equals(One))
                throw new ArithmeticException("Numbers not relatively prime.");

            if (x.SignValue < 0)
            {
                x.SignValue = 1;
                //x = m.Subtract(x);
                x.Magnitude = doSubBigLil(m.Magnitude, x.Magnitude);
            }

            return x;
        }

        /**
         * Calculate the numbers u1, u2, and u3 such that:
         *
         * u1 * a + u2 * b = u3
         *
         * where u3 is the greatest common divider of a and b.
         * a and b using the extended Euclid algorithm (refer p. 323
         * of The Art of Computer Programming vol 2, 2nd ed).
         * This also seems to have the side effect of calculating
         * some form of multiplicative inverse.
         *
         * @param a    First number to calculate gcd for
         * @param b    Second number to calculate gcd for
         * @param u1Out      the return object for the u1 value
         * @param u2Out      the return object for the u2 value
         * @return     The greatest common divisor of a and b
         */
        private static IBigInteger ExtEuclid(
            IBigInteger a,
            IBigInteger b,
            IBigInteger u1Out,
            IBigInteger u2Out)
        {
            IBigInteger u1 = One;
            IBigInteger u3 = a;
            IBigInteger v1 = Zero;
            IBigInteger v3 = b;

            while (v3.SignValue > 0)
            {
                IBigInteger[] q = u3.DivideAndRemainder(v3);

                IBigInteger tmp = v1.Multiply(q[0]);
                IBigInteger tn = u1.Subtract(tmp);
                u1 = v1;
                v1 = tn;

                u3 = v3;
                v3 = q[1];
            }

            if (u1Out != null)
            {
                u1Out.SignValue = u1.SignValue;
                u1Out.Magnitude = u1.Magnitude;
            }

            if (u2Out != null)
            {
                IBigInteger tmp = u1.Multiply(a);
                tmp = u3.Subtract(tmp);
                IBigInteger res = tmp.Divide(b);
                u2Out.SignValue = res.SignValue;
                u2Out.Magnitude = res.Magnitude;
            }

            return u3;
        }

        private static void ZeroOut(
            int[] x)
        {
            Array.Clear(x, 0, x.Length);
        }

        public IBigInteger ModPow(
            IBigInteger exponent,
            IBigInteger m)
        {
            if (m.SignValue < 1)
                throw new ArithmeticException("Modulus must be positive");

            if (m.Equals(One))
                return Zero;

            if (exponent.SignValue == 0)
                return One;

            if (SignValue == 0)
                return Zero;

            int[] zVal = null;
            int[] yAccum = null;
            int[] yVal;

            // Montgomery exponentiation is only possible if the modulus is odd,
            // but AFAIK, this is always the case for crypto algo's
            bool useMonty = ((m.Magnitude[m.Magnitude.Length - 1] & 1) == 1);
            long mQ = 0;
            if (useMonty)
            {
                mQ = m.GetMQuote();

                // tmp = this * R mod m
                IBigInteger tmp = ShiftLeft(32 * m.Magnitude.Length).Mod(m);
                zVal = tmp.Magnitude;

                useMonty = (zVal.Length <= m.Magnitude.Length);

                if (useMonty)
                {
                    yAccum = new int[m.Magnitude.Length + 1];
                    if (zVal.Length < m.Magnitude.Length)
                    {
                        int[] longZ = new int[m.Magnitude.Length];
                        zVal.CopyTo(longZ, longZ.Length - zVal.Length);
                        zVal = longZ;
                    }
                }
            }

            if (!useMonty)
            {
                if (Magnitude.Length <= m.Magnitude.Length)
                {
                    //zAccum = new int[m.Magnitude.Length * 2];
                    zVal = new int[m.Magnitude.Length];
                    Magnitude.CopyTo(zVal, zVal.Length - Magnitude.Length);
                }
                else
                {
                    //
                    // in normal practice we'll never see this...
                    //
                    IBigInteger tmp = Remainder(m);

                    //zAccum = new int[m.Magnitude.Length * 2];
                    zVal = new int[m.Magnitude.Length];
                    tmp.Magnitude.CopyTo(zVal, zVal.Length - tmp.Magnitude.Length);
                }

                yAccum = new int[m.Magnitude.Length * 2];
            }

            yVal = new int[m.Magnitude.Length];

            //
            // from LSW to MSW
            //
            for (int i = 0; i < exponent.Magnitude.Length; i++)
            {
                int v = exponent.Magnitude[i];
                int bits = 0;

                if (i == 0)
                {
                    while (v > 0)
                    {
                        v <<= 1;
                        bits++;
                    }

                    //
                    // first time in initialise y
                    //
                    zVal.CopyTo(yVal, 0);

                    v <<= 1;
                    bits++;
                }

                while (v != 0)
                {
                    if (useMonty)
                    {
                        // Montgomery square algo doesn't exist, and a normal
                        // square followed by a Montgomery reduction proved to
                        // be almost as heavy as a Montgomery mulitply.
                        MultiplyMonty(yAccum, yVal, yVal, m.Magnitude, mQ);
                    }
                    else
                    {
                        Square(yAccum, yVal);
                        Remainder(yAccum, m.Magnitude);
                        Array.Copy(yAccum, yAccum.Length - yVal.Length, yVal, 0, yVal.Length);
                        ZeroOut(yAccum);
                    }
                    bits++;

                    if (v < 0)
                    {
                        if (useMonty)
                        {
                            MultiplyMonty(yAccum, yVal, zVal, m.Magnitude, mQ);
                        }
                        else
                        {
                            Multiply(yAccum, yVal, zVal);
                            Remainder(yAccum, m.Magnitude);
                            Array.Copy(yAccum, yAccum.Length - yVal.Length, yVal, 0,
                                yVal.Length);
                            ZeroOut(yAccum);
                        }
                    }

                    v <<= 1;
                }

                while (bits < 32)
                {
                    if (useMonty)
                    {
                        MultiplyMonty(yAccum, yVal, yVal, m.Magnitude, mQ);
                    }
                    else
                    {
                        Square(yAccum, yVal);
                        Remainder(yAccum, m.Magnitude);
                        Array.Copy(yAccum, yAccum.Length - yVal.Length, yVal, 0, yVal.Length);
                        ZeroOut(yAccum);
                    }
                    bits++;
                }
            }

            if (useMonty)
            {
                // Return y * R^(-1) mod m by doing y * 1 * R^(-1) mod m
                ZeroOut(zVal);
                zVal[zVal.Length - 1] = 1;
                MultiplyMonty(yAccum, yVal, zVal, m.Magnitude, mQ);
            }

            IBigInteger result = new BigInteger(1, yVal, true);

            return exponent.SignValue > 0
                ? result
                : result.ModInverse(m);
        }

        /**
         * return w with w = x * x - w is assumed to have enough space.
         */
        private static int[] Square(
            int[] w,
            int[] x)
        {
            // Note: this method allows w to be only (2 * x.Length - 1) words if result will fit
            //			if (w.Length != 2 * x.Length)
            //				throw new ArgumentException("no I don't think so...");

            ulong u1, u2, c;

            int wBase = w.Length - 1;

            for (int i = x.Length - 1; i != 0; i--)
            {
                ulong v = (ulong)(uint)x[i];

                u1 = v * v;
                u2 = u1 >> 32;
                u1 = (uint)u1;

                u1 += (ulong)(uint)w[wBase];

                w[wBase] = (int)(uint)u1;
                c = u2 + (u1 >> 32);

                for (int j = i - 1; j >= 0; j--)
                {
                    --wBase;
                    u1 = v * (ulong)(uint)x[j];
                    u2 = u1 >> 31; // multiply by 2!
                    u1 = (uint)(u1 << 1); // multiply by 2!
                    u1 += c + (ulong)(uint)w[wBase];

                    w[wBase] = (int)(uint)u1;
                    c = u2 + (u1 >> 32);
                }

                c += (ulong)(uint)w[--wBase];
                w[wBase] = (int)(uint)c;

                if (--wBase >= 0)
                {
                    w[wBase] = (int)(uint)(c >> 32);
                }
                else
                {
                    Debug.Assert((uint)(c >> 32) == 0);
                }
                wBase += i;
            }

            u1 = (ulong)(uint)x[0];
            u1 = u1 * u1;
            u2 = u1 >> 32;
            u1 = u1 & IMASK;

            u1 += (ulong)(uint)w[wBase];

            w[wBase] = (int)(uint)u1;
            if (--wBase >= 0)
            {
                w[wBase] = (int)(uint)(u2 + (u1 >> 32) + (ulong)(uint)w[wBase]);
            }
            else
            {
                Debug.Assert((uint)(u2 + (u1 >> 32)) == 0);
            }

            return w;
        }

        /**
         * return x with x = y * z - x is assumed to have enough space.
         */
        private static int[] Multiply(
            int[] x,
            int[] y,
            int[] z)
        {
            int i = z.Length;

            if (i < 1)
                return x;

            int xBase = x.Length - y.Length;

            for (; ; )
            {
                long a = z[--i] & IMASK;
                long val = 0;

                for (int j = y.Length - 1; j >= 0; j--)
                {
                    val += a * (y[j] & IMASK) + (x[xBase + j] & IMASK);

                    x[xBase + j] = (int)val;

                    val = (long)((ulong)val >> 32);
                }

                --xBase;

                if (i < 1)
                {
                    if (xBase >= 0)
                    {
                        x[xBase] = (int)val;
                    }
                    else
                    {
                        Debug.Assert(val == 0);
                    }
                    break;
                }

                x[xBase] = (int)val;
            }

            return x;
        }

        private static long FastExtEuclid(
            long a,
            long b,
            long[] uOut)
        {
            long u1 = 1;
            long u3 = a;
            long v1 = 0;
            long v3 = b;

            while (v3 > 0)
            {
                long q, tn;

                q = u3 / v3;

                tn = u1 - (v1 * q);
                u1 = v1;
                v1 = tn;

                tn = u3 - (v3 * q);
                u3 = v3;
                v3 = tn;
            }

            uOut[0] = u1;
            uOut[1] = (u3 - (u1 * a)) / b;

            return u3;
        }

        private static long FastModInverse(
            long v,
            long m)
        {
            if (m < 1)
                throw new ArithmeticException("Modulus must be positive");

            long[] x = new long[2];
            long gcd = FastExtEuclid(v, m, x);

            if (gcd != 1)
                throw new ArithmeticException("Numbers not relatively prime.");

            if (x[0] < 0)
            {
                x[0] += m;
            }

            return x[0];
        }

        //		private static BigInteger MQuoteB = One.ShiftLeft(32);
        //		private static BigInteger MQuoteBSub1 = MQuoteB.Subtract(One);

        /**
         * Calculate mQuote = -m^(-1) mod b with b = 2^32 (32 = word size)
         */
        public long GetMQuote()
        {
            Debug.Assert(this.SignValue > 0);

            if (mQuote != -1)
            {
                return mQuote; // already calculated
            }

            if (Magnitude.Length == 0 || (Magnitude[Magnitude.Length - 1] & 1) == 0)
            {
                return -1; // not for even numbers
            }

            long v = (((~this.Magnitude[this.Magnitude.Length - 1]) | 1) & 0xffffffffL);
            mQuote = FastModInverse(v, 0x100000000L);

            return mQuote;
        }

        /**
         * Montgomery multiplication: a = x * y * R^(-1) mod m
         * <br/>
         * Based algorithm 14.36 of Handbook of Applied Cryptography.
         * <br/>
         * <li> m, x, y should have length n </li>
         * <li> a should have length (n + 1) </li>
         * <li> b = 2^32, R = b^n </li>
         * <br/>
         * The result is put in x
         * <br/>
         * NOTE: the indices of x, y, m, a different in HAC and in Java
         */
        private static void MultiplyMonty(
            int[] a,
            int[] x,
            int[] y,
            int[] m,
            long mQuote)
        // mQuote = -m^(-1) mod b
        {
            if (m.Length == 1)
            {
                x[0] = (int)MultiplyMontyNIsOne((uint)x[0], (uint)y[0], (uint)m[0], (ulong)mQuote);
                return;
            }

            int n = m.Length;
            int nMinus1 = n - 1;
            long y_0 = y[nMinus1] & IMASK;

            // 1. a = 0 (Notation: a = (a_{n} a_{n-1} ... a_{0})_{b} )
            Array.Clear(a, 0, n + 1);

            // 2. for i from 0 to (n - 1) do the following:
            for (int i = n; i > 0; i--)
            {
                long x_i = x[i - 1] & IMASK;

                // 2.1 u = ((a[0] + (x[i] * y[0]) * mQuote) mod b
                long u = ((((a[n] & IMASK) + ((x_i * y_0) & IMASK)) & IMASK) * mQuote) & IMASK;

                // 2.2 a = (a + x_i * y + u * m) / b
                long prod1 = x_i * y_0;
                long prod2 = u * (m[nMinus1] & IMASK);
                long tmp = (a[n] & IMASK) + (prod1 & IMASK) + (prod2 & IMASK);
                long carry = (long)((ulong)prod1 >> 32) + (long)((ulong)prod2 >> 32) + (long)((ulong)tmp >> 32);
                for (int j = nMinus1; j > 0; j--)
                {
                    prod1 = x_i * (y[j - 1] & IMASK);
                    prod2 = u * (m[j - 1] & IMASK);
                    tmp = (a[j] & IMASK) + (prod1 & IMASK) + (prod2 & IMASK) + (carry & IMASK);
                    carry = (long)((ulong)carry >> 32) + (long)((ulong)prod1 >> 32) +
                        (long)((ulong)prod2 >> 32) + (long)((ulong)tmp >> 32);
                    a[j + 1] = (int)tmp; // division by b
                }
                carry += (a[0] & IMASK);
                a[1] = (int)carry;
                a[0] = (int)((ulong)carry >> 32); // OJO!!!!!
            }

            // 3. if x >= m the x = x - m
            if (CompareTo(0, a, 0, m) >= 0)
            {
                Subtract(0, a, 0, m);
            }

            // put the result in x
            Array.Copy(a, 1, x, 0, n);
        }

        private static uint MultiplyMontyNIsOne(
            uint x,
            uint y,
            uint m,
            ulong mQuote)
        {
            ulong um = m;
            ulong prod1 = (ulong)x * (ulong)y;
            ulong u = (prod1 * mQuote) & UIMASK;
            ulong prod2 = u * um;
            ulong tmp = (prod1 & UIMASK) + (prod2 & UIMASK);
            ulong carry = (prod1 >> 32) + (prod2 >> 32) + (tmp >> 32);

            if (carry > um)
            {
                carry -= um;
            }

            return (uint)(carry & UIMASK);
        }

        public IBigInteger Multiply(
            IBigInteger val)
        {
            if (SignValue == 0 || val.SignValue == 0)
                return Zero;

            if (val.QuickPow2Check()) // val is power of two
            {
                IBigInteger result = this.ShiftLeft(val.Abs().BitLength - 1);
                return val.SignValue > 0 ? result : result.Negate();
            }

            if (this.QuickPow2Check()) // this is power of two
            {
                IBigInteger result = val.ShiftLeft(this.Abs().BitLength - 1);
                return this.SignValue > 0 ? result : result.Negate();
            }

            int resLength = (this.BitLength + val.BitLength) / BitsPerInt + 1;
            int[] res = new int[resLength];

            if (val == this)
            {
                Square(res, this.Magnitude);
            }
            else
            {
                Multiply(res, this.Magnitude, val.Magnitude);
            }

            return new BigInteger(SignValue * val.SignValue, res, true);
        }

        public IBigInteger Negate()
        {
            if (SignValue == 0)
                return this;

            return new BigInteger(-SignValue, Magnitude, false);
        }

        public IBigInteger NextProbablePrime()
        {
            if (SignValue < 0)
                throw new ArithmeticException("Cannot be called on value < 0");

            if (CompareTo(Two) < 0)
                return Two;

            IBigInteger n = Inc().SetBit(0);

            while (!((BigInteger)n).CheckProbablePrime(100, RandomSource))
            {
                n = n.Add(Two);
            }

            return n;
        }

        public IBigInteger Not()
        {
            return Inc().Negate();
        }

        public IBigInteger Pow(int exp)
        {
            if (exp < 0)
            {
                throw new ArithmeticException("Negative exponent");
            }

            if (exp == 0)
            {
                return One;
            }

            if (SignValue == 0 || Equals(One))
            {
                return this;
            }

            IBigInteger y = One;
            IBigInteger z = this;

            for (; ; )
            {
                if ((exp & 0x1) == 1)
                {
                    y = y.Multiply(z);
                }
                exp >>= 1;
                if (exp == 0) break;
                z = z.Multiply(z);
            }

            return y;
        }

        public static IBigInteger ProbablePrime(
            int bitLength,
            IRandom random)
        {
            return new BigInteger(bitLength, 100, random);
        }

        private int Remainder(
            int m)
        {
            Debug.Assert(m > 0);

            long acc = 0;
            for (int pos = 0; pos < Magnitude.Length; ++pos)
            {
                long posVal = (uint)Magnitude[pos];
                acc = (acc << 32 | posVal) % m;
            }

            return (int)acc;
        }

        /**
         * return x = x % y - done in place (y value preserved)
         */
        private int[] Remainder(
            int[] x,
            int[] y)
        {
            int xStart = 0;
            while (xStart < x.Length && x[xStart] == 0)
            {
                ++xStart;
            }

            int yStart = 0;
            while (yStart < y.Length && y[yStart] == 0)
            {
                ++yStart;
            }

            Debug.Assert(yStart < y.Length);

            int xyCmp = CompareNoLeadingZeroes(xStart, x, yStart, y);

            if (xyCmp > 0)
            {
                int yBitLength = calcBitLength(yStart, y);
                int xBitLength = calcBitLength(xStart, x);
                int shift = xBitLength - yBitLength;

                int[] c;
                int cStart = 0;
                int cBitLength = yBitLength;
                if (shift > 0)
                {
                    c = ShiftLeft(y, shift);
                    cBitLength += shift;
                    Debug.Assert(c[0] != 0);
                }
                else
                {
                    int len = y.Length - yStart;
                    c = new int[len];
                    Array.Copy(y, yStart, c, 0, len);
                }

                for (; ; )
                {
                    if (cBitLength < xBitLength
                        || CompareNoLeadingZeroes(xStart, x, cStart, c) >= 0)
                    {
                        Subtract(xStart, x, cStart, c);

                        while (x[xStart] == 0)
                        {
                            if (++xStart == x.Length)
                                return x;
                        }

                        //xBitLength = calcBitLength(xStart, x);
                        xBitLength = 32 * (x.Length - xStart - 1) + BitLen(x[xStart]);

                        if (xBitLength <= yBitLength)
                        {
                            if (xBitLength < yBitLength)
                                return x;

                            xyCmp = CompareNoLeadingZeroes(xStart, x, yStart, y);

                            if (xyCmp <= 0)
                                break;
                        }
                    }

                    shift = cBitLength - xBitLength;

                    // NB: The case where c[cStart] is 1-bit is harmless
                    if (shift == 1)
                    {
                        uint firstC = (uint)c[cStart] >> 1;
                        uint firstX = (uint)x[xStart];
                        if (firstC > firstX)
                            ++shift;
                    }

                    if (shift < 2)
                    {
                        ShiftRightOneInPlace(cStart, c);
                        --cBitLength;
                    }
                    else
                    {
                        ShiftRightInPlace(cStart, c, shift);
                        cBitLength -= shift;
                    }

                    //cStart = c.Length - ((cBitLength + 31) / 32);
                    while (c[cStart] == 0)
                    {
                        ++cStart;
                    }
                }
            }

            if (xyCmp == 0)
            {
                Array.Clear(x, xStart, x.Length - xStart);
            }

            return x;
        }

        public IBigInteger Remainder(
            IBigInteger n)
        {
            if (n.SignValue == 0)
                throw new ArithmeticException("Division by zero error");

            if (this.SignValue == 0)
                return Zero;

            // For small values, use fast remainder method
            if (n.Magnitude.Length == 1)
            {
                int val = n.Magnitude[0];

                if (val > 0)
                {
                    if (val == 1)
                        return Zero;

                    // TODO Make this func work on uint, and handle val == 1?
                    int rem = Remainder(val);

                    return rem == 0
                        ? Zero
                        : new BigInteger(SignValue, new int[] { rem }, false);
                }
            }

            if (CompareNoLeadingZeroes(0, Magnitude, 0, n.Magnitude) < 0)
                return this;

            int[] result;
            if (n.QuickPow2Check())  // n is power of two
            {
                // TODO Move before small values branch above?
                result = LastNBits(n.Abs().BitLength - 1);
            }
            else
            {
                result = (int[])this.Magnitude.Clone();
                result = Remainder(result, n.Magnitude);
            }

            return new BigInteger(SignValue, result, true);
        }

        private int[] LastNBits(
            int n)
        {
            if (n < 1)
                return ZeroMagnitude;

            int numWords = (n + BitsPerInt - 1) / BitsPerInt;
            numWords = System.Math.Min(numWords, this.Magnitude.Length);
            int[] result = new int[numWords];

            Array.Copy(this.Magnitude, this.Magnitude.Length - numWords, result, 0, numWords);

            int hiBits = n % 32;
            if (hiBits != 0)
            {
                result[0] &= ~(-1 << hiBits);
            }

            return result;
        }

        /**
         * do a left shift - this returns a new array.
         */
        private static int[] ShiftLeft(
            int[] mag,
            int n)
        {
            int nInts = (int)((uint)n >> 5);
            int nBits = n & 0x1f;
            int magLen = mag.Length;
            int[] newMag;

            if (nBits == 0)
            {
                newMag = new int[magLen + nInts];
                mag.CopyTo(newMag, 0);
            }
            else
            {
                int i = 0;
                int nBits2 = 32 - nBits;
                int highBits = (int)((uint)mag[0] >> nBits2);

                if (highBits != 0)
                {
                    newMag = new int[magLen + nInts + 1];
                    newMag[i++] = highBits;
                }
                else
                {
                    newMag = new int[magLen + nInts];
                }

                int m = mag[0];
                for (int j = 0; j < magLen - 1; j++)
                {
                    int next = mag[j + 1];

                    newMag[i++] = (m << nBits) | (int)((uint)next >> nBits2);
                    m = next;
                }

                newMag[i] = mag[magLen - 1] << nBits;
            }

            return newMag;
        }

        public IBigInteger ShiftLeft(
            int n)
        {
            if (SignValue == 0 || Magnitude.Length == 0)
                return Zero;

            if (n == 0)
                return this;

            if (n < 0)
                return ShiftRight(-n);

            BigInteger result = new BigInteger(SignValue, ShiftLeft(Magnitude, n), true);

            if (this.nBits != -1)
            {
                result.nBits = SignValue > 0
                    ? this.nBits
                    : this.nBits + n;
            }

            if (this.nBitLength != -1)
            {
                result.nBitLength = this.nBitLength + n;
            }

            return result;
        }

        /**
         * do a right shift - this does it in place.
         */
        private static void ShiftRightInPlace(
            int start,
            int[] mag,
            int n)
        {
            int nInts = (int)((uint)n >> 5) + start;
            int nBits = n & 0x1f;
            int magEnd = mag.Length - 1;

            if (nInts != start)
            {
                int delta = (nInts - start);

                for (int i = magEnd; i >= nInts; i--)
                {
                    mag[i] = mag[i - delta];
                }
                for (int i = nInts - 1; i >= start; i--)
                {
                    mag[i] = 0;
                }
            }

            if (nBits != 0)
            {
                int nBits2 = 32 - nBits;
                int m = mag[magEnd];

                for (int i = magEnd; i > nInts; --i)
                {
                    int next = mag[i - 1];

                    mag[i] = (int)((uint)m >> nBits) | (next << nBits2);
                    m = next;
                }

                mag[nInts] = (int)((uint)mag[nInts] >> nBits);
            }
        }

        /**
         * do a right shift by one - this does it in place.
         */
        private static void ShiftRightOneInPlace(
            int start,
            int[] mag)
        {
            int i = mag.Length;
            int m = mag[i - 1];

            while (--i > start)
            {
                int next = mag[i - 1];
                mag[i] = ((int)((uint)m >> 1)) | (next << 31);
                m = next;
            }

            mag[start] = (int)((uint)mag[start] >> 1);
        }

        public IBigInteger ShiftRight(
            int n)
        {
            if (n == 0)
                return this;

            if (n < 0)
                return ShiftLeft(-n);

            if (n >= BitLength)
                return (this.SignValue < 0 ? One.Negate() : Zero);

            //			int[] res = (int[]) this.Magnitude.Clone();
            //
            //			ShiftRightInPlace(0, res, n);
            //
            //			return new BigInteger(this.sign, res, true);

            int resultLength = (BitLength - n + 31) >> 5;
            int[] res = new int[resultLength];

            int numInts = n >> 5;
            int numBits = n & 31;

            if (numBits == 0)
            {
                Array.Copy(this.Magnitude, 0, res, 0, res.Length);
            }
            else
            {
                int numBits2 = 32 - numBits;

                int magPos = this.Magnitude.Length - 1 - numInts;
                for (int i = resultLength - 1; i >= 0; --i)
                {
                    res[i] = (int)((uint)this.Magnitude[magPos--] >> numBits);

                    if (magPos >= 0)
                    {
                        res[i] |= this.Magnitude[magPos] << numBits2;
                    }
                }
            }

            Debug.Assert(res[0] != 0);

            return new BigInteger(this.SignValue, res, false);
        }

        public int SignValue { get; set; }        

        /**
         * returns x = x - y - we assume x is >= y
         */
        private static int[] Subtract(
            int xStart,
            int[] x,
            int yStart,
            int[] y)
        {
            Debug.Assert(yStart < y.Length);
            Debug.Assert(x.Length - xStart >= y.Length - yStart);

            int iT = x.Length;
            int iV = y.Length;
            long m;
            int borrow = 0;

            do
            {
                m = (x[--iT] & IMASK) - (y[--iV] & IMASK) + borrow;
                x[iT] = (int)m;

                //				borrow = (m < 0) ? -1 : 0;
                borrow = (int)(m >> 63);
            }
            while (iV > yStart);

            if (borrow != 0)
            {
                while (--x[--iT] == -1)
                {
                }
            }

            return x;
        }

        public IBigInteger Subtract(
            IBigInteger n)
        {
            if (n.SignValue == 0)
                return this;

            if (this.SignValue == 0)
                return n.Negate();

            if (this.SignValue != n.SignValue)
                return Add(n.Negate());

            int compare = CompareNoLeadingZeroes(0, Magnitude, 0, n.Magnitude);
            if (compare == 0)
                return Zero;

            IBigInteger bigun, lilun;
            if (compare < 0)
            {
                bigun = n;
                lilun = this;
            }
            else
            {
                bigun = this;
                lilun = n;
            }

            return new BigInteger(this.SignValue * compare, doSubBigLil(bigun.Magnitude, lilun.Magnitude), true);
        }

        private static int[] doSubBigLil(
            int[] bigMag,
            int[] lilMag)
        {
            int[] res = (int[])bigMag.Clone();

            return Subtract(0, res, 0, lilMag);
        }

        public byte[] ToByteArray()
        {
            return ToByteArray(false);
        }

        public byte[] ToByteArrayUnsigned()
        {
            return ToByteArray(true);
        }

        private byte[] ToByteArray(
            bool unsigned)
        {
            if (SignValue == 0)
                return unsigned ? ZeroEncoding : new byte[1];

            int nBits = (unsigned && SignValue > 0)
                ? BitLength
                : BitLength + 1;

            int nBytes = GetByteLength(nBits);
            byte[] bytes = new byte[nBytes];

            int magIndex = Magnitude.Length;
            int bytesIndex = bytes.Length;

            if (SignValue > 0)
            {
                while (magIndex > 1)
                {
                    uint mag = (uint)Magnitude[--magIndex];
                    bytes[--bytesIndex] = (byte)mag;
                    bytes[--bytesIndex] = (byte)(mag >> 8);
                    bytes[--bytesIndex] = (byte)(mag >> 16);
                    bytes[--bytesIndex] = (byte)(mag >> 24);
                }

                uint lastMag = (uint)Magnitude[0];
                while (lastMag > byte.MaxValue)
                {
                    bytes[--bytesIndex] = (byte)lastMag;
                    lastMag >>= 8;
                }

                bytes[--bytesIndex] = (byte)lastMag;
            }
            else // sign < 0
            {
                bool carry = true;

                while (magIndex > 1)
                {
                    uint mag = ~((uint)Magnitude[--magIndex]);

                    if (carry)
                    {
                        carry = (++mag == uint.MinValue);
                    }

                    bytes[--bytesIndex] = (byte)mag;
                    bytes[--bytesIndex] = (byte)(mag >> 8);
                    bytes[--bytesIndex] = (byte)(mag >> 16);
                    bytes[--bytesIndex] = (byte)(mag >> 24);
                }

                uint lastMag = (uint)Magnitude[0];

                if (carry)
                {
                    // Never wraps because Magnitude[0] != 0
                    --lastMag;
                }

                while (lastMag > byte.MaxValue)
                {
                    bytes[--bytesIndex] = (byte)~lastMag;
                    lastMag >>= 8;
                }

                bytes[--bytesIndex] = (byte)~lastMag;

                if (bytesIndex > 0)
                {
                    bytes[--bytesIndex] = byte.MaxValue;
                }
            }

            return bytes;
        }

        public override string ToString()
        {
            return ToString(10);
        }

        public string ToString(
            int radix)
        {
            // TODO Make this method work for other radices (ideally 2 <= radix <= 16)

            switch (radix)
            {
                case 2:
                case 10:
                case 16:
                    break;
                default:
                    throw new FormatException("Only bases 2, 10, 16 are allowed");
            }

            // NB: Can only happen to internally managed instances
            if (Magnitude == null)
                return "null";

            if (SignValue == 0)
                return "0";

            Debug.Assert(Magnitude.Length > 0);

            StringBuilder sb = new StringBuilder();

            if (radix == 16)
            {
                sb.Append(Magnitude[0].ToString("x"));

                for (int i = 1; i < Magnitude.Length; i++)
                {
                    sb.Append(Magnitude[i].ToString("x8"));
                }
            }
            else if (radix == 2)
            {
                sb.Append('1');

                for (int i = BitLength - 2; i >= 0; --i)
                {
                    sb.Append(TestBit(i) ? '1' : '0');
                }
            }
            else
            {
                // This is algorithm 1a from chapter 4.4 in Seminumerical Algorithms, slow but it works
                IList S = Platform.CreateArrayList();
                IBigInteger bs = ValueOf(radix);

                // The sign is handled separatly.
                // Notice however that for this to work, radix 16 _MUST_ be a special case,
                // unless we want to enter a recursion well. In their infinite wisdom, why did not
                // the Sun engineers made a c'tor for BigIntegers taking a IBigInteger as parameter?
                // (Answer: Becuase Sun's BigIntger is clonable, something bouncycastle's isn't.)
                //				IBigInteger u = new IBigInteger(Abs().ToString(16), 16);
                IBigInteger u = this.Abs();
                IBigInteger b;

                while (u.SignValue != 0)
                {
                    b = u.Mod(bs);
                    if (b.SignValue == 0)
                    {
                        S.Add("0");
                    }
                    else
                    {
                        // see how to interact with different bases
                        S.Add(b.Magnitude[0].ToString("d"));
                    }
                    u = u.Divide(bs);
                }

                // Then pop the stack
                for (int i = S.Count - 1; i >= 0; --i)
                {
                    sb.Append((string)S[i]);
                }
            }

            string s = sb.ToString();

            Debug.Assert(s.Length > 0);

            // Strip leading zeros. (We know this number is not all zeroes though)
            if (s[0] == '0')
            {
                int nonZeroPos = 0;
                while (s[++nonZeroPos] == '0') { }

                s = s.Substring(nonZeroPos);
            }

            if (SignValue == -1)
            {
                s = "-" + s;
            }

            return s;
        }

        private static IBigInteger createUValueOf(
            ulong value)
        {
            int msw = (int)(value >> 32);
            int lsw = (int)value;

            if (msw != 0)
                return new BigInteger(1, new int[] { msw, lsw }, false);

            if (lsw != 0)
            {
                BigInteger n = new BigInteger(1, new int[] { lsw }, false);
                // Check for a power of two
                if ((lsw & -lsw) == lsw)
                {
                    n.nBits = 1;
                }
                return n;
            }

            return Zero;
        }

        private static IBigInteger createValueOf(
            long value)
        {
            if (value < 0)
            {
                if (value == long.MinValue)
                    return createValueOf(~value).Not();

                return createValueOf(-value).Negate();
            }

            return createUValueOf((ulong)value);

            //			// store value into a byte array
            //			byte[] b = new byte[8];
            //			for (int i = 0; i < 8; i++)
            //			{
            //				b[7 - i] = (byte)value;
            //				value >>= 8;
            //			}
            //
            //			return new BigInteger(b);
        }

        public static IBigInteger ValueOf(
            long value)
        {
            switch (value)
            {
                case 0:
                    return Zero;
                case 1:
                    return One;
                case 2:
                    return Two;
                case 3:
                    return Three;
                case 10:
                    return Ten;
            }

            return createValueOf(value);
        }

        public int GetLowestSetBit()
        {
            if (this.SignValue == 0)
                return -1;

            int w = Magnitude.Length;

            while (--w > 0)
            {
                if (Magnitude[w] != 0)
                    break;
            }

            int word = (int)Magnitude[w];
            Debug.Assert(word != 0);

            int b = (word & 0x0000FFFF) == 0
                ? (word & 0x00FF0000) == 0
                    ? 7
                    : 15
                : (word & 0x000000FF) == 0
                    ? 23
                    : 31;

            while (b > 0)
            {
                if ((word << b) == int.MinValue)
                    break;

                b--;
            }

            return ((Magnitude.Length - w) * 32 - (b + 1));
        }

        public bool TestBit(
            int n)
        {
            if (n < 0)
                throw new ArithmeticException("Bit position must not be negative");

            if (SignValue < 0)
                return !Not().TestBit(n);

            int wordNum = n / 32;
            if (wordNum >= Magnitude.Length)
                return false;

            int word = Magnitude[Magnitude.Length - 1 - wordNum];
            return ((word >> (n % 32)) & 1) > 0;
        }

        public IBigInteger Or(
            IBigInteger value)
        {
            if (this.SignValue == 0)
                return value;

            if (value.SignValue == 0)
                return this;

            int[] aMag = this.SignValue > 0
                ? this.Magnitude
                : Add(One).Magnitude;

            int[] bMag = value.SignValue > 0
                ? value.Magnitude
                : value.Add(One).Magnitude;

            bool resultNeg = SignValue < 0 || value.SignValue < 0;
            int resultLength = System.Math.Max(aMag.Length, bMag.Length);
            int[] resultMag = new int[resultLength];

            int aStart = resultMag.Length - aMag.Length;
            int bStart = resultMag.Length - bMag.Length;

            for (int i = 0; i < resultMag.Length; ++i)
            {
                int aWord = i >= aStart ? aMag[i - aStart] : 0;
                int bWord = i >= bStart ? bMag[i - bStart] : 0;

                if (this.SignValue < 0)
                {
                    aWord = ~aWord;
                }

                if (value.SignValue < 0)
                {
                    bWord = ~bWord;
                }

                resultMag[i] = aWord | bWord;

                if (resultNeg)
                {
                    resultMag[i] = ~resultMag[i];
                }
            }

            IBigInteger result = new BigInteger(1, resultMag, true);

            // TODO Optimise this case
            if (resultNeg)
            {
                result = result.Not();
            }

            return result;
        }

        public IBigInteger Xor(
            IBigInteger value)
        {
            if (this.SignValue == 0)
                return value;

            if (value.SignValue == 0)
                return this;

            int[] aMag = this.SignValue > 0
                ? this.Magnitude
                : Add(One).Magnitude;

            int[] bMag = value.SignValue > 0
                ? value.Magnitude
                : value.Add(One).Magnitude;

            // TODO Can just replace with sign != value.sign?
            bool resultNeg = (SignValue < 0 && value.SignValue >= 0) || (SignValue >= 0 && value.SignValue < 0);
            int resultLength = System.Math.Max(aMag.Length, bMag.Length);
            int[] resultMag = new int[resultLength];

            int aStart = resultMag.Length - aMag.Length;
            int bStart = resultMag.Length - bMag.Length;

            for (int i = 0; i < resultMag.Length; ++i)
            {
                int aWord = i >= aStart ? aMag[i - aStart] : 0;
                int bWord = i >= bStart ? bMag[i - bStart] : 0;

                if (this.SignValue < 0)
                {
                    aWord = ~aWord;
                }

                if (value.SignValue < 0)
                {
                    bWord = ~bWord;
                }

                resultMag[i] = aWord ^ bWord;

                if (resultNeg)
                {
                    resultMag[i] = ~resultMag[i];
                }
            }

            IBigInteger result = new BigInteger(1, resultMag, true);

            // TODO Optimise this case
            if (resultNeg)
            {
                result = result.Not();
            }

            return result;
        }

        public IBigInteger SetBit(
            int n)
        {
            if (n < 0)
                throw new ArithmeticException("Bit address less than zero");

            if (TestBit(n))
                return this;

            // TODO Handle negative values and zero
            if (SignValue > 0 && n < (BitLength - 1))
                return FlipExistingBit(n);

            return Or(One.ShiftLeft(n));
        }

        public IBigInteger ClearBit(
            int n)
        {
            if (n < 0)
                throw new ArithmeticException("Bit address less than zero");

            if (!TestBit(n))
                return this;

            // TODO Handle negative values
            if (SignValue > 0 && n < (BitLength - 1))
                return FlipExistingBit(n);

            return AndNot(One.ShiftLeft(n));
        }

        public IBigInteger FlipBit(
            int n)
        {
            if (n < 0)
                throw new ArithmeticException("Bit address less than zero");

            // TODO Handle negative values and zero
            if (SignValue > 0 && n < (BitLength - 1))
                return FlipExistingBit(n);

            return Xor(One.ShiftLeft(n));
        }

        private IBigInteger FlipExistingBit(
            int n)
        {
            Debug.Assert(SignValue > 0);
            Debug.Assert(n >= 0);
            Debug.Assert(n < BitLength - 1);

            int[] mag = (int[])this.Magnitude.Clone();
            mag[mag.Length - 1 - (n >> 5)] ^= (1 << (n & 31)); // Flip bit
            //mag[mag.Length - 1 - (n / 32)] ^= (1 << (n % 32));
            return new BigInteger(this.SignValue, mag, false);
        }
    }
}
