using System.IO;

namespace Org.BouncyCastle.Utilities.IO
{
	public static class Streams
	{
		private const int BufferSize = 512;

	    public static void Drain(Stream inStr)
		{
			var bs = new byte[BufferSize];
			while (inStr.Read(bs, 0, bs.Length) > 0)
			{
			}
		}

		public static byte[] ReadAll(Stream inStr)
		{
		    using (var buf = new MemoryStream())
		    {
		        PipeAll(inStr, buf);
		        return buf.ToArray();
		    }
		}

		public static byte[] ReadAllLimited(Stream inStr, int limit)
		{
		    using (var buf = new MemoryStream())
		    {
		        PipeAllLimited(inStr, limit, buf);
		        return buf.ToArray();
		    }
		}

		public static int ReadFully(Stream inStr, byte[] buf)
		{
			return ReadFully(inStr, buf, 0, buf.Length);
		}

		public static int ReadFully(Stream inStr, byte[] buf, int off, int len)
		{
			var totalRead = 0;
			while (totalRead < len)
			{
				var numRead = inStr.Read(buf, off + totalRead, len - totalRead);
				if (numRead < 1)
					break;
				totalRead += numRead;
			}
			return totalRead;
		}

		public static void PipeAll(Stream inStr, Stream outStr)
		{
			var bs = new byte[BufferSize];
			int numRead;
			while ((numRead = inStr.Read(bs, 0, bs.Length)) > 0)
			{
				outStr.Write(bs, 0, numRead);
			}
		}

		/// <summary>
		/// Pipe all bytes from <c>inStr</c> to <c>outStr</c>, throwing <c>StreamFlowException</c> if greater
		/// than <c>limit</c> bytes in <c>inStr</c>.
		/// </summary>
		/// <param name="inStr">
		/// A <see cref="Stream"/>
		/// </param>
		/// <param name="limit">
		/// A <see cref="System.Int64"/>
		/// </param>
		/// <param name="outStr">
		/// A <see cref="Stream"/>
		/// </param>
		/// <returns>The number of bytes actually transferred, if not greater than <c>limit</c></returns>
		/// <exception cref="IOException"></exception>
		public static long PipeAllLimited(Stream inStr, long limit, Stream outStr)
		{
			var bs = new byte[BufferSize];
			long total = 0;
			int numRead;
			while ((numRead = inStr.Read(bs, 0, bs.Length)) > 0)
			{
				total += numRead;
				if (total > limit)
					throw new StreamOverflowException("Data Overflow");
				outStr.Write(bs, 0, numRead);
			}
			return total;
		}
	}
}
