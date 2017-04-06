using System;
using System.IO;
using System.IO.Compression;
using System.Net.Security;
using System.Reflection;

namespace StackExchange.Redis
{
	internal static class MonoHelper
	{
		public static bool RunninOnUnix
		{
			get
			{
				int p = (int)Environment.OSVersion.Platform;
				return ((p == 4) || (p == 6) || (p == 128));
			}
		}

		public static bool RunninOnLinux
		{
			get
			{
				int p = (int)Environment.OSVersion.Platform;
				return ((p == 4) || (p == 128));
			}
		}

		public static bool RunningOnMono
		{
			get
			{
				Type t = Type.GetType("Mono.Runtime");
				if (t != null)
					return true;

				return false;
			}
		}

		public static bool RunningOnMonoUnix
		{
			get
			{
				return RunningOnMono && RunninOnUnix;
			}
		}

#region CreateSslStream

		/// <summary>
		/// Creates the SSL stream, using EncryptionPolicy.RequireEncryption on platforms 
		/// supporting SslStream with a .ctor accepting such argument (like NET4.5 and newer mono).
		/// </summary>
		/// <param name="innerStream">The inner stream.</param>
		/// <param name="leaveInnerStreamOpen">if set to <c>true</c> [leave inner stream open].</param>
		/// <param name="userCertificateValidationCallback">The user certificate validation callback.</param>
		/// <param name="userCertificateSelectionCallback">The user certificate selection callback.</param>
		/// <returns></returns>
		public static SslStream CreateSslStream(Stream innerStream, bool leaveInnerStreamOpen, 
			RemoteCertificateValidationCallback userCertificateValidationCallback, 
			LocalCertificateSelectionCallback userCertificateSelectionCallback)
		{
			SslStream result = null;

			try {
				object policy = Activator.CreateInstance("System", "System.Net.Security.EncryptionPolicy").Unwrap();

				return Activator.CreateInstance(typeof(SslStream),
					innerStream, leaveInnerStreamOpen, userCertificateValidationCallback,
					userCertificateSelectionCallback, policy) as SslStream;
			}
			catch (TypeLoadException)
			{
				// Mono version not yet supporting EncryptionPolicy..
			}
			catch (MissingMethodException)
			{
				// Mono version not yet supporting SslStream with a ctor accepting EncryptionPolicy..
			}

			return result ?? Activator.CreateInstance(typeof(SslStream),
				innerStream, leaveInnerStreamOpen, userCertificateValidationCallback,
				userCertificateSelectionCallback) as SslStream;
		}

#endregion

#if !NET40
#region GetCompressionLevel

		/// <summary>
		/// Gets the compression level from a string, avoiding a naming bug inside ancient mono versions.
		/// </summary>
		/// <remarks>
		/// See: https://github.com/mono/mono/commit/714efcf7d1f9c9017b370af16bb3117179dd60e5
		/// </remarks>
		/// <param name="level">The level.</param>
		/// <returns></returns>
		public static CompressionLevel GetCompressionLevel(string level)
		{
			try
			{
				return (CompressionLevel)Enum.Parse(typeof(CompressionLevel), level);
			}
			catch (ArgumentException)
			{
				// Oops, ancient mono here.. let's tray again.
				return (CompressionLevel)Enum.Parse(typeof(CompressionLevel), level.Replace("Optimal", "Optional"));
			}
		}
#endregion
#endif
	}
}
