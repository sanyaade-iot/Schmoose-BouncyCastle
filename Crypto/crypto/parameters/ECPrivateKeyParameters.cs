using System;
using System.Globalization;

using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Math;

namespace Org.BouncyCastle.Crypto.Parameters
{
    public class ECPrivateKeyParameters
		: ECKeyParameters
    {
		private readonly IBigInteger d;

		public ECPrivateKeyParameters(
			IBigInteger			d,
			ECDomainParameters	parameters)
			: this("EC", d, parameters)
		{
		}

		[Obsolete("Use version with explicit 'algorithm' parameter")]
		public ECPrivateKeyParameters(
			IBigInteger			d,
			DerObjectIdentifier publicKeyParamSet)
			: base("ECGOST3410", true, publicKeyParamSet)
		{
			if (d == null)
				throw new ArgumentNullException("d");

			this.d = d;
		}

		public ECPrivateKeyParameters(
			string				algorithm,
			IBigInteger			d,
			ECDomainParameters	parameters)
			: base(algorithm, true, parameters)
		{
			if (d == null)
				throw new ArgumentNullException("d");

			this.d = d;
		}

		public ECPrivateKeyParameters(
			string				algorithm,
			IBigInteger			d,
			DerObjectIdentifier publicKeyParamSet)
			: base(algorithm, true, publicKeyParamSet)
		{
			if (d == null)
				throw new ArgumentNullException("d");

			this.d = d;
		}

		public IBigInteger D
		{
			get { return d; }
		}

		public override bool Equals(
			object obj)
        {
			if (obj == this)
				return true;

			ECPrivateKeyParameters other = obj as ECPrivateKeyParameters;

			if (other == null)
				return false;

			return Equals(other);
        }

		protected bool Equals(
			ECPrivateKeyParameters other)
		{
			return d.Equals(other.d) && base.Equals(other);
		}

		public override int GetHashCode()
        {
            return d.GetHashCode() ^ base.GetHashCode();
        }
	}
}
