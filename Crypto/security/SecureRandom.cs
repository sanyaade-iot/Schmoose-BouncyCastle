using System;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Utilities;
using Random = Org.BouncyCastle.Bcpg.Random;

namespace Org.BouncyCastle.Security
{
    public class SecureRandom : Random, ISecureRandom
    {
        // Note: all objects of this class should be deriving their random data from
        // a single generator appropriate to the digest being used.
        private static readonly IRandomGenerator _sha1Generator = new DigestRandomGenerator(new Sha1Digest());
        private static readonly IRandomGenerator _sha256Generator = new DigestRandomGenerator(new Sha256Digest());
#if SUPPORT_SECURERND512
        private static readonly IRandomGenerator _sha512Generator = new DigestRandomGenerator(new Sha512Digest());
#endif

        private static readonly SecureRandom[] _master = { null };
        private static SecureRandom Master
        {
            get
            {
                if (_master[0] != null) 
                    return _master[0];

                var threaded = new ThreadedSeedGenerator();

#if SUPPORT_SECURERND512

                var gen = _sha512Generator;
                gen = new ReversedWindowGenerator(gen, 64);
                
                var sr = new SecureRandom(gen);
                sr.SetSeed(DateTime.Now.Ticks);
                sr.SetSeed(threaded.GenerateSeed(48, true));
                sr.GenerateSeed(1 + sr.Next(64));

                _master[0] = sr;

#else

                var gen = _sha256Generator;
                gen = new ReversedWindowGenerator(gen, 32);
                
                var sr = new SecureRandom(gen);                
                sr.SetSeed(DateTime.Now.Ticks);
                sr.SetSeed(threaded.GenerateSeed(24, true));
                sr.GenerateSeed(1 + sr.Next(32));

                _master[0] = sr;
#endif

                return _master[0];
            }
        }

        public static SecureRandom GetInstance(string algorithm)
        {
            // TODO Compared to JDK, we don't auto-seed if the client forgets - problem?

            // TODO Support all digests more generally, by stripping PRNG and calling DigestUtilities?

            IRandomGenerator drg;
            switch (Platform.StringToUpper(algorithm))
            {
                case "SHA1PRNG":
                    drg = _sha1Generator;
                    break;
                case "SHA256PRNG":
                    drg = _sha256Generator;
                    break;
#if SUPPORT_SECURERND512
                case "SHA512PRNG":
                    drg = _sha512Generator;
                    break;
#endif
                default:
                    throw new ArgumentException("Unrecognised PRNG algorithm: " + algorithm, "algorithm");
            }

            return new SecureRandom(drg);
        }

        public static byte[] GetSeed(int length)
        {
            return Master.GenerateSeed(length);
        }

        protected IRandomGenerator Generator;

        public SecureRandom()
#if SUPPORT_SECURERND512
            : this(_sha256Generator)
#else
            : this(_sha1Generator)
#endif
        {
#if SUPPORT_SECURERND512
            this.InitializeSeed(GetSeed(16));
#else
            this.InitializeSeed(GetSeed(8));
#endif
        }

        public SecureRandom(byte[] inSeed)
#if SUPPORT_SECURERND512
            : this(_sha256Generator)
#else
            : this(_sha1Generator)
#endif
        {
            this.InitializeSeed(inSeed);
        }

        /// <summary>
        /// Initializes the seed.
        /// </summary>
        /// <param name="inSeed">The information seed.</param>
        private void InitializeSeed(byte[] inSeed)
        {
            this.SetSeed(inSeed);
        }

        /// <summary>Use the specified instance of IRandomGenerator as random source.</summary>
        /// <remarks>
        /// This constructor performs no seeding of either the <c>IRandomGenerator</c> or the
        /// constructed <c>SecureRandom</c>. It is the responsibility of the client to provide
        /// proper seed material as necessary/appropriate for the given <c>IRandomGenerator</c>
        /// implementation.
        /// </remarks>
        /// <param name="generator">The source to generate all random bytes from.</param>
        public SecureRandom(IRandomGenerator generator)
            : base(0)
        {
            this.Generator = generator;
        }

        public virtual byte[] GenerateSeed(int length)
        {
            this.SetSeed(DateTime.Now.Ticks);

            var rv = new byte[length];
            this.NextBytes(rv);
            return rv;
        }

        public virtual void SetSeed(byte[] inSeed)
        {
            this.Generator.AddSeedMaterial(inSeed);
        }

        public virtual void SetSeed(long seed)
        {
            this.Generator.AddSeedMaterial(seed);
        }

        public override int Next()
        {
            for (; ; )
            {
                var i = this.NextInt() & int.MaxValue;
                if (i != int.MaxValue)
                    return i;
            }
        }

        public override int Next(int maxValue)
        {
            if (maxValue < 2)
            {
                if (maxValue < 0)
                    throw new ArgumentOutOfRangeException("maxValue", "maxValue < 0");

                return 0;
            }

            // Test whether maxValue is a power of 2
            if ((maxValue & -maxValue) == maxValue)
            {
                var val = NextInt() & int.MaxValue;
                var lr = ((long)maxValue * (long)val) >> 31;
                return (int)lr;
            }

            int bits, result;
            do
            {
                bits = this.NextInt() & int.MaxValue;
                result = bits % maxValue;
            }
            while (bits - result + (maxValue - 1) < 0); // Ignore results near overflow

            return result;
        }

        public override int Next(int minValue, int maxValue)
        {
            if (maxValue <= minValue)
            {
                if (maxValue == minValue)
                    return minValue;

                throw new ArgumentException("maxValue cannot be less than minValue");
            }

            var diff = maxValue - minValue;
            if (diff > 0)
                return minValue + Next(diff);

            for (; ; )
            {
                var i = this.NextInt();

                if (i >= minValue && i < maxValue)
                    return i;
            }
        }

        public override void NextBytes(byte[] buffer)
        {
            Generator.NextBytes(buffer);
        }

        public virtual void NextBytes(byte[] buffer, int start, int length)
        {
            this.Generator.NextBytes(buffer, start, length);
        }

        private static readonly double _doubleScale = System.Math.Pow(2.0, 64.0);

        public override double NextDouble()
        {
            return Convert.ToDouble((ulong)NextLong()) / _doubleScale;
        }

        public virtual int NextInt()
        {
            var intBytes = new byte[4];
            this.NextBytes(intBytes);

            var result = 0;
            for (var i = 0; i < 4; i++)
            {
                result = (result << 8) + (intBytes[i] & 0xff);
            }

            return result;
        }

        public virtual long NextLong()
        {
            return ((long)(uint)this.NextInt() << 32) | (long)(uint)this.NextInt();
        }
    }
}
